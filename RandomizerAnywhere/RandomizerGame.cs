using ManiaAPI.XmlRpc;
using RandomizerAnywhere.Config;
using System.Diagnostics;
using System.Text.RegularExpressions;
using TmEssentials;

namespace RandomizerAnywhere;

internal sealed partial class RandomizerGame
{
    private readonly RemoteClient client;
    private readonly TmxRules tmxRules;
    private readonly AppConfig config;

    private readonly Dictionary<string, Func<int, string, string[], CancellationToken, Task>> commandHandlers;
    private readonly Dictionary<string, string> nicknameCache = [];

    private Stopwatch? sessionStopwatch;
    private int sessionStopwatchMillisecondOffset;
    private MapInfo? currentMap;
    private string? randomEnqueuedMapFileName;

    private bool SessionActive => sessionStopwatch is not null;

    public RandomizerGame(RemoteClient client, TmxRules tmxRules, AppConfig config)
    {
        this.client = client;
        this.tmxRules = tmxRules;
        this.config = config;

        commandHandlers = new()
        {
            ["start"] = StartAsync,
            ["stop"] = StopAsync,
            ["end"] = StopAsync,
            ["skip"] = SkipAsync,
            ["imp"] = ImpossibleAsync,
            ["commands"] = CommandsAsync,
            ["timelimit"] = TimeLimitAsync,
            ["tl"] = TimeLimitAsync,
            ["preset"] = PresetAsync,
            ["presets"] = PresetsAsync
        };

        /*client.Callback += async (methodName, methodParams, cancellationToken) =>
        {
            Console.WriteLine($"{methodName} {string.Join(' ', methodParams.Select(x =>
            {
                return x is Dictionary<string, object> dict
                    ? $"{{{string.Join(", ", dict.Select(kv => $"{kv.Key}: {kv.Value}"))}}}"
                    : x?.ToString() ?? "null";
            }))}");
        };*/
    }

    private void RegisterCallbacks()
    {
        client.On("TrackMania.BeginRace", async (methodParams, cancellationToken) =>
        {
            var mapInfo = (Dictionary<string, object>)methodParams[0];

            currentMap = new MapInfo(
                AuthorTime: (int)mapInfo["AuthorTime"],
                GoldTime: (int)mapInfo["GoldTime"],
                SilverTime: (int)mapInfo["SilverTime"],
                BronzeTime: (int)mapInfo["BronzeTime"]
            );
        });

        client.On("TrackMania.EndRace", async (methodParams, cancellationToken) =>
        {
            currentMap = null;
            randomEnqueuedMapFileName = null;
        });

        client.On("TrackMania.PlayerConnect", async (methodParams, cancellationToken) =>
        {
            var login = (string)methodParams[0];

            nicknameCache[login] = await client.GetPlayerNicknameAsync(login, cancellationToken);

            if (!SessionActive)
            {
                await SendWelcomeMessageAsync(login, cancellationToken);
            }
        });

        client.On("TrackMania.PlayerChat", async (methodParams, cancellationToken) =>
        {
            var playerUid = (int)methodParams[0];
            var login = (string)methodParams[1];
            var message = (string)methodParams[2];
            var isRegisteredCmd = (bool)methodParams[3];

            if (isRegisteredCmd)
            {
                await OnCommand(playerUid, login, message, cancellationToken);
            }
        });

        client.On("TrackMania.PlayerFinish", async (methodParams, cancellationToken) =>
        {
            var playerUid = (int)methodParams[0];
            var login = (string)methodParams[1];
            var score = (int)methodParams[2];

            await OnPlayerFinish(playerUid, login, score, cancellationToken);
        });

        client.On("TrackMania.StatusChanged", async (methodParams, cancellationToken) =>
        {
            var statusCode = (TrackManiaStatusCode)(int)methodParams[0];

            if (SessionActive)
            {
                switch (statusCode)
                {
                    case TrackManiaStatusCode.Play:
                        sessionStopwatch?.Start();
                        break;
                    case TrackManiaStatusCode.Finish:
                        await FinishMapAsync(cancellationToken);
                        break;
                }
            }
        });

        client.On("TrackMania.EndRound", async (methodParams, cancellationToken) =>
        {
            await FinishMapAsync(cancellationToken);
        });
    }

    private async Task FinishMapAsync(CancellationToken cancellationToken)
    {
        // TODO: there should be some second tolerance
        var sessionExpired = config.TimeLimit.TotalMilliseconds > 0
            && sessionStopwatch is not null
            && sessionStopwatch.ElapsedMilliseconds - sessionStopwatchMillisecondOffset >= config.TimeLimit.TotalMilliseconds;

        // freeze time if it was still running
        if (sessionStopwatch?.IsRunning == true)
        {
            sessionStopwatch.Stop();

            if (!sessionExpired)
            {
                await SendFrozenTimeMessageAsync(cancellationToken);
            }
        }

        // if session expired, stop the session and reset the time limit
        if (sessionExpired)
        {
            await SendMessageAsync("$FF0Time limit reached! Stopping the session.", cancellationToken);
            await StopSessionAsync(cancellationToken);
        }
        else
        {
            await SetCalculatedTimeLimitAsync(cancellationToken);
        }
    }

    public async Task OnCommand(int playerUid, string login, string message, CancellationToken cancellationToken)
    {
        var trimmedMessage = message.TrimStart('/');
        var firstSpaceIndex = trimmedMessage.IndexOf(' ');
        var mainCommand = firstSpaceIndex == -1 ? trimmedMessage : trimmedMessage.Substring(0, firstSpaceIndex);

        if (commandHandlers.TryGetValue(mainCommand, out var handler))
        {
            var args = CommandArgsRegex().Matches(trimmedMessage)
                .Cast<Match>()
                .Skip(1)
                .Select(m => m.Value.Trim('"'))
                .ToArray();

            await handler(playerUid, login, args, cancellationToken);
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        RegisterCallbacks();

        await SendWelcomeMessageAsync(login: null, cancellationToken);

        await client.WaitForCloseAsync(cancellationToken);
    }

    private async Task StartAsync(int playerUid, string login, string[] args, CancellationToken cancellationToken)
    {
        if (!SessionActive)
        {
            sessionStopwatch = new();
            await SetTimeLimitAsync(cancellationToken);
            await SendMessageAsync([string.Empty, "$0F0Let's begin!"], cancellationToken);

            if (config.TimeLimit.TotalMilliseconds > 0)
            {
                await SendMessageAsync($"Time limit set to $FF0{new TimeSpan(config.TimeLimit.Ticks):g}", cancellationToken);
            }

            await NextRandomMapAsync(goalReached: false, cancellationToken);
        }
    }

    private async Task StopAsync(int playerUid, string login, string[] args, CancellationToken cancellationToken)
    {
        if (!SessionActive)
        {
            await SendMessageAsync(login, "$F00No active session to stop.", cancellationToken);
            return;
        }

        await StopSessionAsync(cancellationToken);

        if (await client.IsMultiplePlayersAsync(cancellationToken))
        {
            await SendMessageAsync($"$FF0Player {GetNicknameOrLogin(login)} has stopped the session!", cancellationToken);
        }
        else
        {
            await SendMessageAsync("$F00Session stopped!", cancellationToken);
        }
    }

    private async Task StopSessionAsync(CancellationToken cancellationToken)
    {
        sessionStopwatch?.Stop();
        sessionStopwatch = null;
        sessionStopwatchMillisecondOffset = 0;
        currentMap = null;
        randomEnqueuedMapFileName = null;

        await client.CallAsync("SetTimeAttackLimit", [0], cancellationToken);
        await client.CallAsync("ChallengeRestart", [], cancellationToken);
    }

    private async Task SetTimeLimitAsync(CancellationToken cancellationToken)
    {
        await client.CallAsync("SetTimeAttackLimit", [config.TimeLimit.TotalMilliseconds], cancellationToken);
    }

    private async Task SetCalculatedTimeLimitAsync(CancellationToken cancellationToken)
    {
        if (config.TimeLimit.TotalMilliseconds <= 0 || sessionStopwatch is null)
        {
            return;
        }

        var elapsedMilliseconds = sessionStopwatch.ElapsedMilliseconds - sessionStopwatchMillisecondOffset;

        sessionStopwatchMillisecondOffset += 1500;

        await client.CallAsync("SetTimeAttackLimit", [config.TimeLimit.TotalMilliseconds - (int)elapsedMilliseconds], cancellationToken);
    }

    private async Task SkipAsync(int playerUid, string login, string[] args, CancellationToken cancellationToken)
    {
        if (await client.IsMultiplePlayersAsync(cancellationToken))
        {
            await SendMessageAsync($"Player {GetNicknameOrLogin(login)} wants to skip the current challenge.", cancellationToken);
        }
        else
        {
            await SendMessageAsync("Skipping the current challenge...", cancellationToken);
        }

        await NextRandomMapAsync(goalReached: false, cancellationToken);
    }

    private async Task ImpossibleAsync(int playerUid, string login, string[] args, CancellationToken cancellationToken)
    {

    }

    private async Task CommandsAsync(int playerUid, string login, string[] args, CancellationToken cancellationToken)
    {
        var commands = await client.GetChatCommandListAsync(cancellationToken);
        var formattedCommands = commands
            .Select(cmd => $"$FF0{cmd}$FFF")
            .Order();

        await SendMessageAsync(login, $"Commands: {string.Join(", ", formattedCommands)}", cancellationToken);
    }

    private async Task TimeLimitAsync(int playerUid, string login, string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            if (config.TimeLimit.TotalMilliseconds <= 0)
            {
                await SendMessageAsync(login, "Time limit is currently disabled. No time pressure!", cancellationToken);
            }
            else
            {
                await SendMessageAsync(login, $"Time limit is currently set to $FF0{new TimeSpan(config.TimeLimit.Ticks):g}", cancellationToken);
            }

            return;
        }

        var arg = args[0];

        if (arg.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            await SendMessageAsync(login, "Usage: $FF0/timelimit <seconds>", cancellationToken);
            return;
        }

        if (SessionActive)
        {
            await SendMessageAsync(login, "$F00Cannot change time limit while a session is active", cancellationToken);
            return;
        }

        if (!int.TryParse(arg, out var seconds) || seconds < 0)
        {
            await SendMessageAsync(login, $"$F00Invalid time limit value: {arg}. Please provide a non-negative integer.", cancellationToken);
            return;
        }

        config.TimeLimit = new TimeInt32(seconds * 1000);

        if (config.TimeLimit.TotalMilliseconds == 0)
        {
            if (await client.IsMultiplePlayersAsync(cancellationToken))
            {
                await SendMessageAsync($"Player {GetNicknameOrLogin(login)} has disabled the time limit.", cancellationToken);
            }
            else
            {
                await SendMessageAsync("Time limit disabled.", cancellationToken);
            }
        }
        else
        {
            if (await client.IsMultiplePlayersAsync(cancellationToken))
            {
                await SendMessageAsync($"Player {GetNicknameOrLogin(login)} has set the time limit to $FF0{new TimeSpan(config.TimeLimit.Ticks):g}", cancellationToken);
            }
            else
            {
                await SendMessageAsync($"Time limit set to $FF0{new TimeSpan(config.TimeLimit.Ticks):g}", cancellationToken);
            }
        }
    }

    private async Task PresetAsync(int playerUid, string login, string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            var currentPresetMessage = string.IsNullOrWhiteSpace(config.LastPreset?.DisplayName)
                ? "No preset was yet applied."
                : $"Last preset: $FF0{config.LastPreset.DisplayName}";

            await SendMessageAsync(login, [currentPresetMessage, "Usage: $FF0/preset <name>"], cancellationToken);
            return;
        }

        if (SessionActive)
        {
            await SendMessageAsync(login, "$F00Cannot change preset while a session is active", cancellationToken);
            return;
        }

        var presetName = args[0];
        var presetPath = Path.Combine(AppContext.BaseDirectory, "Presets", presetName + ".toml");

        if (!File.Exists(presetPath))
        {
            await SendMessageAsync(login, $"$F00Preset '{presetName}' not found.", cancellationToken);
            return;
        }

        var presetConfig = TomlLoader.LoadPresetConfig(presetPath);

        if (presetConfig is null)
        {
            await SendMessageAsync(login, $"$F00Failed to load preset '{presetName}'.", cancellationToken);
            return;
        }

        presetConfig.Apply(config);

        var displayName = string.IsNullOrWhiteSpace(presetConfig.DisplayName) ? presetName : presetConfig.DisplayName;
        config.LastPreset = presetConfig;

        if (await client.IsMultiplePlayersAsync(cancellationToken))
        {
            await SendMessageAsync($"Player {GetNicknameOrLogin(login)} has applied the $FF0{displayName}$FFF preset.", cancellationToken);
        }
        else
        {
            await SendMessageAsync($"$0F0Preset $FF0{displayName}$0F0 applied.", cancellationToken);
        }
    }

    private async Task PresetsAsync(int playerUid, string login, string[] args, CancellationToken cancellationToken)
    {
        var presetsDir = Path.Combine(AppContext.BaseDirectory, "Presets");

        if (!Directory.Exists(presetsDir))
        {
            await SendMessageAsync(login, "$F00No presets available.", cancellationToken);
            return;
        }

        var presetNames = Directory.EnumerateFiles(presetsDir, "*.toml")
            .Select(path => $"$FF0{Path.GetFileNameWithoutExtension(path)}$FFF")
            .Order()
            .ToList();

        if (presetNames.Count == 0)
        {
            await SendMessageAsync(login, "$F00No presets available.", cancellationToken);
            return;
        }

        await SendMessageAsync(login, [$"Presets: {string.Join(", ", presetNames)}", "Select a preset using $FF0/preset <name>$FFF"], cancellationToken);
    }

    public async Task OnPlayerFinish(int playerUid, string login, int score, CancellationToken cancellationToken)
    {
        if (!SessionActive)
        {
            return;
        }

        if (currentMap is null)
        {
            return;
        }

        int? goalTime = config.AutoSkipMode switch
        {
            AutoSkipMode.AuthorMedal => currentMap.AuthorTime,
            AutoSkipMode.GoldMedal => currentMap.GoldTime,
            AutoSkipMode.SilverMedal => currentMap.SilverTime,
            AutoSkipMode.BronzeMedal => currentMap.BronzeTime,
            _ => null
        };

        if (score > 0 && (config.AutoSkipMode == AutoSkipMode.Finished || score <= goalTime))
        {
            var goalName = config.AutoSkipMode switch
            {
                AutoSkipMode.AuthorMedal => "Author Medal",
                AutoSkipMode.GoldMedal => "Gold Medal",
                AutoSkipMode.SilverMedal => "Silver Medal",
                AutoSkipMode.BronzeMedal => "Bronze Medal",
                _ => "finish line"
            };

            sessionStopwatch?.Stop();

            if (await client.IsMultiplePlayersAsync(cancellationToken))
            {
                await SendMessageAsync($"Player {GetNicknameOrLogin(login)} has reached the $FF0{goalName}$0F0!", cancellationToken);
            }
            else
            {
                await SendMessageAsync($"$0F0You have reached the $FF0{goalName}$0F0!", cancellationToken);
            }
            await SendFrozenTimeMessageAsync(cancellationToken);

            await NextRandomMapAsync(goalReached: true, cancellationToken);

            //var validationData = await client.CallAsync<byte[]>("GetValidationReplay", [login], cancellationToken);
        }
    }

    public async Task NextRandomMapAsync(bool goalReached, CancellationToken cancellationToken)
    {
        // In case there are multiple players, the session stopwatch cannot be stopped immediately
        // so in case there is actually just one player, we need to account for the time it took to setup the next challenge
        var setupWatch = Stopwatch.StartNew();

        if (randomEnqueuedMapFileName is null)
        {
            var nextMap = await tmxRules.NextMapGbxAsync(cancellationToken);

            var mapPath = Path.Combine("_RandomizerAny", nextMap.FileName);
            await client.WriteFileAsync(mapPath, nextMap.Data, cancellationToken);
            await client.CallAsync("InsertChallenge", [mapPath], cancellationToken);
            await client.CallAsync("SetGameMode", [1], cancellationToken);

            randomEnqueuedMapFileName = mapPath;
        }

        if (await client.IsMultiplePlayersAsync(cancellationToken) && (!goalReached || config.CallVoteOnFinish))
        {
            await client.CallAsync("CallVote", [XmlRpcClient.GenerateXmlPayload("NextChallenge", [])], cancellationToken);
        }
        else
        {
            if (sessionStopwatch?.IsRunning == true)
            {
                sessionStopwatchMillisecondOffset += (int)setupWatch.ElapsedMilliseconds;
                sessionStopwatch.Stop();
                await SendFrozenTimeMessageAsync(cancellationToken);
            }

            var mapName = await client.GetMapNameAsync(randomEnqueuedMapFileName, cancellationToken);
            await SendMessageAsync($"Next map is ready: {mapName}", cancellationToken);
            await client.CallAsync("NextChallenge", [], cancellationToken);
        }
    }

    private async Task SendWelcomeMessageAsync(string? login, CancellationToken cancellationToken)
    {
        await SendMessageAsync(login, config.WelcomeMessage.Prepend(string.Empty), cancellationToken);
    }

    private string GetServerMessageType(string? login)
    {
        if (false)
        {
            return login is null ? "ChatSend" : "ChatSendToLogin";
        }
        else
        {
            return login is null ? "ChatSendServerMessage" : "ChatSendServerMessageToLogin";
        }
    }

    private async Task SendMessageAsync(string? login, string message, CancellationToken cancellationToken)
    {
        await client.CallAsync(GetServerMessageType(login), login is null ? [message] : [message, login], cancellationToken);
    }

    private async Task SendMessageAsync(string message, CancellationToken cancellationToken)
    {
        await SendMessageAsync(login: null, message, cancellationToken);
    }

    private async Task SendMessageAsync(string? login, IEnumerable<string> messageLines, CancellationToken cancellationToken)
    {
        var serverMessageType = GetServerMessageType(login);

        await client.SystemMulticallAsync(messageLines
            .Select(msg => new XmlRpcMulticall(serverMessageType, login is null ? [msg] : [msg, login])), cancellationToken);
    }

    private async Task SendMessageAsync(IEnumerable<string> messageLines, CancellationToken cancellationToken)
    {
        await SendMessageAsync(login: null, messageLines, cancellationToken);
    }

    private async Task SendFrozenTimeMessageAsync(CancellationToken cancellationToken)
    {
        if (config.TimeLimit.TotalMilliseconds <= 0 || sessionStopwatch is null)
        {
            return;
        }

        var millisecondsLeft = config.TimeLimit.TotalMilliseconds - (sessionStopwatch.ElapsedMilliseconds - sessionStopwatchMillisecondOffset);

        await SendMessageAsync($"Time limit frozen at $FF0{TimeSpan.FromMilliseconds(millisecondsLeft):g}", cancellationToken);
    }

    private string GetNicknameOrLogin(string login)
    {
        return nicknameCache.TryGetValue(login, out var nickname) ? $"$<{nickname}$>" : login;
    }

    [GeneratedRegex(@"[^\s""]+|""[^""]*""")]
    private static partial Regex CommandArgsRegex();
}

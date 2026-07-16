using ManiaAPI.XmlRpc;
using RandomizerAnywhere.Config;
using System.Diagnostics;
using System.Text.RegularExpressions;
using TmEssentials;

namespace RandomizerAnywhere;

internal sealed partial class RandomizerGame
{
    private readonly XmlRpcClient client;
    private readonly TmxRules tmxRules;
    private readonly AppConfig config;

    private readonly Dictionary<string, Func<int, string, string[], CancellationToken, Task>> commandHandlers;
    private readonly Dictionary<string, string> nicknameCache = [];

    private Stopwatch? sessionStopwatch;
    private int sessionStopwatchMillisecondOffset;
    private ChallengeInfo? currentChallenge;
    private string? randomEnqueuedChallengeFileName;

    private bool SessionActive => sessionStopwatch is not null;

    public RandomizerGame(XmlRpcClient client, TmxRules tmxRules, AppConfig config)
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
            ["tl"] = TimeLimitAsync
        };

        client.On("TrackMania.BeginRace", async (methodParams, cancellationToken) =>
        {
            var challengeInfo = (Dictionary<string, object>)methodParams[0];

            currentChallenge = new ChallengeInfo(
                AuthorTime: (int)challengeInfo["AuthorTime"],
                GoldTime: (int)challengeInfo["GoldTime"],
                SilverTime: (int)challengeInfo["SilverTime"],
                BronzeTime: (int)challengeInfo["BronzeTime"]
            );
        });

        client.On("TrackMania.EndRace", async (methodParams, cancellationToken) =>
        {
            currentChallenge = null;
            randomEnqueuedChallengeFileName = null;
        });

        client.On("TrackMania.PlayerConnect", async (methodParams, cancellationToken) =>
        {
            var login = (string)methodParams[0];

            var playerInfo = await client.CallAsync<Dictionary<string, object>>("GetPlayerInfo", [login], cancellationToken);
            nicknameCache[login] = (string)playerInfo["NickName"];

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
                        await FinishChallengeAsync(cancellationToken);
                        break;
                }
            }
        });

        client.On("TrackMania.EndRound", async (methodParams, cancellationToken) =>
        {
            await FinishChallengeAsync(cancellationToken);
        });

        client.Callback += async (methodName, methodParams, cancellationToken) =>
        {
            Console.WriteLine($"{methodName} {string.Join(' ', methodParams.Select(x =>
            {
                return x is Dictionary<string, object> dict
                    ? $"{{{string.Join(", ", dict.Select(kv => $"{kv.Key}: {kv.Value}"))}}}"
                    : x?.ToString() ?? "null";
            }))}");
        };
    }

    private async Task FinishChallengeAsync(CancellationToken cancellationToken)
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

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        await SendWelcomeMessageAsync(login: null, cancellationToken);
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

            await NextRandomChallengeAsync(goalReached: false, cancellationToken);
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

        if (await IsMultiplePlayersAsync(cancellationToken))
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
        currentChallenge = null;
        randomEnqueuedChallengeFileName = null;

        await client.CallAsync("SetTimeAttackLimit", [0], cancellationToken);
        await client.CallAsync("ChallengeRestart", cancellationToken);
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
        if (await IsMultiplePlayersAsync(cancellationToken))
        {
            await SendMessageAsync($"Player {GetNicknameOrLogin(login)} wants to skip the current challenge.", cancellationToken);
        }
        else
        {
            await SendMessageAsync("Skipping the current challenge...", cancellationToken);
        }

        await NextRandomChallengeAsync(goalReached: false, cancellationToken);
    }

    private async Task ImpossibleAsync(int playerUid, string login, string[] args, CancellationToken cancellationToken)
    {

    }

    private async Task CommandsAsync(int playerUid, string login, string[] args, CancellationToken cancellationToken)
    {
        var commandList = await client.CallAsync<List<object>>("GetChatCommandList", [(int)short.MaxValue, 0], cancellationToken);
        var commandNames = commandList.OfType<IReadOnlyDictionary<string, object>>()
            .Select(cmd => $"$FF0{(string)cmd["Name"]}$FFF")
            .Order();

        await SendMessageAsync(login, $"Commands: {string.Join(", ", commandNames)}", cancellationToken);
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
            if (await IsMultiplePlayersAsync(cancellationToken))
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
            if (await IsMultiplePlayersAsync(cancellationToken))
            {
                await SendMessageAsync($"Player {GetNicknameOrLogin(login)} has set the time limit to $FF0{new TimeSpan(config.TimeLimit.Ticks):g}", cancellationToken);
            }
            else
            {
                await SendMessageAsync($"Time limit set to $FF0{new TimeSpan(config.TimeLimit.Ticks):g}", cancellationToken);
            }
        }
    }

    public async Task OnPlayerFinish(int playerUid, string login, int score, CancellationToken cancellationToken)
    {
        if (!SessionActive)
        {
            return;
        }

        var goalTime = config.AutoSkipMode switch
        {
            AutoSkipMode.AuthorMedal => currentChallenge?.AuthorTime,
            AutoSkipMode.GoldMedal => currentChallenge?.GoldTime,
            AutoSkipMode.SilverMedal => currentChallenge?.SilverTime,
            AutoSkipMode.BronzeMedal => currentChallenge?.BronzeTime,
            _ => null
        };

        if (score > 0 && (config.AutoSkipMode == AutoSkipMode.Finished || score <= goalTime.Value))
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

            if (await IsMultiplePlayersAsync(cancellationToken))
            {
                await SendMessageAsync($"Player {GetNicknameOrLogin(login)} has reached the $FF0{goalName}$0F0!", cancellationToken);
            }
            else
            {
                await SendMessageAsync($"$0F0You have reached the $FF0{goalName}$0F0!", cancellationToken);
            }
            await SendFrozenTimeMessageAsync(cancellationToken);

            //await client.CallAsync("GetValidationReplay", [login], cancellationToken);

            await NextRandomChallengeAsync(goalReached: true, cancellationToken);
        }
    }

    public async Task NextRandomChallengeAsync(bool goalReached, CancellationToken cancellationToken)
    {
        // In case there are multiple players, the session stopwatch cannot be stopped immediately
        // so in case there is actually just one player, we need to account for the time it took to setup the next challenge
        var setupWatch = Stopwatch.StartNew();

        if (randomEnqueuedChallengeFileName is null)
        {
            var nextChallenge = await tmxRules.NextChallengeGbxAsync(cancellationToken);

            await client.CallAsync<bool>("WriteFile", [nextChallenge.FileName, nextChallenge.Data], cancellationToken);
            await client.CallAsync<bool>("InsertChallenge", [nextChallenge.FileName], cancellationToken);
            await client.CallAsync<bool>("SetGameMode", [1], cancellationToken);

            randomEnqueuedChallengeFileName = nextChallenge.FileName;
        }

        if (await IsMultiplePlayersAsync(cancellationToken) && (!goalReached || config.CallVoteOnFinish))
        {
            await client.CallAsync<bool>("CallVote", [XmlRpcClient.GenerateXmlPayload("NextChallenge", [])], cancellationToken);
        }
        else
        {
            if (sessionStopwatch?.IsRunning == true)
            {
                sessionStopwatchMillisecondOffset += (int)setupWatch.ElapsedMilliseconds;
                sessionStopwatch.Stop();
                await SendFrozenTimeMessageAsync(cancellationToken);
            }

            var challengeInfo = await client.CallAsync<Dictionary<string, object>>("GetChallengeInfo", [randomEnqueuedChallengeFileName], cancellationToken);
            await SendMessageAsync($"Next challenge is ready: {challengeInfo["Name"]}", cancellationToken);
            await client.CallAsync<bool>("NextChallenge", [], cancellationToken);
        }
    }

    private async Task SendWelcomeMessageAsync(string? login, CancellationToken cancellationToken)
    {
        await SendMessageAsync(login, config.WelcomeMessage.Prepend(string.Empty), cancellationToken);
    }

    private async Task SendMessageAsync(string? login, string message, CancellationToken cancellationToken)
    {
        var serverMessageType = login is null ? "ChatSendServerMessage" : "ChatSendServerMessageToLogin";
        await client.CallAsync<bool>(serverMessageType, login is null ? [message] : [message, login], cancellationToken);
    }

    private async Task SendMessageAsync(string message, CancellationToken cancellationToken)
    {
        await SendMessageAsync(login: null, message, cancellationToken);
    }

    private async Task SendMessageAsync(string? login, IEnumerable<string> messageLines, CancellationToken cancellationToken)
    {
        var serverMessageType = login is null ? "ChatSendServerMessage" : "ChatSendServerMessageToLogin";

        await client.SystemMulticallAsync(messageLines
            .Select(msg => new XmlRpcMulticall(serverMessageType, login is null ? [msg] : [msg, login])), cancellationToken);
    }

    private async Task SendMessageAsync(IEnumerable<string> messageLines, CancellationToken cancellationToken)
    {
        await SendMessageAsync(login: null, messageLines, cancellationToken);
    }

    private async Task<bool> IsMultiplePlayersAsync(CancellationToken cancellationToken)
    {
        var playerList = await client.CallAsync<List<object>>("GetPlayerList", [2, 0], cancellationToken);
        return playerList.Count > 1;
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

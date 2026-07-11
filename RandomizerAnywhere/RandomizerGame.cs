using ManiaAPI.XmlRpc;
using Polly;
using Polly.Retry;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace RandomizerAnywhere;

internal sealed class RandomizerGame
{
    private readonly ResiliencePipeline connectionPipeline;
    private readonly TmxRules tmxRules;
    private readonly AppConfig appConfig;

    public RandomizerGame(TmxRules tmxRules, AppConfig appConfig)
    {
        connectionPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 5,
                BackoffType = DelayBackoffType.Exponential
            })
            .Build();
        this.tmxRules = tmxRules;
        this.appConfig = appConfig;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await using var client = await connectionPipeline.ExecuteAsync(async token =>
            await XmlRpcClient.ConnectAsync(IPAddress.Loopback, appConfig.XmlRpcPort, cancellationToken: token), cancellationToken);

        Console.WriteLine();
        Console.WriteLine("Ready!");
        Console.WriteLine("The server is now available in the 'Local network' menu.");
        Console.WriteLine("Can't see the server? Check the generated CANNOT_SEE_SERVER.txt guide.");
        Console.WriteLine();

        var cannotSeeServerBuilder = new StringBuilder();

        cannotSeeServerBuilder.AppendLine("If you can't see the server in the 'Local network' menu, please try changing the BindIP in config.toml to one of these addresses and restart the app:");
        cannotSeeServerBuilder.AppendLine();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    cannotSeeServerBuilder.AppendLine(ua.Address.ToString());
                }
            }
        }
        cannotSeeServerBuilder.AppendLine();
        cannotSeeServerBuilder.AppendLine("Also check that your Server Port in launcher settings is set to 2350-2359 in TMF, 2350-2369 in ESWC, or 2350 in TMS/TMO. LAN discovery only scans the specified port ranges by default. The ranges can be changed by Port Broadcast Length option, but it is not necessary.");

        await File.WriteAllTextAsync("CANNOT_SEE_SERVER.txt", cannotSeeServerBuilder.ToString(), cancellationToken);

        await client.CallAsync<bool>("Authenticate", ["SuperAdmin", "SuperAdmin"], cancellationToken);
        await client.CallAsync<bool>("EnableCallbacks", [true], cancellationToken);

        await client.CallAsync("SetGameMode", [1], cancellationToken);
        await client.CallAsync("SetTimeAttackLimit", [0], cancellationToken);
        await client.CallAsync("SetChatTime", [0], cancellationToken);

        await client.SystemMulticallAsync([
            new("AddChatCommand", ["start"]),
            new("AddChatCommand", ["skip"]),
            new("AddChatCommand", ["imp"]),
        ], cancellationToken);

        var sessionManager = new SessionManager(client, tmxRules);

        await foreach (var callback in client.StreamCallbacksAsync(cancellationToken))
        {
            // Handle callbacks here
            Console.WriteLine($"{callback.MethodName} {string.Join(' ', callback.MethodParams.Select(x =>
            {
                return x is Dictionary<string, object> dict
                    ? $"{{{string.Join(", ", dict.Select(kv => $"{kv.Key}: {kv.Value}"))}}}"
                    : x?.ToString() ?? "null";
            }))}");

            if (callback.MethodName == "TrackMania.PlayerConnect")
            {
                await client.CallAsync("ChatSendServerMessageToLogin", ["$FF0Welcome to $s$0BFRandomizer $FF8Anywhere$FF0!", callback.MethodParams[0]], cancellationToken);
                await client.CallAsync("ChatSendServerMessageToLogin", ["Once you're ready, type $FF0/start", callback.MethodParams[0]], cancellationToken);
                await client.CallAsync("ChatSendServerMessageToLogin", ["To skip a challenge, type $FF0/skip", callback.MethodParams[0]], cancellationToken);
                await client.CallAsync("ChatSendServerMessageToLogin", ["To ban a challenge from appearing again, type $FF0/imp", callback.MethodParams[0]], cancellationToken);
            }

            if (callback.MethodName == "TrackMania.PlayerChat")
            {
                var message = (string)callback.MethodParams[2]!;
                var isRegisteredCmd = (bool)callback.MethodParams[3]!;

                if (isRegisteredCmd)
                {
                    if (message == "/start")
                    {

                    }
                    else if (message == "/skip")
                    {
                        await sessionManager.NextRandomChallengeAsync(cancellationToken);
                    }
                }
            }


            if (callback.MethodName == "TrackMania.PlayerFinish")
            {
                var score = Convert.ToInt32(callback.MethodParams[2]);

                if (score > 0)
                {
                    await sessionManager.NextRandomChallengeAsync(cancellationToken);
                }
            }
        }
    }
}

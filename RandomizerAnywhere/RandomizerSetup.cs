using ManiaAPI.XmlRpc;
using Polly;
using Polly.Retry;
using RandomizerAnywhere.Config;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace RandomizerAnywhere;

internal sealed class RandomizerSetup
{
    private readonly RemoteClient client;
    private readonly TmxRules tmxRules;
    private readonly AppConfig config;

    private readonly HashSet<string> commands = ["help", "commands", "start", "skip", "imp", "tmxquery", "timelimit", "tl", "stop", "preset", "presets"];

    public RandomizerSetup(RemoteClient client, TmxRules tmxRules, AppConfig config)
    {
        this.client = client;
        this.tmxRules = tmxRules;
        this.config = config;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await client.ConnectAsync(cancellationToken);

        var versionInfo = await client.GetVersionAsync(cancellationToken);

        Console.WriteLine();
        Console.WriteLine("Ready!");
        Console.WriteLine("The server is now available in the 'Local network' menu.");

        try
        {
            await SetupCannotSeeServerAsync(cancellationToken);
            Console.WriteLine("Can't see the server? Check the generated CANNOT_SEE_SERVER.txt guide.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: couldn't create CANNOT_SEE_SERVER.txt guide - {ex.Message}");
        }

        Console.WriteLine();

        await client.AuthenticateAsync(cancellationToken);
        await client.SetServerNameAsync(config.ServerName, cancellationToken);

        if (versionInfo.Build >= RemoteVersion.CallbackSupport)
        {
            await client.EnableCallbacksAsync(cancellationToken);
        }

        await client.CallAsync("SetTimeAttackLimit", [0], cancellationToken);
        await client.CallAsync("SetChatTime", [0], cancellationToken);

        if (!config.DedicatedServerMode)
        {
            var nextChallenge = await tmxRules.NextChallengeGbxAsync(cancellationToken);

            var challengeFilePath = Path.Combine("_RandomizerAny", nextChallenge.FileName);

            await client.WriteFileAsync(challengeFilePath, nextChallenge.Data, cancellationToken);
            await client.CallAsync("AddChallengeList", [new string[]{challengeFilePath}], cancellationToken);

            await client.CallAsync("SetGameMode", [1], cancellationToken);

            await client.CallAsync("StartServerLan", [], cancellationToken);
        }

        await client.SystemMulticallAsync(commands.Select(cmd => new XmlRpcMulticall("AddChatCommand", [cmd])), cancellationToken);
    }

    private static async Task SetupCannotSeeServerAsync(CancellationToken cancellationToken)
    {
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
    }
}

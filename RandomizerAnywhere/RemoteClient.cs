using ManiaAPI.XmlRpc;
using Polly;
using Polly.Retry;
using RandomizerAnywhere.Config;
using System.Globalization;
using System.Net;

namespace RandomizerAnywhere;

internal sealed class RemoteClient : IAsyncDisposable, IDisposable
{
    private static readonly ResiliencePipeline connectionPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 5,
            BackoffType = DelayBackoffType.Exponential
        })
        .Build();

    private static readonly string[] buildFormats = [
        "yyyy-MM-dd_HH_mm",
        "yyyy-MM-dd"
    ];

    private readonly AppConfig config;

    private XmlRpcClient? rawClient;
    public XmlRpcClient RawClient => rawClient ?? throw new InvalidOperationException("Client is not connected.");

    private RemoteVersion? versionInfo;
    public RemoteVersion VersionInfo => versionInfo ?? throw new InvalidOperationException("Version info is not available. Call GetVersionAsync first.");

    public RemoteClient(AppConfig config)
    {
        this.config = config;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        rawClient = await connectionPipeline.ExecuteAsync(async token =>
            await XmlRpcClient.ConnectAsync(IPAddress.Loopback, config.XmlRpcPort, cancellationToken: token), cancellationToken);
    }

    public async Task AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        var result = await RawClient.CallAsync<bool>("Authenticate", ["SuperAdmin", "SuperAdmin"], cancellationToken);

        if (!result)
        {
            throw new InvalidOperationException("Failed to authenticate as SuperAdmin.");
        }
    }

    public async Task SetServerNameAsync(string serverName, CancellationToken cancellationToken = default)
    {
        var result = await RawClient.CallAsync<bool>("SetServerName", [serverName], cancellationToken);

        if (!result)
        {
            throw new InvalidOperationException("Failed to set server name.");
        }
    }

    public async ValueTask<RemoteVersion> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        if (versionInfo is not null)
        {
            return versionInfo;
        }

        var versionDict = await RawClient.CallAsync<Dictionary<string, object>>("GetVersion", [], cancellationToken);

        var buildString = versionDict.TryGetValue("Build", out var build) ? build as string : null;
        var buildDate = buildString is null ? default : DateTime.TryParseExact(buildString, buildFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedBuild) ? parsedBuild : default(DateTime?);

        return versionInfo = new RemoteVersion(
            versionDict["Name"] as string ?? throw new InvalidOperationException("Missing Name in GetVersion response"),
            versionDict["Version"] as string ?? throw new InvalidOperationException("Missing Version in GetVersion response"),
            buildDate,
            versionDict.TryGetValue("TitleId", out var titleId) ? titleId as string : null);
    }

    public async Task EnableCallbacksAsync(CancellationToken cancellationToken = default)
    {
        var result = await RawClient.CallAsync<bool>("EnableCallbacks", [true], cancellationToken);

        if (!result)
        {
            throw new InvalidOperationException("Failed to enable callbacks.");
        }
    }

    public async Task WriteFileAsync(string filePath, byte[] fileData, CancellationToken cancellationToken = default)
    {
        if (VersionInfo.Build >= RemoteVersion.WriteFileSupport)
        {
            await CallAsync("WriteFile", [filePath, fileData], cancellationToken);
            return;
        }

        var tracksDirectory = await RawClient.CallAsync<string>("GetTracksDirectory", [], cancellationToken);

        if (Path.GetDirectoryName(filePath) is string directoryRelativePath)
        {
            Directory.CreateDirectory(Path.Combine(tracksDirectory, directoryRelativePath));
        }

        await File.WriteAllBytesAsync(Path.Combine(tracksDirectory, filePath), fileData, cancellationToken);
    }

    public async Task CallAsync(string methodName, object[] parameters, CancellationToken cancellationToken = default)
    {
        var result = await RawClient.CallAsync(methodName, parameters, cancellationToken);

        if (result is false)
        {
            throw new InvalidOperationException($"Failed to call method {methodName}.");
        }
    }

    public async Task<IEnumerable<XmlRpcMulticallResult>> SystemMulticallAsync(IEnumerable<XmlRpcMulticall> calls, CancellationToken cancellationToken = default)
    {
        return await RawClient.SystemMulticallAsync(calls, cancellationToken);
    }

    public async Task WaitForCloseAsync(CancellationToken cancellationToken)
    {
        await RawClient.WaitForCloseAsync(cancellationToken);
    }

    public async Task<string> GetPlayerNicknameAsync(string login, CancellationToken cancellationToken = default)
    {
        var playerInfo = await RawClient.CallAsync<Dictionary<string, object>>("GetPlayerInfo", [login], cancellationToken);
        return (string)playerInfo["NickName"];
    }

    public async Task<IEnumerable<string>> GetChatCommandListAsync(CancellationToken cancellationToken = default)
    {
        var commandList = await RawClient.CallAsync<List<object>>("GetChatCommandList", [(int)short.MaxValue, 0], cancellationToken);
        return commandList.OfType<IReadOnlyDictionary<string, object>>()
            .Select(x => (string)x["Name"]);
    }

    public async Task<bool> IsMultiplePlayersAsync(CancellationToken cancellationToken = default)
    {
        var playerList = await RawClient.CallAsync<List<object>>("GetPlayerList", [2, 0], cancellationToken);
        return playerList.Count > 1;
    }

    public async Task<string> GetMapNameAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var challengeInfo = await RawClient.CallAsync<Dictionary<string, object>>("GetChallengeInfo", [fileName], cancellationToken);
        return (string)challengeInfo["Name"];
    }

    public void On(string methodName, Func<object[], CancellationToken, Task> handler)
    {
        RawClient.On(methodName, handler);
    }

    public async ValueTask DisposeAsync()
    {
        if (rawClient is not null)
        {
            await rawClient.DisposeAsync();
            rawClient = null;
        }
    }

    public void Dispose()
    {
        rawClient?.Dispose();
        rawClient = null;
    }
}

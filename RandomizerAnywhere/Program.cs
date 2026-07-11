using ManiaAPI.XmlRpc;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Retry;
using RandomizerAnywhere;

var configPath = Path.Combine(AppContext.BaseDirectory, "config.toml");
var tomlConfig = ConfigLoader.Load(configPath);
var cmdConfig = CmdParser.Parse(args);
var appConfig = new AppConfig
{
    DownloadUrls = tomlConfig.DownloadUrls.ToDictionary(x => Enum.Parse<DedicatedServerType>(x.Key, ignoreCase: true), x => x.Value),
    TmxQuery = cmdConfig.TmxQuery
};

if (cmdConfig.Game is null && GameTitleParser.TryParse(tomlConfig.Game, out var configuredGame))
{
    appConfig.Game = configuredGame;
}
else
{
    appConfig.Game = cmdConfig.Game ?? GamePrompt.Ask();
}

using var http = CreateHttpClient();

var tmxRules = new TmxRules(http, appConfig)
{
    Game = appConfig.Game,
    QueryParameters = tomlConfig.TmxQuery,
};

var serverSetup = new ServerSetup(http, tmxRules, appConfig);

AppDomain.CurrentDomain.ProcessExit += (_, _) => serverSetup.StopServer();
Console.CancelKeyPress += (_, _) => serverSetup.StopServer();

try
{
    await serverSetup.SetupServerAsync();
    await serverSetup.SetupDedicatedCfgAsync();
    await serverSetup.SetupMatchSettingsAsync();
    serverSetup.StartServer();

    var pipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 5,
            BackoffType = DelayBackoffType.Exponential
        })
        .Build();

    await using var client = await pipeline.ExecuteAsync(async token =>
        await XmlRpcClient.ConnectAsync("127.0.0.1", 5000, cancellationToken: token));

    var sessionManager = new SessionManager(client, tmxRules);
    await sessionManager.RunAsync();
}
finally
{
    serverSetup.StopServer();
}

static HttpClient CreateHttpClient()
{
    var httpResilienceOptions = new HttpStandardResilienceOptions();
    httpResilienceOptions.Retry.MaxRetryAttempts = int.MaxValue;

    var httpRetryPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
        .AddRetry(httpResilienceOptions.Retry)
        .Build();

    return new HttpClient(new ResilienceHandler(httpRetryPipeline)
    {
        InnerHandler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            AllowAutoRedirect = false
        }
    });
}
using Microsoft.Extensions.Http.Resilience;
using Polly;
using RandomizerAnywhere;
using System.Net;

var configPath = Path.Combine(AppContext.BaseDirectory, "config.toml");
var tomlConfig = ConfigLoader.Load(configPath);
var cmdConfig = CmdParser.Parse(args);
var appConfig = new AppConfig
{
    BindIP = string.IsNullOrEmpty(tomlConfig.BindIP) ? null : IPAddress.Parse(tomlConfig.BindIP),
    XmlRpcPort = tomlConfig.XmlRpcPort,
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

    var randomizerGame = new RandomizerGame(tmxRules, appConfig);
    await randomizerGame.RunAsync();
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
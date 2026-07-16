using Microsoft.Extensions.Http.Resilience;
using Polly;
using RandomizerAnywhere;
using RandomizerAnywhere.Config;

var configPath = Path.Combine(AppContext.BaseDirectory, "config.toml");
var globalConfig = TomlLoader.LoadGlobalConfig(configPath);
var cmdConfig = CmdConfig.Parse(args);
var appConfig = new AppConfig
{
    Game = Configurator.GetOrAskEnum(globalConfig.Game, cmdConfig.Game, "RANDANY_GAME", "a game"),
    TmxGame = Configurator.GetOptionalEnum(globalConfig.TmxGame, cmdConfig.TmxGame, "RANDANY_TMX_GAME"),
    BindIP = Configurator.GetIP(globalConfig.BindIP, cmdConfig.BindIP, "RANDANY_BIND_IP"),
    XmlRpcPort = Configurator.GetNumber(globalConfig.XmlRpcPort, cmdConfig.XmlRpcPort, "RANDANY_XMLRPC_PORT"),
    AutoSkipMode = Configurator.GetEnum<AutoSkipMode>(globalConfig.AutoSkipMode, cmdValue: null, "RANDANY_AUTO_SKIP_MODE"),
    DownloadUrls = globalConfig.DownloadUrls
        .ToDictionary(
            x => Enum.Parse<DedicatedServerType>(x.Key, ignoreCase: true), 
            x => x.Value),
    TmxQuery = globalConfig.TmxQuery,
    TmxQueryOverride = cmdConfig.TmxQuery,
    NoServer = Configurator.GetBool(cfgValue: null, cmdConfig.NoServer, "RANDANY_NO_SERVER"),
    TimeLimit = new(globalConfig.TimeLimit),
    CallVoteOnFinish = Configurator.GetBool(globalConfig.CallVoteOnFinish, cmdValue: null, "RANDANY_CALL_VOTE_ON_FINISH"),
    WelcomeMessage = globalConfig.WelcomeMessage.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries),
    ServerName = Configurator.GetString(globalConfig.ServerName, cmdConfig.ServerName, "RANDANY_SERVER_NAME"),
    GameSettings = Configurator.GetString(globalConfig.GameSettings, cmdValue: null, "RANDANY_GAME_SETTINGS"),
};

if (!string.IsNullOrWhiteSpace(globalConfig.Preset))
{
    var presetPath = Path.Combine(AppContext.BaseDirectory, "Presets", globalConfig.Preset + ".toml");

    if (File.Exists(presetPath))
    {
        var presetConfig = TomlLoader.LoadPresetConfig(presetPath);
        presetConfig?.Apply(appConfig);
    }
    else
    {
        Console.WriteLine($"Preset '{globalConfig.Preset}' not found at '{presetPath}'.");
    }
}

using var http = CreateHttpClient();

var tmxRules = new TmxRules(http, appConfig);

var serverSetup = new ServerSetup(http, tmxRules, appConfig);

AppDomain.CurrentDomain.ProcessExit += (_, _) => serverSetup.StopServer();
Console.CancelKeyPress += (_, _) => serverSetup.StopServer();

try
{
    if (!appConfig.NoServer)
    {
        var firstTimeSetup = await serverSetup.SetupServerAsync();
        await serverSetup.SetupDedicatedCfgAsync();
        await serverSetup.SetupMatchSettingsAsync();
        serverSetup.StartServer(showServerWindow: firstTimeSetup);
    }

    var randomizerSetup = new RandomizerSetup(tmxRules, appConfig);
    await randomizerSetup.RunAsync();
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
using Jab;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using RandomizerAnywhere;
using RandomizerAnywhere.Config;

var provider = new AppServiceProvider();

var serverSetup = provider.GetRequiredService<ServerSetup>();
await serverSetup.TrySetupAsync();

var randomizerSetup = provider.GetRequiredService<RandomizerSetup>();
await randomizerSetup.RunAsync();

var randomizerGame = provider.GetRequiredService<RandomizerGame>();
await randomizerGame.RunAsync();

[ServiceProvider]
[Singleton(typeof(HttpClient), Factory = nameof(CreateHttpClient))]
[Singleton(typeof(AppConfig), Factory = nameof(CreateAppConfig))]
[Transient(typeof(TmxRules))]
[Transient(typeof(ServerSetup))]
[Transient(typeof(RandomizerSetup))]
[Singleton(typeof(RemoteClient))]
[Transient(typeof(RandomizerGame))]
internal partial class AppServiceProvider
{
    public static HttpClient CreateHttpClient()
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

    public static AppConfig CreateAppConfig()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.toml");
        var globalConfig = TomlLoader.LoadGlobalConfig(configPath);
        var cmdConfig = CmdConfig.Parse(Environment.GetCommandLineArgs());
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
            DedicatedServerMode = Configurator.GetBool(cfgValue: null, cmdConfig.DedicatedServerMode, "RANDANY_DEDICATED"),
            SkipSetup = Configurator.GetBool(cfgValue: null, cmdConfig.SkipSetup, "RANDANY_SKIP_SETUP"),
            TimeLimit = new(globalConfig.TimeLimit),
            CallVoteOnFinish = Configurator.GetBool(globalConfig.CallVoteOnFinish, cmdValue: null, "RANDANY_CALLVOTE_ON_FINISH"),
            WelcomeMessage = globalConfig.WelcomeMessage.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries),
            ServerName = Configurator.GetString(globalConfig.ServerName, cmdConfig.ServerName, "RANDANY_SERVER_NAME"),
            GameSettings = Configurator.GetString(globalConfig.GameSettings, cmdValue: null, "RANDANY_GAMESETTINGS"),
        };

        if (!string.IsNullOrWhiteSpace(globalConfig.Preset))
        {
            var presetPath = Path.Combine(AppContext.BaseDirectory, "Presets", globalConfig.Preset + ".toml");

            if (File.Exists(presetPath))
            {
                var presetConfig = TomlLoader.LoadPresetConfig(presetPath);
                presetConfig?.Apply(appConfig);
                appConfig.LastPreset = presetConfig;
            }
            else
            {
                Console.WriteLine($"Preset '{globalConfig.Preset}' not found.");
            }
        }

        return appConfig;
    }
}
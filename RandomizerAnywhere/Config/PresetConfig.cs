using TmEssentials;

namespace RandomizerAnywhere.Config;

internal sealed class PresetConfig
{
    public string DisplayName { get; set; } = string.Empty;

    //public string[] Games { get; init; } = [];
    public int TimeLimit { get; set; } = 0;
    public string AutoSkipMode { get; set; } = "AuthorMedal";

    public string? ServerName { get; set; }
    public string? WelcomeMessage { get; set; }

    public Dictionary<string, object> TmxQuery { get; set; } = [];

    public void Apply(AppConfig config)
    {
        config.TimeLimit = new TimeInt32(TimeLimit);
        config.AutoSkipMode = Enum.Parse<AutoSkipMode>(AutoSkipMode, ignoreCase: true);

        if (ServerName is not null) config.ServerName = ServerName;
        if (WelcomeMessage is not null) config.WelcomeMessage = WelcomeMessage.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        config.TmxQuery = TmxQuery;
    }
}

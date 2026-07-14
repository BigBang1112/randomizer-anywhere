namespace RandomizerAnywhere.Config;

internal sealed class TomlConfig
{
    public string Game { get; set; } = "";
    public string BindIP { get; set; } = "";
    public ushort XmlRpcPort { get; set; }
    public string AutoSkipMode { get; set; } = "";
    public int TimeLimit { get; set; }
    public bool CallVoteOnFinish { get; set; }
    public string ServerName { get; set; } = "";
    public string WelcomeMessage { get; set; } = "";

    public Dictionary<string, string> DownloadUrls { get; set; } = [];
    public Dictionary<string, object> TmxQuery { get; set; } = [];
    public string GameSettings { get; set; } = "";
}

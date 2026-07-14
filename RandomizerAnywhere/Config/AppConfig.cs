using System.Net;

namespace RandomizerAnywhere.Config;

internal sealed class AppConfig
{
    public GameTitle Game { get; set; }
    public IPAddress? BindIP { get; set; }
    public ushort XmlRpcPort { get; set; }
    public Dictionary<DedicatedServerType, string> DownloadUrls { get; set; } = [];
    public string? TmxQuery { get; set; }
    public bool NoServer { get; set; }
    public AutoSkipMode AutoSkipMode { get; set; }
    public int TimeLimit { get; set; }
    public bool CallVoteOnFinish { get; set; }
    public string[] WelcomeMessage { get; set; } = [];
    public string? ServerName { get; set; }
    public string? GameSettings { get; set; }
}

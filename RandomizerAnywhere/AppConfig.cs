using System.Net;

namespace RandomizerAnywhere;

internal sealed class AppConfig
{
    public GameTitle Game { get; set; }
    public IPAddress? BindIP { get; set; }
    public ushort XmlRpcPort { get; set; }
    public Dictionary<DedicatedServerType, string> DownloadUrls { get; set; } = [];
    public string? TmxQuery { get; set; }
}

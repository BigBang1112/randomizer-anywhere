namespace RandomizerAnywhere;

internal sealed class TomlConfig
{
    public string Game { get; set; } = "";
    public string BindIP { get; set; } = "";
    public ushort XmlRpcPort { get; set; }

    public Dictionary<string, string> DownloadUrls { get; set; } = [];
    public Dictionary<string, object> TmxQuery { get; set; } = [];
}

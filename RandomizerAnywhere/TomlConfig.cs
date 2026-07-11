namespace RandomizerAnywhere;

internal sealed class TomlConfig
{
    public string Game { get; set; } = "";

    public Dictionary<string, string> DownloadUrls { get; set; } = [];
    public Dictionary<string, object> TmxQuery { get; set; } = [];
}

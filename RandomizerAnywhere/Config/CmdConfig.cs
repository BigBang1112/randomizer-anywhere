namespace RandomizerAnywhere.Config;

internal sealed class CmdConfig
{
    public GameTitle? Game { get; set; }
    public string? TmxQuery { get; set; }
    public bool NoServer { get; set; }
}

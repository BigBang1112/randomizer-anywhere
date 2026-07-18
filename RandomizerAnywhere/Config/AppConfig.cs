using System.Net;
using TmEssentials;

namespace RandomizerAnywhere.Config;

internal sealed class AppConfig
{
    public required GameTitle Game { get; init; }
    public required GameTitle? TmxGame { get; init; }
    public required IPAddress? BindIP { get; init; }
    public required ushort XmlRpcPort { get; init; }
    public required Dictionary<DedicatedServerType, string> DownloadUrls { get; init; }
    public required IReadOnlyDictionary<string, object> TmxQuery { get; set; }
    public required string? TmxQueryOverride { get; set; }
    public required bool DedicatedServerMode { get; init; }
    public required bool SkipSetup { get; init; }
    public required AutoSkipMode AutoSkipMode { get; set; }
    public required TimeInt32 TimeLimit { get; set; }
    public required bool CallVoteOnFinish { get; set; }
    public required string[] WelcomeMessage { get; set; }
    public required string ServerName { get; set; }
    public required string GameSettings { get; init; }
    public PresetConfig? LastPreset { get; set; }
}

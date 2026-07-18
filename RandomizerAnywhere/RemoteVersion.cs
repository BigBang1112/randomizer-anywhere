namespace RandomizerAnywhere;

internal sealed record RemoteVersion(string Name, string Version, DateTime? Build, string? TitleId)
{
    public static DateTime CallbackSupport => new(2006, 5, 30);
    public static DateTime WriteFileSupport => new(2006, 3, 10);
}

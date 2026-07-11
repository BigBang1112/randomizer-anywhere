namespace RandomizerAnywhere;

internal enum GameTitle
{
    TMNF,
    TMUF,
    TMN,
    TMS,
    TMO
}

internal static class GameTitleParser
{
    public static bool TryParse(string? input, out GameTitle game)
    {
        game = default;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        if (Enum.TryParse<GameTitle>(input.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            game = parsed;
            return true;
        }

        return false;
    }
}

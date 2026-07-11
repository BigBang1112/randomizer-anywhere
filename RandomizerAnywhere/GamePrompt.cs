namespace RandomizerAnywhere;

internal static class GamePrompt
{
    private static readonly GameTitle[] Choices = [
        GameTitle.TMNF,
        GameTitle.TMUF,
        GameTitle.TMN,
        GameTitle.TMS,
        GameTitle.TMO
    ];

    public static GameTitle Ask()
    {
        while (true)
        {
            Console.Write($"Select a game ({string.Join(", ", Choices)}): ");
            var input = Console.ReadLine()?.Trim();

            if (GameTitleParser.TryParse(input, out var game))
            {
                return game;
            }

            Console.WriteLine($"'{input}' isn't a recognized game.");
        }
    }
}

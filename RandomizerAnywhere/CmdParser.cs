namespace RandomizerAnywhere;

internal static class CmdParser
{
    public static CmdConfig Parse(string[] args)
    {
        var cmdArgs = new CmdConfig();
        var enumerator = args.AsEnumerable().GetEnumerator();

        while (enumerator.MoveNext())
        {
            switch (enumerator.Current)
            {
                case "--help":
                case "-h":
                    Console.WriteLine("Usage: RandomizerAnywhere [--game <game>] [--help]");
                    Console.WriteLine("  --game, -g <game>         Specify the game to setup (TMNF, TMUF, TMN, TMS, TMO)");
                    Console.WriteLine("  --help, -h                Show this help message");
                    Console.WriteLine("  --tmx-query, -q <query>   Specify a TMX query to filter the maps");
                    return cmdArgs;
                case "--game":
                case "-g":
                    var gameArg = enumerator.Current;
                    if (!enumerator.MoveNext())
                    {
                        Warn($"The {gameArg} option requires a value. Ignoring.");
                        break;
                    }
                    if (!GameTitleParser.TryParse(enumerator.Current, out var game))
                    {
                        Warn($"unrecognized game '{enumerator.Current}' passed on the command line. Ignoring.");
                        break;
                    }
                    cmdArgs.Game = game;
                    break;
                case "--tmx-query":
                case "-q":
                    var tmxQueryArg = enumerator.Current;
                    if (!enumerator.MoveNext())
                    {
                        Warn($"The {tmxQueryArg} option requires a value. Ignoring.");
                        break;
                    }
                    cmdArgs.TmxQuery = enumerator.Current;
                    break;
            }
        }

        return cmdArgs;
    }

    private static void Warn(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"Warning: {message}");
        Console.ResetColor();
    }
}

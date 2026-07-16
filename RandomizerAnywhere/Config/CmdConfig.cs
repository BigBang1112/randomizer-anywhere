using System.Net;

namespace RandomizerAnywhere.Config;

internal sealed class CmdConfig
{
    public GameTitle? Game { get; set; }
    public GameTitle? TmxGame { get; set; }
    public IPAddress? BindIP { get; set; }
    public ushort? XmlRpcPort { get; set; }
    public string? TmxQuery { get; set; }
    public bool NoServer { get; set; }
    public string? ServerName { get; set; }

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
                    Console.WriteLine("Usage: RandomizerAnywhere [--game <game>] [--tmx-game <game>] [--tmx-query <query>] [--bind-ip <ip>] [--xmlrpc-port <port>] [--server-name <name>] [--no-server] [--help]");
                    Console.WriteLine("  --game, -g <game>         Specify the game to setup (TMNF, TMUF, TMN, TMS, TMO)");
                    Console.WriteLine("  --tmx-game <game>         Specify the TMX game to use, if different from --game (TMNF, TMUF, TMN, TMS, TMO)");
                    Console.WriteLine("  --tmx-query, -q <query>   Specify a TMX query to filter the maps");
                    Console.WriteLine("  --bind-ip <ip>            Specify the IP address the dedicated server binds to");
                    Console.WriteLine("  --xmlrpc-port <port>      Specify the XML-RPC port for the dedicated server");
                    Console.WriteLine("  --server-name <name>      Specify the name shown for the server in the game's server list");
                    Console.WriteLine("  --no-server               Skip downloading/starting the dedicated server");
                    Console.WriteLine("  --help, -h                Show this help message");
                    return cmdArgs;
                case "--game":
                case "-g":
                    var gameArg = enumerator.Current;
                    if (!enumerator.MoveNext())
                    {
                        Warn($"The {gameArg} option requires a value. Ignoring.");
                        break;
                    }
                    if (!Enum.TryParse<GameTitle>(enumerator.Current, ignoreCase: true, out var game))
                    {
                        Warn($"unrecognized game '{enumerator.Current}' passed on the command line. Ignoring.");
                        break;
                    }
                    cmdArgs.Game = game;
                    break;
                case "--tmx-game":
                    var tmxGameArg = enumerator.Current;
                    if (!enumerator.MoveNext())
                    {
                        Warn($"The {tmxGameArg} option requires a value. Ignoring.");
                        break;
                    }
                    if (!Enum.TryParse<GameTitle>(enumerator.Current, ignoreCase: true, out var tmxGame))
                    {
                        Warn($"unrecognized game '{enumerator.Current}' passed on the command line. Ignoring.");
                        break;
                    }
                    cmdArgs.TmxGame = tmxGame;
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
                case "--no-server":
                    cmdArgs.NoServer = true;
                    break;
                case "--bind-ip":
                    var bindIpArg = enumerator.Current;
                    if (!enumerator.MoveNext())
                    {
                        Warn($"The {bindIpArg} option requires a value. Ignoring.");
                        break;
                    }
                    if (!IPAddress.TryParse(enumerator.Current, out var ip))
                    {
                        Warn($"unrecognized IP address '{enumerator.Current}' passed on the command line. Ignoring.");
                        break;
                    }
                    cmdArgs.BindIP = ip;
                    break;
                case "--xmlrpc-port":
                    var xmlRpcPortArg = enumerator.Current;
                    if (!enumerator.MoveNext())
                    {
                        Warn($"The {xmlRpcPortArg} option requires a value. Ignoring.");
                        break;
                    }
                    if (!ushort.TryParse(enumerator.Current, out var port))
                    {
                        Warn($"unrecognized port '{enumerator.Current}' passed on the command line. Ignoring.");
                        break;
                    }
                    cmdArgs.XmlRpcPort = port;
                    break;
                case "--server-name":
                    var serverNameArg = enumerator.Current;
                    if (!enumerator.MoveNext())
                    {
                        Warn($"The {serverNameArg} option requires a value. Ignoring.");
                        break;
                    }
                    cmdArgs.ServerName = enumerator.Current;
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

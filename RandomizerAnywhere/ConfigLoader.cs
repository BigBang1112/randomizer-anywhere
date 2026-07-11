using Tomlyn;

namespace RandomizerAnywhere;

internal static class ConfigLoader
{
    public static TomlConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            return new();
        }

        try
        {
            var tomlText = File.ReadAllText(path);
            return TomlSerializer.Deserialize<TomlConfig>(tomlText) ?? new TomlConfig();
        }
        catch (TomlException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Warning: failed to parse '{path}': {ex.Message}");
            Console.WriteLine("Falling back to defaults (game = none).");
            Console.ResetColor();
            return new TomlConfig();
        }
    }
}

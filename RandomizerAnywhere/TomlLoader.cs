using RandomizerAnywhere.Config;
using Tomlyn;

namespace RandomizerAnywhere;

internal static class TomlLoader
{
    public static GlobalConfig LoadGlobalConfig(string path)
    {
        if (!File.Exists(path))
        {
            return new();
        }

        try
        {
            var tomlText = File.ReadAllText(path);
            return TomlSerializer.Deserialize<GlobalConfig>(tomlText) ?? new GlobalConfig();
        }
        catch (TomlException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Warning: failed to parse '{path}': {ex.Message}");
            Console.WriteLine("Falling back to defaults.");
            Console.ResetColor();
            return new GlobalConfig();
        }
    }

    public static PresetConfig? LoadPresetConfig(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var tomlText = File.ReadAllText(path);
            return TomlSerializer.Deserialize<PresetConfig>(tomlText);
        }
        catch (TomlException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Warning: failed to parse '{path}': {ex.Message}");
            Console.WriteLine("Falling back to defaults.");
            Console.ResetColor();
            return null;
        }
    }
}

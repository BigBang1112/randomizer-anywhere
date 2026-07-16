using System.Net;

namespace RandomizerAnywhere.Config;

internal static class Configurator
{
    public static T GetOrAskEnum<T>(string? cfgValue, T? cmdValue, string? envVariableName, string humanizedName) where T : struct, Enum
    {
        if (cmdValue.HasValue)
        {
            return cmdValue.Value;
        }

        if (!string.IsNullOrWhiteSpace(envVariableName))
        {
            var envValue = Environment.GetEnvironmentVariable(envVariableName);
            if (!string.IsNullOrWhiteSpace(envValue))
            {
                cfgValue = envValue;
            }
        }

        if (!string.IsNullOrWhiteSpace(cfgValue) && Enum.TryParse<T>(cfgValue.Trim(), ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        while (true)
        {
            Console.Write($"Select {humanizedName} ({string.Join(", ", Enum.GetNames<T>())}): ");
            var input = Console.ReadLine()?.Trim();

            if (Enum.TryParse(input, ignoreCase: true, out parsed))
            {
                return parsed;
            }

            Console.WriteLine($"'{input}' isn't a recognized value.");
        }
    }

    public static T GetEnum<T>(string? cfgValue, T? cmdValue, string? envVariableName) where T : struct, Enum
    {
        if (cmdValue.HasValue)
        {
            return cmdValue.Value;
        }

        if (!string.IsNullOrWhiteSpace(envVariableName))
        {
            var envValue = Environment.GetEnvironmentVariable(envVariableName);
            if (!string.IsNullOrWhiteSpace(envValue))
            {
                cfgValue = envValue;
            }
        }

        if (string.IsNullOrWhiteSpace(cfgValue) || !Enum.TryParse<T>(cfgValue.Trim(), ignoreCase: true, out var parsed))
        {
            return default;
        }

        return parsed;
    }

    public static T? GetOptionalEnum<T>(string? cfgValue, T? cmdValue, string? envVariableName) where T : struct, Enum
    {
        if (cmdValue.HasValue)
        {
            return cmdValue;
        }

        if (!string.IsNullOrWhiteSpace(envVariableName))
        {
            var envValue = Environment.GetEnvironmentVariable(envVariableName);
            if (!string.IsNullOrWhiteSpace(envValue))
            {
                cfgValue = envValue;
            }
        }

        if (string.IsNullOrWhiteSpace(cfgValue) || !Enum.TryParse<T>(cfgValue.Trim(), ignoreCase: true, out var parsed))
        {
            return null;
        }

        return parsed;
    }

    public static IPAddress? GetIP(string? cfgValue, IPAddress? cmdValue, string? envVariableName)
    {
        if (cmdValue is not null)
        {
            return cmdValue;
        }

        if (!string.IsNullOrWhiteSpace(envVariableName))
        {
            var envValue = Environment.GetEnvironmentVariable(envVariableName);
            if (!string.IsNullOrWhiteSpace(envValue))
            {
                cfgValue = envValue;
            }
        }

        if (string.IsNullOrWhiteSpace(cfgValue) || !IPAddress.TryParse(cfgValue.Trim(), out var ip))
        {
            return null;
        }

        return ip;
    }

    public static ushort GetNumber(ushort cfgValue, ushort? cmdValue, string? envVariableName)
    {
        if (cmdValue.HasValue)
        {
            return cmdValue.Value;
        }

        if (!string.IsNullOrWhiteSpace(envVariableName))
        {
            var envValue = Environment.GetEnvironmentVariable(envVariableName);
            if (!string.IsNullOrWhiteSpace(envValue) && ushort.TryParse(envValue.Trim(), out var parsed))
            {
                return parsed;
            }
        }

        return cfgValue;
    }

    public static bool GetBool(bool? cfgValue, bool? cmdValue, string? envVariableName)
    {
        if (cmdValue.HasValue)
        {
            return cmdValue.Value;
        }

        if (!string.IsNullOrWhiteSpace(envVariableName))
        {
            var envValue = Environment.GetEnvironmentVariable(envVariableName);
            if (!string.IsNullOrWhiteSpace(envValue) && bool.TryParse(envValue.Trim(), out var parsed))
            {
                return parsed;
            }
        }

        return cfgValue ?? false;
    }

    public static string GetString(string? cfgValue, string? cmdValue, string? envVariableName)
    {
        if (!string.IsNullOrWhiteSpace(cmdValue))
        {
            return cmdValue;
        }

        if (!string.IsNullOrWhiteSpace(envVariableName))
        {
            var envValue = Environment.GetEnvironmentVariable(envVariableName);
            if (!string.IsNullOrWhiteSpace(envValue))
            {
                return envValue;
            }
        }

        return cfgValue ?? string.Empty;
    }
}

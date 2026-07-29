using System.ComponentModel;
using System.Reflection;

namespace AIBridgeCLI;

public class ParsedArgs
{
    /// <summary>
    /// The command name (first positional argument, e.g. "GameObjectCommand_Find")
    /// </summary>
    public string CommandName { get; set; }

    public Dictionary<string, string> Options { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // Global options as strongly typed properties
    [Description("help")]
    public bool Help { get; set; } = false;
    [Description("raw")]
    public bool Raw { get; set; } = false;
    [Description("quiet")]
    public bool Quiet { get; set; } = false;
    [Description("stdin")]
    public bool Stdin { get; set; } = false;
    [Description("no-wait")]
    public bool NoWait { get; set; } = false;
    [Description("timeout")]
    public int Timeout { get; set; } = CliConstants.DEFAULT_TIMEOUT;
    public OutputMode OutputMode
    {
        get
        {
            if (Raw) return OutputMode.Raw;
            if (Quiet) return OutputMode.Quiet;
            return OutputMode.Pretty;
        }
    }

    // Reflection-based property cache for global options
    private static readonly Dictionary<string, PropertyInfo> GlobalOptionProperties;

    static ParsedArgs()
    {
        GlobalOptionProperties = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
        var properties = typeof(ParsedArgs).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var prop in properties)
        {
            var desc = prop.GetCustomAttribute<DescriptionAttribute>();
            if (desc == null) continue;
            GlobalOptionProperties[desc.Description] = prop;
        }
    }

    /// <summary>
    /// Try to set a global option value by property name.
    /// Returns true if the property was found and set, false otherwise.
    /// </summary>
    public bool TrySetGlobalOption(string key, string value)
    {
        key = key.ToLower();
        if (!GlobalOptionProperties.TryGetValue(key, out var prop))
            return false;
        prop.SetValue(this, Convert.ChangeType(value, prop.PropertyType));
        Options[key] = value;
        return true;
    }

    public bool GetBool(string key)
    {
        return Options.TryGetValue(key, out var value) &&
               (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1");
    }

    public int GetInt(string key, int defaultValue)
    {
        if (Options.TryGetValue(key, out var value) && int.TryParse(value, out var intValue))
            return intValue;
        return defaultValue;
    }

    public static ParsedArgs Parse(string[] args)
    {
        var result = new ParsedArgs();

        var i = 0;
        while (i < args.Length)
        {
            var arg = args[i];

            if (arg.StartsWith("--"))
            {
                var key = arg.Substring(2);

                if (result.IsBooleanGlobalOption(key))
                {
                    result.TrySetGlobalOption(key, "true");
                    i++;
                    continue;
                }

                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                {
                    if (result.TrySetGlobalOption(key, args[i + 1]))
                    {
                        i += 2;
                        continue;
                    }
                    result.Options[key] = args[i + 1];
                    i += 2;
                }
                else
                {
                    if (result.TrySetGlobalOption(key, "true"))
                    {
                        i++;
                        continue;
                    }
                    result.Options[key] = "true";
                    i++;
                }
            }
            else if (arg.StartsWith("-"))
            {
                if (arg == "-h")
                {
                    result.TrySetGlobalOption("help", "true");
                    i++;
                    continue;
                }

                throw new ArgumentException($"Short form arguments not supported: {arg}");
            }
            else
            {
                // First positional argument is the command name
                if (result.CommandName == null)
                {
                    result.CommandName = arg;
                }
                else
                {
                    throw new ArgumentException($"Unexpected positional argument: {arg}. Use --key value format for parameters.");
                }
                i++;
            }
        }

        return result;
    }

    private bool IsBooleanGlobalOption(string key)
    {
        if (!GlobalOptionProperties.TryGetValue(key, out var property))
        {
            return false;
        }

        return property.PropertyType == typeof(bool);
    }
}
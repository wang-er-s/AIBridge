using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AIBridgeCLI;

/// <summary>
/// Output formatting options
/// </summary>
public enum OutputMode
{
    /// <summary>
    /// Human-readable formatted output
    /// </summary>
    Pretty,

    /// <summary>
    /// Raw JSON output (single line)
    /// </summary>
    Raw,

    /// <summary>
    /// Quiet mode - only output essential info
    /// </summary>
    Quiet
}

/// <summary>
/// Handles output formatting
/// </summary>
public static class OutputFormatter
{
    /// <summary>
    /// Format and print a command result
    /// </summary>
    public static void PrintResult(CommandResult result, OutputMode mode)
    {
        switch (mode)
        {
            case OutputMode.Raw:
                PrintRaw(result);
                break;
            case OutputMode.Quiet:
                PrintQuiet(result);
                break;
            case OutputMode.Pretty:
            default:
                PrintPretty(result);
                break;
        }
    }

    public static void PrintHelpResult(CommandResult result, OutputMode mode)
    {
        if (!result.success || mode == OutputMode.Raw || mode == OutputMode.Quiet)
        {
            PrintResult(result, mode);
            return;
        }

        var data = result.data as JObject;
        if (data?["commands"] is JArray categories)
        {
            Console.WriteLine("Commands:");
            foreach (var category in categories)
            {
                Console.WriteLine();
                Console.WriteLine($"  {category["category"]}");
                if (category["commands"] is not JArray commands)
                {
                    continue;
                }

                foreach (var command in commands)
                {
                    Console.WriteLine($"    {command["name"],-40} {command["description"]}");
                }
            }
            return;
        }

        if (data?["command"] is JObject commandDetails)
        {
            Console.WriteLine(commandDetails["name"]);
            Console.WriteLine($"  {commandDetails["description"]}");
            Console.WriteLine();
            Console.WriteLine($"Usage: {commandDetails["usage"]}");

            if (commandDetails["parameters"] is JArray parameters && parameters.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Options:");
                foreach (var parameter in parameters)
                {
                    var required = parameter["required"]?.Value<bool>() == true ? "required" : "optional";
                    var defaultValue = parameter["defaultValue"]?.Type == JTokenType.Null
                        ? string.Empty
                        : $" default={parameter["defaultValue"]}";
                    Console.WriteLine(
                        $"  --{parameter["name"],-24} {parameter["type"],-8} {required}{defaultValue}");
                    Console.WriteLine($"      {parameter["description"]}");
                }
            }

            if (!string.IsNullOrWhiteSpace(commandDetails["example"]?.ToString()))
            {
                Console.WriteLine();
                Console.WriteLine("Example:");
                Console.WriteLine($"  {commandDetails["example"]}");
            }
            return;
        }

        PrintResult(result, mode);
    }

    private static void PrintRaw(CommandResult result)
    {
        var json = JsonConvert.SerializeObject(result, Formatting.None);
        Console.WriteLine(json);
    }

    private static void PrintQuiet(CommandResult result)
    {
        if (result.success)
        {
            if (result.data != null)
            {
                var json = JsonConvert.SerializeObject(result.data, Formatting.None);
                Console.WriteLine(json);
            }
        }
        else
        {
            Console.Error.WriteLine(result.error ?? "Command failed");
        }
    }

    private static void PrintPretty(CommandResult result)
    {
        if (result.success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("✓ ");
            Console.ResetColor();
            Console.WriteLine($"Command executed successfully ({result.executionTime}ms)");

            if (result.data != null)
            {
                PrintData(result.data, "  ");
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("✗ ");
            Console.ResetColor();
            Console.WriteLine($"Command failed ({result.executionTime}ms)");

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  Error: {result.error}");
            Console.ResetColor();
        }
    }

    private static void PrintData(object data, string indent)
    {
        if (data == null) return;

        // Convert to JToken for easier traversal
        JToken token;
        if (data is JToken jt)
        {
            token = jt;
        }
        else
        {
            var json = JsonConvert.SerializeObject(data);
            token = JToken.Parse(json);
        }

        PrintJToken(token, indent);
    }

    private static void PrintJToken(JToken token, string indent)
    {
        switch (token.Type)
        {
            case JTokenType.Object:
                foreach (var prop in ((JObject)token).Properties())
                {
                    if (prop.Value.Type == JTokenType.Object || prop.Value.Type == JTokenType.Array)
                    {
                        Console.WriteLine($"{indent}{prop.Name}:");
                        PrintJToken(prop.Value, indent + "  ");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.Write($"{indent}{prop.Name}: ");
                        Console.ResetColor();
                        PrintValue(prop.Value);
                    }
                }
                break;

            case JTokenType.Array:
                var index = 0;
                foreach (var item in (JArray)token)
                {
                    if (item.Type == JTokenType.Object || item.Type == JTokenType.Array)
                    {
                        Console.WriteLine($"{indent}[{index}]:");
                        PrintJToken(item, indent + "  ");
                    }
                    else
                    {
                        Console.Write($"{indent}[{index}]: ");
                        PrintValue(item);
                    }
                    index++;
                }
                break;

            default:
                Console.Write(indent);
                PrintValue(token);
                break;
        }
    }

    private static void PrintValue(JToken token)
    {
        switch (token.Type)
        {
            case JTokenType.Boolean:
                Console.ForegroundColor = token.Value<bool>() ? ConsoleColor.Green : ConsoleColor.Red;
                Console.WriteLine(token.ToString().ToLower());
                Console.ResetColor();
                break;

            case JTokenType.Null:
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("null");
                Console.ResetColor();
                break;

            case JTokenType.Integer:
            case JTokenType.Float:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(token.ToString());
                Console.ResetColor();
                break;

            case JTokenType.String:
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(token.ToString());
                Console.ResetColor();
                break;

            default:
                Console.WriteLine(token.ToString());
                break;
        }
    }

    /// <summary>
    /// Print an error message
    /// </summary>
    public static void PrintError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"Error: {message}");
        Console.ResetColor();
    }

    /// <summary>
    /// Print a warning message
    /// </summary>
    public static void PrintWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Error.WriteLine($"Warning: {message}");
        Console.ResetColor();
    }

    /// <summary>
    /// Print an info message
    /// </summary>
    public static void PrintInfo(string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>
    /// Print a success message
    /// </summary>
    public static void PrintSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("✓ ");
        Console.ResetColor();
        Console.WriteLine(message);
    }
}
using System.Text;
using AIBridgeCLI.Commands;
using Newtonsoft.Json;

namespace AIBridgeCLI;

public class Program
{
    private const string CommandsCommandName = "Commands";

    public static int Main(string[] args)
    {
        // Set console output encoding to UTF-8
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            OutputFormatter.PrintError(ex.Message);
            return 1;
        }
    }

    static int Run(string[] args)
    {
        var parsed = ParsedArgs.Parse(args);
            
        if (string.IsNullOrEmpty(parsed.CommandName))
        {
            Console.WriteLine(HelpProvider.GetGlobalHelp());
            return parsed.Help ? 0 : 1;
        }

        if (parsed.Help)
        {
            return RunCommandHelp(parsed);
        }

        if (parsed.CommandName == "Compile")
        {
            var compileResult = CompileUnityCommand.Compile();
            OutputFormatter.PrintResult(compileResult, parsed.OutputMode);
            return compileResult.success ? 0 : 1;
        }

        // Handle stdin input
        if (parsed.Stdin)
        {
            var stdinJson = Console.In.ReadToEnd();
            if (!string.IsNullOrWhiteSpace(stdinJson))
            {
                try
                {
                    var stdinParams = JsonConvert.DeserializeObject<Dictionary<string, string>>(stdinJson);
                    foreach (var kvp in stdinParams)
                    {
                        if (!parsed.Options.ContainsKey(kvp.Key))
                            parsed.Options[kvp.Key] = kvp.Value;
                    }
                }
                catch
                {
                    parsed.Options["json"] = stdinJson;
                }
            }
        }

        // Build request
        CommandRequest request;
        try
        {
            request = RequestBuilder.BuildRequest(parsed);
        }
        catch (ArgumentException ex)
        {
            OutputFormatter.PrintError(ex.Message);
            return 1;
        }

        var sender = new CommandSender(parsed.Timeout);

        if (parsed.NoWait)
        {
            var commandId = sender.SendCommandNoWait(request);
            if (parsed.OutputMode == OutputMode.Pretty)
                OutputFormatter.PrintInfo($"Command sent with ID: {commandId}");
            else
                Console.WriteLine(JsonConvert.SerializeObject(new { id = commandId, status = "sent" }));
            return 0;
        }

        var result = sender.SendCommand(request);
        if (string.Equals(parsed.CommandName, CommandsCommandName, StringComparison.OrdinalIgnoreCase))
            OutputFormatter.PrintHelpResult(result, parsed.OutputMode);
        else
            OutputFormatter.PrintResult(result, parsed.OutputMode);
        return result.success ? 0 : 1;
    }

    private static int RunCommandHelp(ParsedArgs parsed)
    {
        var commandName = parsed.CommandName;
        if (string.Equals(commandName, CommandsCommandName, StringComparison.OrdinalIgnoreCase) &&
            parsed.Options.TryGetValue("command", out var requestedCommand) &&
            !string.IsNullOrWhiteSpace(requestedCommand))
        {
            commandName = requestedCommand;
        }

        var request = new CommandRequest
        {
            id = PathHelper.GenerateCommandId(),
            type = CommandsCommandName,
            @params = new Dictionary<string, object>
            {
                { "command", commandName }
            }
        };

        var result = new CommandSender(parsed.Timeout).SendCommand(request);
        OutputFormatter.PrintHelpResult(result, parsed.OutputMode);
        return result.success ? 0 : 1;
    }


    static int Test(string[] args, out CommandRequest request)
    {
        var parsed = ParsedArgs.Parse(args);
        request = null;

        if (string.Equals(parsed.CommandName, "focus", StringComparison.OrdinalIgnoreCase))
            return 0;

        if (string.IsNullOrEmpty(parsed.CommandName))
        {
            Console.WriteLine(HelpProvider.GetGlobalHelp());
            return parsed.Help ? 0 : 1;
        }

        if (parsed.Help)
        {
            request = new CommandRequest
            {
                id = PathHelper.GenerateCommandId(),
                type = CommandsCommandName,
                @params = new Dictionary<string, object>
                {
                    { "command", parsed.CommandName }
                }
            };
            return 0;
        }

        try
        {
            request = RequestBuilder.BuildRequest(parsed);
        }
        catch (ArgumentException ex)
        {
            OutputFormatter.PrintError(ex.Message);
            return 1;
        }

        return 0;
    }
}
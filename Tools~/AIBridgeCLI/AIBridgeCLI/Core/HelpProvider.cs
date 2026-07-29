using System.Text;

namespace AIBridgeCLI;

public static class HelpProvider
{
    public static string GetGlobalHelp()
    {
        var sb = new StringBuilder();
        sb.AppendLine("AIBridgeCLI - Unity command forwarder");
        sb.AppendLine();
        sb.AppendLine("Usage:");
        sb.AppendLine("  AIBridgeCLI --help");
        sb.AppendLine("  AIBridgeCLI Commands");
        sb.AppendLine("  AIBridgeCLI <CommandName> --help");
        sb.AppendLine("  AIBridgeCLI <CommandName> [options]");
        sb.AppendLine();
        sb.AppendLine("Global Options:");
        sb.AppendLine("  --timeout <ms>     Timeout in milliseconds (default: 5000)");
        sb.AppendLine("  --no-wait          Don't wait for result");
        sb.AppendLine("  --raw              Output raw JSON");
        sb.AppendLine("  --quiet            Quiet mode");
        sb.AppendLine("  --json <json>      Merge JSON object into forwarded params (overrides same keys)");
        sb.AppendLine("  --stdin            Read params JSON from stdin");
        sb.AppendLine("  --help, -h         Show help without executing a command");
        sb.AppendLine();
        sb.AppendLine("Examples:");
        sb.AppendLine("  AIBridgeCLI Commands");
        sb.AppendLine("  AIBridgeCLI InputSimulationCommand_Click --help");
        sb.AppendLine("  AIBridgeCLI EditorCommand_Play");
        sb.AppendLine();
        sb.AppendLine("Use `AIBridgeCLI Commands --command <CommandName>` for machine-readable command metadata.");

        return sb.ToString();
    }
}
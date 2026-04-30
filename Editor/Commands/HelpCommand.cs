using System.Collections;
using System.ComponentModel;
using System.Linq;

namespace AIBridge.Editor
{
    public static class HelpCommand
    {
        [AIBridge("获取特定命令的详细信息",
            @"
AIBridgeCLI Help --command GameObjectCommand_Find  # 获取Find命令的详细信息",
            "Help")]
        public static IEnumerator Help(
            [Description("要获取详细帮助的命令名称")] string command)
        {
            if (string.IsNullOrEmpty(command))
            {
                var all = CommandRegistry.GetAll()
                    .OrderBy(e => e.Name)
                    .Select(e => new { e.Name, e.Description });
                yield return CommandResult.Success(new { count = CommandRegistry.GetAll().Count(), commands = all });
            }
            else
            {
                if (command == "Compile")
                {
                    yield return CommandResult.Success($"name:Compile\nDescription:编译项目并返回编译结果，如果错误为0且状态为idle则表明编译完成且没有错误");
                }
                if (!CommandRegistry.TryGetCommand(command, out var entry))
                {
                    yield return CommandResult.Failure($"Command '{command}' not found");
                }
                yield return CommandResult.Success(BuildDetail(entry));
            }
        }

        private static object BuildDetail(CommandEntry entry)
        {
            var parameters = entry.Parameters.Select(p => new
            {
                name = p.Name,
                type = entry.GetTypeName(p),
                required = entry.IsRequired(p),
                description = entry.GetParamDescription(p),
                defaultValue = p.HasDefaultValue ? p.DefaultValue?.ToString() : null
            });

            return new
            {
                entry.Name,
                entry.Description,
                entry.Example,
                parameters
            };
        }
    }
}

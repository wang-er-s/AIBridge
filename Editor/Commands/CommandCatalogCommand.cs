using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using AIBridge.Runtime;

namespace AIBridge.Editor
{
    public static class CommandCatalogCommand
    {
        [AIBridge(
            "列出可用命令，或获取指定命令的详细用法",
            "AIBridgeCLI Commands",
            "Commands",
            exposeToSkill: false)]
        public static IEnumerator List(
            [Description("要查看详细用法的命令名；留空时列出全部命令")]
            string command = null)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                var categories = CommandRegistry.GetAll()
                    .Where(entry => entry.Attribute.ExposeToSkill)
                    .GroupBy(entry => entry.GetCategoryName())
                    .OrderBy(group => group.Key)
                    .Select(group => new
                    {
                        category = group.Key,
                        commands = group
                            .OrderBy(entry => entry.Name)
                            .Select(CreateSummary)
                            .ToArray()
                    })
                    .ToArray();

                var commandGroups = new List<object>
                {
                    new
                    {
                        category = "Built-in",
                        commands = new object[]
                        {
                            new
                            {
                                name = "Compile",
                                description = "编译代码，并返回编译结果"
                            }
                        }
                    }
                };
                commandGroups.AddRange(categories.Cast<object>());
                yield return CommandResult.Success(new { commands = commandGroups });
                yield break;
            }

            if (string.Equals(command, "Compile", System.StringComparison.OrdinalIgnoreCase))
            {
                yield return CommandResult.Success(new
                {
                    command = new
                    {
                        name = "Compile",
                        category = "Built-in",
                        description = "编译代码，并返回编译结果",
                        usage = "AIBridgeCLI Compile",
                        example = "AIBridgeCLI Compile",
                        parameters = new object[0]
                    }
                });
                yield break;
            }

            if (!CommandRegistry.TryGetCommand(command, out var entry) ||
                !entry.Attribute.ExposeToSkill)
            {
                yield return CommandResult.Failure($"Unknown command: {command}");
                yield break;
            }

            yield return CommandResult.Success(new { command = CreateDetails(entry) });
        }

        private static object CreateSummary(CommandEntry entry)
        {
            return new
            {
                name = entry.Name,
                description = entry.Description
            };
        }

        private static object CreateDetails(CommandEntry entry)
        {
            return new
            {
                name = entry.Name,
                category = entry.GetCategoryName(),
                description = entry.Description,
                usage = $"AIBridgeCLI {entry.Name} [options]",
                example = entry.Example,
                parameters = entry.Parameters.Select(parameter => new
                {
                    name = parameter.Name,
                    type = entry.GetTypeName(parameter),
                    required = entry.IsRequired(parameter),
                    defaultValue = parameter.HasDefaultValue ? parameter.DefaultValue : null,
                    description = entry.GetParamDescription(parameter)
                }).ToArray()
            };
        }
    }
}

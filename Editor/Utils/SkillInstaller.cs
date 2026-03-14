using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AIBridge.Editor
{
    /// <summary>
    /// Automatically installs the AIBridge skill documentation to the project's .agent directory.
    /// This allows AI assistants to discover and use the skill for Unity Editor operations.
    /// </summary>
    public static class SkillInstaller
    {
        private const string SkillFileName = "SKILL.md";
        private static readonly string[] AIDirectories = { ".cursor", ".agent", ".factory", ".claude", ".codex" };
        private static string SkillSourceFile => Path.Combine(AIBridge.PackageRoot, "Skill~", SkillFileName);
        private static string AgentSkillFilePath(string agentName) => Path.Combine(AIBridge.ProjectRoot,agentName, "skills","aibridge", SkillFileName);

        /// <summary>
        /// Install skill to AI assistant directories
        /// </summary>
        public static void CopyToAgent()
        {
            if (!File.Exists(SkillSourceFile))
            {
                throw new FileNotFoundException($"Source SKILL.md not found at: {SkillSourceFile}");
            }

            bool foundAnyDir = false;

            foreach (var dirName in AIDirectories)
            {
                if (!Directory.Exists(Path.Combine(AIBridge.ProjectRoot, dirName))) continue;
                foundAnyDir = true;
                var targetSkillPath = AgentSkillFilePath(dirName);

                if (!Directory.Exists(Path.GetDirectoryName(targetSkillPath)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetSkillPath)!);
                }

                File.Copy(SkillSourceFile, targetSkillPath, true);
                Debug.Log($"[AIBridge] Skill copied to {targetSkillPath}");
            }

            if (!foundAnyDir)
            {
                var targetSkillPath = AgentSkillFilePath(".agent");
                if (!Directory.Exists(Path.GetDirectoryName(targetSkillPath)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetSkillPath)!);
                }

                File.Copy(SkillSourceFile, targetSkillPath, true);
                Debug.Log($"[AIBridge] No AI directories found, created .agent and copied skill: {targetSkillPath}");
            }
        }
        
        /// <summary>
        /// Override/update existing AIBridge skill installations
        /// </summary>
        public static void OverrideSkill()
        {
            if (!File.Exists(SkillSourceFile))
            {
                throw new FileNotFoundException($"Source SKILL.md not found at: {SkillSourceFile}");
            }

            bool foundAny = false;

            foreach (var dirName in AIDirectories)
            {
                var targetSkillPath = AgentSkillFilePath(dirName);
                if (!File.Exists(targetSkillPath)) continue;
                File.Copy(SkillSourceFile, targetSkillPath, true);
                Debug.Log($"[AIBridge] Skill updated in :{targetSkillPath}");
                foundAny = true;
            }

            if (!foundAny)
            {
                Debug.Log("[AIBridge] No existing AIBridge skill found, skipping override.");
            }
        }

        public static void GenerateSkillFile()
        {
            var entries = CommandRegistry.GetAll().ToList();
            if (entries.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "No commands registered. Please scan assemblies first.", "OK");
                return;
            }

            var commandsByClass = entries.GroupBy(e => e.Method.DeclaringType.Name)
                .OrderBy(g => g.Key);

            var commandSections = commandsByClass.Select(group =>
            {
                var sb = new StringBuilder();
                sb.AppendLine($"### {group.Key.Replace("Command", "")}");

                foreach (var entry in group.OrderBy(e => e.Name))
                {
                    var desc = entry.Description ?? "No description";
                    var example = entry.Example ?? $"AIBridgeCLI {entry.Name}";

                    sb.AppendLine($"- **{entry.Name}**: {desc}");
                    sb.AppendLine($"  - Example: `{example}`");
                }

                return sb.ToString();
            });

            var skillContent = $@"---
name: aibridge
description: Automate Unity Editor operations via AI Bridge CLI - manage GameObjects, scenes, assets, prefabs, components, execute C# code, capture screenshots, and control playmode. Use when user needs to interact with Unity project programmatically.
---

# AI Bridge Unity Skill

Control Unity Editor through AI Bridge CLI.

## Installation

1. Ensure Unity project has AI Bridge package installed
2. CLI is located at: `{AIBridge.BridgeCLI}`

## Usage

```bash
AIBridgeCLI <CommandName> [--param value] [--raw]
```

### Common Flags
| Flag | Description |
|------|-------------|
| `--raw` | Output raw JSON (recommended for AI) |
| `--stdin` | Read parameters from stdin (JSON format) |
| `--help` | Show help |

**AI Usage:** Always add `--raw` for JSON output.

---

## Command Reference

{string.Join("\n\n", commandSections)}

**Skill Version**: 1.0
";

            var skillPath = Path.Combine(AIBridge.PackageRoot, "Skill~", SkillFileName);
            var dir = Path.GetDirectoryName(skillPath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(skillPath, skillContent);
            AssetDatabase.Refresh();

            Debug.Log($"[AIBridge] Generated SKILL.md with {entries.Count} commands at: {skillPath}");
            EditorUtility.DisplayDialog("Success",
                $"Generated SKILL.md with {entries.Count} commands.", "OK");
        }
    }
}
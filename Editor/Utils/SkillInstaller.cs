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
        private static readonly string[] AIDirectories = { ".agents", ".cursor", ".factory", ".claude", ".codex" };
        private static string SkillSourceFile => Path.Combine(AIBridge.PackageRoot, "Skill~", SkillFileName);
        private static string AgentSkillDir(string agentName) => Path.Combine(AIBridge.ProjectRoot, agentName, "skills", "aibridge");
        private static string AgentSkillFilePath(string agentName) => Path.Combine(AgentSkillDir(agentName), SkillFileName);

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
                
                var targetDir = AgentSkillDir(dirName);
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                File.Copy(SkillSourceFile, AgentSkillFilePath(dirName), true);
                Debug.Log($"[AIBridge] Skill file copied to {targetDir}");
            }

            if (!foundAnyDir)
            {
                var targetDir = AgentSkillDir(AIDirectories[0]);
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                File.Copy(SkillSourceFile, AgentSkillFilePath(AIDirectories[0]), true);
                Debug.Log($"[AIBridge] No AI directories found, created {AIDirectories[0]} and copied skill file: {targetDir}");
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
                Debug.Log($"[AIBridge] Skill file updated in: {AgentSkillDir(dirName)}");
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

            var skillDir = Path.Combine(AIBridge.PackageRoot, "Skill~");
            if (!Directory.Exists(skillDir))
            {
                Directory.CreateDirectory(skillDir);
            }

            var skillPath = Path.Combine(skillDir, SkillFileName);
            if (!File.Exists(skillPath))
            {
                EditorUtility.DisplayDialog("Error", 
                    $"SKILL.md not found at: {skillPath}\n\nPlease create it manually first.", 
                    "OK");
                return;
            }

            // Update command categories in SKILL.md
            UpdateSkillCommandCategories(skillPath, entries);

            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Success",
                $"Updated SKILL.md with {entries.Count} commands.\n\n" +
                $"Command categories section has been regenerated.\n\n" +
                $"Location: {skillDir}",
                "OK");
        }

        private static void UpdateSkillCommandCategories(string skillPath, System.Collections.Generic.List<CommandEntry> entries)
        {
            var content = File.ReadAllText(skillPath);
            
            const string startMarker = "<!-- AUTO-GENERATED-COMMANDS-START -->";
            const string endMarker = "<!-- AUTO-GENERATED-COMMANDS-END -->";
            
            var startIndex = content.IndexOf(startMarker);
            var endIndex = content.IndexOf(endMarker);
            
            if (startIndex < 0 || endIndex < 0)
            {
                Debug.LogWarning("[AIBridge] SKILL.md missing AUTO-GENERATED markers. Command categories not updated.");
                return;
            }

            // Generate command categories
            var commandsByClass = entries.Where(e=> e.Attribute.ExposeToSkill).GroupBy(e => e.Method.DeclaringType.Name)
                .OrderBy(g => g.Key);

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("## 命令分类");
            sb.AppendLine();

            sb.AppendLine($"-- **Compile** - 编译代码，并返回编译结果，如果有报错是会直接返回，不需要再查看Log");
            sb.AppendLine();

            foreach (var group in commandsByClass)
            {
                var categoryName = group.Key.Replace("Command", "");
                sb.AppendLine($"### {categoryName}");
                sb.AppendLine();

                foreach (var entry in group.OrderBy(e => e.Name))
                {
                    var desc = entry.Description ?? "无描述";
                    sb.AppendLine($"- **{entry.Name}** - {desc}");
                }
                sb.AppendLine();
            }

            // Replace content between markers
            var before = content.Substring(0, startIndex + startMarker.Length);
            var after = content.Substring(endIndex);
            var newContent = before + sb.ToString() + after;

            File.WriteAllText(skillPath, newContent);
            Debug.Log($"[AIBridge] Updated command categories in SKILL.md with {entries.Count} commands");
        }
    }
}

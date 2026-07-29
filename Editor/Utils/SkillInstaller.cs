using System;
using System.IO;
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
        
    }
}

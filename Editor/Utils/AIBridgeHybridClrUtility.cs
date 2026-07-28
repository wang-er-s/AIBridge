using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor.PackageManager;
using UnityEngine;

namespace AIBridge.Editor
{
    internal static class AIBridgeHybridClrUtility
    {
        public const string PackageName = "com.code-philosophy.hybridclr";
        public const string GitUrl = "https://github.com/focus-creative-games/hybridclr_unity.git";
        private const string PackageAssetPath = "Packages/" + PackageName;

        public static bool IsInstalled()
        {
            try
            {
                if (PackageInfo.FindForAssetPath(PackageAssetPath) != null)
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        public static bool IsDeclaredInManifest()
        {
            return IsDeclaredInManifest(GetManifestPath());
        }

        public static bool Install(out bool changed, out string error)
        {
            return TryInstallToManifest(GetManifestPath(), out changed, out error);
        }

        internal static bool TryInstallToManifest(string manifestPath, out bool changed, out string error)
        {
            changed = false;
            error = null;
            if (!File.Exists(manifestPath))
            {
                error = "Packages/manifest.json was not found.";
                return false;
            }

            try
            {
                var manifest = JObject.Parse(File.ReadAllText(manifestPath));
                var dependencies = manifest["dependencies"] as JObject;
                if (dependencies == null)
                {
                    dependencies = new JObject();
                    manifest["dependencies"] = dependencies;
                }

                var currentValue = dependencies.Value<string>(PackageName);
                if (string.Equals(currentValue, GitUrl, StringComparison.Ordinal))
                {
                    return true;
                }

                dependencies[PackageName] = GitUrl;
                var json = manifest.ToString(Formatting.Indented) + Environment.NewLine;
                var tempPath = manifestPath + ".aibridge.tmp";
                try
                {
                    File.WriteAllText(tempPath, json, new UTF8Encoding(false));
                    File.Copy(tempPath, manifestPath, true);
                }
                finally
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }

                changed = true;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static bool IsDeclaredInManifest(string manifestPath)
        {
            if (!File.Exists(manifestPath))
            {
                return false;
            }

            try
            {
                var manifest = JObject.Parse(File.ReadAllText(manifestPath));
                var dependencies = manifest["dependencies"] as JObject;
                return dependencies != null
                    && string.Equals(
                        dependencies.Value<string>(PackageName),
                        GitUrl,
                        StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static string GetManifestPath()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "manifest.json"));
        }
    }
}

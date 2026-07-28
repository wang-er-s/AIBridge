using System;
using System.IO;
using System.Reflection;
using AIBridge.Runtime;
using NUnit.Framework;

namespace AIBridge.Editor.Tests
{
    public class RuntimeCodeExecutionTests
    {
        [Test]
        public void CompileForRuntime_RuntimeSafeCode_ProducesAssembly()
        {
            var runner = new CSharpCodeRunner(true);

            var result = runner.CompileForRuntime("return UnityEngine.Application.platform.ToString();");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.CompiledAssemblyBytes, Is.Not.Null.And.Not.Empty);
            Assert.That(result.EntryTypeName, Is.EqualTo("CodeExecutor"));
            Assert.That(result.EntryMethodName, Is.EqualTo("Execute"));
        }

        [Test]
        public void CompileForRuntime_UnityEditorCode_IsRejected()
        {
            var runner = new CSharpCodeRunner(true);

            var result = runner.CompileForRuntime(
                "return UnityEditor.Selection.activeObject == null ? null : UnityEditor.Selection.activeObject.name;");

            Assert.That(result.Success, Is.False);
        }

        [Test]
        public void RuntimeProtocol_UsesSelfSpecificEndpoints()
        {
            Assert.That(AIBridgeRuntimeProtocol.HealthPath, Is.EqualTo("/aibridge-self/health"));
            Assert.That(AIBridgeRuntimeProtocol.CodeExecutePath, Is.EqualTo("/aibridge-self/code/execute"));
            Assert.That(AIBridgeRuntimeProtocol.CodeExecuteAction, Is.EqualTo("aibridge-self.code.execute"));
        }

        [Test]
        public void CommandRegistry_BuiltInCommandsAreRegisteredExplicitly()
        {
            Assert.That(
                CommandRegistry.TryGetCommand("EditorCommand_Play", out var entry),
                Is.True);
            Assert.That(entry.Method, Is.EqualTo(typeof(EditorCommand).GetMethod(nameof(EditorCommand.Play))));
            Assert.That(
                typeof(CommandRegistry).GetMethod("Scan", BindingFlags.Public | BindingFlags.Static),
                Is.Null);
        }

        [Test]
        public void CommandRegistry_RegisterExistingMethod_IsIdempotent()
        {
            var first = CommandRegistry.Register(typeof(EditorCommand), nameof(EditorCommand.Play));
            var second = CommandRegistry.Register(first.Method);

            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void HybridClrInstaller_AddsGitDependencyAndPreservesManifest()
        {
            var directory = Path.Combine(Path.GetTempPath(), "AIBridgeSelfTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var manifestPath = Path.Combine(directory, "manifest.json");
            File.WriteAllText(
                manifestPath,
                "{\"dependencies\":{\"com.unity.test-framework\":\"1.1.33\"},\"testables\":[\"example\"]}");

            try
            {
                var utilityType = typeof(AIBridgeSettingsWindow).Assembly.GetType(
                    "AIBridge.Editor.AIBridgeHybridClrUtility",
                    true);
                var installMethod = utilityType.GetMethod(
                    "TryInstallToManifest",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var args = new object[] { manifestPath, false, null };

                var success = (bool)installMethod.Invoke(null, args);
                var manifest = File.ReadAllText(manifestPath);

                Assert.That(success, Is.True, args[2] as string);
                Assert.That((bool)args[1], Is.True);
                Assert.That(manifest, Does.Contain("\"com.unity.test-framework\": \"1.1.33\""));
                Assert.That(
                    manifest,
                    Does.Contain(
                        "\"com.code-philosophy.hybridclr\": \"https://github.com/focus-creative-games/hybridclr_unity.git\""));
                Assert.That(manifest, Does.Contain("\"testables\""));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}

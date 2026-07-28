using System;
using System.Collections.Generic;
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
            Assert.That(AIBridgeRuntimeProtocol.CommandExecutePath, Is.EqualTo("/aibridge-self/command/execute"));
            Assert.That(AIBridgeRuntimeProtocol.ArtifactPathPrefix, Is.EqualTo("/aibridge-self/artifacts/"));
            Assert.That(AIBridgeRuntimeProtocol.CodeExecuteAction, Is.EqualTo("aibridge-self.code.execute"));
            Assert.That(AIBridgeRuntimeProtocol.CommandExecuteAction, Is.EqualTo("aibridge-self.command.execute"));
        }

        [Test]
        public void RuntimeProtocol_NormalizesExecutionTimeout()
        {
            Assert.That(
                AIBridgeRuntimeProtocol.NormalizeTimeoutMs(0),
                Is.EqualTo(AIBridgeRuntimeProtocol.DefaultExecutionTimeoutMs));
            Assert.That(
                AIBridgeRuntimeProtocol.NormalizeTimeoutMs(1),
                Is.EqualTo(AIBridgeRuntimeProtocol.MinExecutionTimeoutMs));
            Assert.That(
                AIBridgeRuntimeProtocol.NormalizeTimeoutMs(int.MaxValue),
                Is.EqualTo(AIBridgeRuntimeProtocol.MaxExecutionTimeoutMs));
        }

        [Test]
        public void PackageRoot_UsesResolvedPackageLocation()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(AIBridge).Assembly);

            Assert.That(packageInfo, Is.Not.Null);
            Assert.That(
                Path.GetFullPath(AIBridge.PackageRoot),
                Is.EqualTo(Path.GetFullPath(packageInfo.resolvedPath)));
            Assert.That(
                File.Exists(Path.Combine(AIBridge.PackageRoot, "Skill~", "SKILL.md")),
                Is.True);
        }

        [Test]
        public void RuntimeProtocol_CommandRequest_PreservesRoutingFields()
        {
            var request = new AIBridgeSelfCommandExecuteRequest
            {
                id = "request-id",
                action = AIBridgeRuntimeProtocol.CommandExecuteAction,
                type = "EditorCommand_Log",
                parameters = new Dictionary<string, object>
                {
                    { "message", "Hello Player" },
                    { "logType", "Warning" }
                },
                timeoutMs = 12345
            };

            Assert.That(request.id, Is.EqualTo("request-id"));
            Assert.That(request.action, Is.EqualTo("aibridge-self.command.execute"));
            Assert.That(request.type, Is.EqualTo("EditorCommand_Log"));
            Assert.That(request.parameters["message"], Is.EqualTo("Hello Player"));
            Assert.That(request.parameters["logType"], Is.EqualTo("Warning"));
            Assert.That(request.timeoutMs, Is.EqualTo(12345));
        }

        [TestCase("EditorCommand_Log")]
        [TestCase("Log")]
        [TestCase("GetLogsCommand_StartCapture")]
        [TestCase("GetLogsCommand_StopCapture")]
        [TestCase("InputSimulationCommand_Click")]
        [TestCase("InputSimulationCommand_ClickByInstanceId")]
        [TestCase("InputSimulationCommand_ClickAt")]
        [TestCase("InputSimulationCommand_Drag")]
        [TestCase("InputSimulationCommand_DragByInstanceId")]
        [TestCase("InputSimulationCommand_LongPress")]
        [TestCase("InputSimulationCommand_LongPressByInstanceId")]
        [TestCase("ScreenshotCommand_Image")]
        [TestCase("ScreenshotCommand_Gif")]
        public void RuntimeCommandRegistry_MigratedCommandsAreRegistered(string command)
        {
            Assert.That(AIBridgeRuntimeCommandRegistry.TryGet(command, out var handler), Is.True);
            Assert.That(handler, Is.Not.Null);
        }

        [Test]
        public void RuntimeCommandParameters_AreCaseInsensitiveAndConvertNumbers()
        {
            var getter = new AIBridgeRuntimeParameterGetter(new Dictionary<string, object>
            {
                { "COUNT", 25L },
                { "Scale", 0.5d }
            });

            Assert.That(getter.GetInt32("count", 0), Is.EqualTo(25));
            Assert.That(getter.GetSingle("scale", 0f), Is.EqualTo(0.5f));
        }

        [Test]
        public void MigratedEditorCommands_ExposeRuntimeRoutingParameters()
        {
            var methods = new[]
            {
                typeof(EditorCommand).GetMethod(nameof(EditorCommand.Log)),
                typeof(GetLogsCommand).GetMethod(nameof(GetLogsCommand.Log)),
                typeof(GetLogsCommand).GetMethod(nameof(GetLogsCommand.StartCapture)),
                typeof(GetLogsCommand).GetMethod(nameof(GetLogsCommand.StopCapture)),
                typeof(InputSimulationCommand).GetMethod(nameof(InputSimulationCommand.Click)),
                typeof(InputSimulationCommand).GetMethod(nameof(InputSimulationCommand.ClickByInstanceId)),
                typeof(InputSimulationCommand).GetMethod(nameof(InputSimulationCommand.ClickAt)),
                typeof(InputSimulationCommand).GetMethod(nameof(InputSimulationCommand.Drag)),
                typeof(InputSimulationCommand).GetMethod(nameof(InputSimulationCommand.DragByInstanceId)),
                typeof(InputSimulationCommand).GetMethod(nameof(InputSimulationCommand.LongPress)),
                typeof(InputSimulationCommand).GetMethod(nameof(InputSimulationCommand.LongPressByInstanceId)),
                typeof(ScreenshotCommand).GetMethod(nameof(ScreenshotCommand.Image)),
                typeof(ScreenshotCommand).GetMethod(nameof(ScreenshotCommand.Gif))
            };

            foreach (var method in methods)
            {
                Assert.That(method, Is.Not.Null);
                Assert.That(method.GetParameters(), Has.Some.Property("Name").EqualTo("url"));
                Assert.That(method.GetParameters(), Has.Some.Property("Name").EqualTo("runtimeTimeout"));
            }
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

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
            Assert.That(AIBridgeProtocol.HealthPath, Is.EqualTo("/aibridge-self/health"));
            Assert.That(AIBridgeProtocol.CodeExecutePath, Is.EqualTo("/aibridge-self/code/execute"));
            Assert.That(AIBridgeProtocol.CommandExecutePath, Is.EqualTo("/aibridge-self/command/execute"));
            Assert.That(AIBridgeProtocol.ArtifactPathPrefix, Is.EqualTo("/aibridge-self/artifacts/"));
            Assert.That(AIBridgeProtocol.CodeExecuteAction, Is.EqualTo("aibridge-self.code.execute"));
            Assert.That(AIBridgeProtocol.CommandExecuteAction, Is.EqualTo("aibridge-self.command.execute"));
        }

        [Test]
        public void RuntimeProtocol_NormalizesExecutionTimeout()
        {
            Assert.That(
                AIBridgeProtocol.NormalizeTimeoutMs(0),
                Is.EqualTo(AIBridgeProtocol.DefaultExecutionTimeoutMs));
            Assert.That(
                AIBridgeProtocol.NormalizeTimeoutMs(1),
                Is.EqualTo(AIBridgeProtocol.MinExecutionTimeoutMs));
            Assert.That(
                AIBridgeProtocol.NormalizeTimeoutMs(int.MaxValue),
                Is.EqualTo(AIBridgeProtocol.MaxExecutionTimeoutMs));
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
                action = AIBridgeProtocol.CommandExecuteAction,
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
        [TestCase("InputSimulationCommand_Drag")]
        [TestCase("InputSimulationCommand_LongPress")]
        [TestCase("ScreenshotCommand_Image")]
        [TestCase("ScreenshotCommand_Gif")]
        public void RuntimeCommandRegistry_MigratedCommandsAreRegistered(string command)
        {
            Assert.That(AIBridgeCommandRegistry.TryGet(command, out var handler), Is.True);
            Assert.That(handler, Is.Not.Null);
        }

        [Test]
        public void RuntimeCommandParameters_AreCaseInsensitiveAndConvertNumbers()
        {
            var getter = new AIBridgeParameterGetter(new Dictionary<string, object>
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
                typeof(InputSimulationCommand).GetMethod(nameof(InputSimulationCommand.Drag)),
                typeof(InputSimulationCommand).GetMethod(nameof(InputSimulationCommand.LongPress)),
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
        public void DragPointParser_AcceptsMixedPathCoordinateAndInstanceIdPoints()
        {
            var rawPoints = new object[]
            {
                new Dictionary<string, object> { { "path", "Canvas/Item" } },
                new Dictionary<string, object>
                {
                    { "path", "Canvas/Duplicate" },
                    { "instanceId", -456L }
                },
                new Dictionary<string, object> { { "x", 480L }, { "y", 720L } },
                new Dictionary<string, object> { { "instanceId", -123L } },
                new Dictionary<string, object> { { "path", "Canvas/Slot" } }
            };

            var success = AIBridgeDragPointParser.TryParse(rawPoints, out var points, out var error);

            Assert.That(success, Is.True, error);
            Assert.That(points, Has.Count.EqualTo(5));
            Assert.That(points[0].Path, Is.EqualTo("Canvas/Item"));
            Assert.That(points[1].Path, Is.EqualTo("Canvas/Duplicate"));
            Assert.That(points[1].InstanceId, Is.EqualTo(-456));
            Assert.That(points[2].Position.Value, Is.EqualTo(new UnityEngine.Vector2(480, 720)));
            Assert.That(points[3].InstanceId, Is.EqualTo(-123));
            Assert.That(points[4].Path, Is.EqualTo("Canvas/Slot"));
        }

        [Test]
        public void RuntimeInputResolver_DuplicatePathRequiresMatchingInstanceId()
        {
            var name = "AIBridgeDuplicate_" + Guid.NewGuid().ToString("N");
            var first = new UnityEngine.GameObject(name);
            var second = new UnityEngine.GameObject(name);

            try
            {
                var resolver = new AIBridgePathInputTargetResolver();
                var resolved = resolver.TryResolve(
                    AIBridgeInputTarget.FromPath(name),
                    out var gameObject,
                    out var errorCode,
                    out var errorMessage);

                Assert.That(resolved, Is.False);
                Assert.That(gameObject, Is.Null);
                Assert.That(errorCode, Is.EqualTo("ambiguous_path"));
                Assert.That(errorMessage, Does.Contain("Provide path + instanceId"));

                resolved = resolver.TryResolve(
                    AIBridgeInputTarget.FromPath(name, second.GetInstanceID()),
                    out gameObject,
                    out errorCode,
                    out errorMessage);

                Assert.That(resolved, Is.True, errorMessage);
                Assert.That(gameObject, Is.SameAs(second));
                Assert.That(errorCode, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void RuntimeInputResolver_InstanceIdAloneRequiresPath()
        {
            var resolved = new AIBridgePathInputTargetResolver().TryResolve(
                AIBridgeInputTarget.FromInstanceId(123),
                out var gameObject,
                out var errorCode,
                out var errorMessage);

            Assert.That(resolved, Is.False);
            Assert.That(gameObject, Is.Null);
            Assert.That(errorCode, Is.EqualTo("path_required"));
            Assert.That(errorMessage, Does.Contain("requires 'path'"));
        }

        [Test]
        public void ScreenshotGifOptions_NormalizeSharedLimits()
        {
            var success = AIBridgeScreenshotGifOptions.TryNormalize(
                500,
                0.01f,
                5f,
                500,
                100,
                out var options,
                out var error);

            Assert.That(success, Is.True, error);
            Assert.That(options.FrameCount, Is.EqualTo(200));
            Assert.That(options.Delay, Is.EqualTo(0.1f));
            Assert.That(options.Scale, Is.EqualTo(1f));
            Assert.That(options.ColorCount, Is.EqualTo(256));
            Assert.That(options.Fps, Is.EqualTo(30));
        }

        [Test]
        public void LogService_InvalidTypeReturnsBindingFailure()
        {
            var result = AIBridgeLogService.Emit("message", "Verbose");

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("binding_failed"));
        }

        [Test]
        public void RuntimeCommandRegistry_RetiredInputCommandsAreNotRegistered()
        {
            var retiredCommands = new[]
            {
                "InputSimulationCommand_ClickByInstanceId",
                "InputSimulationCommand_ClickAt",
                "InputSimulationCommand_DragByInstanceId",
                "InputSimulationCommand_LongPressByInstanceId"
            };

            foreach (var command in retiredCommands)
            {
                Assert.That(AIBridgeCommandRegistry.TryGet(command, out var handler), Is.False);
                Assert.That(handler, Is.Null);
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

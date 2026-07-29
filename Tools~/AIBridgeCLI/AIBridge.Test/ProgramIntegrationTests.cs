using System.Reflection;

namespace AIBridgeCLI.Tests
{
    /// <summary>
    /// Integration tests for Program.Test method.
    /// Covers argument parsing → request building flow without sending to Unity.
    /// </summary>
    public class ProgramIntegrationTests
    {
        private static int InvokeTest(string[] args, out CommandRequest? request)
        {
            MethodInfo method = typeof(Program)
                .GetMethod("Test", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("Program.Test method not found");

            object?[] parameters = [args, null];
            int exitCode = (int)method.Invoke(null, parameters)!;
            request = parameters[1] as CommandRequest;
            return exitCode;
        }

        // ===========================================================================
        // 1. Basic command → type = CommandName
        // ===========================================================================

        [Fact]
        public void BasicCommand_SetsCommandName()
        {
            int exit = InvokeTest(["GameObjectCommand_Find"], out var req);

            Assert.Equal(0, exit);
            Assert.NotNull(req);
            Assert.Equal("GameObjectCommand_Find", req.type);
            Assert.NotNull(req.id);
        }

        [Fact]
        public void BasicCommand_WithOptions_AddsToParams()
        {
            int exit = InvokeTest(["GameObjectCommand_Find", "--name", "Player"], out var req);

            Assert.Equal(0, exit);
            Assert.NotNull(req);
            Assert.Equal("GameObjectCommand_Find", req.type);
            Assert.Equal("Player", req.@params["name"]);
        }

        // ===========================================================================
        // 2. Parameter type parsing
        // ===========================================================================

        [Fact]
        public void IntegerOption_ParsedAsLong()
        {
            int exit = InvokeTest(["TransformCommand_SetPosition", "--x", "10"], out var req);

            Assert.Equal(0, exit);
            Assert.NotNull(req);
            Assert.IsType<long>(req.@params["x"]);
            Assert.Equal(10L, req.@params["x"]);
        }

        [Fact]
        public void DoubleOption_ParsedAsDouble()
        {
            int exit = InvokeTest(["TransformCommand_SetPosition", "--x", "1.5"], out var req);

            Assert.Equal(0, exit);
            Assert.NotNull(req);
            Assert.IsType<double>(req.@params["x"]);
            Assert.Equal(1.5, req.@params["x"]);
        }

        [Fact]
        public void BoolTrueOption_ParsedAsBool()
        {
            int exit = InvokeTest(["GameObjectCommand_SetActive", "--active", "true"], out var req);

            Assert.Equal(0, exit);
            Assert.NotNull(req);
            Assert.IsType<bool>(req.@params["active"]);
            Assert.Equal(true, req.@params["active"]);
        }

        [Fact]
        public void BoolFalseOption_ParsedAsBool()
        {
            int exit = InvokeTest(["GameObjectCommand_SetActive", "--active", "false"], out var req);

            Assert.Equal(0, exit);
            Assert.NotNull(req);
            Assert.IsType<bool>(req.@params["active"]);
            Assert.Equal(false, req.@params["active"]);
        }

        [Fact]
        public void ArrayOption_ParsedAsObjectArray()
        {
            int exit = InvokeTest(["SelectionCommand_Set", "--ids", "[1,2,3]"], out var req);

            Assert.Equal(0, exit);
            Assert.NotNull(req);
            object[] ids = Assert.IsType<object[]>(req.@params["ids"]);
            Assert.Equal(3, ids.Length);
        }

        // ===========================================================================
        // 3. Multiple options
        // ===========================================================================

        [Fact]
        public void MultipleOptions_AllPresentInParams()
        {
            int exit = InvokeTest([
                "PrefabCommand_Save",
                "--gameObjectPath", "MyPrefab",
                "--savePath", "Assets/Prefabs",
                "--override", "true"
            ], out var req);

            Assert.Equal(0, exit);
            Assert.NotNull(req);
            Assert.Equal("PrefabCommand_Save", req.type);
            Assert.Equal("MyPrefab", req.@params["gameObjectPath"]);
            Assert.Equal("Assets/Prefabs", req.@params["savePath"]);
            Assert.Equal(true, req.@params["override"]);
        }

        // ===========================================================================
        // 4. Global options are excluded from request params
        // ===========================================================================

        [Fact]
        public void GlobalOptions_ExcludedFromParams()
        {
            int exit = InvokeTest([
                "GameObjectCommand_Find",
                "--timeout", "10000",
                "--raw",
                "--quiet",
                "--name", "Player"
            ], out var req);

            Assert.Equal(0, exit);
            Assert.NotNull(req);
            Assert.False(req.@params.ContainsKey("timeout"));
            Assert.False(req.@params.ContainsKey("raw"));
            Assert.False(req.@params.ContainsKey("quiet"));
            Assert.True(req.@params.ContainsKey("name"));
            Assert.Equal("Player", req.@params["name"]);
        }

        [Fact]
        public void NoWaitFlag_ExcludedFromParams()
        {
            int exit = InvokeTest(["GameObjectCommand_Find", "--no-wait", "--name", "Enemy"], out var req);

            Assert.Equal(0, exit);
            Assert.NotNull(req);
            Assert.False(req.@params.ContainsKey("no-wait"));
            Assert.Equal("Enemy", req.@params["name"]);
        }

        // ===========================================================================
        // 6. Help flag
        // ===========================================================================

        [Fact]
        public void GlobalHelp_ReturnsExitCode0_RequestNull()
        {
            int exit = InvokeTest(["--help"], out var req);

            Assert.Equal(0, exit);
            Assert.Null(req);
        }

        [Fact]
        public void CommandHelp_QueriesCommandsWithoutExecutingTarget()
        {
            int exit = InvokeTest(["EditorCommand_Play", "--help"], out var req);

            Assert.Equal(0, exit);
            Assert.NotNull(req);
            Assert.Equal("Commands", req.type);
            Assert.Equal("EditorCommand_Play", req.@params["command"]);
        }

        [Fact]
        public void Commands_QueriesCommandDetails()
        {
            int exit = InvokeTest(
                ["Commands", "--command", "EditorCommand_Play"],
                out var req);

            Assert.Equal(0, exit);
            Assert.NotNull(req);
            Assert.Equal("Commands", req.type);
            Assert.Equal("EditorCommand_Play", req.@params["command"]);
        }

        // ===========================================================================
        // 7. Request structure invariants
        // ===========================================================================

        [Fact]
        public void Request_HasUniqueId()
        {
            InvokeTest(["GameObjectCommand_Find"], out var req1);
            InvokeTest(["GameObjectCommand_Find"], out var req2);

            Assert.NotNull(req1);
            Assert.NotNull(req2);
            Assert.NotEqual(req1.id, req2.id);
        }

        [Fact]
        public void Request_TypeEqualsCommandName()
        {
            InvokeTest(["TransformCommand_SetPosition", "--x", "5"], out var req);

            Assert.NotNull(req);
            Assert.Equal("TransformCommand_SetPosition", req.type);
        }

        // ===========================================================================
        // 8. Common real-world command patterns
        // ===========================================================================

        [Fact]
        public void GameObjectFind_ByName()
        {
            int exit = InvokeTest(["GameObjectCommand_Find", "--name", "Player"], out var req);

            Assert.Equal(0, exit);
            Assert.NotNull(req);
            Assert.Equal("GameObjectCommand_Find", req.type);
            Assert.Equal("Player", req.@params["name"]);
        }

        [Fact]
        public void TransformSet_WithPositionComponents()
        {
            int exit = InvokeTest([
                "TransformCommand_SetPosition",
                "--x", "1",
                "--y", "2",
                "--z", "3"
            ], out var req);

            Assert.Equal(0, exit);
            Assert.NotNull(req);
            Assert.Equal(1L, req.@params["x"]);
            Assert.Equal(2L, req.@params["y"]);
            Assert.Equal(3L, req.@params["z"]);
        }

        [Fact]
        public void AssetDatabase_Refresh()
        {
            int exit = InvokeTest(["AssetDatabaseCommand_Refresh"], out var req);

            Assert.Equal(0, exit);
            Assert.NotNull(req);
            Assert.Equal("AssetDatabaseCommand_Refresh", req.type);
        }

        [Fact]
        public void MenuItem_Execute_WithMenuPath()
        {
            int exit = InvokeTest([
                "MenuItemCommand_Execute",
                "--menuPath", "File/Save Project"
            ], out var req);

            Assert.Equal(0, exit);
            Assert.NotNull(req);
            Assert.Equal("MenuItemCommand_Execute", req.type);
            Assert.Equal("File/Save Project", req.@params["menuPath"]);
        }

        // ===========================================================================
        // 9. Focus command (CLI-only, request is always null)
        // ===========================================================================

        [Fact]
        public void FocusCommand_RequestIsNull()
        {
            InvokeTest(["focus"], out var req);

            Assert.Null(req);
        }
    }
}

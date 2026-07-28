namespace AIBridgeCLI.Tests
{
    public class RequestBuilderTests
    {
        [Fact]
        public void BuildRequest_ValidCommandName_ReturnsRequest()
        {
            ParsedArgs? parsed = ParsedArgs.Parse(["GameObjectCommand_Find"]);
            CommandRequest? result = RequestBuilder.BuildRequest(parsed);

            Assert.NotNull(result);
            Assert.Equal("GameObjectCommand_Find", result.type);
            Assert.NotNull(result.id);
            Assert.NotNull(result.@params);
        }

        [Fact]
        public void BuildRequest_WithOptions_AddsParamsCorrectly()
        {
            ParsedArgs? parsed = ParsedArgs.Parse(["GameObjectCommand_Find", "--name", "Player", "--maxResults", "10"]);
            CommandRequest? result = RequestBuilder.BuildRequest(parsed);

            Assert.NotNull(result);
            Assert.Equal("GameObjectCommand_Find", result.type);
            Assert.Equal("Player", result.@params["name"]);
            Assert.Equal(10L, result.@params["maxResults"]);
        }

        [Fact]
        public void BuildPackedParams_ExcludesGlobalOptions()
        {
            ParsedArgs? parsed = ParsedArgs.Parse([
                "GameObjectCommand_Find",
                "--timeout", "5000",
                "--raw",
                "--name", "testvalue"
            ]);

            Dictionary<string, object>? result = RequestBuilder.BuildPackedParams(parsed);

            Assert.False(result.ContainsKey("timeout"));
            Assert.False(result.ContainsKey("raw"));
            Assert.True(result.ContainsKey("name"));
            Assert.Equal("testvalue", result["name"]);
        }

        [Fact]
        public void BuildPackedParams_IncludesCustomOptions()
        {
            ParsedArgs? parsed = ParsedArgs.Parse([
                "GameObjectCommand_Find",
                "--path", "/some/path",
                "--count", "10"
            ]);

            Dictionary<string, object>? result = RequestBuilder.BuildPackedParams(parsed);

            Assert.Equal("/some/path", result["path"]);
            Assert.Equal(10L, result["count"]);
        }

        [Fact]
        public void BuildRequest_CodeExecutePlayer_PreservesRuntimeRoutingOptions()
        {
            ParsedArgs parsed = ParsedArgs.Parse([
                "CodeExecuteCommand_Execute",
                "--code", "return Application.platform.ToString();",
                "--url", "http://192.168.1.20:27182",
                "--runtimeTimeout", "30000"
            ]);

            CommandRequest result = RequestBuilder.BuildRequest(parsed);

            Assert.Equal("CodeExecuteCommand_Execute", result.type);
            Assert.Equal("http://192.168.1.20:27182", result.@params["url"]);
            Assert.Equal(30000L, result.@params["runtimeTimeout"]);
        }

        [Fact]
        public void ParseValue_TrueString_ReturnsTrue()
        {
            object? result = RequestBuilder.ParseValue("true");

            Assert.Equal(true, result);
        }

        [Fact]
        public void ParseValue_TrueUppercase_ReturnsTrue()
        {
            object? result = RequestBuilder.ParseValue("TRUE");

            Assert.Equal(true, result);
        }

        [Fact]
        public void ParseValue_FalseString_ReturnsFalse()
        {
            object? result = RequestBuilder.ParseValue("false");

            Assert.Equal(false, result);
        }

        [Fact]
        public void ParseValue_Integer_ReturnsLong()
        {
            object? result = RequestBuilder.ParseValue("42");

            Assert.IsType<long>(result);
            Assert.Equal(42L, result);
        }

        [Fact]
        public void ParseValue_Double_ReturnsDouble()
        {
            object? result = RequestBuilder.ParseValue("3.14");

            Assert.IsType<double>(result);
            Assert.Equal(3.14, result);
        }

        [Fact]
        public void ParseValue_String_ReturnsString()
        {
            object? result = RequestBuilder.ParseValue("hello");

            Assert.IsType<string>(result);
            Assert.Equal("hello", result);
        }

        [Fact]
        public void ParseValue_JsonArray_ReturnsArray()
        {
            object? result = RequestBuilder.ParseValue("[1,2,3]");

            Assert.IsType<object[]>(result);
            object[] arr = (object[])result;
            Assert.Equal(3, arr.Length);
        }

        [Fact]
        public void ParseValue_Null_ReturnsNull()
        {
            object? result = RequestBuilder.ParseValue(null);

            Assert.Null(result);
        }
    }
}

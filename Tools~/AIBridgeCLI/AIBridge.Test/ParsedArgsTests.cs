namespace AIBridgeCLI.Tests
{
    public class ParsedArgsTests
    {
        [Fact]
        public void Parse_EmptyArgs_ReturnsDefaultValues()
        {
            ParsedArgs? result = ParsedArgs.Parse(Array.Empty<string>());

            Assert.Null(result.CommandName);
            Assert.NotNull(result.Options);
            Assert.Empty(result.Options);
        }

        [Fact]
        public void Parse_SinglePositionalArg_SetsCommandName()
        {
            ParsedArgs? result = ParsedArgs.Parse(["GameObjectCommand_Find"]);

            Assert.Equal("GameObjectCommand_Find", result.CommandName);
        }

        [Fact]
        public void Parse_KeyValueOption_AddsToOptions()
        {
            ParsedArgs? result = ParsedArgs.Parse(["GameObjectCommand_Find", "--name", "Player"]);

            Assert.Equal("GameObjectCommand_Find", result.CommandName);
            Assert.True(result.Options.ContainsKey("name"));
            Assert.Equal("Player", result.Options["name"]);
        }

        [Fact]
        public void Parse_BooleanFlag_SetsTrue()
        {
            ParsedArgs? result = ParsedArgs.Parse(["GameObjectCommand_Find", "--help"]);

            Assert.True(result.Help);
            Assert.True(result.Options.ContainsKey("help"));
            Assert.Equal("true", result.Options["help"]);
        }

        [Fact]
        public void Parse_ShortHelpFlag_SetsTrue()
        {
            ParsedArgs? result = ParsedArgs.Parse(["GameObjectCommand_Find", "-h"]);

            Assert.True(result.Help);
            Assert.Equal("true", result.Options["help"]);
        }

        [Fact]
        public void Parse_HelpBeforeCommand_SetsCommandName()
        {
            ParsedArgs? result = ParsedArgs.Parse(["--help", "Commands"]);

            Assert.True(result.Help);
            Assert.Equal("Commands", result.CommandName);
        }

        [Fact]
        public void Parse_GlobalOption_SetsProperty()
        {
            ParsedArgs? result = ParsedArgs.Parse(new[] { "GameObjectCommand_Find", "--timeout", "10000" });

            Assert.Equal(10000, result.Timeout);
        }

        [Fact]
        public void Parse_RawFlag_SetsRawTrue()
        {
            ParsedArgs? result = ParsedArgs.Parse(["GameObjectCommand_Find", "--raw"]);

            Assert.True(result.Raw);
            Assert.Equal(OutputMode.Raw, result.OutputMode);
        }

        [Fact]
        public void Parse_QuietFlag_SetsQuietTrue()
        {
            ParsedArgs? result = ParsedArgs.Parse(["GameObjectCommand_Find", "--quiet"]);

            Assert.True(result.Quiet);
            Assert.Equal(OutputMode.Quiet, result.OutputMode);
        }

        [Fact]
        public void Parse_DefaultOutputMode_IsPretty()
        {
            ParsedArgs? result = ParsedArgs.Parse(["GameObjectCommand_Find"]);

            Assert.Equal(OutputMode.Pretty, result.OutputMode);
        }

        [Fact]
        public void Parse_MultipleOptions_AllParsed()
        {
            ParsedArgs? result = ParsedArgs.Parse([
                "GameObjectCommand_Find",
                "--key1", "value1",
                "--key2", "value2",
                "--verbose"
            ]);

            Assert.Equal("GameObjectCommand_Find", result.CommandName);
            Assert.Equal("value1", result.Options["key1"]);
            Assert.Equal("value2", result.Options["key2"]);
            Assert.True(result.Options.ContainsKey("verbose"));
        }

        [Fact]
        public void GetBool_TrueValue_ReturnsTrue()
        {
            ParsedArgs? result = ParsedArgs.Parse(["GameObjectCommand_Find", "--flag", "true"]);

            Assert.True(result.GetBool("flag"));
        }

        [Fact]
        public void GetBool_OneValue_ReturnsTrue()
        {
            ParsedArgs? result = ParsedArgs.Parse(["GameObjectCommand_Find", "--flag", "1"]);

            Assert.True(result.GetBool("flag"));
        }

        [Fact]
        public void GetBool_FalseValue_ReturnsFalse()
        {
            ParsedArgs? result = ParsedArgs.Parse(["GameObjectCommand_Find", "--flag", "false"]);

            Assert.False(result.GetBool("flag"));
        }

        [Fact]
        public void GetBool_MissingKey_ReturnsFalse()
        {
            ParsedArgs? result = ParsedArgs.Parse(["GameObjectCommand_Find"]);

            Assert.False(result.GetBool("nonexistent"));
        }

        [Fact]
        public void GetInt_ValidValue_ReturnsValue()
        {
            ParsedArgs? result = ParsedArgs.Parse(["GameObjectCommand_Find", "--count", "42"]);

            Assert.Equal(42, result.GetInt("count", 0));
        }

        [Fact]
        public void GetInt_InvalidValue_ReturnsDefault()
        {
            ParsedArgs? result = ParsedArgs.Parse(["GameObjectCommand_Find", "--count", "invalid"]);

            Assert.Equal(100, result.GetInt("count", 100));
        }

        [Fact]
        public void GetInt_MissingKey_ReturnsDefault()
        {
            ParsedArgs? result = ParsedArgs.Parse(["GameObjectCommand_Find"]);

            Assert.Equal(100, result.GetInt("nonexistent", 100));
        }

        [Fact]
        public void TrySetGlobalOption_ValidProperty_ReturnsTrue()
        {
            ParsedArgs result = new();
            bool success = result.TrySetGlobalOption("timeout", "5000");

            Assert.True(success);
            Assert.Equal(5000, result.Timeout);
        }

        [Fact]
        public void TrySetGlobalOption_InvalidProperty_ReturnsFalse()
        {
            ParsedArgs result = new();
            bool success = result.TrySetGlobalOption("nonexistent", "value");

            Assert.False(success);
        }

        [Fact]
        public void Parse_ShortForm_ThrowsException()
        {
            Assert.Throws<ArgumentException>(() =>
                ParsedArgs.Parse(new[] { "-a" }));
        }

        [Fact]
        public void Parse_TwoPositionalArgs_ThrowsException()
        {
            Assert.Throws<ArgumentException>(() =>
                ParsedArgs.Parse(new[] { "Command", "unexpectedArg" }));
        }
    }
}

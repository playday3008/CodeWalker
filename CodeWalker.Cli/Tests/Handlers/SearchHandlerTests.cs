using System;
using System.IO;

using CodeWalker.Cli.Handlers;
using CodeWalker.Cli.Helpers;

using Xunit;

namespace CodeWalker.Cli.Tests.Handlers;

public sealed class SearchHandlerTests
{
    // ── HasGlobChars ──────────────────────────────────────────────────

    [Fact]
    public void HasGlobChars_WithAsterisk_ReturnsTrue() =>
        Assert.True(SearchHandler.HasGlobChars("*.ydr"));

    [Fact]
    public void HasGlobChars_WithQuestionMark_ReturnsTrue() =>
        Assert.True(SearchHandler.HasGlobChars("file?.txt"));

    [Fact]
    public void HasGlobChars_WithBoth_ReturnsTrue() =>
        Assert.True(SearchHandler.HasGlobChars("**/dir?.txt"));

    [Fact]
    public void HasGlobChars_PlainString_ReturnsFalse() =>
        Assert.False(SearchHandler.HasGlobChars("vehicles"));

    [Fact]
    public void HasGlobChars_Empty_ReturnsFalse() =>
        Assert.False(SearchHandler.HasGlobChars(""));

    [Fact]
    public void HasGlobChars_HexHash_ReturnsFalse() =>
        Assert.False(SearchHandler.HasGlobChars("0xABCD1234"));

    [Fact]
    public void HasGlobChars_DecimalNumber_ReturnsFalse() =>
        Assert.False(SearchHandler.HasGlobChars("123456789"));

    [Fact]
    public void HasGlobChars_PathWithoutGlob_ReturnsFalse() =>
        Assert.False(SearchHandler.HasGlobChars("vehicles/adder.ydr"));

    [Fact]
    public void HasGlobChars_GlobstarPattern_ReturnsTrue() =>
        Assert.True(SearchHandler.HasGlobChars("**/vehicles/*.ydr"));

    [Fact]
    public void HasGlobChars_QuestionMarkOnly_ReturnsTrue() =>
        Assert.True(SearchHandler.HasGlobChars("?"));

    [Fact]
    public void HasGlobChars_AsteriskOnly_ReturnsTrue() =>
        Assert.True(SearchHandler.HasGlobChars("*"));

    // ── Additional HasGlobChars edge cases ────────────────────────────

    [Fact]
    public void HasGlobChars_BracketPattern_ReturnsFalse() =>
        Assert.False(SearchHandler.HasGlobChars("[abc]"));

    [Fact]
    public void HasGlobChars_AsteriskInMiddle_ReturnsTrue() =>
        Assert.True(SearchHandler.HasGlobChars("foo*bar"));
}

[Collection("ConsoleOutput")]
public sealed class SearchHandlerExecuteTests
{
    private static RpfOptions MakeOptions(string rpfPath, bool json) =>
        new()
        {
            RpfPath = rpfPath,
            ExePath = "/nonexistent",
            Gen9 = false,
            Filters = [],
            Verbose = false,
            Json = json,
            Recursive = false,
            Threads = 1,
            SizeFormat = SizeFormat.IEC,
        };

    [Fact]
    public void Execute_MissingRpf_ReturnsOne()
    {
        TextWriter origOut = Console.Out;
        TextWriter origErr = Console.Error;
        try
        {
            StringWriter stderr = new();
            Console.SetOut(new StringWriter());
            Console.SetError(stderr);

            int exitCode = SearchHandler.Execute(MakeOptions("/nonexistent/test.rpf", json: false), "*.ydr", TestContext.Current.CancellationToken);

            Assert.Equal(1, exitCode);
            Assert.Contains("Error:", stderr.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }

    [Fact]
    public void Execute_MissingRpf_Json_ReturnsErrorJson()
    {
        TextWriter origOut = Console.Out;
        TextWriter origErr = Console.Error;
        try
        {
            StringWriter stdout = new();
            Console.SetOut(stdout);
            Console.SetError(new StringWriter());

            int exitCode = SearchHandler.Execute(MakeOptions("/nonexistent/test.rpf", json: true), "adder", TestContext.Current.CancellationToken);

            Assert.Equal(1, exitCode);
            string output = stdout.ToString();
            Assert.Contains("\"success\": false", output);
            Assert.Contains("RPF file not found", output);
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }

    [Fact]
    public void Execute_Json_ErrorContainsExpectedFields()
    {
        TextWriter origOut = Console.Out;
        try
        {
            StringWriter stdout = new();
            Console.SetOut(stdout);

            _ = SearchHandler.Execute(MakeOptions("/nonexistent/test.rpf", json: true), "test*", TestContext.Current.CancellationToken);

            string output = stdout.ToString();
            Assert.Contains("\"rpfFile\":", output);
            Assert.Contains("\"pattern\":", output);
            Assert.Contains("\"matchCount\": 0", output);
        }
        finally { Console.SetOut(origOut); }
    }
}

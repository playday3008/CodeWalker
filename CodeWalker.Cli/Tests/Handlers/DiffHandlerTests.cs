using System;
using System.IO;

using CodeWalker.Cli.Handlers;
using CodeWalker.Cli.Helpers;

using Xunit;

namespace CodeWalker.Cli.Tests.Handlers;

public sealed class DiffHandlerTests
{
    // ── ContentEquals ─────────────────────────────────────────────────

    [Fact]
    public void ContentEquals_BothNull_ReturnsTrue() =>
        Assert.True(DiffHandler.ContentEquals(null, null));

    [Fact]
    public void ContentEquals_LeftNull_ReturnsFalse() =>
        Assert.False(DiffHandler.ContentEquals(null, [1, 2]));

    [Fact]
    public void ContentEquals_RightNull_ReturnsFalse() =>
        Assert.False(DiffHandler.ContentEquals([1, 2], null));

    [Fact]
    public void ContentEquals_DifferentLengths_ReturnsFalse() =>
        Assert.False(DiffHandler.ContentEquals([1, 2], [1, 2, 3]));

    [Fact]
    public void ContentEquals_SameContent_ReturnsTrue() =>
        Assert.True(DiffHandler.ContentEquals([1, 2, 3], [1, 2, 3]));

    [Fact]
    public void ContentEquals_DifferentContent_ReturnsFalse() =>
        Assert.False(DiffHandler.ContentEquals([1, 2, 3], [1, 2, 4]));

    [Fact]
    public void ContentEquals_BothEmpty_ReturnsTrue() =>
        Assert.True(DiffHandler.ContentEquals([], []));

    [Fact]
    public void ContentEquals_SingleByte_Same_ReturnsTrue() =>
        Assert.True(DiffHandler.ContentEquals([0xFF], [0xFF]));

    [Fact]
    public void ContentEquals_SingleByte_Different_ReturnsFalse() =>
        Assert.False(DiffHandler.ContentEquals([0x00], [0xFF]));

    [Fact]
    public void ContentEquals_DifferencesAtEnd_ReturnsFalse() =>
        Assert.False(DiffHandler.ContentEquals([1, 2, 3, 4, 5], [1, 2, 3, 4, 6]));

    [Fact]
    public void ContentEquals_LargeIdenticalArrays_ReturnsTrue()
    {
        byte[] a = new byte[10_000];
        byte[] b = new byte[10_000];
        for (int i = 0; i < a.Length; i++)
        {
            a[i] = (byte)(i % 256);
            b[i] = (byte)(i % 256);
        }
        Assert.True(DiffHandler.ContentEquals(a, b));
    }

    [Fact]
    public void ContentEquals_LargeArrays_LastByteDiffers_ReturnsFalse()
    {
        byte[] a = new byte[10_000];
        byte[] b = new byte[10_000];
        for (int i = 0; i < a.Length; i++)
        {
            a[i] = (byte)(i % 256);
            b[i] = (byte)(i % 256);
        }
        b[^1] = (byte)(a[^1] ^ 0xFF);
        Assert.False(DiffHandler.ContentEquals(a, b));
    }
}

[Collection("ConsoleOutput")]
public sealed class DiffHandlerExecuteTests
{
    private static DiffOptions MakeOptions(string leftPath, string rightPath, bool json) =>
        new()
        {
            LeftPath = leftPath,
            RightPath = rightPath,
            LeftExePath = "/nonexistent",
            RightExePath = "/nonexistent",
            LeftGen9 = false,
            RightGen9 = false,
            Recursive = false,
            Progress = false,
            Verbose = false,
            Json = json,
            SizeFormat = SizeFormat.IEC,
            Threads = 1,
        };

    [Fact]
    public void Execute_LeftMissing_ReturnsOne()
    {
        TextWriter origOut = Console.Out;
        TextWriter origErr = Console.Error;
        try
        {
            StringWriter stderr = new();
            Console.SetOut(new StringWriter());
            Console.SetError(stderr);

            int exitCode = DiffHandler.Execute(MakeOptions("/nonexistent/left.rpf", "/nonexistent/right.rpf", json: false), TestContext.Current.CancellationToken);

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
    public void Execute_LeftMissing_Json_ReturnsErrorJson()
    {
        TextWriter origOut = Console.Out;
        TextWriter origErr = Console.Error;
        try
        {
            StringWriter stdout = new();
            Console.SetOut(stdout);
            Console.SetError(new StringWriter());

            int exitCode = DiffHandler.Execute(MakeOptions("/nonexistent/left.rpf", "/nonexistent/right.rpf", json: true), TestContext.Current.CancellationToken);

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
    public void Execute_RightMissing_WithExistingLeft_ReturnsOne()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cw_diff_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(dir);
        string leftRpf = Path.Combine(dir, "left.rpf");
        File.WriteAllBytes(leftRpf, []);
        // Also need a valid exe dir
        File.WriteAllBytes(Path.Combine(dir, "GTA5.exe"), []);
        try
        {
            TextWriter origOut = Console.Out;
            TextWriter origErr = Console.Error;
            try
            {
                StringWriter stderr = new();
                Console.SetOut(new StringWriter());
                Console.SetError(stderr);

                DiffOptions options = new()
                {
                    LeftPath = leftRpf,
                    RightPath = "/nonexistent/right.rpf",
                    LeftExePath = dir,
                    RightExePath = dir,
                    LeftGen9 = false,
                    RightGen9 = false,
                    Recursive = false,
                    Progress = false,
                    Verbose = false,
                    Json = false,
                    SizeFormat = SizeFormat.IEC,
                    Threads = 1,
                };

                int exitCode = DiffHandler.Execute(options, TestContext.Current.CancellationToken);

                Assert.Equal(1, exitCode);
                Assert.Contains("RPF file not found", stderr.ToString());
            }
            finally
            {
                Console.SetOut(origOut);
                Console.SetError(origErr);
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Execute_Json_ErrorContainsExpectedFields()
    {
        TextWriter origOut = Console.Out;
        try
        {
            StringWriter stdout = new();
            Console.SetOut(stdout);

            _ = DiffHandler.Execute(MakeOptions("/nonexistent/left.rpf", "/nonexistent/right.rpf", json: true), TestContext.Current.CancellationToken);

            string output = stdout.ToString();
            Assert.Contains("\"leftRpf\":", output);
            Assert.Contains("\"rightRpf\":", output);
        }
        finally { Console.SetOut(origOut); }
    }
}

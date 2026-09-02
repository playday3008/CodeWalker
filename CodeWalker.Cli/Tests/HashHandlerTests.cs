using System;
using System.IO;

using Xunit;

namespace CodeWalker.Cli.Tests;

[Collection("ConsoleOutput")]
public sealed class HashHandlerTests
{
    // ── Encoding validation ───────────────────────────────────────────

    [Fact]
    public void Execute_UnknownEncoding_ReturnsOne()
    {
        TextWriter origOut = Console.Out;
        TextWriter origErr = Console.Error;
        try
        {
            Console.SetOut(new StringWriter());
            StringWriter stderr = new();
            Console.SetError(stderr);

            int exitCode = HashHandler.Execute(new HashOptions
            {
                Inputs = ["test"],
                Encoding = "unknown-codec",
                Json = false,
            });

            Assert.Equal(1, exitCode);
            Assert.Contains("Unknown encoding", stderr.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }

    [Fact]
    public void Execute_UnknownEncoding_Json_ReturnsErrorJson()
    {
        TextWriter origOut = Console.Out;
        try
        {
            StringWriter stdout = new();
            Console.SetOut(stdout);

            int exitCode = HashHandler.Execute(new HashOptions
            {
                Inputs = ["test"],
                Encoding = "bad",
                Json = true,
            });

            Assert.Equal(1, exitCode);
            string output = stdout.ToString();
            Assert.Contains("\"success\": false", output);
            Assert.Contains("Unknown encoding", output);
        }
        finally { Console.SetOut(origOut); }
    }

    // ── Successful hashing ────────────────────────────────────────────

    [Fact]
    public void Execute_Utf8Encoding_ReturnsZero()
    {
        TextWriter origOut = Console.Out;
        try
        {
            StringWriter stdout = new();
            Console.SetOut(stdout);

            int exitCode = HashHandler.Execute(new HashOptions
            {
                Inputs = ["test"],
                Encoding = "utf8",
                Json = false,
            });

            Assert.Equal(0, exitCode);
            string output = stdout.ToString();
            Assert.Contains("Input: test", output);
            Assert.Contains("Hash (uint):", output);
            Assert.Contains("Hash (hex):", output);
        }
        finally { Console.SetOut(origOut); }
    }

    [Fact]
    public void Execute_Utf8WithDash_ReturnsZero()
    {
        TextWriter origOut = Console.Out;
        try
        {
            Console.SetOut(new StringWriter());

            int exitCode = HashHandler.Execute(new HashOptions
            {
                Inputs = ["test"],
                Encoding = "utf-8",
                Json = false,
            });

            Assert.Equal(0, exitCode);
        }
        finally { Console.SetOut(origOut); }
    }

    [Fact]
    public void Execute_AsciiEncoding_ReturnsZero()
    {
        TextWriter origOut = Console.Out;
        try
        {
            Console.SetOut(new StringWriter());

            int exitCode = HashHandler.Execute(new HashOptions
            {
                Inputs = ["hello"],
                Encoding = "ascii",
                Json = false,
            });

            Assert.Equal(0, exitCode);
        }
        finally { Console.SetOut(origOut); }
    }

    [Fact]
    public void Execute_EncodingIsCaseInsensitive()
    {
        TextWriter origOut = Console.Out;
        try
        {
            Console.SetOut(new StringWriter());

            int exitCode = HashHandler.Execute(new HashOptions
            {
                Inputs = ["test"],
                Encoding = "ASCII",
                Json = false,
            });

            Assert.Equal(0, exitCode);
        }
        finally { Console.SetOut(origOut); }
    }

    // ── Multiple inputs ───────────────────────────────────────────────

    [Fact]
    public void Execute_MultipleInputs_HashesAll()
    {
        TextWriter origOut = Console.Out;
        try
        {
            StringWriter stdout = new();
            Console.SetOut(stdout);

            int exitCode = HashHandler.Execute(new HashOptions
            {
                Inputs = ["alpha", "bravo", "charlie"],
                Encoding = "utf8",
                Json = false,
            });

            Assert.Equal(0, exitCode);
            string output = stdout.ToString();
            Assert.Contains("Input: alpha", output);
            Assert.Contains("Input: bravo", output);
            Assert.Contains("Input: charlie", output);
        }
        finally { Console.SetOut(origOut); }
    }

    // ── JSON output ───────────────────────────────────────────────────

    [Fact]
    public void Execute_JsonMode_ContainsExpectedFields()
    {
        TextWriter origOut = Console.Out;
        try
        {
            StringWriter stdout = new();
            Console.SetOut(stdout);

            int exitCode = HashHandler.Execute(new HashOptions
            {
                Inputs = ["vehicle"],
                Encoding = "utf8",
                Json = true,
            });

            Assert.Equal(0, exitCode);
            string output = stdout.ToString();
            Assert.Contains("\"success\": true", output);
            Assert.Contains("\"input\": \"vehicle\"", output);
            Assert.Contains("\"hash\":", output);
            Assert.Contains("\"hashHex\":", output);
            Assert.Contains("\"encoding\":", output);
        }
        finally { Console.SetOut(origOut); }
    }

    [Fact]
    public void Execute_JsonMode_MultipleInputs_HasMultipleEntries()
    {
        TextWriter origOut = Console.Out;
        try
        {
            StringWriter stdout = new();
            Console.SetOut(stdout);

            int exitCode = HashHandler.Execute(new HashOptions
            {
                Inputs = ["one", "two"],
                Encoding = "utf8",
                Json = true,
            });

            Assert.Equal(0, exitCode);
            string output = stdout.ToString();
            Assert.Contains("\"input\": \"one\"", output);
            Assert.Contains("\"input\": \"two\"", output);
        }
        finally { Console.SetOut(origOut); }
    }

    // ── Deterministic hashes ──────────────────────────────────────────

    [Fact]
    public void Execute_SameInput_ProducesSameHash()
    {
        TextWriter origOut = Console.Out;
        try
        {
            StringWriter stdout1 = new();
            Console.SetOut(stdout1);
            _ = HashHandler.Execute(new HashOptions
            {
                Inputs = ["deterministic"],
                Encoding = "utf8",
                Json = true,
            });

            StringWriter stdout2 = new();
            Console.SetOut(stdout2);
            _ = HashHandler.Execute(new HashOptions
            {
                Inputs = ["deterministic"],
                Encoding = "utf8",
                Json = true,
            });

            Assert.Equal(stdout1.ToString(), stdout2.ToString());
        }
        finally { Console.SetOut(origOut); }
    }

    // ── Edge cases ────────────────────────────────────────────────────

    [Fact]
    public void Execute_EmptyStringInput_ReturnsZero()
    {
        TextWriter origOut = Console.Out;
        try
        {
            StringWriter stdout = new();
            Console.SetOut(stdout);

            int exitCode = HashHandler.Execute(new HashOptions
            {
                Inputs = [""],
                Encoding = "utf8",
                Json = false,
            });

            Assert.Equal(0, exitCode);
            Assert.Contains("Input: ", stdout.ToString());
        }
        finally { Console.SetOut(origOut); }
    }

    [Fact]
    public void Execute_CancellationToken_ThrowsOperationCanceled()
    {
        using System.Threading.CancellationTokenSource cts = new();
        cts.Cancel();

        TextWriter origOut = Console.Out;
        try
        {
            Console.SetOut(new StringWriter());
            _ = Assert.Throws<OperationCanceledException>(() =>
                HashHandler.Execute(new HashOptions
                {
                    Inputs = ["test"],
                    Encoding = "utf8",
                    Json = false,
                }, cts.Token)
            );
        }
        finally { Console.SetOut(origOut); }
    }

    [Fact]
    public void Execute_DifferentInputs_ProduceDifferentHashes()
    {
        TextWriter origOut = Console.Out;
        try
        {
            StringWriter stdout1 = new();
            Console.SetOut(stdout1);
            _ = HashHandler.Execute(new HashOptions
            {
                Inputs = ["alpha"],
                Encoding = "utf8",
                Json = true,
            });

            StringWriter stdout2 = new();
            Console.SetOut(stdout2);
            _ = HashHandler.Execute(new HashOptions
            {
                Inputs = ["beta"],
                Encoding = "utf8",
                Json = true,
            });

            Assert.NotEqual(stdout1.ToString(), stdout2.ToString());
        }
        finally { Console.SetOut(origOut); }
    }

    [Fact]
    public void Execute_Json_ContainsHashSigned()
    {
        TextWriter origOut = Console.Out;
        try
        {
            StringWriter stdout = new();
            Console.SetOut(stdout);

            _ = HashHandler.Execute(new HashOptions
            {
                Inputs = ["test"],
                Encoding = "utf8",
                Json = true,
            });

            string output = stdout.ToString();
            Assert.Contains("\"hashSigned\":", output);
        }
        finally { Console.SetOut(origOut); }
    }

    [Fact]
    public void Execute_TextMode_ShowsIntHash()
    {
        TextWriter origOut = Console.Out;
        try
        {
            StringWriter stdout = new();
            Console.SetOut(stdout);

            _ = HashHandler.Execute(new HashOptions
            {
                Inputs = ["test"],
                Encoding = "utf8",
                Json = false,
            });

            string output = stdout.ToString();
            Assert.Contains("Hash (int):", output);
        }
        finally { Console.SetOut(origOut); }
    }
}

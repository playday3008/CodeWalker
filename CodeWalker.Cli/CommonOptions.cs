using System;
using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.IO;

using CodeWalker.Cli.Helpers;

#if TESTING
using Xunit;
#endif

namespace CodeWalker.Cli;

[ExcludeFromCodeCoverage]
internal sealed record CommonOptions
{
    public required string ExePath { get; init; }
    public required bool Verbose { get; init; }
    public required bool Json { get; init; }
    public required SizeFormat SizeFormat { get; init; }
    public required int Threads { get; init; }
}

/// <summary>
/// Shared System.CommandLine option definitions for --exe, --verbose, --json, --si, --threads.
/// Create an instance, call <see cref="AddTo"/> to register options on a command,
/// then call <see cref="Parse"/> inside the action to build a <see cref="CommonOptions"/>.
/// </summary>
internal sealed class CommonCommandOptions
{
    public Option<DirectoryInfo> Exe { get; } = new("--exe", "-e")
    {
        Description = "Path to the GTA V installation directory (containing GTA5.exe)",
        Required = true,
    };

    public Option<bool> Verbose { get; } = new("--verbose", "-v")
    {
        Description = "Show verbose output",
    };

    public Option<bool> Json { get; } = new("--json")
    {
        Description = "Output results in JSON format for scripting",
    };

    public Option<bool> Si { get; } = new("--si")
    {
        Description = "Use SI units (1000-based: KB, MB) instead of IEC (1024-based: KiB, MiB)",
    };

    public Option<int> Threads { get; } = new("--threads", "-t")
    {
        Description = "Number of threads for parallel processing",
        DefaultValueFactory = _ => Environment.ProcessorCount,
    };

    public CommonCommandOptions()
    {
        Threads.Validators.Add(result =>
        {
            if (result.GetValue(Threads) < 1)
                result.AddError("--threads must be at least 1.");
        });
    }

    public void AddTo(Command command, bool includeThreads = true)
    {
        command.Add(Exe);
        command.Add(Verbose);
        command.Add(Json);
        command.Add(Si);
        if (includeThreads)
            command.Add(Threads);
    }

    public CommonOptions Parse(ParseResult parseResult)
    {
        return new CommonOptions
        {
            ExePath = parseResult.GetRequiredValue(Exe).FullName,
            Verbose = parseResult.GetValue(Verbose),
            Json = parseResult.GetValue(Json),
            SizeFormat = parseResult.GetValue(Si) ? SizeFormat.SI : SizeFormat.IEC,
            Threads = parseResult.GetValue(Threads),
        };
    }
}

#if TESTING
public sealed class CommonOptionsTests
{
    [Fact]
    public void Parse_MapsAllValues()
    {
        RootCommand root = [];
        CommonCommandOptions opts = new();
        opts.AddTo(root);
        ParseResult pr = root.Parse("--exe /tmp/testdir --verbose --json --si --threads 4");
        Assert.Empty(pr.Errors);
        CommonOptions common = opts.Parse(pr);
        Assert.Equal("/tmp/testdir", common.ExePath);
        Assert.True(common.Verbose);
        Assert.True(common.Json);
        Assert.Equal(SizeFormat.SI, common.SizeFormat);
        Assert.Equal(4, common.Threads);
    }

    [Fact]
    public void Parse_Defaults()
    {
        RootCommand root = [];
        CommonCommandOptions opts = new();
        opts.AddTo(root);
        ParseResult pr = root.Parse("--exe /tmp/testdir");
        Assert.Empty(pr.Errors);
        CommonOptions common = opts.Parse(pr);
        Assert.False(common.Verbose);
        Assert.False(common.Json);
        Assert.Equal(SizeFormat.IEC, common.SizeFormat);
        Assert.True(common.Threads >= 1); // defaults to Environment.ProcessorCount
    }

    [Fact]
    public void Parse_SiEnabled_ReturnsSI()
    {
        RootCommand root = [];
        CommonCommandOptions opts = new();
        opts.AddTo(root);
        ParseResult pr = root.Parse("--exe /tmp/testdir --si");
        Assert.Equal(SizeFormat.SI, opts.Parse(pr).SizeFormat);
    }

    [Fact]
    public void ThreadValidator_RejectsZero()
    {
        RootCommand root = [];
        CommonCommandOptions opts = new();
        opts.AddTo(root);
        ParseResult pr = root.Parse("--exe /tmp/testdir --threads 0");
        Assert.NotEmpty(pr.Errors);
    }

    [Fact]
    public void ThreadValidator_AcceptsOne()
    {
        RootCommand root = [];
        CommonCommandOptions opts = new();
        opts.AddTo(root);
        ParseResult pr = root.Parse("--exe /tmp/testdir --threads 1");
        Assert.Empty(pr.Errors);
    }

    [Fact]
    public void AddTo_IncludesThreadsByDefault()
    {
        RootCommand root = [];
        CommonCommandOptions opts = new();
        opts.AddTo(root);
        ParseResult pr = root.Parse("--exe /tmp/testdir --threads 2");
        Assert.Empty(pr.Errors);
    }

    [Fact]
    public void AddTo_ExcludesThreads_WhenFlagIsFalse()
    {
        RootCommand root = [];
        CommonCommandOptions opts = new();
        opts.AddTo(root, includeThreads: false);
        ParseResult pr = root.Parse("--exe /tmp/testdir --threads 2");
        Assert.NotEmpty(pr.Errors);
    }
}
#endif

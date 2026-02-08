using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.IO;

using CodeWalker.Cli.Helpers;

#if TESTING
using Xunit;
#endif

namespace CodeWalker.Cli;

[ExcludeFromCodeCoverage]
internal sealed record RpfOptions
{
    public required string RpfPath { get; init; }
    public required string ExePath { get; init; }
    public required bool Gen9 { get; init; }
    public required string[] Filters { get; init; }
    public required bool Verbose { get; init; }
    public required bool Json { get; init; }
    public required bool Recursive { get; init; }
    public required int Threads { get; init; }
    public required SizeFormat SizeFormat { get; init; }
}

/// <summary>
/// Shared System.CommandLine option definitions for RPF-based commands.
/// Create an instance, call <see cref="AddTo"/> to register options on a command,
/// then call <see cref="Parse"/> inside the action to build an <see cref="RpfOptions"/>.
/// </summary>
internal sealed class RpfCommandOptions
{
    private readonly CommonCommandOptions _commonOpts = new();

    public Option<FileInfo> Rpf { get; } = new("--rpf", "-r")
    {
        Description = "Path to the RPF file",
        Required = true,
    };

    public Option<bool> Gen9 { get; } = new("--gen9", "-g")
    {
        Description = "Use GTA V Enhanced (Gen9) mode",
    };

    public Option<string[]> Filter { get; } = new("--filter", "-f")
    {
        Description = "Filter files by glob patterns (e.g. *.ydd); can be specified multiple times",
        AllowMultipleArgumentsPerToken = true,
    };

    public Option<bool> Recursive { get; } = new("--recursive", "-R")
    {
        Description = "Process nested RPF archives",
    };

    public void AddTo(Command command)
    {
        command.Add(Rpf);
        _commonOpts.AddTo(command);
        command.Add(Gen9);
        command.Add(Filter);
        command.Add(Recursive);
    }

    public RpfOptions Parse(ParseResult parseResult)
    {
        CommonOptions common = _commonOpts.Parse(parseResult);
        return new RpfOptions
        {
            RpfPath = parseResult.GetRequiredValue(Rpf).FullName,
            ExePath = common.ExePath,
            Gen9 = parseResult.GetValue(Gen9),
            Filters = Helpers.Filter.Normalize(parseResult.GetValue(Filter)),
            Verbose = common.Verbose,
            Json = common.Json,
            Recursive = parseResult.GetValue(Recursive),
            Threads = common.Threads,
            SizeFormat = common.SizeFormat,
        };
    }
}

#if TESTING
public sealed class RpfOptionsTests
{
    [Fact]
    public void Parse_MapsAllValues()
    {
        RootCommand root = [];
        RpfCommandOptions opts = new();
        opts.AddTo(root);
        ParseResult pr = root.Parse(
            "--rpf /tmp/test.rpf --exe /tmp/testdir --gen9 --recursive --verbose --json --si --threads 4 --filter *.ydr"
        );
        Assert.Empty(pr.Errors);
        RpfOptions rpfOpts = opts.Parse(pr);
        Assert.Equal("/tmp/test.rpf", rpfOpts.RpfPath);
        Assert.Equal("/tmp/testdir", rpfOpts.ExePath);
        Assert.True(rpfOpts.Gen9);
        Assert.True(rpfOpts.Recursive);
        Assert.True(rpfOpts.Verbose);
        Assert.True(rpfOpts.Json);
        Assert.Equal(SizeFormat.SI, rpfOpts.SizeFormat);
        Assert.Equal(4, rpfOpts.Threads);
    }

    [Fact]
    public void Parse_Defaults()
    {
        RootCommand root = [];
        RpfCommandOptions opts = new();
        opts.AddTo(root);
        ParseResult pr = root.Parse("--rpf /tmp/test.rpf --exe /tmp/testdir");
        Assert.Empty(pr.Errors);
        RpfOptions rpfOpts = opts.Parse(pr);
        Assert.False(rpfOpts.Gen9);
        Assert.False(rpfOpts.Recursive);
        Assert.False(rpfOpts.Verbose);
        Assert.False(rpfOpts.Json);
        Assert.Equal(SizeFormat.IEC, rpfOpts.SizeFormat);
        Assert.Empty(rpfOpts.Filters);
    }

    [Fact]
    public void Parse_MultipleFilters()
    {
        RootCommand root = [];
        RpfCommandOptions opts = new();
        opts.AddTo(root);
        ParseResult pr = root.Parse("--rpf /tmp/test.rpf --exe /tmp/testdir --filter *.ydr *.ytd");
        Assert.Empty(pr.Errors);
        RpfOptions rpfOpts = opts.Parse(pr);
        Assert.Equal(2, rpfOpts.Filters.Length);
    }

    [Fact]
    public void Parse_Aliases()
    {
        RootCommand root = [];
        RpfCommandOptions opts = new();
        opts.AddTo(root);
        ParseResult pr = root.Parse("-r /tmp/test.rpf -e /tmp/testdir -g -R -v -t 2");
        Assert.Empty(pr.Errors);
        RpfOptions rpfOpts = opts.Parse(pr);
        Assert.True(rpfOpts.Gen9);
        Assert.True(rpfOpts.Recursive);
        Assert.True(rpfOpts.Verbose);
        Assert.Equal(2, rpfOpts.Threads);
    }
}
#endif

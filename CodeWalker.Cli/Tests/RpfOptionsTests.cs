using System.CommandLine;

using CodeWalker.Cli.Helpers;

using Xunit;

namespace CodeWalker.Cli.Tests;

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

    [Fact]
    public void AddTo_IncludesThreadsByDefault()
    {
        RootCommand root = [];
        RpfCommandOptions opts = new();
        opts.AddTo(root);
        ParseResult pr = root.Parse("--rpf /tmp/test.rpf --exe /tmp/testdir  --threads 2");
        Assert.Empty(pr.Errors);
    }

    [Fact]
    public void AddTo_ExcludesThreads_WhenFlagIsFalse()
    {
        RootCommand root = [];
        RpfCommandOptions opts = new();
        opts.AddTo(root, includeThreads: false);
        ParseResult pr = root.Parse("--rpf /tmp/test.rpf --exe /tmp/testdir  --threads 2");
        Assert.NotEmpty(pr.Errors);
    }
}

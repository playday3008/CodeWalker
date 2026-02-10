using System.CommandLine;

using CodeWalker.Cli.Helpers;

using Xunit;

namespace CodeWalker.Cli.Tests;

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

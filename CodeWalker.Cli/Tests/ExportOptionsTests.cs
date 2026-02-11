using System.CommandLine;

using CodeWalker.Cli.Handlers;
using CodeWalker.Cli.Helpers;

using Xunit;

namespace CodeWalker.Cli.Tests;

public sealed class ExportOptionsTests
{
    [Fact]
    public void Parse_MapsAllValues()
    {
        RootCommand root = [];
        ExportCommandOptions opts = new();
        opts.AddTo(root);
        ParseResult pr = root.Parse(
            "--rpf /tmp/test.rpf --exe /tmp/testdir --output /tmp/out --dry-run --no-overwrite --progress --gen9 --recursive --verbose --json --si --threads 2"
        );
        Assert.Empty(pr.Errors);
        ExportOptions exportOpts = opts.Parse(pr);
        Assert.EndsWith("out", exportOpts.OutputPath);
        Assert.True(exportOpts.DryRun);
        Assert.True(exportOpts.NoOverwrite);
        Assert.True(exportOpts.Progress);
        // Verify RPF sub-options are populated
        Assert.True(exportOpts.Rpf.Gen9);
        Assert.True(exportOpts.Rpf.Recursive);
        Assert.True(exportOpts.Rpf.Verbose);
        Assert.True(exportOpts.Rpf.Json);
        Assert.Equal(SizeFormat.SI, exportOpts.Rpf.SizeFormat);
        Assert.Equal(2, exportOpts.Rpf.Threads);
    }

    [Fact]
    public void Parse_Defaults()
    {
        RootCommand root = [];
        ExportCommandOptions opts = new();
        opts.AddTo(root);
        ParseResult pr = root.Parse("--rpf /tmp/test.rpf --exe /tmp/testdir");
        Assert.Empty(pr.Errors);
        ExportOptions exportOpts = opts.Parse(pr);
        Assert.False(exportOpts.DryRun);
        Assert.False(exportOpts.NoOverwrite);
        Assert.False(exportOpts.Progress);
        Assert.NotEmpty(exportOpts.OutputPath); // defaults to cwd
    }

    [Fact]
    public void Parse_DryRunAlias()
    {
        RootCommand root = [];
        ExportCommandOptions opts = new();
        opts.AddTo(root);
        ParseResult pr = root.Parse("--rpf /tmp/test.rpf --exe /tmp/testdir -n");
        Assert.Empty(pr.Errors);
        Assert.True(opts.Parse(pr).DryRun);
    }

    [Fact]
    public void Parse_ProgressAlias()
    {
        RootCommand root = [];
        ExportCommandOptions opts = new();
        opts.AddTo(root);
        ParseResult pr = root.Parse("--rpf /tmp/test.rpf --exe /tmp/testdir -P");
        Assert.Empty(pr.Errors);
        Assert.True(opts.Parse(pr).Progress);
    }

    [Fact]
    public void Parse_OutputAlias()
    {
        RootCommand root = [];
        ExportCommandOptions opts = new();
        opts.AddTo(root);
        ParseResult pr = root.Parse("--rpf /tmp/test.rpf --exe /tmp/testdir -o /tmp/mydir");
        Assert.Empty(pr.Errors);
        Assert.EndsWith("mydir", opts.Parse(pr).OutputPath);
    }
}

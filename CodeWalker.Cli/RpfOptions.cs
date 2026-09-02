using System.CommandLine;
using System.IO;
using CodeWalker.Cli.Helpers;

namespace CodeWalker.Cli;

public record RpfOptions
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
public sealed class RpfCommandOptions
{
    private readonly CommonCommandOptions _commonOpts = new();

    // csharpier-ignore-start
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
    // csharpier-ignore-end

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

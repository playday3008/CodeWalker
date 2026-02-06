using System;
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
    // csharpier-ignore-start
    public Option<FileInfo> Rpf { get; } = new("--rpf", "-r")
    {
        Description = "Path to the RPF file",
        Required = true,
    };

    public Option<DirectoryInfo> Exe { get; } = new("--exe", "-e")
    {
        Description = "Path to the GTA V installation directory (containing GTA5.exe)",
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

    public Option<bool> Verbose { get; } = new("--verbose", "-v")
    {
        Description = "Show verbose output",
    };

    public Option<bool> Json { get; } = new("--json")
    {
        Description = "Output results in JSON format for scripting",
    };

    public Option<bool> Recursive { get; } = new("--recursive", "-R")
    {
        Description = "Process nested RPF archives",
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
    // csharpier-ignore-end

    public void AddTo(Command command)
    {
        command.Add(Rpf);
        command.Add(Exe);
        command.Add(Gen9);
        command.Add(Filter);
        command.Add(Verbose);
        command.Add(Json);
        command.Add(Recursive);
        command.Add(Si);
        command.Add(Threads);
    }

    public RpfOptions Parse(ParseResult parseResult)
    {
        return new RpfOptions
        {
            RpfPath = parseResult.GetRequiredValue(Rpf).FullName,
            ExePath = parseResult.GetRequiredValue(Exe).FullName,
            Gen9 = parseResult.GetValue(Gen9),
            Filters = parseResult.GetValue(Filter) ?? [],
            Verbose = parseResult.GetValue(Verbose),
            Json = parseResult.GetValue(Json),
            Recursive = parseResult.GetValue(Recursive),
            Threads = parseResult.GetValue(Threads),
            SizeFormat = parseResult.GetValue(Si) ? SizeFormat.SI : SizeFormat.IEC,
        };
    }
}

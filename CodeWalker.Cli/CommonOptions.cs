using System;
using System.CommandLine;
using System.IO;

using CodeWalker.Cli.Helpers;

namespace CodeWalker.Cli;

public sealed record CommonOptions
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
public sealed class CommonCommandOptions
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

    public void AddTo(Command command)
    {
        command.Add(Exe);
        command.Add(Verbose);
        command.Add(Json);
        command.Add(Si);
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

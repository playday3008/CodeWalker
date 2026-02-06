using System;
using System.CommandLine;
using System.IO;

namespace CodeWalker.Cli;

public record ExportOptions
{
    public required RpfOptions Rpf { get; init; }
    public required string OutputPath { get; init; }
    public required bool DryRun { get; init; }
    public required bool NoOverwrite { get; init; }
    public required bool Progress { get; init; }
}

public sealed class ExportCommandOptions
{
    private readonly RpfCommandOptions _rpfOpts = new();

    // csharpier-ignore-start
    public Option<DirectoryInfo> Output { get; } = new("--output", "-o")
    {
        Description = "Output directory",
        DefaultValueFactory = _ => new DirectoryInfo(Directory.GetCurrentDirectory()),
    };

    public Option<bool> DryRun { get; } = new("--dry-run", "-n")
    {
        Description = "Show what would be exported without writing files",
    };

    public Option<bool> NoOverwrite { get; } = new("--no-overwrite")
    {
        Description = "Skip existing output files instead of overwriting",
    };

    public Option<bool> Progress { get; } = new("--progress", "-P")
    {
        Description = "Show progress bar during export",
    };
    // csharpier-ignore-end

    public void AddTo(Command command)
    {
        _rpfOpts.AddTo(command);
        command.Add(Output);
        command.Add(DryRun);
        command.Add(NoOverwrite);
        command.Add(Progress);
    }

    public ExportOptions Parse(ParseResult parseResult)
    {
        return new ExportOptions
        {
            Rpf = _rpfOpts.Parse(parseResult),
            OutputPath = parseResult.GetValue(Output)?.FullName ?? Directory.GetCurrentDirectory(),
            DryRun = parseResult.GetValue(DryRun),
            NoOverwrite = parseResult.GetValue(NoOverwrite),
            Progress = parseResult.GetValue(Progress),
        };
    }
}

public static class ExportHandler
{
    public static Command CreateCommand()
    {
        Command command = new(
            "export",
            "Export game files to external formats (XML, DDS, WAV, text)"
        )
        {
            ExportXmlHandler.CreateCommand(),
            ExportTexturesHandler.CreateCommand(),
            ExportAudioHandler.CreateCommand(),
            ExportTextHandler.CreateCommand(),
        };
        command.Aliases.Add("e");

        return command;
    }
}

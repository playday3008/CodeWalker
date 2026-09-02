using System.CommandLine;
using System.IO;
using System.Threading;

namespace CodeWalker.Cli.Handlers;

internal sealed record ExportOptions
{
    public required RpfOptions Rpf { get; init; }
    public required string OutputPath { get; init; }
    public required bool DryRun { get; init; }
    public required bool NoOverwrite { get; init; }
    public required bool Progress { get; init; }
}

internal sealed class ExportCommandOptions
{
    private readonly RpfCommandOptions _rpfOpts = new();

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

    public void AddTo(Command command)
    {
        this._rpfOpts.AddTo(command);
        command.Add(this.Output);
        command.Add(this.DryRun);
        command.Add(this.NoOverwrite);
        command.Add(this.Progress);
    }

    public ExportOptions Parse(ParseResult parseResult) => new()
    {
        Rpf = this._rpfOpts.Parse(parseResult),
        OutputPath = parseResult.GetValue(this.Output)?.FullName ?? Directory.GetCurrentDirectory(),
        DryRun = parseResult.GetValue(this.DryRun),
        NoOverwrite = parseResult.GetValue(this.NoOverwrite),
        Progress = parseResult.GetValue(this.Progress),
    };
}

internal static class ExportHandler
{
    public static Command CreateCommand(CancellationToken cancellationToken = default)
    {
        Command command = new(
            "export",
            "Export game files to external formats (XML, DDS, WAV, text)"
        )
        {
            ExportXmlHandler.CreateCommand(cancellationToken),
            ExportTexturesHandler.CreateCommand(cancellationToken),
            ExportAudioHandler.CreateCommand(cancellationToken),
            ExportTextHandler.CreateCommand(cancellationToken),
        };
        command.Aliases.Add("e");

        return command;
    }
}

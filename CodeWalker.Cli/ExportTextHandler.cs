using System.CommandLine;
using System.IO;
using System.Text;

using CodeWalker.GameFiles;

namespace CodeWalker.Cli;

internal static class ExportTextHandler
{
    private static readonly string[] DefaultFilters = ["*.gxt2"];

    public static Command CreateCommand()
    {
        ExportCommandOptions exportOpts = new();

        Command command = new("text", "Export .gxt2 localization files to plain text");
        exportOpts.AddTo(command);
        command.Aliases.Add("g");
        command.Aliases.Add("gxt2");

        command.SetAction(parseResult =>
        {
            ExportOptions options = exportOpts.Parse(parseResult);
            if (options.Rpf.Filters.Length == 0)
            {
                options = options with { Rpf = options.Rpf with { Filters = DefaultFilters } };
            }
            return ExportService.Execute(options, "txt", "Text", ProcessFile);
        });

        return command;
    }

    private static (Json.ExportFileEntry? entry, string? error) ProcessFile(
        RpfFileEntry fileEntry,
        byte[] data,
        string fileOutputDir,
        bool noOverwrite
    )
    {
        Gxt2File gxt = RpfFile.GetFile<Gxt2File>(fileEntry, data);
        if (gxt == null)
        {
            return (
                new Json.ExportFileEntry
                {
                    Path = fileEntry.Path,
                    Name = fileEntry.Name,
                    OutputFiles = 0,
                    Status = "skipped",
                },
                null
            );
        }

        string text = gxt.ToText();

        if (string.IsNullOrEmpty(text))
        {
            return (
                new Json.ExportFileEntry
                {
                    Path = fileEntry.Path,
                    Name = fileEntry.Name,
                    OutputFiles = 0,
                    Status = "skipped",
                },
                null
            );
        }

        if (!Directory.Exists(fileOutputDir))
        {
            Directory.CreateDirectory(fileOutputDir);
        }

        string outputFileName = Path.GetFileNameWithoutExtension(fileEntry.Name) + ".txt";
        string outputPath = Path.Combine(fileOutputDir, outputFileName);

        if (noOverwrite && File.Exists(outputPath))
        {
            return (
                new Json.ExportFileEntry
                {
                    Path = fileEntry.Path,
                    Name = fileEntry.Name,
                    OutputFiles = 0,
                    Status = "skipped",
                },
                null
            );
        }

        File.WriteAllText(outputPath, text, Encoding.UTF8);

        return (
            new Json.ExportFileEntry
            {
                Path = fileEntry.Path,
                Name = fileEntry.Name,
                OutputPath = outputPath,
                OutputFiles = 1,
                Status = "exported",
            },
            null
        );
    }
}

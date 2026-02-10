using System.CommandLine;
using System.IO;
using System.Text;
using System.Threading;

using CodeWalker.GameFiles;

namespace CodeWalker.Cli;

internal static class ExportXmlHandler
{
    public static Command CreateCommand(CancellationToken cancellationToken = default)
    {
        ExportCommandOptions exportOpts = new();

        Command command = new("xml", "Export binary game files to XML");
        exportOpts.AddTo(command);
        command.Aliases.Add("x");

        command.SetAction(parseResult =>
        {
            ExportOptions options = exportOpts.Parse(parseResult);
            return ExportService.Execute(options, "xml", "XML", ProcessFile, cancellationToken);
        });

        return command;
    }

    private static (Json.ExportFileEntry entry, string? _) ProcessFile(
        RpfFileEntry fileEntry,
        byte[] data,
        string fileOutputDir,
        bool noOverwrite
    )
    {
        string xml = MetaXml.GetXml(fileEntry, data, out string filename, fileOutputDir);

        if (string.IsNullOrEmpty(xml))
        {
            return (
                new Json.ExportFileEntry
                {
                    Path = fileEntry.Path,
                    Name = fileEntry.Name,
                    OutputFiles = 0,
                    Status = "unsupported",
                },
                null
            );
        }

        if (!string.IsNullOrEmpty(fileOutputDir) && !Directory.Exists(fileOutputDir))
        {
            _ = Directory.CreateDirectory(fileOutputDir);
        }

        string outputPath = Path.Combine(fileOutputDir, filename);

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

        File.WriteAllText(outputPath, xml, Encoding.UTF8);

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

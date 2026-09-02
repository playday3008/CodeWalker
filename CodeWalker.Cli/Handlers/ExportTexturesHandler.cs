using System.CommandLine;
using System.IO;
using System.Threading;

using CodeWalker.GameFiles;
using CodeWalker.Utils;

namespace CodeWalker.Cli.Handlers;

internal static class ExportTexturesHandler
{
    private static readonly string[] DefaultFilters = ["*.ytd"];

    public static Command CreateCommand(CancellationToken cancellationToken = default)
    {
        ExportCommandOptions exportOpts = new();

        Command command = new("textures", "Export .ytd texture dictionaries to DDS files");
        exportOpts.AddTo(command);
        command.Aliases.Add("t");
        command.Aliases.Add("ytd");

        command.SetAction(parseResult =>
        {
            ExportOptions options = exportOpts.Parse(parseResult);
            if (options.Rpf.Filters.Length == 0)
            {
                options = options with { Rpf = options.Rpf with { Filters = DefaultFilters } };
            }
            return ExportService.Execute(options, "dds", "Texture", ProcessFile, cancellationToken);
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
        YtdFile ytd = RpfFile.GetFile<YtdFile>(fileEntry, data);
        if (
            ytd?.TextureDict?.Textures?.data_items == null
            || ytd.TextureDict.Textures.data_items.Length == 0
        )
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

        bool dirCreated = false;
        int texCount = 0;
        foreach (Texture tex in ytd.TextureDict.Textures.data_items)
        {
            string texName = (tex.Name ?? "unknown") + ".dds";
            string outputPath = Path.Combine(fileOutputDir, texName);

            if (noOverwrite && File.Exists(outputPath))
                continue;

            if (!dirCreated)
            {
                _ = Directory.CreateDirectory(fileOutputDir);
                dirCreated = true;
            }

            byte[] dds = DDSIO.GetDDSFile(tex);
            File.WriteAllBytes(outputPath, dds);
            texCount++;
        }

        return (
            new Json.ExportFileEntry
            {
                Path = fileEntry.Path,
                Name = fileEntry.Name,
                OutputPath = texCount > 0 ? fileOutputDir : null,
                OutputFiles = texCount,
                Status = texCount > 0 ? "exported" : "skipped",
            },
            null
        );
    }
}

using System.CommandLine;
using System.IO;

using CodeWalker.GameFiles;
using CodeWalker.Utils;

namespace CodeWalker.Cli;

internal static class ExportTexturesHandler
{
    private static readonly string[] DefaultFilters = ["*.ytd"];

    public static Command CreateCommand()
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
            return ExportService.Execute(options, "dds", "Texture", ProcessFile);
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
                    Status = "skipped",
                },
                null
            );
        }

        if (!Directory.Exists(fileOutputDir))
        {
            Directory.CreateDirectory(fileOutputDir);
        }

        int texCount = 0;
        foreach (Texture tex in ytd.TextureDict.Textures.data_items)
        {
            string texName = (tex.Name ?? "unknown") + ".dds";
            string outputPath = Path.Combine(fileOutputDir, texName);

            if (noOverwrite && File.Exists(outputPath))
                continue;

            byte[] dds = DDSIO.GetDDSFile(tex);
            File.WriteAllBytes(outputPath, dds);
            texCount++;
        }

        return (
            new Json.ExportFileEntry
            {
                Path = fileEntry.Path,
                Name = fileEntry.Name,
                OutputPath = fileOutputDir,
                OutputFiles = texCount,
                Status = "exported",
            },
            null
        );
    }
}

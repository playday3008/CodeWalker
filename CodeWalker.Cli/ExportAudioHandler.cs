using System.CommandLine;
using System.IO;

using CodeWalker.GameFiles;

namespace CodeWalker.Cli;

internal static class ExportAudioHandler
{
    private static readonly string[] DefaultFilters = ["*.awc"];

    public static Command CreateCommand()
    {
        ExportCommandOptions exportOpts = new();

        Command command = new("audio", "Export .awc audio containers to WAV/MIDI files");
        exportOpts.AddTo(command);
        command.Aliases.Add("a");
        command.Aliases.Add("awc");

        command.SetAction(parseResult =>
        {
            ExportOptions options = exportOpts.Parse(parseResult);
            if (options.Rpf.Filters.Length == 0)
            {
                options = options with { Rpf = options.Rpf with { Filters = DefaultFilters } };
            }
            return ExportService.Execute(options, "wav", "Audio", ProcessFile);
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
        AwcFile awc = RpfFile.GetFile<AwcFile>(fileEntry, data);
        if (awc?.Streams == null || awc.Streams.Length == 0)
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
        int streamCount = 0;
        foreach (AwcStream stream in awc.Streams)
        {
            if (stream.Hash == 0)
                continue;

            string streamName = stream.Name;

            if (stream.MidiChunk?.Data != null)
            {
                string midiPath = Path.Combine(fileOutputDir, streamName + ".midi");
                if (noOverwrite && File.Exists(midiPath))
                    continue;
                if (!dirCreated)
                {
                    if (!Directory.Exists(fileOutputDir))
                        Directory.CreateDirectory(fileOutputDir);
                    dirCreated = true;
                }
                File.WriteAllBytes(midiPath, stream.MidiChunk.Data);
                streamCount++;
            }
            else
            {
                byte[] wav = stream.GetWavFile();
                string wavPath = Path.Combine(fileOutputDir, streamName + ".wav");
                if (noOverwrite && File.Exists(wavPath))
                    continue;
                if (!dirCreated)
                {
                    if (!Directory.Exists(fileOutputDir))
                        Directory.CreateDirectory(fileOutputDir);
                    dirCreated = true;
                }
                File.WriteAllBytes(wavPath, wav);
                streamCount++;
            }
        }

        return (
            new Json.ExportFileEntry
            {
                Path = fileEntry.Path,
                Name = fileEntry.Name,
                OutputPath = streamCount > 0 ? fileOutputDir : null,
                OutputFiles = streamCount,
                Status = streamCount > 0 ? "exported" : "skipped",
            },
            null
        );
    }
}

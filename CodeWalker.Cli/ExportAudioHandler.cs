using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

namespace CodeWalker.Cli;

public static class ExportAudioHandler
{
    private static readonly string[] DefaultFilters = ["*.awc"];

    public static Command CreateCommand()
    {
        ExportCommandOptions exportOpts = new();

        Command command = new("audio", "Export .awc audio containers to WAV/MIDI files");
        exportOpts.AddTo(command);
        command.Aliases.Add("a");

        command.SetAction(parseResult =>
        {
            ExportOptions options = exportOpts.Parse(parseResult);
            if (options.Rpf.Filters.Length == 0)
            {
                options = options with { Rpf = options.Rpf with { Filters = DefaultFilters } };
            }
            return Execute(options);
        });

        return command;
    }

    public static int Execute(ExportOptions options)
    {
        List<Json.ExportFileEntry> files = [];
        List<string> errorMessages = [];

        Json.ExportResult result = new()
        {
            Success = false,
            RpfFile = options.Rpf.RpfPath,
            OutputDir = options.OutputPath,
            Format = "wav",
            TotalFiles = 0,
            Exported = 0,
            Skipped = 0,
            Errors = 0,
            DryRun = options.DryRun,
            Files = files,
            ErrorMessages = errorMessages,
        };

        string? initError = RpfService.ValidateAndLoadKeys(
            options.Rpf.RpfPath,
            options.Rpf.ExePath,
            options.Rpf.Gen9,
            options.Rpf.Json
        );
        if (initError != null)
        {
            return RpfService.ReportError(initError, options.Rpf.Json, result);
        }

        try
        {
            RpfFile rpf = RpfService.OpenRpf(
                options.Rpf.RpfPath,
                options.Rpf.Verbose,
                options.Rpf.Json,
                errorMessages
            );

            if (!options.Rpf.Json && options.DryRun)
            {
                Console.Error.WriteLine("Dry run mode - no files will be exported");
            }

            string outputDir = options.OutputPath;

            if (!options.DryRun && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            List<(RpfFile rpf, RpfFileEntry entry)> filesToExport = RpfService.CollectFiles(
                rpf,
                options.Rpf.Filters,
                options.Rpf.Recursive
            );

            result = result with { TotalFiles = filesToExport.Count };

            (bool success, Json.ExportFileEntry? jsonEntry, string? errorMessage)[] results = new (
                bool,
                Json.ExportFileEntry?,
                string?
            )[filesToExport.Count];

            object consoleLock = new();

            using (
                ProgressBar progress = new(
                    filesToExport.Count,
                    options.Progress && !options.Rpf.Json
                )
            )
            {
                Parallel.For(
                    0,
                    filesToExport.Count,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Max(1, options.Rpf.Threads),
                    },
                    i =>
                    {
                        (RpfFile sourceRpf, RpfFileEntry fileEntry) = filesToExport[i];
                        try
                        {
                            string relativePath =
                                Path.GetDirectoryName(fileEntry.Path)
                                    ?.Replace('\\', Path.DirectorySeparatorChar)
                                ?? "";

                            if (options.DryRun)
                            {
                                if (options.Rpf.Verbose && !options.Rpf.Json)
                                {
                                    lock (consoleLock)
                                    {
                                        Console.WriteLine($"Would export: {fileEntry.Path}");
                                    }
                                }
                                results[i] = (
                                    true,
                                    new Json.ExportFileEntry
                                    {
                                        Path = fileEntry.Path,
                                        Name = fileEntry.Name,
                                        OutputFiles = 0,
                                        Status = "dry_run",
                                    },
                                    null
                                );
                                progress.Increment(fileEntry.Path);
                                return;
                            }

                            byte[]? data = sourceRpf.ExtractFile(fileEntry);
                            if (data == null)
                            {
                                results[i] = (false, null, $"Failed to extract: {fileEntry.Path}");
                                progress.Increment();
                                return;
                            }

                            AwcFile awc = RpfFile.GetFile<AwcFile>(fileEntry, data);
                            if (awc?.Streams == null || awc.Streams.Length == 0)
                            {
                                results[i] = (
                                    false,
                                    new Json.ExportFileEntry
                                    {
                                        Path = fileEntry.Path,
                                        Name = fileEntry.Name,
                                        OutputFiles = 0,
                                        Status = "skipped",
                                    },
                                    null
                                );
                                progress.Increment(fileEntry.Path);
                                return;
                            }

                            string fileOutputDir = Path.Combine(outputDir, relativePath);
                            if (!Directory.Exists(fileOutputDir))
                            {
                                Directory.CreateDirectory(fileOutputDir);
                            }

                            int streamCount = 0;
                            foreach (AwcStream stream in awc.Streams)
                            {
                                // Skip multichannel source streams (Hash == 0)
                                if (stream.Hash == 0)
                                    continue;

                                string streamName = stream.Name;

                                if (stream.MidiChunk?.Data != null)
                                {
                                    string midiPath = Path.Combine(
                                        fileOutputDir,
                                        streamName + ".midi"
                                    );
                                    File.WriteAllBytes(midiPath, stream.MidiChunk.Data);
                                    streamCount++;
                                }
                                else
                                {
                                    byte[] wav = stream.GetWavFile();
                                    string wavPath = Path.Combine(
                                        fileOutputDir,
                                        streamName + ".wav"
                                    );
                                    File.WriteAllBytes(wavPath, wav);
                                    streamCount++;
                                }
                            }

                            if (options.Rpf.Verbose && !options.Rpf.Json && !options.Progress)
                            {
                                lock (consoleLock)
                                {
                                    Console.Error.WriteLine(
                                        $"Exported: {fileEntry.Path} -> {streamCount} stream(s)"
                                    );
                                }
                            }

                            results[i] = (
                                true,
                                new Json.ExportFileEntry
                                {
                                    Path = fileEntry.Path,
                                    Name = fileEntry.Name,
                                    OutputPath = fileOutputDir,
                                    OutputFiles = streamCount,
                                    Status = "exported",
                                },
                                null
                            );
                            progress.Increment(fileEntry.Path);
                        }
                        catch (Exception ex)
                        {
                            if (!options.Rpf.Json)
                            {
                                lock (consoleLock)
                                {
                                    Console.Error.WriteLine(
                                        $"Error exporting {fileEntry.Path}: {ex.Message}"
                                    );
                                }
                            }
                            results[i] = (
                                false,
                                null,
                                $"Error exporting {fileEntry.Path}: {ex.Message}"
                            );
                            progress.Increment();
                        }
                    }
                );
            }

            int exported = 0;
            int skipped = 0;
            int errors = 0;
            foreach (var (success, jsonEntry, errorMessage) in results)
            {
                if (success && jsonEntry?.Status == "exported")
                    exported++;

                if (jsonEntry?.Status == "skipped")
                    skipped++;

                if (jsonEntry != null)
                    files.Add(jsonEntry);

                if (!success && errorMessage != null)
                {
                    errors++;
                    errorMessages.Add(errorMessage);
                }
            }

            result = result with
            {
                Exported = exported,
                Skipped = skipped,
                Errors = errors,
                Success = errors == 0,
            };

            if (options.Rpf.Json)
            {
                Console.WriteLine(
                    JsonSerializer.Serialize(result, RpfService.JsonSerializerOptions)
                );
            }
            else
            {
                Console.Error.WriteLine();
                string action = options.DryRun ? "would be exported" : "exported";
                Console.Error.WriteLine(
                    $"Audio export complete: {exported} files {action}, {skipped} skipped, {errors} errors"
                );
            }

            return errors > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            return RpfService.ReportError(
                ex.Message,
                options.Rpf.Json,
                result,
                options.Rpf.Verbose ? ex.StackTrace : null
            );
        }
    }
}

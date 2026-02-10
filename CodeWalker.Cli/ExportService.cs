using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

namespace CodeWalker.Cli;

/// <summary>
/// Delegate for processing a single file entry during export.
/// Returns an <see cref="Json.ExportFileEntry"/> on success (status = "exported", "unsupported", "skipped"),
/// or a tuple with a null entry and error string on failure.
/// </summary>
/// <param name="fileEntry">The RPF file entry to process.</param>
/// <param name="data">The raw file data extracted from the RPF.</param>
/// <param name="fileOutputDir">The output directory for this file (includes relative path).</param>
/// <param name="noOverwrite">When true, skip files that already exist at the output path.</param>
internal delegate (Json.ExportFileEntry? entry, string? error) ExportFileProcessor(
    RpfFileEntry fileEntry,
    byte[] data,
    string fileOutputDir,
    bool noOverwrite
);

internal static class ExportService
{
    internal readonly record struct ExportAggregation
    {
        public required int Exported { get; init; }
        public required int Skipped { get; init; }
        public required int Errors { get; init; }
        public required IReadOnlyList<Json.ExportFileEntry> Files { get; init; }
        public required IReadOnlyList<string> ErrorMessages { get; init; }
    }

    internal static (Json.ExportFileEntry? entry, string? error) ProcessSingleFile(
        RpfFileEntry fileEntry,
        byte[]? data,
        string outputDir,
        bool dryRun,
        bool noOverwrite,
        ExportFileProcessor processor
    )
    {
        string relativePath =
            Path.GetDirectoryName(fileEntry.Path)
                ?.Replace('\\', Path.DirectorySeparatorChar)
            ?? "";

        string fileOutputDir = Path.Combine(outputDir, relativePath);

        if (dryRun)
        {
            return (
                new Json.ExportFileEntry
                {
                    Path = fileEntry.Path,
                    Name = fileEntry.Name,
                    OutputFiles = 0,
                    Status = "dry_run",
                },
                null
            );
        }

        if (data == null)
        {
            return (null, $"Failed to extract: {fileEntry.Path}");
        }

        (Json.ExportFileEntry? entry, string? error) = processor(
            fileEntry,
            data,
            fileOutputDir,
            noOverwrite
        );

        if (error != null)
        {
            return (entry, error);
        }

        if (entry != null)
        {
            return (entry, null);
        }

        return (null, $"No result for: {fileEntry.Path}");
    }

    internal static ExportAggregation AggregateResults(
        (Json.ExportFileEntry? jsonEntry, string? errorMessage)[] results,
        IReadOnlyList<string> scanErrors,
        int filterSkipped
    )
    {
        int exported = 0;
        int skipped = 0;
        int errors = 0;
        List<Json.ExportFileEntry> files = [];
        List<string> errorMessages = [.. scanErrors];

        foreach ((Json.ExportFileEntry? jsonEntry, string? errorMessage) in results)
        {
            if (errorMessage == null && jsonEntry?.Status is "exported" or "dry_run")
                exported++;

            if (jsonEntry?.Status is "unsupported" or "skipped")
                skipped++;

            if (jsonEntry != null)
                files.Add(jsonEntry);

            if (errorMessage != null)
            {
                errors++;
                errorMessages.Add(errorMessage);
            }
        }

        skipped += filterSkipped;

        return new ExportAggregation
        {
            Exported = exported,
            Skipped = skipped,
            Errors = errors,
            Files = files,
            ErrorMessages = errorMessages,
        };
    }

    public static int Execute(
        ExportOptions options,
        string format,
        string summaryLabel,
        ExportFileProcessor processor
    )
    {
        Json.ExportResult ErrorResult(string[] errorMessages) =>
            new()
            {
                Success = false,
                RpfFile = options.Rpf.RpfPath,
                OutputDir = options.OutputPath,
                Format = format,
                TotalFiles = 0,
                Exported = 0,
                Skipped = 0,
                Errors = 0,
                DryRun = options.DryRun,
                Files = [],
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
            return RpfService.ReportError(initError, options.Rpf.Json, ErrorResult([]));
        }

        try
        {
            List<string> scanErrors = [];
            RpfFile rpf = RpfService.OpenRpf(
                options.Rpf.RpfPath,
                options.Rpf.Verbose,
                options.Rpf.Json,
                scanErrors
            );

            if (!options.Rpf.Json && options.DryRun)
            {
                Console.Error.WriteLine("Dry run mode - no files will be exported");
            }

            string outputDir = options.OutputPath;

            if (!options.DryRun && !Directory.Exists(outputDir))
            {
                _ = Directory.CreateDirectory(outputDir);
            }

            List<(RpfFile rpf, RpfFileEntry entry)> filesToExport = RpfService.CollectFiles(
                rpf,
                options.Rpf.Filters,
                options.Rpf.Recursive
            );

            int totalNonRpfFiles = RpfService.CountNonRpfFiles(rpf, options.Rpf.Recursive);
            int filterSkipped = totalNonRpfFiles - filesToExport.Count;

            (Json.ExportFileEntry? jsonEntry, string? errorMessage)[] results =
                new (Json.ExportFileEntry?, string?)[filesToExport.Count];

            object consoleLock = new();

            using (
                ProgressBar progress = new(
                    filesToExport.Count,
                    options.Progress && !options.Rpf.Json
                )
            )
            {
                _ = Parallel.For(
                    0,
                    filesToExport.Count,
                    new ParallelOptions { MaxDegreeOfParallelism = options.Rpf.Threads },
                    i =>
                    {
                        (RpfFile sourceRpf, RpfFileEntry fileEntry) = filesToExport[i];
                        try
                        {
                            byte[]? data = options.DryRun
                                ? null
                                : sourceRpf.ExtractFile(fileEntry);

                            (Json.ExportFileEntry? entry, string? error) result = ProcessSingleFile(
                                fileEntry,
                                data,
                                outputDir,
                                options.DryRun,
                                options.NoOverwrite,
                                processor
                            );

                            results[i] = result;

                            if (
                                result.entry != null
                                && options.Rpf.Verbose
                                && !options.Rpf.Json
                                && !options.Progress
                            )
                            {
                                if (options.DryRun)
                                {
                                    lock (consoleLock)
                                    {
                                        Console.WriteLine(
                                            $"Would export: {fileEntry.Path}"
                                        );
                                    }
                                }
                                else if (result.entry.Status == "exported")
                                {
                                    lock (consoleLock)
                                    {
                                        Console.Error.WriteLine(
                                            $"Exported: {fileEntry.Path} -> {result.entry.OutputFiles} file(s)"
                                        );
                                    }
                                }
                            }

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
                                null,
                                $"Error exporting {fileEntry.Path}: {ex.Message}"
                            );
                            progress.Increment();
                        }
                    }
                );
            }

            ExportAggregation agg = AggregateResults(results, scanErrors, filterSkipped);

            Json.ExportResult jsonResult = new()
            {
                Success = agg.Errors == 0 && scanErrors.Count == 0,
                RpfFile = options.Rpf.RpfPath,
                OutputDir = options.OutputPath,
                Format = format,
                TotalFiles = totalNonRpfFiles,
                Exported = agg.Exported,
                Skipped = agg.Skipped,
                Errors = agg.Errors,
                DryRun = options.DryRun,
                Files = agg.Files,
                ErrorMessages = agg.ErrorMessages,
            };

            if (options.Rpf.Json)
            {
                Console.WriteLine(
                    JsonSerializer.Serialize(jsonResult, RpfService.JsonSerializerOptions)
                );
            }
            else
            {
                Console.Error.WriteLine();
                string action = options.DryRun ? "would be exported" : "exported";
                Console.Error.WriteLine(
                    $"{summaryLabel} export complete: {agg.Exported} files {action}, {agg.Skipped} skipped, {agg.Errors} errors"
                );
            }

            return (agg.Errors > 0 || scanErrors.Count > 0) ? 1 : 0;
        }
        catch (Exception ex)
        {
            return RpfService.ReportError(
                ex.Message,
                options.Rpf.Json,
                ErrorResult([]),
                options.Rpf.Verbose ? ex.StackTrace : null
            );
        }
    }
}

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
public delegate (Json.ExportFileEntry? entry, string? error) ExportFileProcessor(
    RpfFileEntry fileEntry,
    byte[] data,
    string fileOutputDir,
    bool noOverwrite
);

public static class ExportService
{
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
                Directory.CreateDirectory(outputDir);
            }

            List<(RpfFile rpf, RpfFileEntry entry)> filesToExport = RpfService.CollectFiles(
                rpf,
                options.Rpf.Filters,
                options.Rpf.Recursive
            );

            int totalNonRpfFiles = RpfService.CountNonRpfFiles(rpf, options.Rpf.Recursive);
            int filterSkipped = totalNonRpfFiles - filesToExport.Count;

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
                    new ParallelOptions { MaxDegreeOfParallelism = options.Rpf.Threads },
                    i =>
                    {
                        (RpfFile sourceRpf, RpfFileEntry fileEntry) = filesToExport[i];
                        try
                        {
                            string relativePath =
                                Path.GetDirectoryName(fileEntry.Path)
                                    ?.Replace('\\', Path.DirectorySeparatorChar)
                                ?? "";

                            string fileOutputDir = Path.Combine(outputDir, relativePath);

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

                            (Json.ExportFileEntry? entry, string? error) = processor(
                                fileEntry,
                                data,
                                fileOutputDir,
                                options.NoOverwrite
                            );

                            if (error != null)
                            {
                                results[i] = (false, entry, error);
                            }
                            else if (entry != null)
                            {
                                if (
                                    options.Rpf.Verbose
                                    && !options.Rpf.Json
                                    && !options.Progress
                                    && entry.Status == "exported"
                                )
                                {
                                    lock (consoleLock)
                                    {
                                        Console.Error.WriteLine(
                                            $"Exported: {fileEntry.Path} -> {entry.OutputFiles} file(s)"
                                        );
                                    }
                                }
                                results[i] = (true, entry, null);
                            }
                            else
                            {
                                results[i] = (false, null, $"No result for: {fileEntry.Path}");
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
            List<Json.ExportFileEntry> files = [];
            List<string> errorMessages = new(scanErrors);

            foreach (var (success, jsonEntry, errorMessage) in results)
            {
                if (success && jsonEntry?.Status == "exported")
                    exported++;

                if (jsonEntry?.Status is "unsupported" or "skipped")
                    skipped++;

                if (jsonEntry != null)
                    files.Add(jsonEntry);

                if (!success && errorMessage != null)
                {
                    errors++;
                    errorMessages.Add(errorMessage);
                }
            }

            skipped += filterSkipped;

            Json.ExportResult result = new()
            {
                Success = errors == 0,
                RpfFile = options.Rpf.RpfPath,
                OutputDir = options.OutputPath,
                Format = format,
                TotalFiles = filesToExport.Count,
                Exported = exported,
                Skipped = skipped,
                Errors = errors,
                DryRun = options.DryRun,
                Files = files.ToArray(),
                ErrorMessages = errorMessages.ToArray(),
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
                    $"{summaryLabel} export complete: {exported} files {action}, {skipped} skipped, {errors} errors"
                );
            }

            return errors > 0 ? 1 : 0;
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

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

#if !NETCOREAPP
using CodeWalker.Cli.Polyfills;
#endif

namespace CodeWalker.Cli;

internal sealed record ExtractOptions
{
    public required RpfOptions Rpf { get; init; }
    public required string? OutputPath { get; init; }
    public required bool DryRun { get; init; }
    public required bool NoOverwrite { get; init; }
    public required bool Progress { get; init; }
}

internal static class ExtractHandler
{
    public static Command CreateCommand()
    {
        RpfCommandOptions rpfOpts = new();
        Option<DirectoryInfo> outputOption = new("--output", "-o")
        {
            Description = "Output directory",
            DefaultValueFactory = _ => new DirectoryInfo(Directory.GetCurrentDirectory()),
        };

        Option<bool> dryRunOption = new("--dry-run", "-n")
        {
            Description = "Show what would be extracted without actually extracting",
        };

        Option<bool> noOverwriteOption = new("--no-overwrite")
        {
            Description = "Skip existing files instead of overwriting",
        };

        Option<bool> progressOption = new("--progress", "-P")
        {
            Description = "Show progress bar during extraction",
        };

        Command command = new("extract", "Extract files from an RPF archive")
        {
            outputOption,
            dryRunOption,
            noOverwriteOption,
            progressOption,
        };
        rpfOpts.AddTo(command);
        command.Aliases.Add("x");

        command.SetAction(parseResult =>
        {
            ExtractOptions options = new()
            {
                Rpf = rpfOpts.Parse(parseResult),
                OutputPath = parseResult.GetValue(outputOption)?.FullName,
                DryRun = parseResult.GetValue(dryRunOption),
                NoOverwrite = parseResult.GetValue(noOverwriteOption),
                Progress = parseResult.GetValue(progressOption),
            };
            return Execute(options);
        });

        return command;
    }

    public static int Execute(ExtractOptions options)
    {
        Json.ExtractResult ErrorResult(string[] errorMessages) =>
            new()
            {
                Success = false,
                RpfFile = options.Rpf.RpfPath,
                OutputDir = options.OutputPath ?? Directory.GetCurrentDirectory(),
                TotalFiles = 0,
                Extracted = 0,
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
                Console.Error.WriteLine("Dry run mode - no files will be extracted");
            }

            string outputDir = options.OutputPath ?? Directory.GetCurrentDirectory();

            if (!options.DryRun && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Collect files first for progress bar
            List<(RpfFile rpf, RpfFileEntry entry)> filesToExtract = RpfService.CollectFiles(
                rpf,
                options.Rpf.Filters,
                options.Rpf.Recursive
            );

            // Count non-RPF files that were excluded by filters
            int totalNonRpfFiles = RpfService.CountNonRpfFiles(rpf, options.Rpf.Recursive);
            int skipped = totalNonRpfFiles - filesToExtract.Count;
            int overwriteSkipped = 0;

            // Process files in parallel, storing results by index to preserve order
            (bool success, Json.FileEntry? jsonEntry, string? errorMessage)[] results = new (
                bool,
                Json.FileEntry?,
                string?
            )[filesToExtract.Count];

            object consoleLock = new();

            using (
                ProgressBar progress = new(
                    filesToExtract.Count,
                    options.Progress && !options.Rpf.Json
                )
            )
            {
                Parallel.For(
                    0,
                    filesToExtract.Count,
                    new ParallelOptions { MaxDegreeOfParallelism = options.Rpf.Threads },
                    i =>
                    {
                        (RpfFile sourceRpf, RpfFileEntry fileEntry) = filesToExtract[i];
                        try
                        {
                            string relativePath = fileEntry.Path;
                            string outputPath = Path.Combine(
                                outputDir,
                                relativePath.Replace("\\", Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                            );
                            string? fileDir = Path.GetDirectoryName(outputPath);

                            long size = fileEntry.GetFileSize();
                            string ext = Path.GetExtension(fileEntry.Name).ToLowerInvariant();

                            Json.FileEntry jsonEntry = new()
                            {
                                Path = fileEntry.Path,
                                Name = fileEntry.Name,
                                Size = size,
                                SizeFormatted = options.Rpf.SizeFormat.ToFormattedString(size),
                                Type = RpfService.GetFileType(fileEntry),
                                Extension = ext,
                            };

                            if (options.DryRun)
                            {
                                if (options.Rpf.Verbose && !options.Rpf.Json)
                                {
                                    lock (consoleLock)
                                    {
                                        Console.WriteLine($"Would extract: {fileEntry.Path}");
                                    }
                                }
                                results[i] = (true, jsonEntry with { Status = "dry_run" }, null);
                            }
                            else if (options.NoOverwrite && File.Exists(outputPath))
                            {
                                Interlocked.Increment(ref overwriteSkipped);
                                if (options.Rpf.Verbose && !options.Rpf.Json && !options.Progress)
                                {
                                    lock (consoleLock)
                                    {
                                        Console.Error.WriteLine(
                                            $"Skipping (exists): {fileEntry.Path}"
                                        );
                                    }
                                }
                                results[i] = (false, jsonEntry with { Status = "skipped" }, null);
                            }
                            else
                            {
                                if (!string.IsNullOrEmpty(fileDir) && !Directory.Exists(fileDir))
                                {
                                    Directory.CreateDirectory(fileDir);
                                }

                                if (options.Rpf.Verbose && !options.Rpf.Json && !options.Progress)
                                {
                                    lock (consoleLock)
                                    {
                                        Console.Error.WriteLine($"Extracting: {fileEntry.Path}");
                                    }
                                }

                                byte[]? data = sourceRpf.ExtractFile(fileEntry);
                                if (data != null)
                                {
                                    File.WriteAllBytes(outputPath, data);
                                    results[i] = (
                                        true,
                                        jsonEntry with
                                        {
                                            Status = "extracted",
                                        },
                                        null
                                    );
                                }
                                else
                                {
                                    if (options.Rpf.Verbose && !options.Rpf.Json)
                                    {
                                        lock (consoleLock)
                                        {
                                            Console.Error.WriteLine(
                                                $"Warning: Failed to extract {fileEntry.Path}"
                                            );
                                        }
                                    }
                                    results[i] = (
                                        false,
                                        null,
                                        $"Failed to extract: {fileEntry.Path}"
                                    );
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
                                        $"Error extracting {fileEntry.Path}: {ex.Message}"
                                    );
                                }
                            }
                            results[i] = (
                                false,
                                null,
                                $"Error extracting {fileEntry.Path}: {ex.Message}"
                            );
                            progress.Increment();
                        }
                    }
                );
            }

            // Aggregate results in order
            int extracted = 0;
            int errors = 0;
            List<Json.FileEntry> files = [];
            List<string> errorMessages = new(scanErrors);

            foreach (var (success, jsonEntry, errorMessage) in results)
            {
                if (success)
                    extracted++;

                if (jsonEntry != null)
                    files.Add(jsonEntry);

                if (!success && errorMessage != null)
                {
                    errors++;
                    errorMessages.Add(errorMessage);
                }
            }

            skipped += overwriteSkipped;

            Json.ExtractResult result = new()
            {
                Success = errors == 0,
                RpfFile = options.Rpf.RpfPath,
                OutputDir = options.OutputPath ?? Directory.GetCurrentDirectory(),
                TotalFiles = totalNonRpfFiles,
                Extracted = extracted,
                Skipped = skipped,
                Errors = errors,
                DryRun = options.DryRun,
                Files = [.. files],
                ErrorMessages = [.. errorMessages],
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
                string action = options.DryRun ? "would be extracted" : "extracted";
                Console.Error.WriteLine(
                    $"Extraction complete: {extracted} files {action}, {skipped} skipped, {errors} errors"
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

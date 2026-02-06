using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

namespace CodeWalker.Cli;

public record ExtractOptions
{
    public required string RpfPath { get; init; }
    public required string ExePath { get; init; }
    public required string? OutputPath { get; init; }
    public required bool Gen9 { get; init; }
    public required string[] Filters { get; init; }
    public required bool Verbose { get; init; }
    public required bool Json { get; init; }
    public required bool Recursive { get; init; }
    public required bool DryRun { get; init; }
    public required bool Progress { get; init; }
    public required int Threads { get; init; }
    public required SizeFormat SizeFormat { get; init; } = SizeFormat.IEC;

    public static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true,
    };
}

public static class ExtractHandler
{
    public static Command CreateCommand()
    {
        // csharpier-ignore-start
        Option<FileInfo> rpfOption = new("--rpf", "-r")
        {
            Description = "Path to the RPF file",
            Required = true,
        };

        Option<DirectoryInfo> exeOption = new("--exe", "-e")
        {
            Description = "Path to the GTA V installation directory (containing GTA5.exe)",
            Required = true,
        };

        Option<bool> gen9Option = new("--gen9", "-g")
        {
            Description = "Use GTA V Enhanced (Gen9) mode",
        };

        Option<string[]> filterOption = new("--filter", "-f")
        {
            Description = "Filter files by glob patterns (e.g. *.ydd); can be specified multiple times",
            AllowMultipleArgumentsPerToken = true,
        };

        Option<bool> verboseOption = new("--verbose", "-v")
        {
            Description = "Show verbose output",
        };

        Option<bool> jsonOption = new("--json")
        {
            Description = "Output results in JSON format for scripting",
        };

        Option<bool> recursiveOption = new("--recursive", "-R")
        {
            Description = "Process nested RPF archives",
        };

        Option<bool> siOption = new("--si")
        {
            Description = "Use SI units (1000-based: KB, MB) instead of IEC (1024-based: KiB, MiB)",
        };

        Option<DirectoryInfo> outputOption = new("--output", "-o")
        {
            Description = "Output directory",
            DefaultValueFactory = _ => new DirectoryInfo(Directory.GetCurrentDirectory()),
        };

        Option<bool> dryRunOption = new("--dry-run", "-n")
        {
            Description = "Show what would be extracted without actually extracting",
        };

        Option<bool> progressOption = new("--progress", "-P")
        {
            Description = "Show progress bar during extraction",
        };

        Option<int> threadsOption = new("--threads", "-t")
        {
            Description = "Number of threads for parallel processing",
            DefaultValueFactory = _ => Environment.ProcessorCount,
        };
        // csharpier-ignore-end

        Command command = new("extract", "Extract files from an RPF archive")
        {
            rpfOption,
            exeOption,
            gen9Option,
            filterOption,
            verboseOption,
            outputOption,
            jsonOption,
            recursiveOption,
            dryRunOption,
            progressOption,
            siOption,
            threadsOption,
        };
        command.Aliases.Add("x");

        command.SetAction(parseResult =>
        {
            ExtractOptions options = new()
            {
                RpfPath = parseResult.GetRequiredValue(rpfOption).FullName,
                ExePath = parseResult.GetRequiredValue(exeOption).FullName,
                OutputPath = parseResult.GetValue(outputOption)?.FullName,
                Gen9 = parseResult.GetValue(gen9Option),
                Filters = parseResult.GetValue(filterOption) ?? [],
                Verbose = parseResult.GetValue(verboseOption),
                Json = parseResult.GetValue(jsonOption),
                Recursive = parseResult.GetValue(recursiveOption),
                DryRun = parseResult.GetValue(dryRunOption),
                Progress = parseResult.GetValue(progressOption),
                Threads = parseResult.GetValue(threadsOption),
                SizeFormat = parseResult.GetValue(siOption) ? SizeFormat.SI : SizeFormat.IEC,
            };
            return Execute(options);
        });

        return command;
    }

    public static int Execute(ExtractOptions options)
    {
        List<Json.FileEntry> files = [];
        List<string> errorMessages = [];

        Json.ExtractResult result = new()
        {
            Success = false,
            RpfFile = null!,
            OutputDir = null!,
            TotalFiles = 0,
            Extracted = 0,
            Skipped = 0,
            Errors = 0,
            DryRun = options.DryRun,
            Files = files,
            ErrorMessages = errorMessages,
        };

        // Validate inputs
        if (!File.Exists(options.RpfPath))
        {
            return ReportError($"RPF file not found: {options.RpfPath}", options, result);
        }

        string exeFile = options.Gen9 ? "GTA5_Enhanced.exe" : "GTA5.exe";
        if (!File.Exists(Path.Combine(options.ExePath, exeFile)))
        {
            return ReportError($"{exeFile} not found in: {options.ExePath}", options, result);
        }

        try
        {
            if (!options.Json)
            {
                Console.Error.WriteLine("Loading encryption keys...");
            }
            GTA5Keys.LoadFromPath(options.ExePath, options.Gen9);

            if (!options.Json)
            {
                Console.Error.WriteLine($"Opening RPF: {options.RpfPath}");
            }

            string rpfName = Path.GetFileName(options.RpfPath);
            RpfFile rpf = new(options.RpfPath, rpfName);

            rpf.ScanStructure(
                status =>
                {
                    if (options.Verbose && !options.Json)
                        Console.Error.WriteLine(status);
                },
                error =>
                {
                    if (!options.Json)
                        Console.Error.WriteLine($"Error: {error}");
                    errorMessages.Add(error);
                }
            );

            if (!options.Json)
            {
                Console.Error.WriteLine(
                    $"Found {rpf.GrandTotalFileCount} files in {rpf.GrandTotalRpfCount} archive(s)"
                );
            }

            result = result with
            {
                RpfFile = options.RpfPath,
                OutputDir = options.OutputPath ?? Directory.GetCurrentDirectory(),
                TotalFiles = rpf.GrandTotalFileCount,
            };

            if (!options.Json && options.DryRun)
            {
                Console.Error.WriteLine("Dry run mode - no files will be extracted");
            }

            string outputDir = options.OutputPath ?? Directory.GetCurrentDirectory();

            if (!options.DryRun && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Collect files first for progress bar
            List<(RpfFile rpf, RpfFileEntry entry)> filesToExtract = [];
            CollectFiles(rpf, options.Filters, options.Recursive, filesToExtract);

            // Count non-RPF files that were excluded by filters
            int totalNonRpfFiles = 0;
            CountNonRpfFiles(rpf, options.Recursive, ref totalNonRpfFiles);
            int skipped = totalNonRpfFiles - filesToExtract.Count;

            // Process files in parallel, storing results by index to preserve order
            (bool success, Json.FileEntry? jsonEntry, string? errorMessage)[] results = new (
                bool,
                Json.FileEntry?,
                string?
            )[filesToExtract.Count];

            object consoleLock = new();

            using (
                ProgressBar progress = new(filesToExtract.Count, options.Progress && !options.Json)
            )
            {
                Parallel.For(
                    0,
                    filesToExtract.Count,
                    new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Threads) },
                    i =>
                    {
                        (RpfFile sourceRpf, RpfFileEntry fileEntry) = filesToExtract[i];
                        try
                        {
                            string relativePath = fileEntry.Path;
                            string outputPath = Path.Combine(
                                outputDir,
                                relativePath.Replace("\\", Path.DirectorySeparatorChar.ToString())
                            );
                            string? fileDir = Path.GetDirectoryName(outputPath);

                            long size = fileEntry.GetFileSize();
                            string ext = Path.GetExtension(fileEntry.Name).ToLowerInvariant();

                            Json.FileEntry jsonEntry = new()
                            {
                                Path = fileEntry.Path,
                                Name = fileEntry.Name,
                                Size = size,
                                SizeFormatted = options.SizeFormat.ToFormattedString(size),
                                Type = GetFileType(fileEntry),
                                Extension = ext,
                            };

                            if (options.DryRun)
                            {
                                if (options.Verbose && !options.Json)
                                {
                                    lock (consoleLock)
                                    {
                                        Console.WriteLine($"Would extract: {fileEntry.Path}");
                                    }
                                }
                                results[i] = (true, jsonEntry, null);
                            }
                            else
                            {
                                if (!string.IsNullOrEmpty(fileDir) && !Directory.Exists(fileDir))
                                {
                                    Directory.CreateDirectory(fileDir);
                                }

                                if (options.Verbose && !options.Json && !options.Progress)
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
                                    results[i] = (true, jsonEntry, null);
                                }
                                else
                                {
                                    if (options.Verbose && !options.Json)
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
                            if (!options.Json)
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
            foreach (var (success, jsonEntry, errorMessage) in results)
            {
                if (success)
                {
                    extracted++;
                    if (jsonEntry != null)
                        files.Add(jsonEntry);
                }
                else if (errorMessage != null)
                {
                    errors++;
                    errorMessages.Add(errorMessage);
                }
            }

            result = result with
            {
                Extracted = extracted,
                Skipped = skipped,
                Errors = errors,
                Success = errors == 0,
            };

            if (options.Json)
            {
                Console.WriteLine(
                    JsonSerializer.Serialize(result, ExtractOptions.JsonSerializerOptions)
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
            return ReportError(ex.Message, options, result, options.Verbose ? ex.StackTrace : null);
        }
    }

    private static void CollectFiles(
        RpfFile rpf,
        string[]? filters,
        bool recursive,
        List<(RpfFile, RpfFileEntry)> files
    )
    {
        foreach (RpfEntry entry in rpf.AllEntries)
        {
            if (entry is RpfFileEntry fileEntry)
            {
                // Skip nested RPFs in collection
                if (entry.NameLower.EndsWith(".rpf"))
                    continue;

                if (!Filter.Matches(entry.Path, filters))
                    continue;

                files.Add((rpf, fileEntry));
            }
        }

        // Process nested RPFs
        if (recursive && rpf.Children != null)
        {
            foreach (RpfFile child in rpf.Children)
            {
                CollectFiles(child, filters, recursive, files);
            }
        }
    }

    private static void CountNonRpfFiles(RpfFile rpf, bool recursive, ref int count)
    {
        foreach (RpfEntry entry in rpf.AllEntries)
        {
            if (entry is RpfFileEntry && !entry.NameLower.EndsWith(".rpf"))
            {
                count++;
            }
        }

        if (recursive && rpf.Children != null)
        {
            foreach (RpfFile child in rpf.Children)
            {
                CountNonRpfFiles(child, recursive, ref count);
            }
        }
    }

    private static string GetFileType(RpfFileEntry fileEntry)
    {
        return fileEntry switch
        {
            RpfResourceFileEntry => "resource",
            RpfBinaryFileEntry => "binary",
            _ => "unknown",
        };
    }

    private static int ReportError(
        string message,
        ExtractOptions options,
        Json.ExtractResult result,
        string? stackTrace = null
    )
    {
        if (options.Json)
        {
            result = result with
            {
                Success = false,
                ErrorMessages = [.. result.ErrorMessages, message],
            };
            Console.WriteLine(
                JsonSerializer.Serialize(result, ExtractOptions.JsonSerializerOptions)
            );
        }
        else
        {
            Console.Error.WriteLine($"Error: {message}");
            if (stackTrace != null)
            {
                Console.Error.WriteLine(stackTrace);
            }
        }
        return 1;
    }
}

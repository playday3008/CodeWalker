using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using CodeWalker.Cli.Helpers;
using CodeWalker.Core.Utils;
using CodeWalker.GameFiles;

namespace CodeWalker.Cli;

internal sealed record Gen9Options
{
    public required string InputPath { get; init; }
    public required string OutputPath { get; init; }
    public required CommonOptions Common { get; init; }
    public required bool NoRecurse { get; init; }
    public required bool NoOverwrite { get; init; }
    public required bool SkipUnconverted { get; init; }
    public required bool Progress { get; init; }
}

internal static class Gen9Handler
{
    public static Command CreateCommand(CancellationToken cancellationToken = default)
    {
        CommonCommandOptions commonOpts = new();
        Option<DirectoryInfo> inputOption = new("--input", "-i")
        {
            Description = "Input folder containing files to convert",
            Required = true,
        };

        Option<DirectoryInfo> outputOption = new("--output", "-o")
        {
            Description = "Output folder for converted files",
            Required = true,
        };

        Option<bool> noRecurseOption = new("--no-recurse")
        {
            Description = "Skip subfolders (default: recurse)",
        };

        Option<bool> noOverwriteOption = new("--no-overwrite")
        {
            Description = "Skip existing output files",
        };

        Option<bool> skipUnconvertedOption = new("--skip-unconverted")
        {
            Description = "Don't copy files that don't need conversion",
        };

        Option<bool> progressOption = new("--progress", "-P")
        {
            Description = "Show progress bar",
        };

        Command command = new("gen9", "Convert files to enhanced (Gen9) format")
        {
            inputOption,
            outputOption,
            noRecurseOption,
            noOverwriteOption,
            skipUnconvertedOption,
            progressOption,
        };
        commonOpts.AddTo(command);
        command.Aliases.Add("g");

        command.SetAction(parseResult =>
        {
            Gen9Options options = new()
            {
                InputPath = parseResult.GetRequiredValue(inputOption).FullName,
                OutputPath = parseResult.GetRequiredValue(outputOption).FullName,
                Common = commonOpts.Parse(parseResult),
                NoRecurse = parseResult.GetValue(noRecurseOption),
                NoOverwrite = parseResult.GetValue(noOverwriteOption),
                SkipUnconverted = parseResult.GetValue(skipUnconvertedOption),
                Progress = parseResult.GetValue(progressOption),
            };
            return Execute(options, cancellationToken);
        });

        return command;
    }

    public static int Execute(Gen9Options options, CancellationToken cancellationToken = default)
    {
        Json.Gen9Result ErrorResult(string[] errorMessages) =>
            new()
            {
                Success = false,
                InputFolder = options.InputPath,
                OutputFolder = options.OutputPath,
                TotalFiles = 0,
                Converted = 0,
                Skipped = 0,
                Copied = 0,
                Errors = 0,
                Files = [],
                ErrorMessages = errorMessages,
            };

        if (!Directory.Exists(options.InputPath))
        {
            return RpfService.ReportError(
                $"Input folder not found: {options.InputPath}",
                options.Common.Json,
                ErrorResult([])
            );
        }

        if (
            string.Equals(
                Path.GetFullPath(options.InputPath),
                Path.GetFullPath(options.OutputPath),
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return RpfService.ReportError(
                "Input folder and Output folder must be different.",
                options.Common.Json,
                ErrorResult([])
            );
        }

        string? exeError = RpfService.ValidateExeAndLoadKeys(
            options.Common.ExePath,
            true,
            options.Common.Json
        );
        if (exeError != null)
        {
            return RpfService.ReportError(exeError, options.Common.Json, ErrorResult([]));
        }

        try
        {
            bool previousGen9 = RpfManager.IsGen9;
            RpfManager.IsGen9 = true;

            try
            {
                if (!Directory.Exists(options.OutputPath))
                {
                    _ = Directory.CreateDirectory(options.OutputPath);
                }

                string inputFolder = options.InputPath;
                if (!inputFolder.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                {
                    inputFolder += Path.DirectorySeparatorChar;
                }

                SearchOption searchOption = options.NoRecurse
                    ? SearchOption.TopDirectoryOnly
                    : SearchOption.AllDirectories;

                string[] allPaths = Directory.GetFileSystemEntries(inputFolder, "*", searchOption);

                ILookup<bool, string> pathsByType = allPaths
                    .Where(File.Exists)
                    .ToLookup(p => Path.GetExtension(p).Equals(".rpf", StringComparison.OrdinalIgnoreCase));
                List<string> rpfPaths = [.. pathsByType[true]];
                List<string> filePaths = [.. pathsByType[false]];

                int totalFileCount = filePaths.Count + rpfPaths.Count;

                if (!options.Common.Json)
                {
                    Console.Error.WriteLine($"Found {totalFileCount} files in {options.InputPath}");
                }

                int converted = 0;
                int skipped = 0;
                int copied = 0;
                int errors = 0;
                bool copyUnconverted = !options.SkipUnconverted;
                List<Json.Gen9FileEntry> files = [];
                List<string> errorMessages = [];

                using (
                    ProgressBar progress = new(
                        totalFileCount,
                        options.Progress && !options.Common.Json
                    )
                )
                {
                    (Json.Gen9FileEntry entry, string? error)[] nonRpfResults = new (
                        Json.Gen9FileEntry,
                        string?
                    )[filePaths.Count];

                    object consoleLock = new();

                    _ = Parallel.For(
                        0,
                        filePaths.Count,
                        new ParallelOptions { MaxDegreeOfParallelism = options.Common.Threads, CancellationToken = cancellationToken },
                        i =>
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            string path = filePaths[i];
                            string relPath = path[inputFolder.Length..];
                            string outPath = Path.Combine(options.OutputPath, relPath);

                            try
                            {
                                if (options.NoOverwrite && File.Exists(outPath))
                                {
                                    nonRpfResults[i] = (
                                        new Json.Gen9FileEntry
                                        {
                                            Path = relPath,
                                            Status = "skipped",
                                            Message = "Output file already exists",
                                        },
                                        null
                                    );
                                    if (options.Common.Verbose && !options.Common.Json)
                                    {
                                        lock (consoleLock)
                                        {
                                            Console.Error.WriteLine(
                                                $"{relPath} - skipped (exists)"
                                            );
                                        }
                                    }
                                    progress.Increment(relPath);
                                    return;
                                }

                                string? outDir = Path.GetDirectoryName(outPath);
                                if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                                {
                                    _ = Directory.CreateDirectory(outDir);
                                }

                                string ext = Path.GetExtension(path).ToLowerInvariant();
                                byte[] dataIn = File.ReadAllBytes(path);
                                byte[]? dataOut = Gen9Converter.TryConvert(
                                    dataIn,
                                    ext,
                                    msg =>
                                    {
                                        if (options.Common.Verbose && !options.Common.Json)
                                        {
                                            lock (consoleLock)
                                            {
                                                Console.Error.WriteLine(msg);
                                            }
                                        }
                                    },
                                    relPath,
                                    copyUnconverted,
                                    out bool wasConverted
                                );

                                if (wasConverted && dataOut != null)
                                {
                                    File.WriteAllBytes(outPath, dataOut);
                                    nonRpfResults[i] = (
                                        new Json.Gen9FileEntry
                                        {
                                            Path = relPath,
                                            Status = "converted",
                                        },
                                        null
                                    );
                                }
                                else if (dataOut != null)
                                {
                                    File.WriteAllBytes(outPath, dataOut);
                                    nonRpfResults[i] = (
                                        new Json.Gen9FileEntry
                                        {
                                            Path = relPath,
                                            Status = "copied",
                                        },
                                        null
                                    );
                                }
                                else
                                {
                                    nonRpfResults[i] = (
                                        new Json.Gen9FileEntry
                                        {
                                            Path = relPath,
                                            Status = "skipped",
                                        },
                                        null
                                    );
                                }

                                progress.Increment(relPath);
                            }
                            catch (Exception ex)
                            {
                                string errorMsg = $"Error processing {relPath}: {ex.Message}";
                                nonRpfResults[i] = (
                                    new Json.Gen9FileEntry
                                    {
                                        Path = relPath,
                                        Status = "error",
                                        Message = ex.Message,
                                    },
                                    errorMsg
                                );
                                if (!options.Common.Json)
                                {
                                    lock (consoleLock)
                                    {
                                        Console.Error.WriteLine($"Error: {errorMsg}");
                                    }
                                }
                                progress.Increment();
                            }
                        }
                    );

                    // Aggregate non-RPF results
                    foreach ((Json.Gen9FileEntry entry, string? error) in nonRpfResults)
                    {
                        files.Add(entry);
                        switch (entry.Status)
                        {
                            case "converted":
                                converted++;
                                break;
                            case "copied":
                                copied++;
                                break;
                            case "skipped":
                                skipped++;
                                break;
                            case "error":
                                errors++;
                                if (error != null)
                                    errorMessages.Add(error);
                                break;
                        }
                    }

                    // Process RPF files sequentially (unsafe to parallelize)
                    foreach (string path in rpfPaths)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string relPath = path[inputFolder.Length..];
                        string outPath = Path.Combine(options.OutputPath, relPath);

                        try
                        {
                            if (options.NoOverwrite && File.Exists(outPath))
                            {
                                skipped++;
                                files.Add(
                                    new Json.Gen9FileEntry
                                    {
                                        Path = relPath,
                                        Status = "skipped",
                                        Message = "Output file already exists",
                                    }
                                );
                                if (options.Common.Verbose && !options.Common.Json)
                                {
                                    Console.Error.WriteLine($"{relPath} - skipped (exists)");
                                }
                                progress.Increment(relPath);
                                continue;
                            }

                            string? outDir = Path.GetDirectoryName(outPath);
                            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                            {
                                _ = Directory.CreateDirectory(outDir);
                            }

                            ProcessRpfFile(
                                path,
                                outPath,
                                relPath,
                                options,
                                files,
                                errorMessages,
                                ref converted,
                                ref errors
                            );

                            progress.Increment(relPath);
                        }
                        catch (Exception ex)
                        {
                            errors++;
                            string errorMsg = $"Error processing {relPath}: {ex.Message}";
                            errorMessages.Add(errorMsg);
                            files.Add(
                                new Json.Gen9FileEntry
                                {
                                    Path = relPath,
                                    Status = "error",
                                    Message = ex.Message,
                                }
                            );
                            if (!options.Common.Json)
                            {
                                Console.Error.WriteLine($"Error: {errorMsg}");
                            }
                            progress.Increment();
                        }
                    }
                }

                Json.Gen9Result result = new()
                {
                    Success = errors == 0,
                    InputFolder = options.InputPath,
                    OutputFolder = options.OutputPath,
                    TotalFiles = totalFileCount,
                    Converted = converted,
                    Skipped = skipped,
                    Copied = copied,
                    Errors = errors,
                    Files = [.. files],
                    ErrorMessages = [.. errorMessages],
                };

                if (options.Common.Json)
                {
                    Console.WriteLine(
                        JsonSerializer.Serialize(result, RpfService.JsonSerializerOptions)
                    );
                }
                else
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine(
                        $"Conversion complete: {converted} converted, {copied} copied, {skipped} skipped, {errors} errors"
                    );
                }

                return errors > 0 ? 1 : 0;
            }
            finally
            {
                RpfManager.IsGen9 = previousGen9;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return RpfService.ReportError(
                ex.Message,
                options.Common.Json,
                ErrorResult([]),
                options.Common.Verbose ? ex.StackTrace : null
            );
        }
    }

    private static void ProcessRpfFile(
        string inputPath,
        string outputPath,
        string relPath,
        Gen9Options options,
        List<Json.Gen9FileEntry> files,
        List<string> errorMessages,
        ref int converted,
        ref int errors
    )
    {
        if (options.Common.Verbose && !options.Common.Json)
        {
            Console.Error.WriteLine($"{relPath} - Converting RPF contents...");
        }

        File.Copy(inputPath, outputPath, overwrite: true);

        RpfFile rpf = new(outputPath, relPath);
        rpf.ScanStructure(
            status =>
            {
                if (options.Common.Verbose && !options.Common.Json)
                    Console.Error.WriteLine(status);
            },
            error =>
            {
                if (!options.Common.Json)
                    Console.Error.WriteLine($"Error: {error}");
                errorMessages.Add(error);
            }
        );

        // Build list of all RPFs (children first, then parents)
        List<RpfFile> rpfList = [];
        Stack<RpfFile> rpfStack = new();
        rpfStack.Push(rpf);
        while (rpfStack.Count > 0)
        {
            RpfFile current = rpfStack.Pop();
            if (current.Children != null)
            {
                foreach (RpfFile child in current.Children)
                {
                    rpfStack.Push(child);
                }
            }
            rpfList.Add(current);
        }
        rpfList.Reverse();

        HashSet<RpfFile> changedParents = [];

        foreach (RpfFile currentRpf in rpfList)
        {
            if (currentRpf.AllEntries == null)
                continue;

            bool changed = changedParents.Contains(currentRpf);

            List<RpfResourceFileEntry> resourceEntries = currentRpf.AllEntries
                .OfType<RpfResourceFileEntry>()
                .OrderBy(rfe => rfe.FileOffset)
                .ToList();

            foreach (RpfResourceFileEntry rfe in resourceEntries)
            {
                if (!Gen9Converter.RequiresConversion(rfe))
                    continue;

                RpfDirectoryEntry dir = rfe.Parent;
                string name = rfe.Name;
                string type = Path.GetExtension(rfe.NameLower);

                byte[]? dataIn = currentRpf.ExtractFile(rfe);
                if (dataIn == null)
                {
                    errors++;
                    string errorMsg = $"{rfe.Path} - failed to extract";
                    errorMessages.Add(errorMsg);
                    files.Add(
                        new Json.Gen9FileEntry
                        {
                            Path = rfe.Path,
                            Status = "error",
                            Message = "Failed to extract file data",
                        }
                    );
                    continue;
                }
                dataIn = ResourceBuilder.Compress(dataIn);
                dataIn = ResourceBuilder.AddResourceHeader(rfe, dataIn);

                byte[]? dataOut = Gen9Converter.TryConvert(
                    dataIn,
                    type,
                    msg =>
                    {
                        if (options.Common.Verbose && !options.Common.Json)
                            Console.Error.WriteLine(msg);
                    },
                    rfe.Path,
                    false,
                    out bool wasConverted
                );

                if (!wasConverted || dataOut == null)
                {
                    errors++;
                    string errorMsg = $"{rfe.Path} - unable to convert";
                    errorMessages.Add(errorMsg);
                    files.Add(
                        new Json.Gen9FileEntry
                        {
                            Path = rfe.Path,
                            Status = "error",
                            Message = "Unable to convert",
                        }
                    );
                    continue;
                }

                _ = RpfFile.CreateFile(dir, name, dataOut, true);
                converted++;
                files.Add(new Json.Gen9FileEntry { Path = rfe.Path, Status = "converted" });
                changed = true;
            }

            if (changed)
            {
                if (options.Common.Verbose && !options.Common.Json)
                {
                    Console.Error.WriteLine($"{currentRpf.Path} - Defragmenting");
                }
                RpfFile.Defragment(currentRpf, null, false);

                if (currentRpf.Parent != null)
                {
                    _ = changedParents.Add(currentRpf.Parent);
                }
            }
        }
    }
}

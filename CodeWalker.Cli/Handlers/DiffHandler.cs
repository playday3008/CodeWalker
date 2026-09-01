using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

namespace CodeWalker.Cli.Handlers;

internal sealed record DiffOptions
{
    public required string LeftPath { get; init; }
    public required string RightPath { get; init; }
    public required string LeftExePath { get; init; }
    public required string RightExePath { get; init; }
    public required bool LeftGen9 { get; init; }
    public required bool RightGen9 { get; init; }
    public required bool Recursive { get; init; }
    public required bool Progress { get; init; }
    public required bool Verbose { get; init; }
    public required bool Json { get; init; }
    public required SizeFormat SizeFormat { get; init; }
    public required int Threads { get; init; }
}

internal static class DiffHandler
{
    public static Command CreateCommand(CancellationToken cancellationToken = default)
    {
        Option<bool> verboseOpt = CliOptions.Verbose();
        Option<bool> jsonOpt = CliOptions.Json();
        Option<bool> siOpt = CliOptions.Si();
        Option<int> threadsOpt = CliOptions.Threads();

        Option<FileInfo> leftOption = new("--left", "-l")
        {
            Description = "First RPF archive to compare",
            Required = true,
        };

        Option<FileInfo> rightOption = new("--right", "-r")
        {
            Description = "Second RPF archive to compare",
            Required = true,
        };

        Option<DirectoryInfo> leftExeOption = new("--left-exe", "-le")
        {
            Description = "Path to the GTA V installation for the left archive",
            Required = true,
        };

        Option<DirectoryInfo> rightExeOption = new("--right-exe", "-re")
        {
            Description = "Path to the GTA V installation for the right archive",
            Required = true,
        };

        Option<bool> leftGen9Option = new("--left-gen9", "-lg")
        {
            Description = "Use GTA V Enhanced (Gen9) mode for the left archive",
        };

        Option<bool> rightGen9Option = new("--right-gen9", "-rg")
        {
            Description = "Use GTA V Enhanced (Gen9) mode for the right archive",
        };

        Option<bool> recursiveOption = new("--recursive", "-R")
        {
            Description = "Include nested RPFs in comparison",
        };

        Option<bool> progressOption = new("--progress", "-P")
        {
            Description = "Show progress bar",
        };


        Command command = new("diff", "Compare two RPF archives")
        {
            leftOption,
            rightOption,
            leftExeOption,
            rightExeOption,
            leftGen9Option,
            rightGen9Option,
            recursiveOption,

            progressOption,
            verboseOpt,
            jsonOpt,
            siOpt,
            threadsOpt
        };

        command.Aliases.Add("d");

        command.SetAction(parseResult =>
        {
            DiffOptions options = new()
            {
                LeftPath = parseResult.GetRequiredValue(leftOption).FullName,
                RightPath = parseResult.GetRequiredValue(rightOption).FullName,
                LeftExePath = parseResult.GetRequiredValue(leftExeOption).FullName,
                RightExePath = parseResult.GetRequiredValue(rightExeOption).FullName,
                LeftGen9 = parseResult.GetValue(leftGen9Option),
                RightGen9 = parseResult.GetValue(rightGen9Option),
                Recursive = parseResult.GetValue(recursiveOption),
                Progress = parseResult.GetValue(progressOption),
                Verbose = parseResult.GetValue(verboseOpt),
                Json = parseResult.GetValue(jsonOpt),
                SizeFormat = parseResult.GetValue(siOpt) ? SizeFormat.SI : SizeFormat.IEC,
                Threads = parseResult.GetValue(threadsOpt),
            };
            return Execute(options, cancellationToken);
        });

        return command;
    }

    public static int Execute(DiffOptions options, CancellationToken cancellationToken = default)
    {
        // Validate both RPF files and exe paths before loading keys
        string? leftError = RpfHelper.ValidateInputs(
            options.LeftPath,
            options.LeftExePath,
            options.LeftGen9
        );
        if (leftError != null)
        {
            return Output.ReportError(
                leftError,
                options.Json,
                ErrorResult([], options)
            );
        }

        string? rightError = RpfHelper.ValidateInputs(
            options.RightPath,
            options.RightExePath,
            options.RightGen9
        );
        if (rightError != null)
        {
            return Output.ReportError(
                rightError,
                options.Json,
                ErrorResult([], options)
            );
        }

        List<string> errorMessages = [];
        try
        {
            if (!options.Json)
                Console.Error.WriteLine("Loading encryption keys...");
            RpfHelper.LoadKeys(options.LeftExePath, options.LeftGen9);
            if (options.RightExePath != options.LeftExePath || options.RightGen9 != options.LeftGen9)
                RpfHelper.LoadKeys(options.RightExePath, options.RightGen9);

            RpfFile leftRpf = RpfHelper.OpenRpf(
                options.LeftPath,
                options.Verbose,
                options.Json,
                errorMessages
            );

            RpfFile rightRpf = RpfHelper.OpenRpf(
                options.RightPath,
                options.Verbose,
                options.Json,
                errorMessages
            );

            Json.DiffResult result = CollectDiff(leftRpf, rightRpf, errorMessages, options, cancellationToken);

            if (options.Json)
                PrintJsonDiff(result);
            else
                PrintDiff(result, options);

            return errorMessages.Count > 0 ? 1 : 0;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return Output.ReportError(
                ex.Message,
                options.Json,
                ErrorResult([.. errorMessages], options),
                options.Verbose ? ex.StackTrace : null
            );
        }
    }

    internal static Json.DiffResult ErrorResult(string[] errorMessages, DiffOptions options) =>
        new()
        {
            Success = false,
            LeftRpf = options.LeftPath,
            RightRpf = options.RightPath,
            Added = [],
            Removed = [],
            Modified = [],
            Unchanged = [],
            Summary = new Json.DiffSummary
            {
                AddedCount = 0,
                RemovedCount = 0,
                ModifiedCount = 0,
                UnchangedCount = 0,
            },
            ErrorMessages = errorMessages,
        };

    internal static Json.DiffResult CollectDiff(
        RpfFile leftRpf,
        RpfFile rightRpf,
        List<string> errorMessages,
        DiffOptions options,
        CancellationToken cancellationToken = default)
    {
        // Collect files from both archives
        List<(RpfFile rpf, RpfFileEntry entry)> leftFiles = RpfHelper.CollectFiles(
            leftRpf,
            null,
            options.Recursive
        );
        List<(RpfFile rpf, RpfFileEntry entry)> rightFiles = RpfHelper.CollectFiles(
            rightRpf,
            null,
            options.Recursive
        );

        // Build dictionaries keyed by path
        Dictionary<string, (RpfFile rpf, RpfFileEntry entry)> leftDict =
            leftFiles.ToDictionary(f => f.entry.Path, f => f);

        Dictionary<string, (RpfFile rpf, RpfFileEntry entry)> rightDict =
            rightFiles.ToDictionary(f => f.entry.Path, f => f);

        SizeFormat sizeFormat = options.SizeFormat;

        // Find removed and modified/unchanged — entries in left that also appear in right
        // need byte comparison, so parallelize this
        string[] commonPaths = leftDict.Keys.Where(rightDict.ContainsKey).ToArray();

        // Result per common path: false = unchanged, true = modified
        bool[] isModifiedArr = new bool[commonPaths.Length];
        object errorLock = new();

        using (ProgressBar progress = new(commonPaths.Length, options.Progress && !options.Json))
        {
            _ = Parallel.For(
                0,
                commonPaths.Length,
                new ParallelOptions { MaxDegreeOfParallelism = options.Threads, CancellationToken = cancellationToken },
                i =>
                {
                    string path = commonPaths[i];
                    (RpfFile leftRpfRef, RpfFileEntry leftEntry) = leftDict[path];
                    (RpfFile rightRpfRef, RpfFileEntry rightEntry) = rightDict[path];

                    long leftSize = leftEntry.GetFileSize();
                    long rightSize = rightEntry.GetFileSize();
                    string leftType = RpfHelper.GetFileType(leftEntry);
                    string rightType = RpfHelper.GetFileType(rightEntry);

                    if (leftSize != rightSize || leftType != rightType)
                    {
                        isModifiedArr[i] = true;
                    }
                    else
                    {
                        byte[]? leftData = leftRpfRef.ExtractFile(leftEntry);
                        byte[]? rightData = rightRpfRef.ExtractFile(rightEntry);

                        if (leftData == null || rightData == null)
                        {
                            string side = leftData == null ? "left" : "right";
                            lock (errorLock)
                                errorMessages.Add($"Failed to extract {side} entry: {path}");
                            isModifiedArr[i] = true;
                        }
                        else
                        {
                            isModifiedArr[i] = !ContentEquals(leftData, rightData);
                        }
                    }

                    progress.Increment(path);
                }
            );
        }

        List<Json.DiffEntry> added = [];
        List<Json.DiffEntry> removed = [];
        List<Json.DiffEntry> modified = [];
        List<Json.DiffEntry> unchanged = [];

        // Aggregate common path results
        for (int i = 0; i < commonPaths.Length; i++)
        {
            string path = commonPaths[i];
            (_, RpfFileEntry leftEntry) = leftDict[path];
            (_, RpfFileEntry rightEntry) = rightDict[path];

            if (isModifiedArr[i])
            {
                long leftSize = leftEntry.GetFileSize();
                long rightSize = rightEntry.GetFileSize();
                modified.Add(
                    new Json.DiffEntry
                    {
                        Path = path,
                        Name = leftEntry.Name,
                        Type = RpfHelper.GetFileType(leftEntry),
                        LeftSize = leftSize,
                        LeftSizeFormatted = sizeFormat.ToFormattedString(leftSize),
                        RightSize = rightSize,
                        RightSizeFormatted = sizeFormat.ToFormattedString(rightSize),
                    }
                );
            }
            else
            {
                long size = leftEntry.GetFileSize();
                unchanged.Add(
                    new Json.DiffEntry
                    {
                        Path = path,
                        Name = leftEntry.Name,
                        Type = RpfHelper.GetFileType(leftEntry),
                        Size = size,
                        SizeFormatted = sizeFormat.ToFormattedString(size),
                    }
                );
            }
        }

        // Find removed (left only)
        removed.AddRange(
            leftDict
                .Where(kvp => !rightDict.ContainsKey(kvp.Key))
                .Select(kvp =>
                {
                    long size = kvp.Value.entry.GetFileSize();
                    return new Json.DiffEntry
                    {
                        Path = kvp.Key,
                        Name = kvp.Value.entry.Name,
                        Type = RpfHelper.GetFileType(kvp.Value.entry),
                        Size = size,
                        SizeFormatted = sizeFormat.ToFormattedString(size),
                    };
                })
        );

        // Find added (right only)
        added.AddRange(
            rightDict
                .Where(kvp => !leftDict.ContainsKey(kvp.Key))
                .Select(kvp =>
                {
                    long size = kvp.Value.entry.GetFileSize();
                    return new Json.DiffEntry
                    {
                        Path = kvp.Key,
                        Name = kvp.Value.entry.Name,
                        Type = RpfHelper.GetFileType(kvp.Value.entry),
                        Size = size,
                        SizeFormatted = sizeFormat.ToFormattedString(size),
                    };
                })
        );

        // Sort alphabetically
        added.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        removed.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        modified.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        unchanged.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

        Json.DiffSummary summary = new()
        {
            AddedCount = added.Count,
            RemovedCount = removed.Count,
            ModifiedCount = modified.Count,
            UnchangedCount = unchanged.Count,
        };

        return new Json.DiffResult
        {
            Success = errorMessages.Count == 0,
            LeftRpf = options.LeftPath,
            RightRpf = options.RightPath,
            Added = [.. added],
            Removed = [.. removed],
            Modified = [.. modified],
            Unchanged = [.. unchanged],
            Summary = summary,
            ErrorMessages = [.. errorMessages],
        };
    }

    internal static void PrintJsonDiff(Json.DiffResult result) =>
        Console.WriteLine(JsonSerializer.Serialize(result, Output.JsonSerializerOptions));

    internal static void PrintDiff(Json.DiffResult result, DiffOptions options)
    {
        if (result.Added.Count > 0)
        {
            Console.WriteLine($"Added ({result.Added.Count}):");
            foreach (Json.DiffEntry entry in result.Added)
            {
                Console.WriteLine($"  + {entry.Path}");
            }
            Console.WriteLine();
        }

        if (result.Removed.Count > 0)
        {
            Console.WriteLine($"Removed ({result.Removed.Count}):");
            foreach (Json.DiffEntry entry in result.Removed)
            {
                Console.WriteLine($"  - {entry.Path}");
            }
            Console.WriteLine();
        }

        if (result.Modified.Count > 0)
        {
            Console.WriteLine($"Modified ({result.Modified.Count}):");
            foreach (Json.DiffEntry entry in result.Modified)
            {
                Console.WriteLine(
                    $"  ~ {entry.Path}  ({entry.LeftSizeFormatted} -> {entry.RightSizeFormatted})"
                );
            }
            Console.WriteLine();
        }

        if (options.Verbose && result.Unchanged.Count > 0)
        {
            Console.WriteLine($"Unchanged ({result.Unchanged.Count}):");
            foreach (Json.DiffEntry entry in result.Unchanged)
            {
                Console.WriteLine($"  = {entry.Path}");
            }
            Console.WriteLine();
        }

        Console.Error.WriteLine(
            $"Summary: {result.Summary.AddedCount} added, {result.Summary.RemovedCount} removed, {result.Summary.ModifiedCount} modified, {result.Summary.UnchangedCount} unchanged"
        );
    }

    internal static bool ContentEquals(byte[]? a, byte[]? b)
    {
        if (a == null && b == null)
            return true;
        if (a == null || b == null)
            return false;
        if (a.Length != b.Length)
            return false;
#if NET5_0_OR_GREATER
        return a.AsSpan().SequenceEqual(b);
#else
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                return false;
        }
        return true;
#endif
    }
}

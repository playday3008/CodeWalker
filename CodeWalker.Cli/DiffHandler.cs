using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

namespace CodeWalker.Cli;

public record DiffOptions
{
    public required string LeftPath { get; init; }
    public required string RightPath { get; init; }
    public required CommonOptions Common { get; init; }
    public required bool Gen9 { get; init; }
    public required bool Recursive { get; init; }
}

public static class DiffHandler
{
    public static Command CreateCommand()
    {
        CommonCommandOptions commonOpts = new();
        // csharpier-ignore-start
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

        Option<bool> gen9Option = new("--gen9", "-g")
        {
            Description = "Use GTA V Enhanced (Gen9) mode",
        };

        Option<bool> recursiveOption = new("--recursive", "-R")
        {
            Description = "Include nested RPFs in comparison",
        };
        // csharpier-ignore-end

        Command command = new("diff", "Compare two RPF archives")
        {
            leftOption,
            rightOption,
            gen9Option,
            recursiveOption,
        };
        commonOpts.AddTo(command);
        command.Aliases.Add("d");

        command.SetAction(parseResult =>
        {
            DiffOptions options = new()
            {
                LeftPath = parseResult.GetRequiredValue(leftOption).FullName,
                RightPath = parseResult.GetRequiredValue(rightOption).FullName,
                Common = commonOpts.Parse(parseResult),
                Gen9 = parseResult.GetValue(gen9Option),
                Recursive = parseResult.GetValue(recursiveOption),
            };
            return Execute(options);
        });

        return command;
    }

    public static int Execute(DiffOptions options)
    {
        Json.DiffResult ErrorResult(string[] errorMessages) =>
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

        // Validate both RPF files exist before loading keys
        string? leftError = RpfService.ValidateInputs(
            options.LeftPath,
            options.Common.ExePath,
            options.Gen9
        );
        if (leftError != null)
        {
            return RpfService.ReportError(leftError, options.Common.Json, ErrorResult([]));
        }

        if (!File.Exists(options.RightPath))
        {
            return RpfService.ReportError(
                $"RPF file not found: {options.RightPath}",
                options.Common.Json,
                ErrorResult([])
            );
        }

        try
        {
            if (!options.Common.Json)
                Console.Error.WriteLine("Loading encryption keys...");
            RpfService.LoadKeys(options.Common.ExePath, options.Gen9);

            List<string> errorMessages = [];

            RpfFile leftRpf = RpfService.OpenRpf(
                options.LeftPath,
                options.Common.Verbose,
                options.Common.Json,
                errorMessages
            );

            RpfFile rightRpf = RpfService.OpenRpf(
                options.RightPath,
                options.Common.Verbose,
                options.Common.Json,
                errorMessages
            );

            // Collect files from both archives
            List<(RpfFile rpf, RpfFileEntry entry)> leftFiles = RpfService.CollectFiles(
                leftRpf,
                null,
                options.Recursive
            );
            List<(RpfFile rpf, RpfFileEntry entry)> rightFiles = RpfService.CollectFiles(
                rightRpf,
                null,
                options.Recursive
            );

            // Build dictionaries keyed by path
            Dictionary<string, (RpfFile rpf, RpfFileEntry entry)> leftDict = [];
            foreach ((RpfFile rpf, RpfFileEntry entry) in leftFiles)
            {
                leftDict[entry.Path] = (rpf, entry);
            }

            Dictionary<string, (RpfFile rpf, RpfFileEntry entry)> rightDict = [];
            foreach ((RpfFile rpf, RpfFileEntry entry) in rightFiles)
            {
                rightDict[entry.Path] = (rpf, entry);
            }

            SizeFormat sizeFormat = options.Common.SizeFormat;

            // Find removed and modified/unchanged — entries in left that also appear in right
            // need byte comparison, so parallelize this
            string[] commonPaths;
            {
                List<string> paths = [];
                foreach (string path in leftDict.Keys)
                {
                    if (rightDict.ContainsKey(path))
                        paths.Add(path);
                }
                commonPaths = paths.ToArray();
            }

            // Result per common path: null = unchanged, non-null = modified entry
            bool[] isModifiedArr = new bool[commonPaths.Length];

            Parallel.For(
                0,
                commonPaths.Length,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, options.Common.Threads),
                },
                i =>
                {
                    string path = commonPaths[i];
                    (RpfFile leftRpfRef, RpfFileEntry leftEntry) = leftDict[path];
                    (RpfFile rightRpfRef, RpfFileEntry rightEntry) = rightDict[path];

                    long leftSize = leftEntry.GetFileSize();
                    long rightSize = rightEntry.GetFileSize();
                    string leftType = RpfService.GetFileType(leftEntry);
                    string rightType = RpfService.GetFileType(rightEntry);

                    if (leftSize != rightSize || leftType != rightType)
                    {
                        isModifiedArr[i] = true;
                    }
                    else
                    {
                        byte[]? leftData = leftRpfRef.ExtractFile(leftEntry);
                        byte[]? rightData = rightRpfRef.ExtractFile(rightEntry);
                        isModifiedArr[i] = !ContentEquals(leftData, rightData);
                    }
                }
            );

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
                            Type = RpfService.GetFileType(leftEntry),
                            LeftSize = leftSize,
                            RightSize = rightSize,
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
                            Type = RpfService.GetFileType(leftEntry),
                            Size = size,
                            SizeFormatted = sizeFormat.ToFormattedString(size),
                        }
                    );
                }
            }

            // Find removed (left only)
            foreach (KeyValuePair<string, (RpfFile rpf, RpfFileEntry entry)> kvp in leftDict)
            {
                if (!rightDict.ContainsKey(kvp.Key))
                {
                    long size = kvp.Value.entry.GetFileSize();
                    removed.Add(
                        new Json.DiffEntry
                        {
                            Path = kvp.Key,
                            Name = kvp.Value.entry.Name,
                            Type = RpfService.GetFileType(kvp.Value.entry),
                            Size = size,
                            SizeFormatted = sizeFormat.ToFormattedString(size),
                        }
                    );
                }
            }

            // Find added (right only)
            foreach (KeyValuePair<string, (RpfFile rpf, RpfFileEntry entry)> kvp in rightDict)
            {
                if (!leftDict.ContainsKey(kvp.Key))
                {
                    long size = kvp.Value.entry.GetFileSize();
                    added.Add(
                        new Json.DiffEntry
                        {
                            Path = kvp.Key,
                            Name = kvp.Value.entry.Name,
                            Type = RpfService.GetFileType(kvp.Value.entry),
                            Size = size,
                            SizeFormatted = sizeFormat.ToFormattedString(size),
                        }
                    );
                }
            }

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

            Json.DiffResult result = new()
            {
                Success = true,
                LeftRpf = options.LeftPath,
                RightRpf = options.RightPath,
                Added = added.ToArray(),
                Removed = removed.ToArray(),
                Modified = modified.ToArray(),
                Unchanged = unchanged.ToArray(),
                Summary = summary,
                ErrorMessages = errorMessages.ToArray(),
            };

            if (options.Common.Json)
            {
                Console.WriteLine(
                    JsonSerializer.Serialize(result, RpfService.JsonSerializerOptions)
                );
            }
            else
            {
                if (added.Count > 0)
                {
                    Console.WriteLine($"Added ({added.Count}):");
                    foreach (Json.DiffEntry entry in added)
                    {
                        Console.WriteLine($"  + {entry.Path}");
                    }
                    Console.WriteLine();
                }

                if (removed.Count > 0)
                {
                    Console.WriteLine($"Removed ({removed.Count}):");
                    foreach (Json.DiffEntry entry in removed)
                    {
                        Console.WriteLine($"  - {entry.Path}");
                    }
                    Console.WriteLine();
                }

                if (modified.Count > 0)
                {
                    Console.WriteLine($"Modified ({modified.Count}):");
                    foreach (Json.DiffEntry entry in modified)
                    {
                        Console.WriteLine(
                            $"  ~ {entry.Path}  ({sizeFormat.ToFormattedString(entry.LeftSize ?? 0)} -> {sizeFormat.ToFormattedString(entry.RightSize ?? 0)})"
                        );
                    }
                    Console.WriteLine();
                }

                if (options.Common.Verbose && unchanged.Count > 0)
                {
                    Console.WriteLine($"Unchanged ({unchanged.Count}):");
                    foreach (Json.DiffEntry entry in unchanged)
                    {
                        Console.WriteLine($"  = {entry.Path}");
                    }
                    Console.WriteLine();
                }

                Console.Error.WriteLine(
                    $"Summary: {added.Count} added, {removed.Count} removed, {modified.Count} modified, {unchanged.Count} unchanged"
                );
            }

            return 0;
        }
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

    private static bool ContentEquals(byte[]? a, byte[]? b)
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

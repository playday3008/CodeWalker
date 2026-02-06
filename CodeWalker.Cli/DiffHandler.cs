using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

namespace CodeWalker.Cli;

public record DiffOptions
{
    public required string LeftPath { get; init; }
    public required string RightPath { get; init; }
    public required string ExePath { get; init; }
    public required bool Gen9 { get; init; }
    public required bool Recursive { get; init; }
    public required bool Verbose { get; init; }
    public required bool Json { get; init; }
}

public static class DiffHandler
{
    public static Command CreateCommand()
    {
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

        Option<DirectoryInfo> exeOption = new("--exe", "-e")
        {
            Description = "Path to the GTA V installation directory (containing GTA5.exe)",
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

        Option<bool> verboseOption = new("--verbose", "-v")
        {
            Description = "Show unchanged files too",
        };

        Option<bool> jsonOption = new("--json")
        {
            Description = "Output results in JSON format",
        };
        // csharpier-ignore-end

        Command command = new("diff", "Compare two RPF archives")
        {
            leftOption,
            rightOption,
            exeOption,
            gen9Option,
            recursiveOption,
            verboseOption,
            jsonOption,
        };
        command.Aliases.Add("d");

        command.SetAction(parseResult =>
        {
            DiffOptions options = new()
            {
                LeftPath = parseResult.GetRequiredValue(leftOption).FullName,
                RightPath = parseResult.GetRequiredValue(rightOption).FullName,
                ExePath = parseResult.GetRequiredValue(exeOption).FullName,
                Gen9 = parseResult.GetValue(gen9Option),
                Recursive = parseResult.GetValue(recursiveOption),
                Verbose = parseResult.GetValue(verboseOption),
                Json = parseResult.GetValue(jsonOption),
            };
            return Execute(options);
        });

        return command;
    }

    public static int Execute(DiffOptions options)
    {
        List<string> errorMessages = [];

        Json.DiffResult result = new()
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

        // Validate both RPF files
        string? leftError = RpfService.ValidateInputs(
            options.LeftPath,
            options.ExePath,
            options.Gen9
        );
        if (leftError != null)
        {
            return ReportError(leftError, options, result);
        }

        string? rightError = RpfService.ValidateInputs(
            options.RightPath,
            options.ExePath,
            options.Gen9
        );
        if (rightError != null)
        {
            return ReportError(rightError, options, result);
        }

        try
        {
            if (!options.Json)
            {
                Console.Error.WriteLine("Loading encryption keys...");
            }
            RpfService.LoadKeys(options.ExePath, options.Gen9);

            if (!options.Json)
            {
                Console.Error.WriteLine($"Opening left RPF: {options.LeftPath}");
            }

            RpfFile leftRpf = RpfService.OpenRpf(
                options.LeftPath,
                onError: error =>
                {
                    if (!options.Json)
                        Console.Error.WriteLine($"Error: {error}");
                    errorMessages.Add(error);
                }
            );

            if (!options.Json)
            {
                Console.Error.WriteLine($"Opening right RPF: {options.RightPath}");
            }

            RpfFile rightRpf = RpfService.OpenRpf(
                options.RightPath,
                onError: error =>
                {
                    if (!options.Json)
                        Console.Error.WriteLine($"Error: {error}");
                    errorMessages.Add(error);
                }
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
            Dictionary<string, RpfFileEntry> leftDict = [];
            foreach ((_, RpfFileEntry entry) in leftFiles)
            {
                leftDict[entry.Path] = entry;
            }

            Dictionary<string, RpfFileEntry> rightDict = [];
            foreach ((_, RpfFileEntry entry) in rightFiles)
            {
                rightDict[entry.Path] = entry;
            }

            List<Json.DiffEntry> added = [];
            List<Json.DiffEntry> removed = [];
            List<Json.DiffEntry> modified = [];
            List<Json.DiffEntry> unchanged = [];

            SizeFormat sizeFormat = SizeFormat.IEC;

            // Find removed and modified/unchanged
            foreach (KeyValuePair<string, RpfFileEntry> kvp in leftDict)
            {
                string path = kvp.Key;
                RpfFileEntry leftEntry = kvp.Value;

                if (rightDict.TryGetValue(path, out RpfFileEntry? rightEntry))
                {
                    long leftSize = leftEntry.GetFileSize();
                    long rightSize = rightEntry.GetFileSize();
                    string leftType = RpfService.GetFileType(leftEntry);
                    string rightType = RpfService.GetFileType(rightEntry);

                    if (leftSize != rightSize || leftType != rightType)
                    {
                        modified.Add(
                            new Json.DiffEntry
                            {
                                Path = path,
                                Name = leftEntry.Name,
                                Type = leftType,
                                LeftSize = leftSize,
                                RightSize = rightSize,
                            }
                        );
                    }
                    else
                    {
                        unchanged.Add(
                            new Json.DiffEntry
                            {
                                Path = path,
                                Name = leftEntry.Name,
                                Type = leftType,
                                Size = leftSize,
                                SizeFormatted = sizeFormat.ToFormattedString(leftSize),
                            }
                        );
                    }
                }
                else
                {
                    long size = leftEntry.GetFileSize();
                    removed.Add(
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

            // Find added
            foreach (KeyValuePair<string, RpfFileEntry> kvp in rightDict)
            {
                if (!leftDict.ContainsKey(kvp.Key))
                {
                    long size = kvp.Value.GetFileSize();
                    added.Add(
                        new Json.DiffEntry
                        {
                            Path = kvp.Key,
                            Name = kvp.Value.Name,
                            Type = RpfService.GetFileType(kvp.Value),
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

            result = result with
            {
                Success = true,
                Added = added,
                Removed = removed,
                Modified = modified,
                Unchanged = unchanged,
                Summary = summary,
            };

            if (options.Json)
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

                if (options.Verbose && unchanged.Count > 0)
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
            return ReportError(ex.Message, options, result, options.Verbose ? ex.StackTrace : null);
        }
    }

    private static int ReportError(
        string message,
        DiffOptions options,
        Json.DiffResult result,
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
            Console.WriteLine(JsonSerializer.Serialize(result, RpfService.JsonSerializerOptions));
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

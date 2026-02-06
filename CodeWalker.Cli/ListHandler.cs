using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

namespace CodeWalker.Cli;

public record ListOptions
{
    public required string RpfPath { get; init; }
    public required string ExePath { get; init; }
    public required bool Gen9 { get; init; }
    public required string[] Filters { get; init; }
    public required bool Verbose { get; init; }
    public required bool Json { get; init; }
    public required bool Recursive { get; init; }
    public required int Threads { get; init; }
    public required SizeFormat SizeFormat { get; init; } = SizeFormat.IEC;

    public static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true,
    };
}

public static class ListHandler
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
            Description = "Show verbose output (includes file sizes)",
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

        Option<int> threadsOption = new("--threads", "-t")
        {
            Description = "Number of threads for parallel processing",
            DefaultValueFactory = _ => Environment.ProcessorCount,
        };
        // csharpier-ignore-end

        Command command = new("list", "List contents of an RPF archive")
        {
            rpfOption,
            exeOption,
            gen9Option,
            filterOption,
            verboseOption,
            jsonOption,
            recursiveOption,
            siOption,
            threadsOption,
        };
        command.Aliases.Add("l");

        command.SetAction(parseResult =>
        {
            ListOptions options = new()
            {
                RpfPath = parseResult.GetRequiredValue(rpfOption).FullName,
                ExePath = parseResult.GetRequiredValue(exeOption).FullName,
                Gen9 = parseResult.GetValue(gen9Option),
                Filters = parseResult.GetValue(filterOption) ?? [],
                Verbose = parseResult.GetValue(verboseOption),
                Json = parseResult.GetValue(jsonOption),
                Recursive = parseResult.GetValue(recursiveOption),
                Threads = parseResult.GetValue(threadsOption),
                SizeFormat = parseResult.GetValue(siOption) ? SizeFormat.SI : SizeFormat.IEC,
            };
            return Execute(options);
        });

        return command;
    }

    public static int Execute(ListOptions options)
    {
        List<Json.FileEntry> files = [];
        List<string> errorMessages = [];

        Json.ListResult result = new()
        {
            Success = false,
            RpfFile = null!,
            TotalFiles = 0,
            TotalSize = 0,
            TotalSizeFormatted = null!,
            NestedRpfCount = 0,
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
                NestedRpfCount = rpf.GrandTotalRpfCount,
            };

            if (!options.Json)
            {
                Console.Error.WriteLine();
            }

            // Collect all matching entries
            List<(RpfFile rpf, RpfFileEntry entry)> entries = [];
            CollectEntries(rpf, options.Filters, options.Recursive, entries);

            // Process entries in parallel, storing results by index to preserve order
            (Json.FileEntry? jsonEntry, string? line, long size)[] results = new (
                Json.FileEntry?,
                string?,
                long
            )[entries.Count];

            Parallel.For(
                0,
                entries.Count,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Threads) },
                i =>
                {
                    RpfFileEntry fileEntry = entries[i].entry;
                    long size = fileEntry.GetFileSize();
                    string ext = Path.GetExtension(fileEntry.Name).ToLowerInvariant();

                    Json.FileEntry? jsonEntry = null;
                    string? line = null;

                    if (options.Json)
                    {
                        jsonEntry = new Json.FileEntry
                        {
                            Path = fileEntry.Path,
                            Name = fileEntry.Name,
                            Size = size,
                            SizeFormatted = options.SizeFormat.ToFormattedString(size),
                            Type = GetFileType(fileEntry),
                            Extension = ext,
                        };
                    }
                    else if (options.Verbose)
                    {
                        string sizeStr = options.SizeFormat.ToFormattedString(size).PadLeft(12);
                        line = $"{sizeStr}  {fileEntry.Path}";
                    }
                    else
                    {
                        line = fileEntry.Path;
                    }

                    results[i] = (jsonEntry, line, size);
                }
            );

            // Output results sequentially to preserve order
            long totalSize = 0;
            int fileCount = 0;
            foreach (var (jsonEntry, line, size) in results)
            {
                totalSize += size;
                fileCount++;

                if (jsonEntry != null)
                    files.Add(jsonEntry);
                else if (line != null)
                    Console.WriteLine(line);
            }

            result = result with
            {
                Success = errorMessages.Count == 0,
                TotalFiles = fileCount,
                TotalSize = totalSize,
                TotalSizeFormatted = options.SizeFormat.ToFormattedString(totalSize),
            };

            if (options.Json)
            {
                Console.WriteLine(
                    JsonSerializer.Serialize(result, ListOptions.JsonSerializerOptions)
                );
            }
            else
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(
                    $"Total: {fileCount} files, {options.SizeFormat.ToFormattedString(totalSize)}"
                );
            }

            return 0;
        }
        catch (Exception ex)
        {
            return ReportError(ex.Message, options, result, options.Verbose ? ex.StackTrace : null);
        }
    }

    private static void CollectEntries(
        RpfFile rpf,
        string[]? filters,
        bool recursive,
        List<(RpfFile rpf, RpfFileEntry entry)> entries
    )
    {
        foreach (RpfEntry entry in rpf.AllEntries)
        {
            if (entry is RpfFileEntry fileEntry)
            {
                if (entry.NameLower.EndsWith(".rpf"))
                    continue;

                if (!Filter.Matches(entry.Path, filters))
                    continue;

                entries.Add((rpf, fileEntry));
            }
        }

        if (recursive && rpf.Children != null)
        {
            foreach (RpfFile child in rpf.Children)
            {
                CollectEntries(child, filters, recursive, entries);
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
        ListOptions options,
        Json.ListResult result,
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
            Console.WriteLine(JsonSerializer.Serialize(result, ListOptions.JsonSerializerOptions));
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

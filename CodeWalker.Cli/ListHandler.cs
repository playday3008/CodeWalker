using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

namespace CodeWalker.Cli;

internal static class ListHandler
{
    public static Command CreateCommand()
    {
        RpfCommandOptions rpfOpts = new();

        Command command = new("list", "List contents of an RPF archive");
        rpfOpts.AddTo(command);
        command.Aliases.Add("l");

        command.SetAction(parseResult => Execute(rpfOpts.Parse(parseResult)));

        return command;
    }

    public static int Execute(RpfOptions options)
    {
        Json.ListResult ErrorResult(string[] errorMessages) =>
            new()
            {
                Success = false,
                RpfFile = options.RpfPath,
                TotalFiles = 0,
                TotalSize = 0,
                TotalSizeFormatted = "0 B",
                NestedRpfCount = 0,
                Files = [],
                ErrorMessages = errorMessages,
            };

        string? initError = RpfService.ValidateAndLoadKeys(
            options.RpfPath,
            options.ExePath,
            options.Gen9,
            options.Json
        );
        if (initError != null)
        {
            return RpfService.ReportError(initError, options.Json, ErrorResult([]));
        }

        try
        {
            List<string> scanErrors = [];
            RpfFile rpf = RpfService.OpenRpf(
                options.RpfPath,
                options.Verbose,
                options.Json,
                scanErrors
            );

            int nestedRpfCount = (int)rpf.GrandTotalRpfCount;

            if (!options.Json)
            {
                Console.Error.WriteLine();
            }

            // Collect all matching entries
            List<(RpfFile rpf, RpfFileEntry entry)> entries = RpfService.CollectFiles(
                rpf,
                options.Filters,
                options.Recursive
            );

            // Process entries in parallel, storing results by index to preserve order
            (Json.FileEntry? jsonEntry, string? line, long size)[] results = new (
                Json.FileEntry?,
                string?,
                long
            )[entries.Count];

            Parallel.For(
                0,
                entries.Count,
                new ParallelOptions { MaxDegreeOfParallelism = options.Threads },
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
                            Type = RpfService.GetFileType(fileEntry),
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
            List<Json.FileEntry> files = [];
            foreach (var (jsonEntry, line, size) in results)
            {
                totalSize += size;

                if (jsonEntry != null)
                    files.Add(jsonEntry);
                else if (line != null)
                    Console.WriteLine(line);
            }

            Json.ListResult result = new()
            {
                Success = scanErrors.Count == 0,
                RpfFile = options.RpfPath,
                TotalFiles = entries.Count,
                TotalSize = totalSize,
                TotalSizeFormatted = options.SizeFormat.ToFormattedString(totalSize),
                NestedRpfCount = nestedRpfCount,
                Files = [.. files],
                ErrorMessages = [.. scanErrors],
            };

            if (options.Json)
            {
                Console.WriteLine(
                    JsonSerializer.Serialize(result, RpfService.JsonSerializerOptions)
                );
            }
            else
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(
                    $"Total: {entries.Count} files, {options.SizeFormat.ToFormattedString(totalSize)}"
                );
            }

            return 0;
        }
        catch (Exception ex)
        {
            return RpfService.ReportError(
                ex.Message,
                options.Json,
                ErrorResult([]),
                options.Verbose ? ex.StackTrace : null
            );
        }
    }
}

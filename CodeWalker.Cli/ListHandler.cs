using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

namespace CodeWalker.Cli;

public static class ListHandler
{
    public static Command CreateCommand()
    {
        RpfCommandOptions rpfOpts = new();

        Command command = new("list", "List contents of an RPF archive");
        rpfOpts.AddTo(command);
        command.Aliases.Add("l");

        command.SetAction(parseResult =>
        {
            return Execute(rpfOpts.Parse(parseResult));
        });

        return command;
    }

    public static int Execute(RpfOptions options)
    {
        List<Json.FileEntry> files = [];
        List<string> errorMessages = [];

        Json.ListResult result = new()
        {
            Success = false,
            RpfFile = options.RpfPath,
            TotalFiles = 0,
            TotalSize = 0,
            TotalSizeFormatted = "0 B",
            NestedRpfCount = 0,
            Files = files,
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
            return RpfService.ReportError(initError, options.Json, result);
        }

        try
        {
            RpfFile rpf = RpfService.OpenRpf(
                options.RpfPath,
                options.Verbose,
                options.Json,
                errorMessages
            );

            result = result with { NestedRpfCount = rpf.GrandTotalRpfCount };

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
                    JsonSerializer.Serialize(result, RpfService.JsonSerializerOptions)
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
            return RpfService.ReportError(
                ex.Message,
                options.Json,
                result,
                options.Verbose ? ex.StackTrace : null
            );
        }
    }
}

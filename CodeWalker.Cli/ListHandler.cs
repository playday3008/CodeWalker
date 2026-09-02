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
            RpfFile = null!,
            TotalFiles = 0,
            TotalSize = 0,
            TotalSizeFormatted = null!,
            NestedRpfCount = 0,
            Files = files,
            ErrorMessages = errorMessages,
        };

        string? validationError = RpfService.ValidateInputs(
            options.RpfPath,
            options.ExePath,
            options.Gen9
        );
        if (validationError != null)
        {
            return ReportError(validationError, options, result);
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
                Console.Error.WriteLine($"Opening RPF: {options.RpfPath}");
            }

            RpfFile rpf = RpfService.OpenRpf(
                options.RpfPath,
                onStatus: status =>
                {
                    if (options.Verbose && !options.Json)
                        Console.Error.WriteLine(status);
                },
                onError: error =>
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
            return ReportError(ex.Message, options, result, options.Verbose ? ex.StackTrace : null);
        }
    }

    private static int ReportError(
        string message,
        RpfOptions options,
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

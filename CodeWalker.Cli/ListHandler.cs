using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Threading;

using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

namespace CodeWalker.Cli;

internal static class ListHandler
{
    public static Command CreateCommand(CancellationToken cancellationToken = default)
    {
        RpfCommandOptions rpfOpts = new();

        Command command = new("list", "List contents of an RPF archive");
        rpfOpts.AddTo(command);
        command.Aliases.Add("l");

        command.SetAction(parseResult => Execute(rpfOpts.Parse(parseResult), cancellationToken));

        return command;
    }

    public static int Execute(RpfOptions options, CancellationToken cancellationToken = default)
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

            long nestedRpfCount = rpf.GrandTotalRpfCount;

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

            long totalSize = 0;
            List<Json.FileEntry> files = [];

            foreach ((RpfFile _, RpfFileEntry fileEntry) in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long size = fileEntry.GetFileSize();
                totalSize += size;
                string ext = Path.GetExtension(fileEntry.Name).ToLowerInvariant();

                if (options.Json)
                {
                    files.Add(
                        new Json.FileEntry
                        {
                            Path = fileEntry.Path,
                            Name = fileEntry.Name,
                            Size = size,
                            SizeFormatted = options.SizeFormat.ToFormattedString(size),
                            Type = RpfService.GetFileType(fileEntry),
                            Extension = ext,
                        }
                    );
                }
                else if (options.Verbose)
                {
                    string sizeStr = options.SizeFormat.ToFormattedString(size).PadLeft(12);
                    Console.WriteLine($"{sizeStr}  {fileEntry.Path}");
                }
                else
                {
                    Console.WriteLine(fileEntry.Path);
                }
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

            return scanErrors.Count > 0 ? 1 : 0;
        }
        catch (OperationCanceledException) { throw; }
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

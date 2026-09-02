using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Threading;

using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

namespace CodeWalker.Cli.Handlers;

internal static class ListHandler
{
    public static Command CreateCommand(CancellationToken cancellationToken = default)
    {
        RpfCommandOptions rpfOpts = new();

        Command command = new("list", "List contents of an RPF archive");
        rpfOpts.AddTo(command, includeThreads: false);
        command.Aliases.Add("l");

        command.SetAction(parseResult => Execute(rpfOpts.Parse(parseResult), cancellationToken));

        return command;
    }

    public static int Execute(RpfOptions options, CancellationToken cancellationToken = default)
    {
        string? initError = RpfService.ValidateAndLoadKeys(
            options.RpfPath,
            options.ExePath,
            options.Gen9,
            options.Json
        );
        if (initError != null)
        {
            return RpfService.ReportError(
                initError,
                options.Json,
                ErrorResult([], options)
            );
        }

        List<string> scanErrors = [];
        try
        {
            RpfFile rpf = RpfService.OpenRpf(
                options.RpfPath,
                options.Verbose,
                options.Json,
                scanErrors
            );

            if (!options.Json)
                Console.Error.WriteLine();

            List<(RpfFile rpf, RpfFileEntry entry)> entries = RpfService.CollectFiles(
                rpf,
                options.Filters,
                options.Recursive
            );

            Json.ListResult result = CollectList(entries, rpf, scanErrors, options, cancellationToken);

            if (options.Json)
                PrintJsonList(result);
            else
                PrintList(result, options, cancellationToken);

            return scanErrors.Count > 0 ? 1 : 0;
        }
        catch (OperationCanceledException)
        {
            // Gracefully handle cancellation without printing an error message
            throw;
        }
        catch (Exception ex)
        {
            return RpfService.ReportError(
                ex.Message,
                options.Json,
                ErrorResult([.. scanErrors], options),
                options.Verbose ? ex.StackTrace : null
            );
        }
    }

    internal static Json.ListResult ErrorResult(string[] errorMessages, RpfOptions options) =>
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

    internal static Json.ListResult CollectList(
        List<(RpfFile rpf, RpfFileEntry entry)> entries,
        RpfFile rpf,
        List<string> scanErrors,
        RpfOptions options,
        CancellationToken cancellationToken = default)
    {
        long totalSize = 0;
        List<Json.FileEntry> files = [];

        foreach ((RpfFile _, RpfFileEntry fileEntry) in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long size = fileEntry.GetFileSize();
            totalSize += size;
            string ext = Path.GetExtension(fileEntry.Name).ToLowerInvariant();

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

        return new Json.ListResult
        {
            Success = scanErrors.Count == 0,
            RpfFile = options.RpfPath,
            TotalFiles = entries.Count,
            TotalSize = totalSize,
            TotalSizeFormatted = options.SizeFormat.ToFormattedString(totalSize),
            NestedRpfCount = rpf.GrandTotalRpfCount,
            Files = [.. files],
            ErrorMessages = [.. scanErrors],
        };
    }

    internal static void PrintJsonList(Json.ListResult result) =>
        Console.WriteLine(JsonSerializer.Serialize(result, RpfService.JsonSerializerOptions));

    internal static void PrintList(
        Json.ListResult result,
        RpfOptions options,
        CancellationToken cancellationToken = default)
    {
        foreach (Json.FileEntry file in result.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (options.Verbose)
            {
                string sizeStr = file.SizeFormatted.PadLeft(12);
                Console.WriteLine($"{sizeStr}  {file.Path}");
            }
            else
            {
                Console.WriteLine(file.Path);
            }
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine(
            $"Total: {result.TotalFiles} files, {result.TotalSizeFormatted}"
        );
    }
}

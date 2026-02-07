using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;

using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

namespace CodeWalker.Cli;

internal static class StatHandler
{
    public static Command CreateCommand()
    {
        RpfCommandOptions rpfOpts = new();

        Command command = new("stat", "Show aggregate statistics for RPF archive contents");
        rpfOpts.AddTo(command);
        command.Aliases.Add("S");

        command.SetAction(parseResult => Execute(rpfOpts.Parse(parseResult)));

        return command;
    }

    public static int Execute(RpfOptions options)
    {
        Json.StatResult ErrorResult(string[] errorMessages) =>
            new()
            {
                Success = false,
                RpfFile = options.RpfPath,
                TotalFiles = 0,
                TotalSize = 0,
                TotalSizeFormatted = "0 B",
                ResourceCount = 0,
                BinaryCount = 0,
                CompressedSize = 0,
                UncompressedSize = 0,
                CompressionRatio = 0,
                Extensions = [],
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

            if (!options.Json)
            {
                Console.Error.WriteLine();
            }

            List<(RpfFile rpf, RpfFileEntry entry)> entries = RpfService.CollectFiles(
                rpf,
                options.Filters,
                options.Recursive
            );

            long totalSize = 0;
            int resourceCount = 0;
            int binaryCount = 0;
            long compressedSize = 0;
            long uncompressedSize = 0;

            Dictionary<string, (int count, long total, long min, long max)> extStats = new();

            foreach ((RpfFile _, RpfFileEntry fileEntry) in entries)
            {
                long size = fileEntry.GetFileSize();
                totalSize += size;
                string ext = Path.GetExtension(fileEntry.Name).ToLowerInvariant();
                if (string.IsNullOrEmpty(ext))
                    ext = "(none)";

                if (extStats.TryGetValue(ext, out var stat))
                {
                    extStats[ext] = (
                        stat.count + 1,
                        stat.total + size,
                        Math.Min(stat.min, size),
                        Math.Max(stat.max, size)
                    );
                }
                else
                {
                    extStats[ext] = (1, size, size, size);
                }

                if (fileEntry is RpfResourceFileEntry rfe)
                {
                    resourceCount++;
                    compressedSize += rfe.FileSize;
                    uncompressedSize += rfe.SystemSize + rfe.GraphicsSize;
                }
                else if (fileEntry is RpfBinaryFileEntry bfe)
                {
                    binaryCount++;
                    compressedSize += bfe.FileSize;
                    uncompressedSize += bfe.FileUncompressedSize;
                }
            }
            double compressionRatio =
                uncompressedSize > 0 ? (double)compressedSize / uncompressedSize : 0;

            List<Json.ExtensionStat> extensionStats =
            [
                .. extStats
                    .OrderByDescending(kv => kv.Value.total)
                    .Select(kv => new Json.ExtensionStat
                    {
                        Extension = kv.Key,
                        Count = kv.Value.count,
                        TotalSize = kv.Value.total,
                        TotalSizeFormatted = options.SizeFormat.ToFormattedString(kv.Value.total),
                        AvgSize = kv.Value.count > 0 ? kv.Value.total / kv.Value.count : 0,
                        MinSize = kv.Value.min,
                        MaxSize = kv.Value.max,
                    }),
            ];

            Json.StatResult result = new()
            {
                Success = scanErrors.Count == 0,
                RpfFile = options.RpfPath,
                TotalFiles = entries.Count,
                TotalSize = totalSize,
                TotalSizeFormatted = options.SizeFormat.ToFormattedString(totalSize),
                ResourceCount = resourceCount,
                BinaryCount = binaryCount,
                CompressedSize = compressedSize,
                UncompressedSize = uncompressedSize,
                CompressionRatio = Math.Round(compressionRatio, 4),
                Extensions = extensionStats,
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
                // Table header
                Console.WriteLine(
                    $"{"Extension",-12} {"Count",8} {"Total",14} {"Avg",14} {"Min",14} {"Max",14}"
                );
                Console.WriteLine(new string('-', 78));

                foreach (Json.ExtensionStat ext in extensionStats)
                {
                    Console.WriteLine(
                        $"{ext.Extension,-12} {ext.Count,8} {options.SizeFormat.ToFormattedString(ext.TotalSize),14} {options.SizeFormat.ToFormattedString(ext.AvgSize),14} {options.SizeFormat.ToFormattedString(ext.MinSize),14} {options.SizeFormat.ToFormattedString(ext.MaxSize),14}"
                    );
                }

                Console.Error.WriteLine();
                Console.Error.WriteLine(
                    $"Total: {entries.Count} files, {options.SizeFormat.ToFormattedString(totalSize)}"
                );
                Console.Error.WriteLine($"Types: {resourceCount} resource, {binaryCount} binary");

                if (uncompressedSize > 0)
                {
                    Console.Error.WriteLine(
                        $"Compression: {options.SizeFormat.ToFormattedString(compressedSize)} / {options.SizeFormat.ToFormattedString(uncompressedSize)} ({compressionRatio:P1})"
                    );
                }
            }

            return scanErrors.Count > 0 ? 1 : 0;
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

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

namespace CodeWalker.Cli.Handlers;

internal static class StatHandler
{
    public static Command CreateCommand(CancellationToken cancellationToken = default)
    {
        RpfCommandOptions rpfOpts = new();

        Command command = new("stat", "Show aggregate statistics for RPF archive contents");
        rpfOpts.AddTo(command);
        command.Aliases.Add("S");

        command.SetAction(parseResult => Execute(rpfOpts.Parse(parseResult), cancellationToken));

        return command;
    }

    /// <summary>
    /// Executes the stat command by validating the RPF file, collecting file entries, calculating statistics, and printing the results in either JSON or human-readable format.
    /// </summary>
    /// <param name="options">The options for the stat command, including the RPF file path, filters, and output format.</param>
    /// <param name="cancellationToken">A cancellation token to observe while performing the operation.</param>
    /// <returns>An integer exit code indicating success (0) or failure (1).</returns>
    public static int Execute(RpfOptions options, CancellationToken cancellationToken = default)
    {
        string? initError = RpfService.ValidateAndLoadKeys(
            options.RpfPath,
            options.ExePath,
            options.Gen9,
            options.Json
        );
        if (initError != null)
            return RpfService.ReportError(initError, options.Json, ErrorResult([], options));

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
                Console.Error.WriteLine();

            List<(RpfFile rpf, RpfFileEntry entry)> entries = RpfService.CollectFiles(
                rpf,
                options.Filters,
                options.Recursive
            );

            Json.StatResult result = CollectStats(entries, scanErrors, options, cancellationToken);

            if (options.Json)
                PrintJsonStats(result);
            else
                PrintStats(result, options);

            return scanErrors.Count > 0 ? 1 : 0;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return RpfService.ReportError(
                ex.Message,
                options.Json,
                ErrorResult([], options),
                options.Verbose ? ex.StackTrace : null
            );
        }
    }

    /// <summary>
    /// Creates a JSON result object representing an error, with the provided error messages and default values for all statistics fields.
    /// </summary>
    /// <param name="errorMessages">An array of error messages to include in the result.</param>
    /// <param name="options">The options used to populate the RpfFile field in the result.</param>
    /// <returns>A <see cref="Json.StatResult"/> object with success set to false, the RpfFile field set from options, and all statistics fields set to default values.</returns>
    internal static Json.StatResult ErrorResult(string[] errorMessages, RpfOptions options) =>
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
            CompressedSizeFormatted = "0 B",
            UncompressedSize = 0,
            UncompressedSizeFormatted = "0 B",
            CompressionRatio = 0,
            Extensions = [],
            ErrorMessages = errorMessages,
        };

    /// <summary>
    /// Collects the statistics for the given list of RPF file entries, including total size, file counts, compression ratios, and extension-based statistics, and returns the results in a <see cref="Json.StatResult"/> object.
    /// </summary>
    /// <param name="entries">A list of tuples containing the RPF file and its corresponding file entry to analyze for statistics.</param>
    /// <param name="scanErrors">A list of error messages encountered during the scanning process, which will be included in the result.</param>
    /// <param name="options">The options used to populate the RpfFile field in the result and format size values.</param>
    /// <param name="cancellationToken">A cancellation token to observe while performing the statistics collection operation.</param>
    /// <returns>A <see cref="Json.StatResult"/> object containing the collected statistics for the RPF file entries, including total size, file counts, compression ratios, extension-based statistics, and any error messages.</returns>
    internal static Json.StatResult CollectStats(
        List<(RpfFile rpf, RpfFileEntry entry)> entries,
        List<string> scanErrors,
        RpfOptions options,
        CancellationToken cancellationToken = default)
    {
        long totalSize = 0;
        int resourceCount = 0;
        int binaryCount = 0;
        long compressedSize = 0;
        long uncompressedSize = 0;

        Dictionary<string, (int count, long total, long min, long max)> extStats = [];

        foreach ((RpfFile _, RpfFileEntry fileEntry) in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long size = fileEntry.GetFileSize();
            totalSize += size;
            string ext = Path.GetExtension(fileEntry.Name).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext))
                ext = "(none)";

            if (extStats.TryGetValue(ext, out (int count, long total, long min, long max) stat))
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
                    AvgSizeFormatted = options.SizeFormat.ToFormattedString(kv.Value.count > 0 ? kv.Value.total / kv.Value.count : 0),
                    MinSize = kv.Value.min,
                    MinSizeFormatted = options.SizeFormat.ToFormattedString(kv.Value.min),
                    MaxSize = kv.Value.max,
                    MaxSizeFormatted = options.SizeFormat.ToFormattedString(kv.Value.max),
                }),
        ];

        return new Json.StatResult()
        {
            Success = scanErrors.Count == 0,
            RpfFile = options.RpfPath,
            TotalFiles = entries.Count,
            TotalSize = totalSize,
            TotalSizeFormatted = options.SizeFormat.ToFormattedString(totalSize),
            ResourceCount = resourceCount,
            BinaryCount = binaryCount,
            CompressedSize = compressedSize,
            CompressedSizeFormatted = options.SizeFormat.ToFormattedString(compressedSize),
            UncompressedSize = uncompressedSize,
            UncompressedSizeFormatted = options.SizeFormat.ToFormattedString(uncompressedSize),
            CompressionRatio = Math.Round(compressionRatio, 4),
            Extensions = extensionStats,
            ErrorMessages = [.. scanErrors],
        };
    }

    /// <summary>
    /// Prints the collected statistics to the console in JSON format.
    /// </summary>
    /// <param name="result">The collected statistics to serialize.</param>
    internal static void PrintJsonStats(Json.StatResult result) =>
        Console.WriteLine(JsonSerializer.Serialize(result, RpfService.JsonSerializerOptions));

    /// <summary>
    /// Prints the collected statistics to the console in a human-readable format.
    /// </summary>
    /// <param name="result">The collected statistics to print.</param>
    /// <param name="options">The options used to format size values in the output.</param>
    internal static void PrintStats(Json.StatResult result, RpfOptions options)
    {
        // Collect all rows for dynamic column sizing
        string[] headers = ["Extension", "Count", "Total", "Avg", "Min", "Max"];
        List<string[]> rows = new(result.Extensions.Count);
        foreach (Json.ExtensionStat ext in result.Extensions)
        {
            rows.Add([
                ext.Extension,
                ext.Count.ToString(CultureInfo.InvariantCulture),
                options.SizeFormat.ToFormattedString(ext.TotalSize),
                options.SizeFormat.ToFormattedString(ext.AvgSize),
                options.SizeFormat.ToFormattedString(ext.MinSize),
                options.SizeFormat.ToFormattedString(ext.MaxSize),
            ]);
        }

        // Calculate column widths from headers and data
        int[] widths = new int[headers.Length];
        for (int i = 0; i < headers.Length; i++)
            widths[i] = headers[i].Length;

        foreach (string[] row in rows)
            for (int i = 0; i < row.Length; i++)
                widths[i] = Math.Max(widths[i], row[i].Length);

        // Print header — first column left-aligned, rest right-aligned
        Console.Write($" {headers[0].PadRight(widths[0])} ");
        for (int i = 1; i < headers.Length; i++)
            Console.Write($"| {headers[i].PadLeft(widths[i])} ");
        Console.WriteLine();

        // Separator with column dividers
        Console.Write(new string('-', widths[0] + 2));
        for (int i = 1; i < widths.Length; i++)
            Console.Write($"+{new string('-', widths[i] + 2)}");
        Console.WriteLine();

        // Print data rows
        foreach (string[] row in rows)
        {
            Console.Write($" {row[0].PadRight(widths[0])} ");
            for (int i = 1; i < row.Length; i++)
                Console.Write($"| {row[i].PadLeft(widths[i])} ");
            Console.WriteLine();
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine(
            $"Total: {result.TotalFiles} files, {options.SizeFormat.ToFormattedString(result.TotalSize)}"
        );
        Console.Error.WriteLine($"Types: {result.ResourceCount} resource, {result.BinaryCount} binary");

        if (result.UncompressedSize > 0)
        {
            string compressedStr = options.SizeFormat.ToFormattedString(result.CompressedSize);
            string uncompressedStr = options.SizeFormat.ToFormattedString(result.UncompressedSize);
            Console.Error.WriteLine(
                $"Compression: {compressedStr} / {uncompressedStr} ({result.CompressionRatio:P1} of original)"
            );
        }
    }
}

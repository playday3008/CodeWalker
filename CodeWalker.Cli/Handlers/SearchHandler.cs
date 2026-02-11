using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

namespace CodeWalker.Cli.Handlers;

internal static class SearchHandler
{
    public static Command CreateCommand(CancellationToken cancellationToken = default)
    {
        RpfCommandOptions rpfOpts = new();
        Argument<string> patternArg = new("pattern")
        {
            Description = "Search pattern: glob, substring, or hash (0x hex or decimal)",
        };

        Command command = new("search", "Search for files by name, path, or hash in an RPF archive")
        {
            patternArg,
        };
        rpfOpts.AddTo(command);
        command.Aliases.Add("s");

        command.SetAction(parseResult =>
            Execute(rpfOpts.Parse(parseResult), parseResult.GetRequiredValue(patternArg), cancellationToken)
        );

        return command;
    }

    public static int Execute(RpfOptions options, string pattern, CancellationToken cancellationToken = default)
    {
        Json.SearchResult ErrorResult(string[] errorMessages) =>
            new()
            {
                Success = false,
                RpfFile = options.RpfPath,
                Pattern = pattern,
                PatternType = "unknown",
                MatchCount = 0,
                Matches = [],
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

            // Collect all entries (including directories) recursively
            List<RpfEntry> allEntries = [];
            CollectAllEntries(rpf, options.Recursive, allEntries);

            // Detect pattern type
            string patternType;
            Func<RpfEntry, bool> matcher;

            if (pattern.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                // Hex hash
                patternType = "hash_hex";
                if (
                    !uint.TryParse(
                        pattern[2..],
                        System.Globalization.NumberStyles.HexNumber,
                        null,
                        out uint hash
                    )
                )
                {
                    return RpfService.ReportError(
                        $"Invalid hex hash: {pattern}",
                        options.Json,
                        ErrorResult([])
                    );
                }
                matcher = entry => entry.NameHash == hash || entry.ShortNameHash == hash;
            }
            else if (
                uint.TryParse(pattern, out uint decHash)
                && pattern.Length >= 5
                && !HasGlobChars(pattern)
            )
            {
                // Decimal hash (require 5+ digits to avoid matching short filenames)
                patternType = "hash_decimal";
                matcher = entry => entry.NameHash == decHash || entry.ShortNameHash == decHash;
            }
            else if (HasGlobChars(pattern))
            {
                // Glob pattern — reuse Filter.Matches
                patternType = "glob";
                string[] filters = Filter.Normalize([pattern]);
                matcher = entry => entry.Path != null && Filter.Matches(entry.Path, filters);
            }
            else
            {
                // Substring match
                patternType = "substring";
                string normalizedPattern = pattern.Replace('\\', '/');
                matcher = entry =>
                    entry.Path?.Replace('\\', '/').Contains(normalizedPattern, StringComparison.OrdinalIgnoreCase) == true;
            }

            // Match in parallel
            Json.SearchMatch?[] results = new Json.SearchMatch?[allEntries.Count];

            _ = Parallel.For(
                0,
                allEntries.Count,
                new ParallelOptions { MaxDegreeOfParallelism = options.Threads, CancellationToken = cancellationToken },
                i =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    RpfEntry entry = allEntries[i];
                    if (!matcher(entry))
                        return;

                    long size = 0;
                    string type = "directory";
                    string ext = "";

                    if (entry is RpfFileEntry fileEntry)
                    {
                        size = fileEntry.GetFileSize();
                        type = RpfService.GetFileType(fileEntry);
                        ext = Path.GetExtension(fileEntry.Name).ToLowerInvariant();
                    }

                    results[i] = new Json.SearchMatch
                    {
                        Path = entry.Path ?? entry.Name ?? "",
                        Name = entry.Name ?? "",
                        Size = size,
                        Type = type,
                        Extension = ext,
                        NameHash = entry.NameHash,
                        ShortNameHash = entry.ShortNameHash,
                    };
                }
            );

            // Collect non-null results
            List<Json.SearchMatch> matches = results.OfType<Json.SearchMatch>().ToList();

            Json.SearchResult result = new()
            {
                Success = scanErrors.Count == 0,
                RpfFile = options.RpfPath,
                Pattern = pattern,
                PatternType = patternType,
                MatchCount = matches.Count,
                Matches = matches,
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
                foreach (Json.SearchMatch match in matches)
                {
                    if (options.Verbose)
                    {
                        string sizeStr = options
                            .SizeFormat.ToFormattedString(match.Size)
                            .PadLeft(12);
                        Console.WriteLine($"{sizeStr}  {match.Path}");
                    }
                    else
                    {
                        Console.WriteLine(match.Path);
                    }
                }

                Console.Error.WriteLine();
                Console.Error.WriteLine(
                    $"Found {matches.Count} matches for '{pattern}' ({patternType})"
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

    internal static bool HasGlobChars(string s) =>
        s.Contains('*', StringComparison.Ordinal) ||
        s.Contains('?', StringComparison.Ordinal);

    private static void CollectAllEntries(RpfFile rpf, bool recursive, List<RpfEntry> entries)
    {
        if (rpf.AllEntries != null)
        {
            entries.AddRange(rpf.AllEntries);
        }

        if (recursive && rpf.Children != null)
        {
            foreach (RpfFile child in rpf.Children)
            {
                CollectAllEntries(child, recursive, entries);
            }
        }
    }
}

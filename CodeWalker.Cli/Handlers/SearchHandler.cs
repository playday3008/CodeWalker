using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using System.Threading;

using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

namespace CodeWalker.Cli.Handlers;

internal static class SearchHandler
{
    public static Command CreateCommand(CancellationToken cancellationToken = default)
    {
        RpfCommandOptions rpfOpts = new()
        {
            Rpf = { Required = false }
        };

        Option<DirectoryInfo> dirOpt = new("--dir", "-D")
        {
            Description = "Directory to search — discovers all .rpf files recursively",
        };

        Argument<string> patternArg = new("pattern")
        {
            Description = "Substring to search for in file paths",
        };

        Command command = new("search", "Search for files by name or path in an RPF archive")
        {
            patternArg,
        };
        rpfOpts.AddTo(command, includeThreads: false);
        command.Add(dirOpt);
        command.Aliases.Add("s");

        command.Validators.Add(result =>
        {
            bool hasRpf = result.GetValue(rpfOpts.Rpf) != null;
            bool hasDir = result.GetValue(dirOpt) != null;
            if (hasRpf == hasDir)
                result.AddError("Specify exactly one of --rpf or --dir.");
        });

        command.SetAction(parseResult =>
            Execute(
                rpfOpts.Parse(parseResult),
                parseResult.GetRequiredValue(patternArg),
                parseResult.GetValue(dirOpt)?.FullName,
                cancellationToken)
        );

        return command;
    }

    public static int Execute(RpfOptions options, string pattern, string? dirPath = null, CancellationToken cancellationToken = default)
    {
        if (dirPath != null)
            return ExecuteDirectory(options, pattern, dirPath, cancellationToken);

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
                ErrorResult([], options, pattern)
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

            Json.SearchResult result = CollectSearch(rpf, scanErrors, options, pattern, cancellationToken: cancellationToken);

            if (options.Json)
                PrintJsonSearch(result);
            else
                PrintSearch(result, options);

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
                ErrorResult([.. scanErrors], options, pattern),
                options.Verbose ? ex.StackTrace : null
            );
        }
    }

    internal static int ExecuteDirectory(RpfOptions options, string pattern, string dirPath, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(dirPath))
        {
            return RpfService.ReportError(
                $"Directory not found: {dirPath}",
                options.Json,
                ErrorResult([], options, pattern)
            );
        }

        string[] rpfPaths = Directory.GetFiles(dirPath, "*.rpf", SearchOption.AllDirectories);
        Array.Sort(rpfPaths, StringComparer.OrdinalIgnoreCase);

        if (rpfPaths.Length == 0)
        {
            return RpfService.ReportError(
                $"No .rpf files found in: {dirPath}",
                options.Json,
                ErrorResult([], options, pattern)
            );
        }

        string? initError = RpfService.ValidateExeAndLoadKeys(
            options.ExePath,
            options.Gen9,
            options.Json
        );
        if (initError != null)
        {
            return RpfService.ReportError(
                initError,
                options.Json,
                ErrorResult([], options, pattern)
            );
        }

        List<string> allScanErrors = [];
        List<Json.SearchMatch> allMatches = [];
        List<string> rpfFiles = [];

        foreach (string rpfPath in rpfPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<string> scanErrors = [];
            try
            {
                RpfFile rpf = RpfService.OpenRpf(
                    rpfPath,
                    options.Verbose,
                    options.Json,
                    scanErrors
                );

                Json.SearchResult partialResult = CollectSearch(rpf, scanErrors, options, pattern, archive: rpfPath, cancellationToken: cancellationToken);
                rpfFiles.Add(rpfPath);

                allMatches.AddRange(partialResult.Matches);
                allScanErrors.AddRange(scanErrors);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                allScanErrors.Add($"{rpfPath}: {ex.Message}");
            }
        }

        if (!options.Json)
            Console.Error.WriteLine();

        Json.SearchResult result = new()
        {
            Success = allScanErrors.Count == 0,
            RpfFile = dirPath,
            RpfFiles = rpfFiles,
            Pattern = pattern,
            PatternType = "substring",
            MatchCount = allMatches.Count,
            Matches = allMatches,
            ErrorMessages = [.. allScanErrors],
        };

        if (options.Json)
            PrintJsonSearch(result);
        else
            PrintSearch(result, options);

        return allScanErrors.Count > 0 ? 1 : 0;
    }

    internal static Json.SearchResult ErrorResult(string[] errorMessages, RpfOptions options, string pattern) =>
        new()
        {
            Success = false,
            RpfFile = options.RpfPath,
            RpfFiles = [],
            Pattern = pattern,
            PatternType = "substring",
            MatchCount = 0,
            Matches = [],
            ErrorMessages = errorMessages,
        };

    internal static Json.SearchResult CollectSearch(
        RpfFile rpf,
        List<string> scanErrors,
        RpfOptions options,
        string pattern,
        string? archive = null,
        CancellationToken cancellationToken = default)
    {
        string archivePath = archive ?? options.RpfPath;
        string normalizedPattern = pattern.Replace('\\', '/');

        List<RpfEntry> allEntries = [];
        CollectAllEntries(rpf, options.Recursive, allEntries);

        List<Json.SearchMatch> matches = [];

        foreach (RpfEntry entry in allEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Filter.Matches(entry.Path ?? "", options.Filters))
                continue;

            if (entry.Path?.Replace('\\', '/').Contains(normalizedPattern, StringComparison.OrdinalIgnoreCase) != true)
                continue;

            long size = 0;
            string type = "directory";
            string ext = "";

            if (entry is RpfFileEntry fileEntry)
            {
                size = fileEntry.GetFileSize();
                type = RpfService.GetFileType(fileEntry);
                ext = Path.GetExtension(fileEntry.Name).ToLowerInvariant();
            }

            matches.Add(new Json.SearchMatch
            {
                Archive = archivePath,
                Path = entry.Path ?? entry.Name ?? "",
                Name = entry.Name ?? "",
                Size = size,
                Type = type,
                Extension = ext,
            });
        }

        return new Json.SearchResult
        {
            Success = scanErrors.Count == 0,
            RpfFile = archivePath,
            RpfFiles = [archivePath],
            Pattern = pattern,
            PatternType = "substring",
            MatchCount = matches.Count,
            Matches = matches,
            ErrorMessages = [.. scanErrors],
        };
    }

    internal static void PrintJsonSearch(Json.SearchResult result) =>
        Console.WriteLine(JsonSerializer.Serialize(result, RpfService.JsonSerializerOptions));

    internal static void PrintSearch(Json.SearchResult result, RpfOptions options)
    {
        bool multiArchive = result.RpfFiles.Count > 1;
        string? lastArchive = null;

        foreach (Json.SearchMatch match in result.Matches)
        {
            if (multiArchive && match.Archive != lastArchive)
            {
                if (lastArchive != null)
                    Console.Error.WriteLine();
                Console.Error.WriteLine($"== {RelativePath(result.RpfFile, match.Archive)} ==");
                lastArchive = match.Archive;
            }

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

        string matchWord = result.MatchCount == 1 ? "match" : "matches";
        Console.Error.WriteLine();
        Console.Error.WriteLine(
            multiArchive
                ? $"Found {result.MatchCount} {matchWord} across {result.RpfFiles.Count} archive(s) for '{result.Pattern}' ({result.PatternType})"
                : $"Found {result.MatchCount} {matchWord} for '{result.Pattern}' ({result.PatternType})"
        );
    }

    internal static string RelativePath(string basePath, string fullPath)
    {
        // Normalize separators and ensure trailing separator on base
        string normalizedBase = basePath.Replace('\\', '/').TrimEnd('/') + "/";
        string normalizedFull = fullPath.Replace('\\', '/');

        return normalizedFull.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase)
            ? normalizedFull[normalizedBase.Length..]
            : Path.GetFileName(fullPath);
    }

    internal static void CollectAllEntries(RpfFile rpf, bool recursive, List<RpfEntry> entries)
    {
        if (rpf.AllEntries != null)
            entries.AddRange(rpf.AllEntries);

        if (!recursive || rpf.Children == null)
            return;

        foreach (RpfFile child in rpf.Children)
            CollectAllEntries(child, recursive, entries);
    }
}

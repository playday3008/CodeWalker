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

namespace CodeWalker.Cli;

internal sealed record ValidateOptions
{
    public required RpfOptions Rpf { get; init; }
    public required bool Progress { get; init; }
}

internal static class ValidateHandler
{
    public static Command CreateCommand(CancellationToken cancellationToken = default)
    {
        RpfCommandOptions rpfOpts = new();
        Option<bool> progressOption = new("--progress", "-P")
        {
            Description = "Show progress bar during validation",
        };

        Command command = new("validate", "Validate game file integrity by parsing RPF contents")
        {
            progressOption,
        };
        rpfOpts.AddTo(command);
        command.Aliases.Add("val");

        command.SetAction(parseResult =>
        {
            ValidateOptions options = new()
            {
                Rpf = rpfOpts.Parse(parseResult),
                Progress = parseResult.GetValue(progressOption),
            };
            return Execute(options, cancellationToken);
        });

        return command;
    }

    public static int Execute(ValidateOptions options, CancellationToken cancellationToken = default)
    {
        Json.ValidateResult ErrorResult(string[] errorMessages) =>
            new()
            {
                Success = false,
                RpfFile = options.Rpf.RpfPath,
                TotalFiles = 0,
                Valid = 0,
                Warnings = 0,
                Errors = 0,
                Skipped = 0,
                Files = [],
                ErrorMessages = errorMessages,
            };

        string? initError = RpfService.ValidateAndLoadKeys(
            options.Rpf.RpfPath,
            options.Rpf.ExePath,
            options.Rpf.Gen9,
            options.Rpf.Json
        );
        if (initError != null)
        {
            return RpfService.ReportError(initError, options.Rpf.Json, ErrorResult([]));
        }

        try
        {
            List<string> scanErrors = [];
            RpfFile rpf = RpfService.OpenRpf(
                options.Rpf.RpfPath,
                options.Rpf.Verbose,
                options.Rpf.Json,
                scanErrors
            );

            if (!options.Rpf.Json)
            {
                Console.Error.WriteLine();
            }

            List<(RpfFile rpf, RpfFileEntry entry)> entries = RpfService.CollectFiles(
                rpf,
                options.Rpf.Filters,
                options.Rpf.Recursive
            );

            Json.ValidateFileEntry?[] results = new Json.ValidateFileEntry?[entries.Count];
            object consoleLock = new();

            using (ProgressBar progress = new(entries.Count, options.Progress && !options.Rpf.Json))
            {
                _ = Parallel.For(
                    0,
                    entries.Count,
                    new ParallelOptions { MaxDegreeOfParallelism = options.Rpf.Threads, CancellationToken = cancellationToken },
                    i =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        (_, RpfFileEntry fileEntry) = entries[i];
                        string ext = Path.GetExtension(fileEntry.Name).ToLowerInvariant();

                        try
                        {
                            (string status, string? message) = ValidateFile(
                                fileEntry,
                                ext
                            );

                            results[i] = new Json.ValidateFileEntry
                            {
                                Path = fileEntry.Path,
                                Name = fileEntry.Name,
                                Status = status,
                                Message = message,
                            };

                            if (
                                !options.Rpf.Json
                                && !options.Progress
                                && (status == "warning" || status == "error")
                            )
                            {
                                lock (consoleLock)
                                {
                                    Console.Error.WriteLine(
                                        $"[{status.ToUpperInvariant()}] {fileEntry.Path}: {message}"
                                    );
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            results[i] = new Json.ValidateFileEntry
                            {
                                Path = fileEntry.Path,
                                Name = fileEntry.Name,
                                Status = "error",
                                Message = ex.Message,
                            };

                            if (!options.Rpf.Json && !options.Progress)
                            {
                                lock (consoleLock)
                                {
                                    Console.Error.WriteLine(
                                        $"[ERROR] {fileEntry.Path}: {ex.Message}"
                                    );
                                }
                            }
                        }

                        progress.Increment(fileEntry.Path);
                    }
                );
            }

            // Aggregate results
            List<Json.ValidateFileEntry> nonNull = results.OfType<Json.ValidateFileEntry>().ToList();
            int valid = nonNull.Count(e => e.Status == "valid");
            int warnings = nonNull.Count(e => e.Status == "warning");
            int errors = nonNull.Count(e => e.Status == "error");
            int skipped = nonNull.Count(e => e.Status == "skipped");

            // In verbose mode or JSON, include all; otherwise only warnings/errors
            List<Json.ValidateFileEntry> files = (options.Rpf.Json || options.Rpf.Verbose)
                ? nonNull
                : nonNull.Where(e => e.Status is "warning" or "error").ToList();

            Json.ValidateResult result = new()
            {
                Success = errors == 0 && scanErrors.Count == 0,
                RpfFile = options.Rpf.RpfPath,
                TotalFiles = entries.Count,
                Valid = valid,
                Warnings = warnings,
                Errors = errors,
                Skipped = skipped,
                Files = files,
                ErrorMessages = [.. scanErrors],
            };

            if (options.Rpf.Json)
            {
                Console.WriteLine(
                    JsonSerializer.Serialize(result, RpfService.JsonSerializerOptions)
                );
            }
            else
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(
                    $"Validation complete: {valid} valid, {warnings} warnings, {errors} errors, {skipped} skipped"
                );
            }

            return (errors > 0 || scanErrors.Count > 0) ? 1 : 0;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return RpfService.ReportError(
                ex.Message,
                options.Rpf.Json,
                ErrorResult([]),
                options.Rpf.Verbose ? ex.StackTrace : null
            );
        }
    }

    private static (string status, string? message) ValidateFile(
        RpfFileEntry fileEntry,
        string ext
    )
    {
        switch (ext)
        {
            case ".ytd":
                {
                    YtdFile file = RpfFile.GetFile<YtdFile>(fileEntry);
                    if (file == null)
                        return ("error", "Failed to load YTD file");
                    if (file.TextureDict?.Textures?.data_items == null || file.TextureDict.Textures.data_items.Length == 0)
                        return ("warning", "Texture dictionary is empty");
                    return ("valid", null);
                }
            case ".ydr":
                {
                    YdrFile file = RpfFile.GetFile<YdrFile>(fileEntry);
                    if (file == null)
                        return ("error", "Failed to load YDR file");
                    if (file.Drawable == null)
                        return ("error", "Drawable is null");
                    return ("valid", null);
                }
            case ".ydd":
                {
                    YddFile file = RpfFile.GetFile<YddFile>(fileEntry);
                    if (file == null)
                        return ("error", "Failed to load YDD file");
                    if (file.DrawableDict == null)
                        return ("error", "DrawableDict is null");
                    return ("valid", null);
                }
            case ".yft":
                {
                    YftFile file = RpfFile.GetFile<YftFile>(fileEntry);
                    if (file == null)
                        return ("error", "Failed to load YFT file");
                    if (file.Fragment == null)
                        return ("error", "Fragment is null");
                    return ("valid", null);
                }
            case ".ymap":
                {
                    YmapFile file = RpfFile.GetFile<YmapFile>(fileEntry);
                    if (file == null)
                        return ("error", "Failed to load YMAP file");
                    if (file.AllEntities == null || file.AllEntities.Length == 0)
                        return ("warning", "No entities found");
                    return ("valid", null);
                }
            case ".ytyp":
                {
                    YtypFile file = RpfFile.GetFile<YtypFile>(fileEntry);
                    if (file == null)
                        return ("error", "Failed to load YTYP file");
                    if (file.AllArchetypes == null || file.AllArchetypes.Length == 0)
                        return ("warning", "No archetypes found");
                    return ("valid", null);
                }
            case ".ybn":
                {
                    YbnFile file = RpfFile.GetFile<YbnFile>(fileEntry);
                    if (file == null)
                        return ("error", "Failed to load YBN file");
                    if (file.Bounds == null)
                        return ("error", "Bounds is null");
                    return ("valid", null);
                }
            case ".awc":
                {
                    AwcFile file = RpfFile.GetFile<AwcFile>(fileEntry);
                    if (file == null)
                        return ("error", "Failed to load AWC file");
                    if (file.Streams == null || file.Streams.Length == 0)
                        return ("warning", "No audio streams found");
                    return ("valid", null);
                }
            case ".gxt2":
                {
                    Gxt2File file = RpfFile.GetFile<Gxt2File>(fileEntry);
                    if (file == null)
                        return ("error", "Failed to load GXT2 file");
                    if (file.TextEntries == null || file.TextEntries.Length == 0)
                        return ("warning", "No text entries found");
                    return ("valid", null);
                }
            default:
                return ("skipped", null);
        }
    }
}

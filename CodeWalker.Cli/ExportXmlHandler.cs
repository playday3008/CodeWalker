using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

namespace CodeWalker.Cli;

public static class ExportXmlHandler
{
    public static Command CreateCommand()
    {
        ExportCommandOptions exportOpts = new();

        Command command = new("xml", "Export binary game files to XML");
        exportOpts.AddTo(command);
        command.Aliases.Add("x");

        command.SetAction(parseResult =>
        {
            ExportOptions options = exportOpts.Parse(parseResult);
            return Execute(options);
        });

        return command;
    }

    public static int Execute(ExportOptions options)
    {
        List<Json.ExportFileEntry> files = [];
        List<string> errorMessages = [];

        Json.ExportResult result = new()
        {
            Success = false,
            RpfFile = options.Rpf.RpfPath,
            OutputDir = options.OutputPath,
            Format = "xml",
            TotalFiles = 0,
            Exported = 0,
            Skipped = 0,
            Errors = 0,
            DryRun = options.DryRun,
            Files = files,
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
            return RpfService.ReportError(initError, options.Rpf.Json, result);
        }

        try
        {
            RpfFile rpf = RpfService.OpenRpf(
                options.Rpf.RpfPath,
                options.Rpf.Verbose,
                options.Rpf.Json,
                errorMessages
            );

            if (!options.Rpf.Json && options.DryRun)
            {
                Console.Error.WriteLine("Dry run mode - no files will be exported");
            }

            string outputDir = options.OutputPath;

            if (!options.DryRun && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            List<(RpfFile rpf, RpfFileEntry entry)> filesToExport = RpfService.CollectFiles(
                rpf,
                options.Rpf.Filters,
                options.Rpf.Recursive
            );

            int totalNonRpfFiles = RpfService.CountNonRpfFiles(rpf, options.Rpf.Recursive);
            int skipped = totalNonRpfFiles - filesToExport.Count;

            result = result with { TotalFiles = filesToExport.Count };

            (bool success, Json.ExportFileEntry? jsonEntry, string? errorMessage)[] results = new (
                bool,
                Json.ExportFileEntry?,
                string?
            )[filesToExport.Count];

            object consoleLock = new();

            using (
                ProgressBar progress = new(
                    filesToExport.Count,
                    options.Progress && !options.Rpf.Json
                )
            )
            {
                Parallel.For(
                    0,
                    filesToExport.Count,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Max(1, options.Rpf.Threads),
                    },
                    i =>
                    {
                        (RpfFile sourceRpf, RpfFileEntry fileEntry) = filesToExport[i];
                        try
                        {
                            string relativePath =
                                Path.GetDirectoryName(fileEntry.Path)
                                    ?.Replace('\\', Path.DirectorySeparatorChar)
                                ?? "";

                            string fileOutputDir = Path.Combine(outputDir, relativePath);

                            if (options.DryRun)
                            {
                                if (options.Rpf.Verbose && !options.Rpf.Json)
                                {
                                    lock (consoleLock)
                                    {
                                        Console.WriteLine($"Would export: {fileEntry.Path}");
                                    }
                                }
                                results[i] = (
                                    true,
                                    new Json.ExportFileEntry
                                    {
                                        Path = fileEntry.Path,
                                        Name = fileEntry.Name,
                                        OutputFiles = 0,
                                        Status = "dry_run",
                                    },
                                    null
                                );
                                progress.Increment(fileEntry.Path);
                                return;
                            }

                            byte[]? data = sourceRpf.ExtractFile(fileEntry);
                            if (data == null)
                            {
                                results[i] = (false, null, $"Failed to extract: {fileEntry.Path}");
                                progress.Increment();
                                return;
                            }

                            string xml = MetaXml.GetXml(
                                fileEntry,
                                data,
                                out string filename,
                                fileOutputDir
                            );

                            if (string.IsNullOrEmpty(xml))
                            {
                                results[i] = (
                                    false,
                                    new Json.ExportFileEntry
                                    {
                                        Path = fileEntry.Path,
                                        Name = fileEntry.Name,
                                        OutputFiles = 0,
                                        Status = "unsupported",
                                    },
                                    null
                                );
                                progress.Increment(fileEntry.Path);
                                return;
                            }

                            if (
                                !string.IsNullOrEmpty(fileOutputDir)
                                && !Directory.Exists(fileOutputDir)
                            )
                            {
                                Directory.CreateDirectory(fileOutputDir);
                            }

                            string outputPath = Path.Combine(fileOutputDir, filename);
                            File.WriteAllText(outputPath, xml, Encoding.UTF8);

                            if (options.Rpf.Verbose && !options.Rpf.Json && !options.Progress)
                            {
                                lock (consoleLock)
                                {
                                    Console.Error.WriteLine(
                                        $"Exported: {fileEntry.Path} -> {filename}"
                                    );
                                }
                            }

                            results[i] = (
                                true,
                                new Json.ExportFileEntry
                                {
                                    Path = fileEntry.Path,
                                    Name = fileEntry.Name,
                                    OutputPath = outputPath,
                                    OutputFiles = 1,
                                    Status = "exported",
                                },
                                null
                            );
                            progress.Increment(fileEntry.Path);
                        }
                        catch (Exception ex)
                        {
                            if (!options.Rpf.Json)
                            {
                                lock (consoleLock)
                                {
                                    Console.Error.WriteLine(
                                        $"Error exporting {fileEntry.Path}: {ex.Message}"
                                    );
                                }
                            }
                            results[i] = (
                                false,
                                null,
                                $"Error exporting {fileEntry.Path}: {ex.Message}"
                            );
                            progress.Increment();
                        }
                    }
                );
            }

            int exported = 0;
            int unsupported = 0;
            int errors = 0;
            foreach (var (success, jsonEntry, errorMessage) in results)
            {
                if (success && jsonEntry?.Status == "exported")
                    exported++;

                if (jsonEntry?.Status == "unsupported")
                    unsupported++;

                if (jsonEntry != null)
                    files.Add(jsonEntry);

                if (!success && errorMessage != null)
                {
                    errors++;
                    errorMessages.Add(errorMessage);
                }
            }

            skipped += unsupported;

            result = result with
            {
                Exported = exported,
                Skipped = skipped,
                Errors = errors,
                Success = errors == 0,
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
                string action = options.DryRun ? "would be exported" : "exported";
                Console.Error.WriteLine(
                    $"XML export complete: {exported} files {action}, {skipped} skipped, {errors} errors"
                );
            }

            return errors > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            return RpfService.ReportError(
                ex.Message,
                options.Rpf.Json,
                result,
                options.Rpf.Verbose ? ex.StackTrace : null
            );
        }
    }
}

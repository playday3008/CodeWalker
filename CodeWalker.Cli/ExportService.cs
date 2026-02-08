using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

#if TESTING
using Xunit;
#endif

namespace CodeWalker.Cli;

/// <summary>
/// Delegate for processing a single file entry during export.
/// Returns an <see cref="Json.ExportFileEntry"/> on success (status = "exported", "unsupported", "skipped"),
/// or a tuple with a null entry and error string on failure.
/// </summary>
/// <param name="fileEntry">The RPF file entry to process.</param>
/// <param name="data">The raw file data extracted from the RPF.</param>
/// <param name="fileOutputDir">The output directory for this file (includes relative path).</param>
/// <param name="noOverwrite">When true, skip files that already exist at the output path.</param>
internal delegate (Json.ExportFileEntry? entry, string? error) ExportFileProcessor(
    RpfFileEntry fileEntry,
    byte[] data,
    string fileOutputDir,
    bool noOverwrite
);

internal static class ExportService
{
    internal readonly record struct ExportAggregation
    {
        public required int Exported { get; init; }
        public required int Skipped { get; init; }
        public required int Errors { get; init; }
        public required IReadOnlyList<Json.ExportFileEntry> Files { get; init; }
        public required IReadOnlyList<string> ErrorMessages { get; init; }
    }

    internal static (Json.ExportFileEntry? entry, string? error) ProcessSingleFile(
        RpfFileEntry fileEntry,
        byte[]? data,
        string outputDir,
        bool dryRun,
        bool noOverwrite,
        ExportFileProcessor processor
    )
    {
        string relativePath =
            Path.GetDirectoryName(fileEntry.Path)
                ?.Replace('\\', Path.DirectorySeparatorChar)
            ?? "";

        string fileOutputDir = Path.Combine(outputDir, relativePath);

        if (dryRun)
        {
            return (
                new Json.ExportFileEntry
                {
                    Path = fileEntry.Path,
                    Name = fileEntry.Name,
                    OutputFiles = 0,
                    Status = "dry_run",
                },
                null
            );
        }

        if (data == null)
        {
            return (null, $"Failed to extract: {fileEntry.Path}");
        }

        (Json.ExportFileEntry? entry, string? error) = processor(
            fileEntry,
            data,
            fileOutputDir,
            noOverwrite
        );

        if (error != null)
        {
            return (entry, error);
        }

        if (entry != null)
        {
            return (entry, null);
        }

        return (null, $"No result for: {fileEntry.Path}");
    }

    internal static ExportAggregation AggregateResults(
        (Json.ExportFileEntry? jsonEntry, string? errorMessage)[] results,
        IReadOnlyList<string> scanErrors,
        int filterSkipped
    )
    {
        int exported = 0;
        int skipped = 0;
        int errors = 0;
        List<Json.ExportFileEntry> files = [];
        List<string> errorMessages = [.. scanErrors];

        foreach ((Json.ExportFileEntry? jsonEntry, string? errorMessage) in results)
        {
            if (errorMessage == null && jsonEntry?.Status is "exported" or "dry_run")
                exported++;

            if (jsonEntry?.Status is "unsupported" or "skipped")
                skipped++;

            if (jsonEntry != null)
                files.Add(jsonEntry);

            if (errorMessage != null)
            {
                errors++;
                errorMessages.Add(errorMessage);
            }
        }

        skipped += filterSkipped;

        return new ExportAggregation
        {
            Exported = exported,
            Skipped = skipped,
            Errors = errors,
            Files = files,
            ErrorMessages = errorMessages,
        };
    }

    public static int Execute(
        ExportOptions options,
        string format,
        string summaryLabel,
        ExportFileProcessor processor
    )
    {
        Json.ExportResult ErrorResult(string[] errorMessages) =>
            new()
            {
                Success = false,
                RpfFile = options.Rpf.RpfPath,
                OutputDir = options.OutputPath,
                Format = format,
                TotalFiles = 0,
                Exported = 0,
                Skipped = 0,
                Errors = 0,
                DryRun = options.DryRun,
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
            int filterSkipped = totalNonRpfFiles - filesToExport.Count;

            (Json.ExportFileEntry? jsonEntry, string? errorMessage)[] results =
                new (Json.ExportFileEntry?, string?)[filesToExport.Count];

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
                    new ParallelOptions { MaxDegreeOfParallelism = options.Rpf.Threads },
                    i =>
                    {
                        (RpfFile sourceRpf, RpfFileEntry fileEntry) = filesToExport[i];
                        try
                        {
                            byte[]? data = options.DryRun
                                ? null
                                : sourceRpf.ExtractFile(fileEntry);

                            (Json.ExportFileEntry? entry, string? error) result = ProcessSingleFile(
                                fileEntry,
                                data,
                                outputDir,
                                options.DryRun,
                                options.NoOverwrite,
                                processor
                            );

                            results[i] = result;

                            if (
                                result.entry != null
                                && options.Rpf.Verbose
                                && !options.Rpf.Json
                                && !options.Progress
                            )
                            {
                                if (options.DryRun)
                                {
                                    lock (consoleLock)
                                    {
                                        Console.WriteLine(
                                            $"Would export: {fileEntry.Path}"
                                        );
                                    }
                                }
                                else if (result.entry.Status == "exported")
                                {
                                    lock (consoleLock)
                                    {
                                        Console.Error.WriteLine(
                                            $"Exported: {fileEntry.Path} -> {result.entry.OutputFiles} file(s)"
                                        );
                                    }
                                }
                            }

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
                                null,
                                $"Error exporting {fileEntry.Path}: {ex.Message}"
                            );
                            progress.Increment();
                        }
                    }
                );
            }

            ExportAggregation agg = AggregateResults(results, scanErrors, filterSkipped);

            Json.ExportResult jsonResult = new()
            {
                Success = agg.Errors == 0 && scanErrors.Count == 0,
                RpfFile = options.Rpf.RpfPath,
                OutputDir = options.OutputPath,
                Format = format,
                TotalFiles = totalNonRpfFiles,
                Exported = agg.Exported,
                Skipped = agg.Skipped,
                Errors = agg.Errors,
                DryRun = options.DryRun,
                Files = agg.Files,
                ErrorMessages = agg.ErrorMessages,
            };

            if (options.Rpf.Json)
            {
                Console.WriteLine(
                    JsonSerializer.Serialize(jsonResult, RpfService.JsonSerializerOptions)
                );
            }
            else
            {
                Console.Error.WriteLine();
                string action = options.DryRun ? "would be exported" : "exported";
                Console.Error.WriteLine(
                    $"{summaryLabel} export complete: {agg.Exported} files {action}, {agg.Skipped} skipped, {agg.Errors} errors"
                );
            }

            return (agg.Errors > 0 || scanErrors.Count > 0) ? 1 : 0;
        }
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
}

#if TESTING
[Collection("ConsoleOutput")]
public sealed class ExportServiceExecuteTests
{
    private static ExportOptions MakeOptions(bool json) =>
        new()
        {
            Rpf = new RpfOptions
            {
                RpfPath = "/nonexistent/test.rpf",
                ExePath = "/nonexistent",
                Gen9 = false,
                Filters = [],
                Verbose = false,
                Json = json,
                Recursive = false,
                Threads = 1,
                SizeFormat = Helpers.SizeFormat.IEC,
            },
            OutputPath = "/tmp/output",
            DryRun = false,
            NoOverwrite = false,
            Progress = false,
        };

    private static readonly ExportFileProcessor NoOpProcessor = (_, _, _, _) => (null, null);

    [Fact]
    public void Execute_ReturnsOne_WhenValidationFails_TextMode()
    {
        TextWriter origOut = Console.Out;
        TextWriter origErr = Console.Error;
        try
        {
            StringWriter stderr = new();
            Console.SetOut(new StringWriter());
            Console.SetError(stderr);
            int exitCode = ExportService.Execute(MakeOptions(json: false), "xml", "XML", NoOpProcessor);
            Assert.Equal(1, exitCode);
            Assert.Contains("Error:", stderr.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }

    [Fact]
    public void Execute_ReturnsOne_WhenValidationFails_JsonMode()
    {
        TextWriter origOut = Console.Out;
        TextWriter origErr = Console.Error;
        try
        {
            StringWriter stdout = new();
            Console.SetOut(stdout);
            Console.SetError(new StringWriter());
            int exitCode = ExportService.Execute(MakeOptions(json: true), "xml", "XML", NoOpProcessor);
            Assert.Equal(1, exitCode);
            string output = stdout.ToString();
            Assert.Contains("\"success\": false", output);
            Assert.Contains("RPF file not found", output);
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }

    [Fact]
    public void Execute_JsonError_ContainsExpectedFields()
    {
        TextWriter origOut = Console.Out;
        try
        {
            StringWriter stdout = new();
            Console.SetOut(stdout);
            int exitCode = ExportService.Execute(MakeOptions(json: true), "textures", "Textures", NoOpProcessor);
            Assert.Equal(1, exitCode);
            string output = stdout.ToString();
            Assert.Contains("\"format\": \"textures\"", output);
            Assert.Contains("\"rpfFile\":", output);
            Assert.Contains("\"outputDir\":", output);
        }
        finally { Console.SetOut(origOut); }
    }
}

public sealed class ProcessSingleFileTests
{
    private static RpfBinaryFileEntry MakeEntry(string path, string name) =>
        new() { Path = path, Name = name };

    [Fact]
    public void DryRun_ReturnsEntryWithNoError()
    {
        (Json.ExportFileEntry? entry, string? error) = ExportService.ProcessSingleFile(
            MakeEntry("folder/test.ydr", "test.ydr"),
            data: null,
            outputDir: "/out",
            dryRun: true,
            noOverwrite: false,
            processor: (_, _, _, _) => throw new InvalidOperationException("Should not be called")
        );

        Assert.NotNull(entry);
        Assert.Null(error);
        Assert.Equal("dry_run", entry.Status);
    }

    [Fact]
    public void DryRun_EntryHasCorrectFields()
    {
        (Json.ExportFileEntry? entry, string? _) = ExportService.ProcessSingleFile(
            MakeEntry("vehicles/adder.ydr", "adder.ydr"),
            data: [1, 2, 3],
            outputDir: "/out",
            dryRun: true,
            noOverwrite: false,
            processor: (_, _, _, _) => throw new InvalidOperationException("Should not be called")
        );

        Assert.NotNull(entry);
        Assert.Equal("vehicles/adder.ydr", entry.Path);
        Assert.Equal("adder.ydr", entry.Name);
        Assert.Equal(0, entry.OutputFiles);
        Assert.Equal("dry_run", entry.Status);
    }

    [Fact]
    public void NullData_ReturnsExtractionFailure()
    {
        (Json.ExportFileEntry? entry, string? error) = ExportService.ProcessSingleFile(
            MakeEntry("test.ydr", "test.ydr"),
            data: null,
            outputDir: "/out",
            dryRun: false,
            noOverwrite: false,
            processor: (_, _, _, _) => throw new InvalidOperationException("Should not be called")
        );

        Assert.Null(entry);
        Assert.NotNull(error);
        Assert.Contains("Failed to extract", error);
        Assert.Contains("test.ydr", error);
    }

    [Fact]
    public void ProcessorReturnsError_ReturnsFailure()
    {
        Json.ExportFileEntry errorEntry = new()
        {
            Path = "test.ydr",
            Name = "test.ydr",
            OutputFiles = 0,
            Status = "error",
        };

        (Json.ExportFileEntry? entry, string? error) = ExportService.ProcessSingleFile(
            MakeEntry("test.ydr", "test.ydr"),
            data: [1],
            outputDir: "/out",
            dryRun: false,
            noOverwrite: false,
            processor: (_, _, _, _) => (errorEntry, "conversion failed")
        );

        Assert.Same(errorEntry, entry);
        Assert.Equal("conversion failed", error);
    }

    [Fact]
    public void ProcessorReturnsSuccessEntry_ReturnsSuccess()
    {
        Json.ExportFileEntry successEntry = new()
        {
            Path = "test.ydr",
            Name = "test.ydr",
            OutputFiles = 3,
            Status = "exported",
        };

        (Json.ExportFileEntry? entry, string? error) = ExportService.ProcessSingleFile(
            MakeEntry("test.ydr", "test.ydr"),
            data: [1],
            outputDir: "/out",
            dryRun: false,
            noOverwrite: false,
            processor: (_, _, _, _) => (successEntry, null)
        );

        Assert.Same(successEntry, entry);
        Assert.Null(error);
    }

    [Fact]
    public void ProcessorReturnsNullEntry_ReturnsNoResult()
    {
        (Json.ExportFileEntry? entry, string? error) = ExportService.ProcessSingleFile(
            MakeEntry("test.ydr", "test.ydr"),
            data: [1],
            outputDir: "/out",
            dryRun: false,
            noOverwrite: false,
            processor: (_, _, _, _) => (null, null)
        );

        Assert.Null(entry);
        Assert.NotNull(error);
        Assert.Contains("No result for", error);
    }

    [Fact]
    public void ProcessorReturnsUnsupported_ReturnsSuccess()
    {
        Json.ExportFileEntry unsupportedEntry = new()
        {
            Path = "test.ybn",
            Name = "test.ybn",
            OutputFiles = 0,
            Status = "unsupported",
        };

        (Json.ExportFileEntry? entry, string? error) = ExportService.ProcessSingleFile(
            MakeEntry("test.ybn", "test.ybn"),
            data: [1],
            outputDir: "/out",
            dryRun: false,
            noOverwrite: false,
            processor: (_, _, _, _) => (unsupportedEntry, null)
        );

        Assert.Same(unsupportedEntry, entry);
        Assert.Null(error);
    }

    [Fact]
    public void OutputDirectory_ComputedFromBackslashPath()
    {
        string? capturedOutputDir = null;

        ExportService.ProcessSingleFile(
            MakeEntry("x64\\levels\\gta5\\vehicles.rpf\\adder.ydr", "adder.ydr"),
            data: [1],
            outputDir: "/out",
            dryRun: false,
            noOverwrite: false,
            processor: (_, _, dir, _) =>
            {
                capturedOutputDir = dir;
                return (
                    new Json.ExportFileEntry
                    {
                        Path = "adder.ydr",
                        Name = "adder.ydr",
                        OutputFiles = 1,
                        Status = "exported",
                    },
                    null
                );
            }
        );

        Assert.NotNull(capturedOutputDir);
        Assert.DoesNotContain("\\", capturedOutputDir);
        Assert.StartsWith("/out", capturedOutputDir);
    }
}

public sealed class AggregateResultsTests
{
    private static readonly string[] OneScanError = ["scan error 1"];
    private static readonly string[] OneScanWarning = ["scan warning"];

    private static Json.ExportFileEntry MakeFileEntry(string status) =>
        new()
        {
            Path = $"test_{status}.ydr",
            Name = $"test_{status}.ydr",
            OutputFiles = 1,
            Status = status,
        };

    [Fact]
    public void EmptyResults_AllZeros_OnlyScanErrors()
    {
        ExportService.ExportAggregation agg = ExportService.AggregateResults(
            Array.Empty<(Json.ExportFileEntry?, string?)>(),
            OneScanError,
            filterSkipped: 0
        );

        Assert.Equal(0, agg.Exported);
        Assert.Equal(0, agg.Skipped);
        Assert.Equal(0, agg.Errors);
        Assert.Empty(agg.Files);
        Assert.Single(agg.ErrorMessages);
        Assert.Equal("scan error 1", agg.ErrorMessages[0]);
    }

    [Fact]
    public void CountsExportedAndDryRun_AsExported()
    {
        (Json.ExportFileEntry?, string?)[] results =
        [
            (MakeFileEntry("exported"), null),
            (MakeFileEntry("dry_run"), null),
            (MakeFileEntry("exported"), null),
        ];

        ExportService.ExportAggregation agg = ExportService.AggregateResults(results, Array.Empty<string>(), filterSkipped: 0);

        Assert.Equal(3, agg.Exported);
    }

    [Fact]
    public void CountsUnsupportedAndSkipped_AsSkipped()
    {
        (Json.ExportFileEntry?, string?)[] results =
        [
            (MakeFileEntry("unsupported"), null),
            (MakeFileEntry("skipped"), null),
        ];

        ExportService.ExportAggregation agg = ExportService.AggregateResults(results, Array.Empty<string>(), filterSkipped: 0);

        Assert.Equal(2, agg.Skipped);
    }

    [Fact]
    public void AddsFilterSkipped_ToSkippedCount()
    {
        (Json.ExportFileEntry?, string?)[] results =
        [
            (MakeFileEntry("skipped"), null),
        ];

        ExportService.ExportAggregation agg = ExportService.AggregateResults(results, Array.Empty<string>(), filterSkipped: 5);

        Assert.Equal(6, agg.Skipped);
    }

    [Fact]
    public void CountsErrors_FromFailedResults()
    {
        (Json.ExportFileEntry?, string?)[] results =
        [
            (null, "error 1"),
            (null, "error 2"),
            (MakeFileEntry("exported"), null),
        ];

        ExportService.ExportAggregation agg = ExportService.AggregateResults(results, Array.Empty<string>(), filterSkipped: 0);

        Assert.Equal(2, agg.Errors);
    }

    [Fact]
    public void CollectsAllNonNullFileEntries()
    {
        Json.ExportFileEntry exported = MakeFileEntry("exported");
        Json.ExportFileEntry skipped = MakeFileEntry("skipped");

        (Json.ExportFileEntry?, string?)[] results =
        [
            (exported, null),
            (null, "error"),
            (skipped, null),
        ];

        ExportService.ExportAggregation agg = ExportService.AggregateResults(results, Array.Empty<string>(), filterSkipped: 0);

        Assert.Equal(2, agg.Files.Count);
        Assert.Same(exported, agg.Files[0]);
        Assert.Same(skipped, agg.Files[1]);
    }

    [Fact]
    public void IncludesScanErrorsAndNewErrors_InErrorMessages()
    {
        (Json.ExportFileEntry?, string?)[] results =
        [
            (null, "extraction failed"),
            (MakeFileEntry("exported"), null),
        ];

        ExportService.ExportAggregation agg = ExportService.AggregateResults(
            results,
            OneScanWarning,
            filterSkipped: 0
        );

        Assert.Equal(2, agg.ErrorMessages.Count);
        Assert.Equal("scan warning", agg.ErrorMessages[0]);
        Assert.Equal("extraction failed", agg.ErrorMessages[1]);
    }
}
#endif

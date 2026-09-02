using System;
using System.IO;

using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

using Xunit;

namespace CodeWalker.Cli.Tests;

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
                SizeFormat = SizeFormat.IEC,
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

        _ = ExportService.ProcessSingleFile(
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
        _ = Assert.Single(agg.ErrorMessages);
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

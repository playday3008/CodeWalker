using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

#if TESTING
using Xunit;
#endif

namespace CodeWalker.Cli;

internal abstract record BaseResult
{
    [JsonPropertyName("success")]
    [JsonPropertyOrder(-1)]
    public required bool Success { get; init; }

    [JsonPropertyName("errorMessages")]
    [JsonPropertyOrder(100)]
    public required IReadOnlyList<string> ErrorMessages { get; init; }
}

internal static class RpfService
{
    public static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Validates that the GTA V executable exists in the given directory.
    /// Returns null on success, or an error message on failure.
    /// </summary>
    public static string? ValidateExe(string exePath, bool gen9)
    {
        string exeFile = gen9 ? "GTA5_Enhanced.exe" : "GTA5.exe";
        if (!File.Exists(Path.Combine(exePath, exeFile)))
            return $"{exeFile} not found in: {exePath}";

        return null;
    }

    /// <summary>
    /// Validates that the RPF file and GTA V executable exist.
    /// Returns null on success, or an error message on failure.
    /// </summary>
    public static string? ValidateInputs(string rpfPath, string exePath, bool gen9)
    {
        if (!File.Exists(rpfPath))
            return $"RPF file not found: {rpfPath}";

        return ValidateExe(exePath, gen9);
    }

    /// <summary>
    /// Validates the GTA V exe, loads encryption keys, and prints status to stderr.
    /// For commands that have no --rpf (e.g. gen9, pack).
    /// Returns an error message on failure, or null on success.
    /// </summary>
    public static string? ValidateExeAndLoadKeys(string exePath, bool gen9, bool json)
    {
        string? error = ValidateExe(exePath, gen9);
        if (error != null)
            return error;

        if (!json)
            Console.Error.WriteLine("Loading encryption keys...");
        LoadKeys(exePath, gen9);

        return null;
    }

    /// <summary>
    /// Loads GTA V encryption keys from the installation directory.
    /// </summary>
    public static void LoadKeys(string exePath, bool gen9)
    {
        GTA5Keys.LoadFromPath(exePath, gen9);
    }

    /// <summary>
    /// Recursively collects file entries from an RPF archive, applying glob filters.
    /// </summary>
    public static List<(RpfFile rpf, RpfFileEntry entry)> CollectFiles(
        RpfFile rpf,
        string[]? filters,
        bool recursive
    )
    {
        List<(RpfFile, RpfFileEntry)> files = [];
        CollectFilesRecursive(rpf, filters, recursive, files);
        return files;
    }

    private static void CollectFilesRecursive(
        RpfFile rpf,
        string[]? filters,
        bool recursive,
        List<(RpfFile, RpfFileEntry)> files
    )
    {
        if (rpf.AllEntries != null)
        {
            foreach (RpfEntry entry in rpf.AllEntries)
            {
                if (entry is RpfFileEntry fileEntry)
                {
                    if (entry.NameLower.EndsWith(".rpf", StringComparison.Ordinal))
                        continue;

                    if (!Filter.Matches(entry.Path, filters))
                        continue;

                    files.Add((rpf, fileEntry));
                }
            }
        }

        if (recursive && rpf.Children != null)
        {
            foreach (RpfFile child in rpf.Children)
            {
                CollectFilesRecursive(child, filters, recursive, files);
            }
        }
    }

    /// <summary>
    /// Counts non-RPF files in the archive, optionally recursing into nested RPFs.
    /// </summary>
    public static int CountNonRpfFiles(RpfFile rpf, bool recursive)
    {
        int count = 0;
        CountNonRpfFilesRecursive(rpf, recursive, ref count);
        return count;
    }

    private static void CountNonRpfFilesRecursive(RpfFile rpf, bool recursive, ref int count)
    {
        if (rpf.AllEntries != null)
        {
            foreach (RpfEntry entry in rpf.AllEntries)
            {
                if (entry is RpfFileEntry && !entry.NameLower.EndsWith(".rpf", StringComparison.Ordinal))
                {
                    count++;
                }
            }
        }

        if (recursive && rpf.Children != null)
        {
            foreach (RpfFile child in rpf.Children)
            {
                CountNonRpfFilesRecursive(child, recursive, ref count);
            }
        }
    }

    /// <summary>
    /// Returns the file type string for a given RPF file entry.
    /// </summary>
    public static string GetFileType(RpfFileEntry fileEntry)
    {
        return fileEntry switch
        {
            RpfResourceFileEntry => "resource",
            RpfBinaryFileEntry => "binary",
            _ => "unknown",
        };
    }

    /// <summary>
    /// Validates inputs, loads encryption keys, and prints status to stderr.
    /// Returns an error message on failure, or null on success.
    /// </summary>
    public static string? ValidateAndLoadKeys(string rpfPath, string exePath, bool gen9, bool json)
    {
        string? error = ValidateInputs(rpfPath, exePath, gen9);
        if (error != null)
            return error;

        if (!json)
            Console.Error.WriteLine("Loading encryption keys...");
        LoadKeys(exePath, gen9);

        return null;
    }

    /// <summary>
    /// Opens an RPF file with standard verbose/json output handling.
    /// </summary>
    public static RpfFile OpenRpf(
        string rpfPath,
        bool verbose,
        bool json,
        List<string> errorMessages
    )
    {
        if (!json)
            Console.Error.WriteLine($"Opening RPF: {rpfPath}");

        string rpfName = Path.GetFileName(rpfPath);
        RpfFile rpf = new(rpfPath, rpfName);
        rpf.ScanStructure(
            status =>
            {
                if (verbose && !json)
                    Console.Error.WriteLine(status);
            },
            error =>
            {
                if (!json)
                    Console.Error.WriteLine($"Error: {error}");
                errorMessages.Add(error);
            }
        );

        if (!json)
            Console.Error.WriteLine(
                $"Found {rpf.GrandTotalFileCount} files in {rpf.GrandTotalRpfCount} archive(s)"
            );

        return rpf;
    }

    /// <summary>
    /// Reports an error in JSON or text format and returns exit code 1.
    /// The <c>with</c> expression preserves the runtime (derived) type, and
    /// <see cref="JsonSerializer"/> serialises using that type so all properties are included.
    /// </summary>
    public static int ReportError(
        string message,
        bool json,
        BaseResult result,
        string? stackTrace = null
    )
    {
        if (json)
        {
            BaseResult errorResult = result with
            {
                Success = false,
                ErrorMessages = [.. result.ErrorMessages, message],
            };
            Console.WriteLine(
                JsonSerializer.Serialize(errorResult, errorResult.GetType(), JsonSerializerOptions)
            );
        }
        else
        {
            Console.Error.WriteLine($"Error: {message}");
            if (stackTrace != null)
                Console.Error.WriteLine(stackTrace);
        }
        return 1;
    }
}

#if TESTING
[Collection("ConsoleOutput")]
public sealed class RpfServiceTests
{
    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cw_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // --- ValidateExe ---

    [Fact]
    public void ValidateExe_ReturnsNull_WhenExeExists()
    {
        string dir = CreateTempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "GTA5.exe"), []);
            Assert.Null(RpfService.ValidateExe(dir, gen9: false));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ValidateExe_ReturnsError_WhenExeMissing()
    {
        string dir = CreateTempDir();
        try
        {
            string? error = RpfService.ValidateExe(dir, gen9: false);
            Assert.NotNull(error);
            Assert.Contains("GTA5.exe", error);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ValidateExe_Gen9_ReturnsNull_WhenEnhancedExeExists()
    {
        string dir = CreateTempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "GTA5_Enhanced.exe"), []);
            Assert.Null(RpfService.ValidateExe(dir, gen9: true));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ValidateExe_Gen9_ReturnsError_WhenEnhancedExeMissing()
    {
        string dir = CreateTempDir();
        try
        {
            string? error = RpfService.ValidateExe(dir, gen9: true);
            Assert.NotNull(error);
            Assert.Contains("GTA5_Enhanced.exe", error);
        }
        finally { Directory.Delete(dir, true); }
    }

    // --- ValidateInputs ---

    [Fact]
    public void ValidateInputs_ReturnsNull_WhenBothExist()
    {
        string dir = CreateTempDir();
        try
        {
            string rpf = Path.Combine(dir, "test.rpf");
            File.WriteAllBytes(rpf, []);
            File.WriteAllBytes(Path.Combine(dir, "GTA5.exe"), []);
            Assert.Null(RpfService.ValidateInputs(rpf, dir, gen9: false));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ValidateInputs_ReturnsError_WhenRpfMissing()
    {
        string? error = RpfService.ValidateInputs("/nonexistent/test.rpf", "/tmp", gen9: false);
        Assert.NotNull(error);
        Assert.Contains("RPF file not found", error);
    }

    [Fact]
    public void ValidateInputs_ReturnsError_WhenExeMissing()
    {
        string dir = CreateTempDir();
        try
        {
            string rpf = Path.Combine(dir, "test.rpf");
            File.WriteAllBytes(rpf, []);
            string? error = RpfService.ValidateInputs(rpf, dir, gen9: false);
            Assert.NotNull(error);
            Assert.Contains("GTA5.exe", error);
        }
        finally { Directory.Delete(dir, true); }
    }

    // --- ValidateExeAndLoadKeys / ValidateAndLoadKeys early-return ---

    [Fact]
    public void ValidateExeAndLoadKeys_ReturnsError_WhenExeMissing()
    {
        string dir = CreateTempDir();
        try
        {
            string? error = RpfService.ValidateExeAndLoadKeys(dir, gen9: false, json: true);
            Assert.NotNull(error);
            Assert.Contains("GTA5.exe", error);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ValidateAndLoadKeys_ReturnsError_WhenRpfMissing()
    {
        string? error = RpfService.ValidateAndLoadKeys(
            "/nonexistent.rpf", "/tmp", gen9: false, json: true
        );
        Assert.NotNull(error);
        Assert.Contains("RPF file not found", error);
    }

    // --- GetFileType ---

    [Fact]
    public void GetFileType_Resource() =>
        Assert.Equal("resource", RpfService.GetFileType(new RpfResourceFileEntry()));

    [Fact]
    public void GetFileType_Binary() =>
        Assert.Equal("binary", RpfService.GetFileType(new RpfBinaryFileEntry()));

    // --- CollectFiles ---

    private static RpfBinaryFileEntry MakeEntry(string name, string? path = null) =>
        new() { Name = name, NameLower = name.ToLowerInvariant(), Path = path ?? name };

    [Fact]
    public void CollectFiles_NullEntries_ReturnsEmpty()
    {
        RpfFile rpf = new("test", "test.rpf", 0) { AllEntries = null };
        Assert.Empty(RpfService.CollectFiles(rpf, null, recursive: false));
    }

    [Fact]
    public void CollectFiles_ReturnsFileEntries()
    {
        RpfBinaryFileEntry entry = MakeEntry("test.ydr");
        RpfFile rpf = new("test", "test.rpf", 0) { AllEntries = [entry] };
        List<(RpfFile rpf, RpfFileEntry entry)> files =
            RpfService.CollectFiles(rpf, null, recursive: false);
        Assert.Single(files);
        Assert.Same(entry, files[0].entry);
    }

    [Fact]
    public void CollectFiles_SkipsRpfEntries()
    {
        RpfBinaryFileEntry rpfEntry = MakeEntry("nested.rpf");
        RpfBinaryFileEntry fileEntry = MakeEntry("test.ydr");
        RpfFile rpf = new("test", "test.rpf", 0) { AllEntries = [rpfEntry, fileEntry] };
        List<(RpfFile rpf, RpfFileEntry entry)> files =
            RpfService.CollectFiles(rpf, null, recursive: false);
        Assert.Single(files);
        Assert.Equal("test.ydr", files[0].entry.Name);
    }

    [Fact]
    public void CollectFiles_SkipsDirectoryEntries()
    {
        RpfDirectoryEntry dirEntry = new() { Name = "subdir", NameLower = "subdir", Path = "subdir" };
        RpfBinaryFileEntry fileEntry = MakeEntry("test.ydr");
        RpfFile rpf = new("test", "test.rpf", 0)
        {
            AllEntries = [dirEntry, fileEntry],
        };
        List<(RpfFile rpf, RpfFileEntry entry)> files =
            RpfService.CollectFiles(rpf, null, recursive: false);
        Assert.Single(files);
    }

    [Fact]
    public void CollectFiles_AppliesFilter()
    {
        RpfBinaryFileEntry e1 = MakeEntry("test.ydr");
        RpfBinaryFileEntry e2 = MakeEntry("test.ytd");
        RpfFile rpf = new("test", "test.rpf", 0) { AllEntries = [e1, e2] };
        List<(RpfFile rpf, RpfFileEntry entry)> files =
            RpfService.CollectFiles(rpf, ["*.ydr"], recursive: false);
        Assert.Single(files);
        Assert.Equal("test.ydr", files[0].entry.Name);
    }

    [Fact]
    public void CollectFiles_Recursive_IncludesChildren()
    {
        RpfBinaryFileEntry parentEntry = MakeEntry("a.ydr");
        RpfBinaryFileEntry childEntry = MakeEntry("b.ydr");
        RpfFile child = new("child", "child.rpf", 0) { AllEntries = [childEntry] };
        RpfFile parent = new("parent", "parent.rpf", 0)
        {
            AllEntries = [parentEntry],
            Children = [child],
        };
        Assert.Equal(2, RpfService.CollectFiles(parent, null, recursive: true).Count);
    }

    [Fact]
    public void CollectFiles_NonRecursive_ExcludesChildren()
    {
        RpfBinaryFileEntry parentEntry = MakeEntry("a.ydr");
        RpfBinaryFileEntry childEntry = MakeEntry("b.ydr");
        RpfFile child = new("child", "child.rpf", 0) { AllEntries = [childEntry] };
        RpfFile parent = new("parent", "parent.rpf", 0)
        {
            AllEntries = [parentEntry],
            Children = [child],
        };
        List<(RpfFile rpf, RpfFileEntry entry)> files =
            RpfService.CollectFiles(parent, null, recursive: false);
        Assert.Single(files);
        Assert.Equal("a.ydr", files[0].entry.Name);
    }

    [Fact]
    public void CollectFiles_Recursive_ReturnsCorrectRpfRef()
    {
        RpfBinaryFileEntry childEntry = MakeEntry("b.ydr");
        RpfFile child = new("child", "child.rpf", 0) { AllEntries = [childEntry] };
        RpfFile parent = new("parent", "parent.rpf", 0)
        {
            AllEntries = [],
            Children = [child],
        };
        List<(RpfFile rpf, RpfFileEntry entry)> files =
            RpfService.CollectFiles(parent, null, recursive: true);
        Assert.Single(files);
        Assert.Same(child, files[0].rpf);
    }

    // --- CountNonRpfFiles ---

    [Fact]
    public void CountNonRpfFiles_CountsCorrectly()
    {
        RpfFile rpf = new("test", "test.rpf", 0)
        {
            AllEntries = [MakeEntry("a.ydr"), MakeEntry("b.ytd")],
        };
        Assert.Equal(2, RpfService.CountNonRpfFiles(rpf, recursive: false));
    }

    [Fact]
    public void CountNonRpfFiles_SkipsRpfFiles()
    {
        RpfFile rpf = new("test", "test.rpf", 0)
        {
            AllEntries = [MakeEntry("nested.rpf"), MakeEntry("test.ydr")],
        };
        Assert.Equal(1, RpfService.CountNonRpfFiles(rpf, recursive: false));
    }

    [Fact]
    public void CountNonRpfFiles_Recursive()
    {
        RpfFile child = new("child", "child.rpf", 0) { AllEntries = [MakeEntry("b.ydr")] };
        RpfFile parent = new("parent", "parent.rpf", 0)
        {
            AllEntries = [MakeEntry("a.ydr")],
            Children = [child],
        };
        Assert.Equal(2, RpfService.CountNonRpfFiles(parent, recursive: true));
    }

    [Fact]
    public void CountNonRpfFiles_NonRecursive_ExcludesChildren()
    {
        RpfFile child = new("child", "child.rpf", 0) { AllEntries = [MakeEntry("b.ydr")] };
        RpfFile parent = new("parent", "parent.rpf", 0)
        {
            AllEntries = [MakeEntry("a.ydr")],
            Children = [child],
        };
        Assert.Equal(1, RpfService.CountNonRpfFiles(parent, recursive: false));
    }

    [Fact]
    public void CountNonRpfFiles_NullEntries_ReturnsZero()
    {
        RpfFile rpf = new("test", "test.rpf", 0) { AllEntries = null };
        Assert.Equal(0, RpfService.CountNonRpfFiles(rpf, recursive: false));
    }

    // --- ReportError ---

    private static Json.ExportResult MakeBaseResult(string[]? errors = null) =>
        new()
        {
            Success = true,
            RpfFile = "test.rpf",
            OutputDir = "/tmp",
            Format = "xml",
            TotalFiles = 0,
            Exported = 0,
            Skipped = 0,
            Errors = 0,
            DryRun = false,
            Files = [],
            ErrorMessages = errors ?? [],
        };

    [Fact]
    public void ReportError_ReturnsOne()
    {
        TextWriter origOut = Console.Out;
        TextWriter origErr = Console.Error;
        try
        {
            Console.SetOut(new StringWriter());
            Console.SetError(new StringWriter());
            Assert.Equal(1, RpfService.ReportError("err", json: false, MakeBaseResult()));
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }

    [Fact]
    public void ReportError_Json_WritesToStdout()
    {
        TextWriter origOut = Console.Out;
        TextWriter origErr = Console.Error;
        try
        {
            StringWriter sw = new();
            Console.SetOut(sw);
            Console.SetError(new StringWriter());
            RpfService.ReportError("test error", json: true, MakeBaseResult());
            string output = sw.ToString();
            Assert.Contains("\"success\": false", output);
            Assert.Contains("test error", output);
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }

    [Fact]
    public void ReportError_Json_PreservesExistingErrors()
    {
        TextWriter origOut = Console.Out;
        try
        {
            StringWriter sw = new();
            Console.SetOut(sw);
            RpfService.ReportError("new error", json: true, MakeBaseResult(["old error"]));
            string output = sw.ToString();
            Assert.Contains("old error", output);
            Assert.Contains("new error", output);
        }
        finally { Console.SetOut(origOut); }
    }

    [Fact]
    public void ReportError_Text_WritesToStderr()
    {
        TextWriter origOut = Console.Out;
        TextWriter origErr = Console.Error;
        try
        {
            StringWriter stdout = new();
            StringWriter stderr = new();
            Console.SetOut(stdout);
            Console.SetError(stderr);
            RpfService.ReportError("test error", json: false, MakeBaseResult());
            Assert.Contains("Error: test error", stderr.ToString());
            Assert.Equal("", stdout.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }

    [Fact]
    public void ReportError_Text_IncludesStackTrace()
    {
        TextWriter origErr = Console.Error;
        try
        {
            StringWriter stderr = new();
            Console.SetError(stderr);
            RpfService.ReportError("err", json: false, MakeBaseResult(), "at Foo.Bar()");
            Assert.Contains("at Foo.Bar()", stderr.ToString());
        }
        finally { Console.SetError(origErr); }
    }

    [Fact]
    public void ReportError_Text_OmitsStackTrace_WhenNull()
    {
        TextWriter origErr = Console.Error;
        try
        {
            StringWriter stderr = new();
            Console.SetError(stderr);
            RpfService.ReportError("err", json: false, MakeBaseResult());
            Assert.DoesNotContain("at ", stderr.ToString());
        }
        finally { Console.SetError(origErr); }
    }
}
#endif

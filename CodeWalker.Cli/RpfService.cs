using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

namespace CodeWalker.Cli;

public static class RpfService
{
    public static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Validates that the RPF file and GTA V executable exist.
    /// Returns null on success, or an error message on failure.
    /// </summary>
    public static string? ValidateInputs(string rpfPath, string exePath, bool gen9)
    {
        if (!File.Exists(rpfPath))
            return $"RPF file not found: {rpfPath}";

        string exeFile = gen9 ? "GTA5_Enhanced.exe" : "GTA5.exe";
        if (!File.Exists(Path.Combine(exePath, exeFile)))
            return $"{exeFile} not found in: {exePath}";

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
    /// Opens an RPF file and scans its structure.
    /// </summary>
    public static RpfFile OpenRpf(
        string rpfPath,
        Action<string>? onStatus = null,
        Action<string>? onError = null
    )
    {
        string rpfName = Path.GetFileName(rpfPath);
        RpfFile rpf = new(rpfPath, rpfName);

        rpf.ScanStructure(status => onStatus?.Invoke(status), error => onError?.Invoke(error));

        return rpf;
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
        foreach (RpfEntry entry in rpf.AllEntries)
        {
            if (entry is RpfFileEntry fileEntry)
            {
                if (entry.NameLower.EndsWith(".rpf"))
                    continue;

                if (!Filter.Matches(entry.Path, filters))
                    continue;

                files.Add((rpf, fileEntry));
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
        foreach (RpfEntry entry in rpf.AllEntries)
        {
            if (entry is RpfFileEntry && !entry.NameLower.EndsWith(".rpf"))
            {
                count++;
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
}

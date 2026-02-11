using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

namespace CodeWalker.Cli.Handlers;

internal sealed record TreeOptions
{
    public required RpfOptions Rpf { get; init; }
    public required int Depth { get; init; }
}

internal static class TreeHandler
{
    public static Command CreateCommand(CancellationToken cancellationToken = default)
    {
        RpfCommandOptions rpfOpts = new();
        Option<int> depthOption = new("--depth", "-d")
        {
            Description = "Maximum depth to display (default: unlimited)",
            DefaultValueFactory = _ => -1,
        };

        depthOption.Validators.Add(result =>
        {
            if (result.GetValue(depthOption) < -1)
                result.AddError("--depth must be -1 (unlimited) or a non-negative integer.");
        });

        Command command = new("tree", "Display a visual tree of the RPF directory structure")
        {
            depthOption,
        };
        rpfOpts.AddTo(command);
        command.Aliases.Add("t");

        command.SetAction(parseResult =>
        {
            TreeOptions options = new()
            {
                Rpf = rpfOpts.Parse(parseResult),
                Depth = parseResult.GetValue(depthOption),
            };
            return Execute(options, cancellationToken);
        });

        return command;
    }

    public static int Execute(TreeOptions options, CancellationToken cancellationToken = default)
    {
        Json.TreeResult ErrorResult(string[] errorMessages) =>
            new()
            {
                Success = false,
                RpfFile = options.Rpf.RpfPath,
                TotalFiles = 0,
                TotalDirs = 0,
                Root = null,
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

            int totalFiles = 0;
            int totalDirs = 0;

            if (options.Rpf.Json)
            {
                Json.TreeNode rootNode = BuildTreeNode(
                    rpf.Root,
                    rpf,
                    options,
                    0,
                    ref totalFiles,
                    ref totalDirs,
                    cancellationToken
                );

                Json.TreeResult result = new()
                {
                    Success = scanErrors.Count == 0,
                    RpfFile = options.Rpf.RpfPath,
                    TotalFiles = totalFiles,
                    TotalDirs = totalDirs,
                    Root = rootNode,
                    ErrorMessages = [.. scanErrors],
                };

                Console.WriteLine(
                    JsonSerializer.Serialize(result, RpfService.JsonSerializerOptions)
                );
            }
            else
            {
                Console.WriteLine(Path.GetFileName(options.Rpf.RpfPath));
                PrintTree(rpf.Root, rpf, options, "", 0, ref totalFiles, ref totalDirs, cancellationToken);

                Console.Error.WriteLine();
                Console.Error.WriteLine($"{totalDirs} directories, {totalFiles} files");
            }

            return scanErrors.Count > 0 ? 1 : 0;
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

    private static void PrintTree(
        RpfDirectoryEntry dir,
        RpfFile rpf,
        TreeOptions options,
        string prefix,
        int depth,
        ref int totalFiles,
        ref int totalDirs,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (options.Depth >= 0 && depth > options.Depth)
            return;

        List<(string name, bool isDir, RpfEntry entry, RpfFile? childRpf)> items = CollectChildren(
            dir,
            rpf,
            options
        );

        for (int i = 0; i < items.Count; i++)
        {
            bool isLast = i == items.Count - 1;
            string connector = isLast ? "\u2514\u2500\u2500 " : "\u251c\u2500\u2500 ";
            string childPrefix = prefix + (isLast ? "    " : "\u2502   ");

            (string name, bool isDirectory, RpfEntry entry, RpfFile? childRpf) = items[i];

            if (isDirectory)
            {
                totalDirs++;
                string display = name + "/";
                Console.WriteLine($"{prefix}{connector}{display}");

                if (entry is RpfDirectoryEntry subDir)
                {
                    PrintTree(
                        subDir,
                        childRpf ?? rpf,
                        options,
                        childPrefix,
                        depth + 1,
                        ref totalFiles,
                        ref totalDirs,
                        cancellationToken
                    );
                }
            }
            else
            {
                totalFiles++;
                if (options.Rpf.Verbose && entry is RpfFileEntry fileEntry)
                {
                    long size = fileEntry.GetFileSize();
                    string sizeStr = options.Rpf.SizeFormat.ToFormattedString(size);
                    string fileType = RpfService.GetFileType(fileEntry);
                    string versionStr = "";
                    if (fileEntry is RpfResourceFileEntry rfe)
                    {
                        versionStr = $" v{rfe.Version}";
                    }
                    Console.WriteLine(
                        $"{prefix}{connector}{name}  ({sizeStr}, {fileType}{versionStr})"
                    );
                }
                else
                {
                    Console.WriteLine($"{prefix}{connector}{name}");
                }
            }
        }
    }

    private static Json.TreeNode BuildTreeNode(
        RpfDirectoryEntry dir,
        RpfFile rpf,
        TreeOptions options,
        int depth,
        ref int totalFiles,
        ref int totalDirs,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<Json.TreeNode> children = [];

        if (options.Depth < 0 || depth < options.Depth)
        {
            List<(string name, bool isDir, RpfEntry entry, RpfFile? childRpf)> items =
                CollectChildren(dir, rpf, options);

            foreach ((string name, bool isDirectory, RpfEntry entry, RpfFile? childRpf) in items)
            {
                if (isDirectory)
                {
                    totalDirs++;
                    if (entry is RpfDirectoryEntry subDir)
                    {
                        children.Add(
                            BuildTreeNode(
                                subDir,
                                childRpf ?? rpf,
                                options,
                                depth + 1,
                                ref totalFiles,
                                ref totalDirs,
                                cancellationToken
                            )
                        );
                    }
                }
                else
                {
                    totalFiles++;
                    long? size = null;
                    string? sizeFormatted = null;
                    string? fileType = null;

                    if (entry is RpfFileEntry fileEntry)
                    {
                        size = fileEntry.GetFileSize();
                        sizeFormatted = options.Rpf.SizeFormat.ToFormattedString(size.Value);
                        fileType = RpfService.GetFileType(fileEntry);
                    }

                    children.Add(
                        new Json.TreeNode
                        {
                            Name = name,
                            Path = entry.Path,
                            Type = "file",
                            Size = size,
                            SizeFormatted = sizeFormatted,
                            FileType = fileType,
                        }
                    );
                }
            }
        }

        return new Json.TreeNode
        {
            Name = dir.Name ?? Path.GetFileName(rpf.FilePath),
            Path = dir.Path ?? rpf.Path,
            Type = "dir",
            Children = children,
        };
    }

    private static List<(
        string name,
        bool isDir,
        RpfEntry entry,
        RpfFile? childRpf
    )> CollectChildren(RpfDirectoryEntry dir, RpfFile rpf, TreeOptions options)
    {
        List<(string name, bool isDir, RpfEntry entry, RpfFile? childRpf)> items = [];

        // Add subdirectories
        if (dir.Directories != null)
        {
            foreach (RpfDirectoryEntry subDir in dir.Directories)
            {
                items.Add((subDir.Name, true, subDir, null));
            }
        }

        // Add nested RPFs as directories if recursive
        if (options.Rpf.Recursive && dir.Files != null && rpf.Children != null)
        {
            foreach (RpfFileEntry fileEntry in dir.Files)
            {
                if (!fileEntry.NameLower.EndsWith(".rpf", StringComparison.Ordinal))
                    continue;

                RpfFile? child = rpf.Children.FirstOrDefault(c => c.Name == fileEntry.Name && c.Root != null);
                if (child != null)
                {
                    items.Add((fileEntry.Name, true, child.Root, child));
                }
            }
        }

        // Add files (non-RPF, matching filters)
        if (dir.Files != null)
        {
            items.AddRange(
                dir.Files
                    .Where(fe => !fe.NameLower.EndsWith(".rpf", StringComparison.Ordinal)
                        && Filter.Matches(fe.Path, options.Rpf.Filters))
                    .Select(fe => (fe.Name, false, (RpfEntry)fe, (RpfFile?)null))
            );
        }

        return items;
    }
}

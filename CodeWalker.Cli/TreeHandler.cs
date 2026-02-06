using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

namespace CodeWalker.Cli;

public record TreeOptions
{
    public required RpfOptions Rpf { get; init; }
    public required int Depth { get; init; }
}

public static class TreeHandler
{
    public static Command CreateCommand()
    {
        RpfCommandOptions rpfOpts = new();
        // csharpier-ignore-start
        Option<int> depthOption = new("--depth", "-d")
        {
            Description = "Maximum depth to display (default: unlimited)",
            DefaultValueFactory = _ => -1,
        };
        // csharpier-ignore-end

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
            return Execute(options);
        });

        return command;
    }

    public static int Execute(TreeOptions options)
    {
        List<string> errorMessages = [];

        Json.TreeResult result = new()
        {
            Success = false,
            RpfFile = null!,
            TotalFiles = 0,
            TotalDirs = 0,
            ErrorMessages = errorMessages,
        };

        string? validationError = RpfService.ValidateInputs(
            options.Rpf.RpfPath,
            options.Rpf.ExePath,
            options.Rpf.Gen9
        );
        if (validationError != null)
        {
            return ReportError(validationError, options, result);
        }

        try
        {
            if (!options.Rpf.Json)
            {
                Console.Error.WriteLine("Loading encryption keys...");
            }
            RpfService.LoadKeys(options.Rpf.ExePath, options.Rpf.Gen9);

            if (!options.Rpf.Json)
            {
                Console.Error.WriteLine($"Opening RPF: {options.Rpf.RpfPath}");
            }

            RpfFile rpf = RpfService.OpenRpf(
                options.Rpf.RpfPath,
                onStatus: status =>
                {
                    if (options.Rpf.Verbose && !options.Rpf.Json)
                        Console.Error.WriteLine(status);
                },
                onError: error =>
                {
                    if (!options.Rpf.Json)
                        Console.Error.WriteLine($"Error: {error}");
                    errorMessages.Add(error);
                }
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
                    ref totalDirs
                );

                result = result with
                {
                    Success = true,
                    RpfFile = options.Rpf.RpfPath,
                    TotalFiles = totalFiles,
                    TotalDirs = totalDirs,
                    Root = rootNode,
                };

                Console.WriteLine(
                    JsonSerializer.Serialize(result, RpfService.JsonSerializerOptions)
                );
            }
            else
            {
                Console.WriteLine(Path.GetFileName(options.Rpf.RpfPath));
                PrintTree(rpf.Root, rpf, options, "", 0, ref totalFiles, ref totalDirs);

                Console.Error.WriteLine();
                Console.Error.WriteLine($"{totalDirs} directories, {totalFiles} files");
            }

            return 0;
        }
        catch (Exception ex)
        {
            return ReportError(
                ex.Message,
                options,
                result,
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
        ref int totalDirs
    )
    {
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
                        ref totalDirs
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
        ref int totalDirs
    )
    {
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
                                ref totalDirs
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
        if (options.Rpf.Recursive && dir.Files != null)
        {
            foreach (RpfFileEntry fileEntry in dir.Files)
            {
                if (fileEntry.NameLower.EndsWith(".rpf") && rpf.Children != null)
                {
                    foreach (RpfFile child in rpf.Children)
                    {
                        if (child.Name == fileEntry.Name && child.Root != null)
                        {
                            items.Add((fileEntry.Name, true, child.Root, child));
                            break;
                        }
                    }
                }
            }
        }

        // Add files (non-RPF, matching filters)
        if (dir.Files != null)
        {
            foreach (RpfFileEntry fileEntry in dir.Files)
            {
                if (fileEntry.NameLower.EndsWith(".rpf"))
                    continue;

                if (!Filter.Matches(fileEntry.Path, options.Rpf.Filters))
                    continue;

                items.Add((fileEntry.Name, false, fileEntry, null));
            }
        }

        return items;
    }

    private static int ReportError(
        string message,
        TreeOptions options,
        Json.TreeResult result,
        string? stackTrace = null
    )
    {
        if (options.Rpf.Json)
        {
            result = result with
            {
                Success = false,
                ErrorMessages = [.. result.ErrorMessages, message],
            };
            Console.WriteLine(JsonSerializer.Serialize(result, RpfService.JsonSerializerOptions));
        }
        else
        {
            Console.Error.WriteLine($"Error: {message}");
            if (stackTrace != null)
            {
                Console.Error.WriteLine(stackTrace);
            }
        }
        return 1;
    }
}

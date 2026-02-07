using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text.Json;

using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

namespace CodeWalker.Cli;

public sealed record PackOptions
{
    public required string InputPath { get; init; }
    public required string OutputPath { get; init; }
    public required CommonOptions Common { get; init; }
    public required bool Gen9 { get; init; }
    public required bool Force { get; init; }
    public required bool Progress { get; init; }
}

public static class PackHandler
{
    public static Command CreateCommand()
    {
        CommonCommandOptions commonOpts = new();
        Option<DirectoryInfo> inputOption = new("--input", "-i")
        {
            Description = "Source directory of loose files to pack",
            Required = true,
        };

        Option<FileInfo> outputOption = new("--output", "-o")
        {
            Description = "Output RPF file path",
            Required = true,
        };

        Option<bool> gen9Option = new("--gen9", "-g")
        {
            Description = "Use GTA V Enhanced (Gen9) mode",
        };

        Option<bool> forceOption = new("--force", "-F")
        {
            Description = "Overwrite existing output file",
        };

        Option<bool> progressOption = new("--progress", "-P")
        {
            Description = "Show progress bar",
        };

        Command command = new("pack", "Create an RPF archive from a directory of loose files")
        {
            inputOption,
            outputOption,
            gen9Option,
            forceOption,
            progressOption,
        };
        commonOpts.AddTo(command);
        command.Aliases.Add("p");

        command.SetAction(parseResult =>
        {
            PackOptions options = new()
            {
                InputPath = parseResult.GetRequiredValue(inputOption).FullName,
                OutputPath = parseResult.GetRequiredValue(outputOption).FullName,
                Common = commonOpts.Parse(parseResult),
                Gen9 = parseResult.GetValue(gen9Option),
                Force = parseResult.GetValue(forceOption),
                Progress = parseResult.GetValue(progressOption),
            };
            return Execute(options);
        });

        return command;
    }

    public static int Execute(PackOptions options)
    {
        Json.PackResult ErrorResult(string[] errorMessages) =>
            new()
            {
                Success = false,
                InputDir = options.InputPath,
                OutputFile = options.OutputPath,
                TotalFiles = 0,
                TotalDirs = 0,
                TotalSize = 0,
                TotalSizeFormatted = "0 B",
                Errors = 0,
                ErrorMessages = errorMessages,
            };

        if (!Directory.Exists(options.InputPath))
        {
            return RpfService.ReportError(
                $"Input directory not found: {options.InputPath}",
                options.Common.Json,
                ErrorResult([])
            );
        }

        if (File.Exists(options.OutputPath))
        {
            if (!options.Force)
            {
                return RpfService.ReportError(
                    $"Output file already exists: {options.OutputPath}. Use --force to overwrite.",
                    options.Common.Json,
                    ErrorResult([])
                );
            }
            File.Delete(options.OutputPath);
        }

        string? exeError = RpfService.ValidateExeAndLoadKeys(
            options.Common.ExePath,
            options.Gen9,
            options.Common.Json
        );
        if (exeError != null)
        {
            return RpfService.ReportError(exeError, options.Common.Json, ErrorResult([]));
        }

        try
        {
            // Count files for progress bar
            string[] allFiles = Directory.GetFiles(
                options.InputPath,
                "*",
                SearchOption.AllDirectories
            );

            if (!options.Common.Json)
            {
                Console.Error.WriteLine(
                    $"Packing {allFiles.Length} files from {options.InputPath}"
                );
            }

            // Create the output directory if needed
            string? outputDir = Path.GetDirectoryName(options.OutputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            string outputFolder = outputDir ?? Directory.GetCurrentDirectory();
            string outputFileName = Path.GetFileName(options.OutputPath);

            RpfFile rpf = RpfFile.CreateNew(outputFolder, outputFileName);

            if (!options.Common.Json)
            {
                Console.Error.WriteLine($"Created RPF: {options.OutputPath}");
            }

            int totalFiles = 0;
            int totalDirs = 0;
            long totalSize = 0;
            int errors = 0;
            List<string> errorMessages = [];

            using (
                ProgressBar progress = new(
                    allFiles.Length,
                    options.Progress && !options.Common.Json
                )
            )
            {
                AddDirectoryContents(
                    rpf.Root,
                    options.InputPath,
                    options,
                    progress,
                    errorMessages,
                    ref totalFiles,
                    ref totalDirs,
                    ref totalSize,
                    ref errors
                );
            }

            if (!options.Common.Json)
            {
                Console.Error.WriteLine("Defragmenting archive...");
            }
            RpfFile.Defragment(rpf);

            SizeFormat sizeFormat = options.Common.SizeFormat;

            Json.PackResult result = new()
            {
                Success = errors == 0,
                InputDir = options.InputPath,
                OutputFile = options.OutputPath,
                TotalFiles = totalFiles,
                TotalDirs = totalDirs,
                TotalSize = totalSize,
                TotalSizeFormatted = sizeFormat.ToFormattedString(totalSize),
                Errors = errors,
                ErrorMessages = [.. errorMessages],
            };

            if (options.Common.Json)
            {
                Console.WriteLine(
                    JsonSerializer.Serialize(result, RpfService.JsonSerializerOptions)
                );
            }
            else
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(
                    $"Pack complete: {totalFiles} files, {totalDirs} directories, {sizeFormat.ToFormattedString(totalSize)}, {errors} errors"
                );
            }

            return errors > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            return RpfService.ReportError(
                ex.Message,
                options.Common.Json,
                ErrorResult([]),
                options.Common.Verbose ? ex.StackTrace : null
            );
        }
    }

    private static void AddDirectoryContents(
        RpfDirectoryEntry parentDir,
        string fsDir,
        PackOptions options,
        ProgressBar progress,
        List<string> errorMessages,
        ref int totalFiles,
        ref int totalDirs,
        ref long totalSize,
        ref int errors
    )
    {
        // Add subdirectories first
        foreach (string subDirPath in Directory.GetDirectories(fsDir))
        {
            string dirName = Path.GetFileName(subDirPath);
            try
            {
                if (options.Common.Verbose && !options.Common.Json)
                {
                    Console.Error.WriteLine($"Creating directory: {dirName}");
                }

                RpfDirectoryEntry newDir = RpfFile.CreateDirectory(parentDir, dirName);
                totalDirs++;

                AddDirectoryContents(
                    newDir,
                    subDirPath,
                    options,
                    progress,
                    errorMessages,
                    ref totalFiles,
                    ref totalDirs,
                    ref totalSize,
                    ref errors
                );
            }
            catch (Exception ex)
            {
                errors++;
                string errorMsg = $"Error creating directory {dirName}: {ex.Message}";
                errorMessages.Add(errorMsg);
                if (!options.Common.Json)
                {
                    Console.Error.WriteLine($"Error: {errorMsg}");
                }
            }
        }

        // Add files
        foreach (string filePath in Directory.GetFiles(fsDir))
        {
            string fileName = Path.GetFileName(filePath);
            try
            {
                byte[] data = File.ReadAllBytes(filePath);

                if (options.Common.Verbose && !options.Common.Json)
                {
                    Console.Error.WriteLine($"Adding file: {fileName} ({data.Length} bytes)");
                }

                RpfFile.CreateFile(parentDir, fileName, data);
                totalFiles++;
                totalSize += data.Length;
                progress.Increment(fileName);
            }
            catch (Exception ex)
            {
                errors++;
                string errorMsg = $"Error adding file {fileName}: {ex.Message}";
                errorMessages.Add(errorMsg);
                if (!options.Common.Json)
                {
                    Console.Error.WriteLine($"Error: {errorMsg}");
                }
                progress.Increment();
            }
        }
    }
}

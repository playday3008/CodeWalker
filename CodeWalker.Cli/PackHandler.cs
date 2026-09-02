using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text.Json;
using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

namespace CodeWalker.Cli;

public record PackOptions
{
    public required string InputPath { get; init; }
    public required string OutputPath { get; init; }
    public required string ExePath { get; init; }
    public required bool Gen9 { get; init; }
    public required bool Force { get; init; }
    public required bool Progress { get; init; }
    public required bool Verbose { get; init; }
    public required bool Json { get; init; }
    public required SizeFormat SizeFormat { get; init; }
}

public static class PackHandler
{
    public static Command CreateCommand()
    {
        // csharpier-ignore-start
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

        Option<DirectoryInfo> exeOption = new("--exe", "-e")
        {
            Description = "Path to the GTA V installation directory (containing GTA5.exe)",
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

        Option<bool> verboseOption = new("--verbose", "-v")
        {
            Description = "Show per-file status",
        };

        Option<bool> jsonOption = new("--json")
        {
            Description = "Output results in JSON format",
        };

        Option<bool> siOption = new("--si")
        {
            Description = "Use SI units (1000-based: KB, MB) instead of IEC (1024-based: KiB, MiB)",
        };
        // csharpier-ignore-end

        Command command = new("pack", "Create an RPF archive from a directory of loose files")
        {
            inputOption,
            outputOption,
            exeOption,
            gen9Option,
            forceOption,
            progressOption,
            verboseOption,
            jsonOption,
            siOption,
        };
        command.Aliases.Add("p");

        command.SetAction(parseResult =>
        {
            PackOptions options = new()
            {
                InputPath = parseResult.GetRequiredValue(inputOption).FullName,
                OutputPath = parseResult.GetRequiredValue(outputOption).FullName,
                ExePath = parseResult.GetRequiredValue(exeOption).FullName,
                Gen9 = parseResult.GetValue(gen9Option),
                Force = parseResult.GetValue(forceOption),
                Progress = parseResult.GetValue(progressOption),
                Verbose = parseResult.GetValue(verboseOption),
                Json = parseResult.GetValue(jsonOption),
                SizeFormat = parseResult.GetValue(siOption) ? SizeFormat.SI : SizeFormat.IEC,
            };
            return Execute(options);
        });

        return command;
    }

    public static int Execute(PackOptions options)
    {
        List<string> errorMessages = [];

        Json.PackResult result = new()
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
                options.Json,
                result
            );
        }

        if (File.Exists(options.OutputPath))
        {
            if (!options.Force)
            {
                return RpfService.ReportError(
                    $"Output file already exists: {options.OutputPath}. Use --force to overwrite.",
                    options.Json,
                    result
                );
            }
            File.Delete(options.OutputPath);
        }

        string exeFile = options.Gen9 ? "GTA5_Enhanced.exe" : "GTA5.exe";
        if (!File.Exists(Path.Combine(options.ExePath, exeFile)))
        {
            return RpfService.ReportError(
                $"{exeFile} not found in: {options.ExePath}",
                options.Json,
                result
            );
        }

        try
        {
            if (!options.Json)
            {
                Console.Error.WriteLine("Loading encryption keys...");
            }
            RpfService.LoadKeys(options.ExePath, options.Gen9);

            // Count files for progress bar
            string[] allFiles = Directory.GetFiles(
                options.InputPath,
                "*",
                SearchOption.AllDirectories
            );

            if (!options.Json)
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

            if (!options.Json)
            {
                Console.Error.WriteLine($"Created RPF: {options.OutputPath}");
            }

            int totalFiles = 0;
            int totalDirs = 0;
            long totalSize = 0;
            int errors = 0;

            using (ProgressBar progress = new(allFiles.Length, options.Progress && !options.Json))
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

            if (!options.Json)
            {
                Console.Error.WriteLine("Defragmenting archive...");
            }
            RpfFile.Defragment(rpf);

            SizeFormat sizeFormat = options.SizeFormat;

            result = result with
            {
                Success = errors == 0,
                TotalFiles = totalFiles,
                TotalDirs = totalDirs,
                TotalSize = totalSize,
                TotalSizeFormatted = sizeFormat.ToFormattedString(totalSize),
                Errors = errors,
            };

            if (options.Json)
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
                options.Json,
                result,
                options.Verbose ? ex.StackTrace : null
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
                if (options.Verbose && !options.Json)
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
                if (!options.Json)
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

                if (options.Verbose && !options.Json)
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
                if (!options.Json)
                {
                    Console.Error.WriteLine($"Error: {errorMsg}");
                }
                progress.Increment();
            }
        }
    }
}

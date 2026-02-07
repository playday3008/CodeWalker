using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Text.Json;

using CodeWalker.GameFiles;

namespace CodeWalker.Cli;

public sealed record HashOptions
{
    public required string[] Inputs { get; init; }
    public required string Encoding { get; init; }
    public required bool Json { get; init; }

    public const string DefaultEncoding = "utf8";
    public const JenkHashInputEncoding DefaultJenkHashEncoding = JenkHashInputEncoding.UTF8;
}

public static class HashHandler
{
    public static Command CreateCommand()
    {
        Option<string[]> inputOption = new("--input", "-i")
        {
            Description = "Text string(s) to hash",
            Required = true,
            AllowMultipleArgumentsPerToken = true,
        };

        Option<string> encodingOption = new("--encoding", "-e")
        {
            Description = "Encoding: utf-8 (default), ascii",
            DefaultValueFactory = _ => HashOptions.DefaultEncoding,
        };

        Option<bool> jsonOption = new("--json")
        {
            Description = "Output results in JSON format",
        };

        Command command = new("hash", "Generate Jenkins hashes for GTA V game identifiers")
        {
            inputOption,
            encodingOption,
            jsonOption,
        };
        command.Aliases.Add("h");

        command.SetAction(parseResult =>
        {
            HashOptions options = new()
            {
                Inputs = parseResult.GetRequiredValue(inputOption),
                Encoding = parseResult.GetValue(encodingOption) ?? HashOptions.DefaultEncoding,
                Json = parseResult.GetValue(jsonOption),
            };
            return Execute(options);
        });

        return command;
    }

    public static int Execute(HashOptions options)
    {
        static Json.HashResult ErrorResult(string[] errorMessages) =>
            new()
            {
                Success = false,
                Hashes = [],
                ErrorMessages = errorMessages,
            };

        // Validate encoding
        JenkHashInputEncoding encoding;
        switch (options.Encoding.ToLowerInvariant())
        {
            case HashOptions.DefaultEncoding:
                encoding = HashOptions.DefaultJenkHashEncoding;
                break;
            case "utf-8":
                encoding = JenkHashInputEncoding.UTF8;
                break;
            case "ascii":
                encoding = JenkHashInputEncoding.ASCII;
                break;
            default:
                return RpfService.ReportError(
                    $"Unknown encoding: {options.Encoding}. Use 'utf-8' or 'ascii'.",
                    options.Json,
                    ErrorResult([])
                );
        }

        try
        {
            List<Json.HashEntry> hashes = [];

            foreach (string input in options.Inputs)
            {
                JenkHash jenkHash = new(input, encoding);

                Json.HashEntry entry = new()
                {
                    Input = input,
                    Hash = jenkHash.HashUint,
                    HashSigned = jenkHash.HashInt,
                    HashHex = jenkHash.HashHex,
                    Encoding = jenkHash.Encoding.ToString(),
                };

                hashes.Add(entry);

                if (!options.Json)
                {
                    Console.WriteLine($"Input: {input}");
                    Console.WriteLine($"  Hash (uint): {jenkHash.HashUint}");
                    Console.WriteLine($"  Hash (int):  {jenkHash.HashInt}");
                    Console.WriteLine($"  Hash (hex):  {jenkHash.HashHex}");
                }
            }

            if (options.Json)
            {
                Json.HashResult result = new()
                {
                    Success = true,
                    Hashes = [.. hashes],
                    ErrorMessages = [],
                };
                Console.WriteLine(
                    JsonSerializer.Serialize(result, RpfService.JsonSerializerOptions)
                );
            }

            return 0;
        }
        catch (Exception ex)
        {
            return RpfService.ReportError(ex.Message, options.Json, ErrorResult([]));
        }
    }
}

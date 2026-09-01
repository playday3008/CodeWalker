using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading;

using CodeWalker.Cli.Helpers;
using CodeWalker.GameFiles;

namespace CodeWalker.Cli.Handlers;

[ExcludeFromCodeCoverage]
internal sealed record HashOptions
{
    public required string[] Inputs { get; init; }
    public required string Encoding { get; init; }
    public required bool Json { get; init; }

    public const string DefaultEncoding = "utf-8";
}

internal static class HashHandler
{
    public static Command CreateCommand(CancellationToken cancellationToken = default)
    {
        Option<string[]> inputOption = new("--input", "-i")
        {
            Description = "Text string(s) to hash",
            Required = true,
            AllowMultipleArgumentsPerToken = true
        };

        Option<string> encodingOption = new("--encoding", "-e")
        {
            Description = "Encoding: utf-8 (default), ascii",
            DefaultValueFactory = _ => HashOptions.DefaultEncoding
        };

        Option<bool> jsonOption = new("--json")
        {
            Description = "Output results in JSON format"
        };

        Command command = new("hash", "Generate Jenkins hashes for GTA V game identifiers")
        {
            inputOption,
            encodingOption,
            jsonOption
        };
        command.Aliases.Add("h");

        command.SetAction(parseResult =>
        {
            HashOptions options = new()
            {
                Inputs = parseResult.GetRequiredValue(inputOption),
                Encoding = parseResult.GetRequiredValue(encodingOption),
                Json = parseResult.GetValue(jsonOption)
            };
            return Execute(options, cancellationToken);
        });

        return command;
    }

    /// <summary>
    /// Executes the hash generation based on the provided options.
    /// It handles both human-readable and JSON output formats, and gracefully manages cancellation and errors.
    /// </summary>
    /// <param name="options">The options containing the input strings, encoding, and output format preferences.</param>
    /// <param name="cancellationToken">A cancellation token to observe while performing the hashing operation.</param>
    /// <returns>An integer exit code indicating success (0) or failure (1).</returns>
    public static int Execute(HashOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            JenkHashInputEncoding encoding = ParseEncoding(options.Encoding);
            Json.HashEntry[] hashes = CollectHashes(options.Inputs, encoding, cancellationToken);
            if (options.Json)
                PrintJsonHashes(hashes);
            else
                PrintHashes(hashes, cancellationToken);

            return 0;
        }
        catch (OperationCanceledException)
        {
            // Gracefully handle cancellation without printing an error message
            throw;
        }
        catch (Exception ex)
        {
            return Output.ReportError(
                ex.Message,
                options.Json,
                ErrorResult([])
            );
        }
    }

    /// <summary>
    /// Creates a JSON result object representing an error, with the provided error messages.
    /// </summary>
    /// <param name="errorMessages">An array of error messages to include in the result.</param>
    /// <returns>A <see cref="Json.HashResult"/> object with success set to false and the provided error messages.</returns>
    internal static Json.HashResult ErrorResult(string[] errorMessages) =>
        new()
        {
            Success = false,
            Hashes = [],
            ErrorMessages = errorMessages
        };

    /// <summary>
    /// Parses the encoding string into a <see cref="JenkHashInputEncoding"/> enum value.
    /// </summary>
    /// <param name="encoding">The encoding string to parse (e.g., "utf-8", "ascii").</param>
    /// <returns>The corresponding <see cref="JenkHashInputEncoding"/> value.</returns>
    /// <exception cref="ArgumentException">Thrown if the encoding string is not recognized.</exception>
    internal static JenkHashInputEncoding ParseEncoding(string encoding) =>
        encoding.ToUpperInvariant() switch
        {
            "UTF-8" => JenkHashInputEncoding.UTF8,
            "ASCII" => JenkHashInputEncoding.ASCII,
            _ => throw new ArgumentException($"Unknown encoding: {encoding}. Use 'utf-8' or 'ascii'.")
        };

    /// <summary>
    /// Spells an encoding the way <c>--encoding</c> accepts it, so reported values can be fed
    /// straight back in.
    /// </summary>
    internal static string EncodingName(JenkHashInputEncoding encoding) => encoding switch
    {
        JenkHashInputEncoding.UTF8 => "utf-8",
        JenkHashInputEncoding.ASCII => "ascii",
        _ => encoding.ToString(),
    };

    /// <summary>
    /// Collects the hash results for each input string and returns them as an array of <see cref="Json.HashEntry"/> objects.
    /// </summary>
    /// <param name="inputs">An array of input strings to hash.</param>
    /// <param name="encoding">The encoding to use for hashing the input strings.</param>
    /// <param name="cancellationToken">A cancellation token to observe while performing the hashing operation.</param>
    /// <returns>An array of <see cref="Json.HashEntry"/> objects containing the hash results for each input string.</returns>
    internal static Json.HashEntry[] CollectHashes(
        string[] inputs,
        JenkHashInputEncoding encoding,
        CancellationToken cancellationToken
    )
    {
        List<Json.HashEntry> hashes = [];
        foreach (string input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            JenkHash jenkHash = new(input, encoding);
            hashes.Add(
                new Json.HashEntry
                {
                    Input = input,
                    Hash = jenkHash.HashUint,
                    HashSigned = jenkHash.HashInt,
                    HashHex = jenkHash.HashHex,
                    Encoding = EncodingName(jenkHash.Encoding)
                }
            );
        }

        return [.. hashes];
    }

    /// <summary>
    /// Prints the hash results to the console in JSON format, including the input, hash values, and encoding used.
    /// </summary>
    /// <param name="hashes">An array of pre-computed hash entries to serialize.</param>
    internal static void PrintJsonHashes(Json.HashEntry[] hashes)
    {
        Json.HashResult result = new()
        {
            Success = true,
            Hashes = hashes,
            ErrorMessages = []
        };
        Console.WriteLine(JsonSerializer.Serialize(result, Output.JsonSerializerOptions));
    }

    /// <summary>
    /// Prints the hash results to the console in a human-readable format.
    /// </summary>
    /// <param name="entries">An array of pre-computed hash entries to print.</param>
    /// <param name="cancellationToken">A cancellation token to observe while printing.</param>
    internal static void PrintHashes(
        Json.HashEntry[] entries,
        CancellationToken cancellationToken
    )
    {
        foreach (Json.HashEntry entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine($"Input ({entry.Encoding}): {entry.Input}");
            Console.WriteLine($"  Hash (uint): {entry.Hash}");
            Console.WriteLine($"  Hash (int):  {entry.HashSigned}");
            Console.WriteLine($"  Hash (hex):  {entry.HashHex}");
        }
    }
}

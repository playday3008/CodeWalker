using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeWalker.Cli.Json;

public record PackResult
{
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    [JsonPropertyName("inputDir")]
    public required string InputDir { get; init; }

    [JsonPropertyName("outputFile")]
    public required string OutputFile { get; init; }

    [JsonPropertyName("totalFiles")]
    public required int TotalFiles { get; init; }

    [JsonPropertyName("totalDirs")]
    public required int TotalDirs { get; init; }

    [JsonPropertyName("totalSize")]
    public required long TotalSize { get; init; }

    [JsonPropertyName("totalSizeFormatted")]
    public required string TotalSizeFormatted { get; init; }

    [JsonPropertyName("errors")]
    public required int Errors { get; init; }

    [JsonPropertyName("errorMessages")]
    public required IReadOnlyList<string> ErrorMessages { get; init; }
}

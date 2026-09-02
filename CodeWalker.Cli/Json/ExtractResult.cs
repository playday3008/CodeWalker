using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeWalker.Cli.Json;

public record ExtractResult
{
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    [JsonPropertyName("rpfFile")]
    public required string RpfFile { get; init; }

    [JsonPropertyName("outputDir")]
    public required string OutputDir { get; init; }

    [JsonPropertyName("totalFiles")]
    public required uint TotalFiles { get; init; }

    [JsonPropertyName("extracted")]
    public required int Extracted { get; init; }

    [JsonPropertyName("skipped")]
    public required int Skipped { get; init; }

    [JsonPropertyName("errors")]
    public required int Errors { get; init; }

    [JsonPropertyName("dryRun")]
    public required bool DryRun { get; init; }

    [JsonPropertyName("files")]
    public required IReadOnlyList<FileEntry> Files { get; init; }

    [JsonPropertyName("errorMessages")]
    public required IReadOnlyList<string> ErrorMessages { get; init; }
}

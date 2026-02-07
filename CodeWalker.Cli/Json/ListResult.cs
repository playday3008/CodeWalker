using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeWalker.Cli.Json;

internal sealed record ListResult : BaseResult
{
    [JsonPropertyName("rpfFile")]
    public required string RpfFile { get; init; }

    [JsonPropertyName("totalFiles")]
    public required int TotalFiles { get; init; }

    [JsonPropertyName("totalSize")]
    public required long TotalSize { get; init; }

    [JsonPropertyName("totalSizeFormatted")]
    public required string TotalSizeFormatted { get; init; }

    [JsonPropertyName("nestedRpfCount")]
    public required int NestedRpfCount { get; init; }

    [JsonPropertyName("files")]
    public required IReadOnlyList<FileEntry> Files { get; init; }
}

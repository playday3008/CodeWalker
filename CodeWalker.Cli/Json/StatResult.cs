using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace CodeWalker.Cli.Json;

[ExcludeFromCodeCoverage]
internal sealed record ExtensionStat
{
    [JsonPropertyName("extension")]
    public required string Extension { get; init; }

    [JsonPropertyName("count")]
    public required int Count { get; init; }

    [JsonPropertyName("totalSize")]
    public required long TotalSize { get; init; }

    [JsonPropertyName("totalSizeFormatted")]
    public required string TotalSizeFormatted { get; init; }

    [JsonPropertyName("avgSize")]
    public required long AvgSize { get; init; }

    [JsonPropertyName("minSize")]
    public required long MinSize { get; init; }

    [JsonPropertyName("maxSize")]
    public required long MaxSize { get; init; }
}

[ExcludeFromCodeCoverage]
internal sealed record StatResult : BaseResult
{
    [JsonPropertyName("rpfFile")]
    public required string RpfFile { get; init; }

    [JsonPropertyName("totalFiles")]
    public required int TotalFiles { get; init; }

    [JsonPropertyName("totalSize")]
    public required long TotalSize { get; init; }

    [JsonPropertyName("totalSizeFormatted")]
    public required string TotalSizeFormatted { get; init; }

    [JsonPropertyName("resourceCount")]
    public required int ResourceCount { get; init; }

    [JsonPropertyName("binaryCount")]
    public required int BinaryCount { get; init; }

    [JsonPropertyName("compressedSize")]
    public required long CompressedSize { get; init; }

    [JsonPropertyName("uncompressedSize")]
    public required long UncompressedSize { get; init; }

    [JsonPropertyName("compressionRatio")]
    public required double CompressionRatio { get; init; }

    [JsonPropertyName("extensions")]
    public required IReadOnlyList<ExtensionStat> Extensions { get; init; }
}

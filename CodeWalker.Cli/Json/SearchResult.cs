using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeWalker.Cli.Json;

public sealed record SearchMatch
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("size")]
    public required long Size { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("extension")]
    public required string Extension { get; init; }

    [JsonPropertyName("nameHash")]
    public required uint NameHash { get; init; }

    [JsonPropertyName("shortNameHash")]
    public required uint ShortNameHash { get; init; }
}

public sealed record SearchResult : BaseResult
{
    [JsonPropertyName("rpfFile")]
    public required string RpfFile { get; init; }

    [JsonPropertyName("pattern")]
    public required string Pattern { get; init; }

    [JsonPropertyName("patternType")]
    public required string PatternType { get; init; }

    [JsonPropertyName("matchCount")]
    public required int MatchCount { get; init; }

    [JsonPropertyName("matches")]
    public required IReadOnlyList<SearchMatch> Matches { get; init; }
}

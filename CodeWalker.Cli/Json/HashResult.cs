using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodeWalker.Cli.Json;

public record HashResult
{
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    [JsonPropertyName("hashes")]
    public required IReadOnlyList<HashEntry> Hashes { get; init; }

    [JsonPropertyName("errorMessages")]
    public required IReadOnlyList<string> ErrorMessages { get; init; }
}

public record HashEntry
{
    [JsonPropertyName("input")]
    public required string Input { get; init; }

    [JsonPropertyName("hash")]
    public required uint Hash { get; init; }

    [JsonPropertyName("hashSigned")]
    public required int HashSigned { get; init; }

    [JsonPropertyName("hashHex")]
    public required string HashHex { get; init; }

    [JsonPropertyName("encoding")]
    public required string Encoding { get; init; }
}

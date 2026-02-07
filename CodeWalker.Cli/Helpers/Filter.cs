using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;

#if !NETCOREAPP
using CodeWalker.Cli.Polyfills;
#endif

namespace CodeWalker.Cli.Helpers;

/// <summary>
/// Provides methods for filtering file paths based on glob patterns.
/// </summary>
internal static class Filter
{
    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();

    /// <summary>
    /// Normalizes filter patterns once at parse time: trims, lowercases, and strips blanks.
    /// </summary>
    public static string[] Normalize(string[]? filters)
    {
        if (filters == null || filters.Length == 0)
            return [];

        List<string> result = [];
        foreach (string filter in filters)
        {
            if (string.IsNullOrWhiteSpace(filter))
                continue;
            result.Add(filter.Trim().ToLowerInvariant());
        }
        return [.. result];
    }

    /// <summary>
    /// Determines if the given path matches any of the provided glob patterns.
    /// Filters should be pre-normalized via <see cref="Normalize"/>.
    /// </summary>
    /// <param name="path">Path to check.</param>
    /// <param name="filters">Glob patterns to match against (pre-normalized).</param>
    /// <returns>True if the path matches any pattern; otherwise, false.</returns>
    public static bool Matches(string path, string[]? filters)
    {
        if (filters == null || filters.Length == 0)
            return true;

        string nameLower = path.ToLowerInvariant();

        foreach (string filter in filters)
        {
            if (MatchesGlob(nameLower, filter))
                return true;
        }

        return false;
    }

    private static bool MatchesGlob(string input, string pattern)
    {
        // Normalize path separators
        input = input.Replace('\\', '/');
        pattern = pattern.Replace('\\', '/');

        bool hasPathSep = pattern.Contains('/', StringComparison.Ordinal);

        // For patterns without path separators, match against filename only
        if (!hasPathSep)
        {
            int lastSlash = input.LastIndexOf('/');
            if (lastSlash >= 0)
                input = input[(lastSlash + 1)..];
        }

        // Handle extension-only patterns (e.g., ".ydr" or "ydr" without wildcards)
        if (!pattern.Contains('*', StringComparison.Ordinal) && !pattern.Contains('?', StringComparison.Ordinal))
        {
            if (pattern.StartsWith('.'))
                return input.EndsWith(pattern, StringComparison.Ordinal);
            else
                return input.EndsWith($".{pattern}", StringComparison.Ordinal);
        }

        Regex regex = RegexCache.GetOrAdd(
            pattern,
            static p =>
            {
                // Convert glob pattern to regex
                // Escape all regex special chars except * and ?
                string regexPattern = Regex.Escape(p);

                // Handle ** (globstar) before * — order matters
                // **/ matches zero or more directory segments
                regexPattern = regexPattern.Replace("\\*\\*/", "(.*/)?", StringComparison.Ordinal);
                // standalone ** matches any characters including /
                regexPattern = regexPattern.Replace("\\*\\*", ".*", StringComparison.Ordinal);
                // * matches any characters except / (single path segment)
                regexPattern = regexPattern.Replace("\\*", "[^/]*", StringComparison.Ordinal);
                // ? matches any single character except /
                regexPattern = regexPattern.Replace("\\?", "[^/]", StringComparison.Ordinal);

                // Patterns with path separators match at any path boundary;
                // filename-only patterns are anchored to the full filename.
                if (p.Contains('/', StringComparison.Ordinal))
                    regexPattern = $"(?:^|/){regexPattern}$";
                else
                    regexPattern = $"^{regexPattern}$";

                return new Regex(regexPattern, RegexOptions.Compiled);
            }
        );

        return regex.IsMatch(input);
    }
}

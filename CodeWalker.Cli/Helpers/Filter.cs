using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace CodeWalker.Cli.Helpers;

/// <summary>
/// Provides methods for filtering file paths based on glob patterns.
/// </summary>
public static class Filter
{
    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();

    /// <summary>
    /// Determines if the given path matches any of the provided glob patterns.
    /// </summary>
    /// <param name="path">Path to check.</param>
    /// <param name="filters">Glob patterns to match against.</param>
    /// <returns>True if the path matches any pattern; otherwise, false.</returns>
    public static bool Matches(string path, string[]? filters)
    {
        if (filters == null || filters.Length == 0)
            return true;

        string nameLower = path.ToLowerInvariant();

        foreach (string filter in filters)
        {
            if (string.IsNullOrWhiteSpace(filter))
                continue;

            string p = filter.Trim().ToLowerInvariant();

            if (MatchesGlob(nameLower, p))
                return true;
        }

        return false;
    }

    private static bool MatchesGlob(string input, string pattern)
    {
        // Normalize path separators
        input = input.Replace('\\', '/');
        pattern = pattern.Replace('\\', '/');

        bool hasPathSep = pattern.Contains("/");

        // For patterns without path separators, match against filename only
        if (!hasPathSep)
        {
            int lastSlash = input.LastIndexOf("/");
            if (lastSlash >= 0)
                input = input[(lastSlash + 1)..];
        }

        // Handle extension-only patterns (e.g., ".ydr" or "ydr" without wildcards)
        if (!pattern.Contains("*") && !pattern.Contains("?"))
        {
            if (pattern.StartsWith("."))
                return input.EndsWith(pattern);
            else
                return input.EndsWith("." + pattern);
        }

        Regex regex = RegexCache.GetOrAdd(
            pattern,
            static p =>
            {
                // Convert glob pattern to regex
                // Escape all regex special chars except * and ?
                string regexPattern = Regex
                    .Escape(p)
                    .Replace("\\*", ".*") // * matches any sequence of characters
                    .Replace("\\?", "."); // ? matches any single character

                // Patterns with path separators match at any path boundary;
                // filename-only patterns are anchored to the full filename.
                if (p.Contains("/"))
                    regexPattern = "(?:^|/)" + regexPattern + "$";
                else
                    regexPattern = "^" + regexPattern + "$";

                return new Regex(regexPattern, RegexOptions.Compiled);
            }
        );

        return regex.IsMatch(input);
    }
}

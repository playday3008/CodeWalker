#if !NETSTANDARD2_1_OR_GREATER
using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
#endif

#if TESTING
using Xunit;
#endif

namespace CodeWalker.Cli;

#if !NETSTANDARD2_1_OR_GREATER

internal static class StringExtensions
{
    public static bool Contains(this string s, string value, StringComparison comparisonType)
    {
#pragma warning disable CA2249 // Consider using 'string.Contains' instead of 'string.IndexOf'... this is the implementation of Contains!
        return s.IndexOf(value, comparisonType) >= 0;
#pragma warning restore CA2249
    }

    public static bool Contains(this string s, char value)
    {
#pragma warning disable CA2249 // Consider using 'string.Contains' instead of 'string.IndexOf'... this is the implementation of Contains!
        return s.IndexOf(value, StringComparison.Ordinal) >= 0;
#pragma warning restore CA2249
    }

    public static bool Contains(this string s, char value, StringComparison comparisonType)
    {
#pragma warning disable CA2249 // Consider using 'string.Contains' instead of 'string.IndexOf'... this is the implementation of Contains!
        return s.IndexOf(value, comparisonType) >= 0;
#pragma warning restore CA2249
    }

    public static bool StartsWith(this string s, char value)
    {
        return s.Length > 0 && s[0] == value;
    }

    public static bool StartsWith(this string s, char value, StringComparison comparisonType)
    {
        return s.StartsWith(value.ToString(), comparisonType);
    }

    public static bool EndsWith(this string s, char value)
    {
        return s.Length > 0 && s[^1] == value;
    }

    public static bool EndsWith(this string s, char value, StringComparison comparisonType)
    {
        return s.EndsWith(value.ToString(), comparisonType);
    }

    private static string? ReplaceCore(
        ReadOnlySpan<char> searchSpace,
        ReadOnlySpan<char> oldValue,
        ReadOnlySpan<char> newValue,
        CompareInfo compareInfo,
        CompareOptions options)
    {
        Debug.Assert(!oldValue.IsEmpty);
        Debug.Assert(compareInfo != null);

        StringBuilder result = new();

        bool hasDoneAnyReplacements = false;

        while (true)
        {
            int index = compareInfo.IndexOf(searchSpace, oldValue, options, out int matchLength);

            // There's the possibility that 'oldValue' has zero collation weight (empty string equivalent).
            // If this is the case, we behave as if there are no more substitutions to be made.

            if (index < 0 || matchLength == 0)
            {
                break;
            }

            // append the unmodified portion of search space
            result.Append(searchSpace[..index]);

            // append the replacement
            result.Append(newValue);

            searchSpace = searchSpace[(index + matchLength)..];
            hasDoneAnyReplacements = true;
        }

        // Didn't find 'oldValue' in the remaining search space, or the match
        // consisted only of zero collation weight characters. As an optimization,
        // if we have not yet performed any replacements, we'll save the
        // allocation.

        if (!hasDoneAnyReplacements)
        {
            return null;
        }

        // Append what remains of the search space, then allocate the new string.

        result.Append(searchSpace);
        return result.ToString();
    }

    public static string Replace(this string s, string oldValue, string? newValue, StringComparison comparisonType)
    {
        if (comparisonType == StringComparison.Ordinal)
        {
#pragma warning disable CA1307 // Specify StringComparison for clarity... this is the implementation of Replace!
            return s.Replace(oldValue, newValue);
#pragma warning restore CA1307
        }

        (CompareInfo ci, CompareOptions options) = comparisonType switch
        {
            StringComparison.CurrentCulture or StringComparison.CurrentCultureIgnoreCase => (
                    CultureInfo.CurrentCulture.CompareInfo,
                    (CompareOptions)((int)comparisonType & (int)CompareOptions.IgnoreCase)
                ),
            StringComparison.InvariantCulture or StringComparison.InvariantCultureIgnoreCase => (
                    CultureInfo.InvariantCulture.CompareInfo,
                    (CompareOptions)((int)comparisonType & (int)CompareOptions.IgnoreCase)
                ),
            StringComparison.OrdinalIgnoreCase => (
                    CultureInfo.InvariantCulture.CompareInfo,
                    CompareOptions.OrdinalIgnoreCase
                ),
            StringComparison.Ordinal => throw new InvalidOperationException("This code path should never be hit, as StringComparison.Ordinal is handled above."),
            _ => throw new ArgumentException("The string comparison type passed in is currently not supported.", nameof(comparisonType)),
        };
        return ReplaceCore(s, oldValue, newValue, ci, options) ?? s;
    }
}

#endif

#if TESTING

/// <summary>
/// Fuzz-tests every polyfill in <see cref="StringExtensions"/> against the built-in
/// .NET implementation. Each method is called with a large combinatorial corpus of
/// hand-picked edge cases plus deterministic random strings, and the result is compared
/// against the equivalent built-in method or string-based overload.
/// </summary>
public sealed class StringExtensionsFuzzTests
{
    private static readonly StringComparison[] AllComparisons =
    [
        StringComparison.CurrentCulture,
        StringComparison.CurrentCultureIgnoreCase,
        StringComparison.InvariantCulture,
        StringComparison.InvariantCultureIgnoreCase,
        StringComparison.Ordinal,
        StringComparison.OrdinalIgnoreCase,
    ];

    private static readonly string[] Corpus = BuildCorpus();

    private static string[] BuildCorpus()
    {
        string[] handPicked =
        [
            "", " ", "a", "A", "ab", "AB", "abc", "ABC",
            "hello", "HELLO", "Hello", "Hello World", "hello world",
            "  spaces  ", "tab\there", "new\nline",
            "straße", "STRASSE", "Straße", "café", "CAFÉ",
            "résumé", "naïve", "日本語",
            "abc123!@#", "path/to/file.txt", @"C:\Windows\System32",
            "\0null\0", "🎮🎲🎯", new string('x', 200),
            "aaa", "aaA", "AaA",
        ];

        // 100 deterministic random strings for breadth
        Random rng = new(42);
        const string alphabet = "aAbBcC xXyYzZ\t\n\0éß";
        string[] random = new string[100];
        for (int i = 0; i < random.Length; i++)
        {
            char[] buf = new char[rng.Next(0, 30)];
            for (int j = 0; j < buf.Length; j++)
                buf[j] = alphabet[rng.Next(alphabet.Length)];
            random[i] = new string(buf);
        }

        string[] result = new string[handPicked.Length + random.Length];
        handPicked.CopyTo(result, 0);
        random.CopyTo(result, handPicked.Length);
        return result;
    }

    private static readonly char[] Chars =
    [
        'a', 'A', 'z', 'Z', ' ', '\t', '\n', '\0',
        '/', '\\', '.', '!', 'é', 'ß', 'ñ', '日', 'x', 'X',
    ];

    private static readonly string[] SearchStrings =
    [
        "a", "A", "hello", "HELLO", "llo", "World", "world",
        "straße", "STRASSE", "ß", "SS", "café", "xyz", " ",
        "/", "\\", "\0", "🎮", "xx",
    ];

    // ─── Contains(string, StringComparison) ─────────────────────────
    // Polyfill wraps IndexOf; built-in is the native implementation.

    [Fact]
    public void Contains_String_Comparison_MatchesBuiltIn()
    {
        foreach (string s in Corpus)
            foreach (string sub in SearchStrings)
                foreach (StringComparison cmp in AllComparisons)
                    AssertBool(
                        s.Contains(sub, cmp),
                        StringExtensions.Contains(s, sub, cmp),
                        $"Contains(\"{Esc(s)}\", \"{Esc(sub)}\", {cmp})");
    }

    // ─── Contains(char) ─────────────────────────────────────────────
    // Built-in string.Contains(char) exists on .NET 5+.

    [Fact]
    public void Contains_Char_MatchesBuiltIn()
    {
        foreach (string s in Corpus)
            foreach (char c in Chars)
                AssertBool(
                    s.Contains(c),
                    StringExtensions.Contains(s, c),
                    $"Contains(\"{Esc(s)}\", '{c}')");
    }

    // ─── Contains(char, StringComparison) ───────────────────────────
    // No built-in char overload; verify against string-based Contains.

    [Fact]
    public void Contains_Char_Comparison_MatchesStringOverload()
    {
        foreach (string s in Corpus)
            foreach (char c in Chars)
                foreach (StringComparison cmp in AllComparisons)
                    AssertBool(
                        s.Contains(c.ToString(), cmp),
                        StringExtensions.Contains(s, c, cmp),
                        $"Contains(\"{Esc(s)}\", '{c}', {cmp})");
    }

    // ─── StartsWith(char) ───────────────────────────────────────────
    // Verify against string-based StartsWith with Ordinal comparison.

    [Fact]
    public void StartsWith_Char_MatchesStringOverload()
    {
        foreach (string s in Corpus)
            foreach (char c in Chars)
                AssertBool(
                    s.StartsWith(c.ToString(), StringComparison.Ordinal),
                    StringExtensions.StartsWith(s, c),
                    $"StartsWith(\"{Esc(s)}\", '{c}')");
    }

    // ─── StartsWith(char, StringComparison) ─────────────────────────

    [Fact]
    public void StartsWith_Char_Comparison_MatchesStringOverload()
    {
        foreach (string s in Corpus)
            foreach (char c in Chars)
                foreach (StringComparison cmp in AllComparisons)
                    AssertBool(
                        s.StartsWith(c.ToString(), cmp),
                        StringExtensions.StartsWith(s, c, cmp),
                        $"StartsWith(\"{Esc(s)}\", '{c}', {cmp})");
    }

    // ─── EndsWith(char) ─────────────────────────────────────────────
    // Verify against string-based EndsWith with Ordinal comparison.

    [Fact]
    public void EndsWith_Char_MatchesStringOverload()
    {
        foreach (string s in Corpus)
            foreach (char c in Chars)
                AssertBool(
                    s.EndsWith(c.ToString(), StringComparison.Ordinal),
                    StringExtensions.EndsWith(s, c),
                    $"EndsWith(\"{Esc(s)}\", '{c}')");
    }

    // ─── EndsWith(char, StringComparison) ───────────────────────────

    [Fact]
    public void EndsWith_Char_Comparison_MatchesStringOverload()
    {
        foreach (string s in Corpus)
            foreach (char c in Chars)
                foreach (StringComparison cmp in AllComparisons)
                    AssertBool(
                        s.EndsWith(c.ToString(), cmp),
                        StringExtensions.EndsWith(s, c, cmp),
                        $"EndsWith(\"{Esc(s)}\", '{c}', {cmp})");
    }

    // ─── Replace(string, string?, StringComparison) ─────────────────
    // Ordinal path delegates to built-in; non-Ordinal uses ReplaceCore.

    [Fact]
    public void Replace_MatchesBuiltIn()
    {
        string[] oldValues = ["a", "A", "hello", "HELLO", "llo", "straße", "SS", "ß", " ", "xx"];
        string?[] newValues = [null, "", "X", "YY", "replaced"];

        foreach (string s in Corpus)
            foreach (string old in oldValues)
                foreach (string? @new in newValues)
                    foreach (StringComparison cmp in AllComparisons)
                        AssertString(
                            s.Replace(old, @new, cmp),
                            StringExtensions.Replace(s, old, @new, cmp),
                            $"Replace(\"{Esc(s)}\", \"{Esc(old)}\", \"{Esc(@new)}\", {cmp})");
    }

    // ─── Helpers ────────────────────────────────────────────────────

    private static void AssertBool(bool expected, bool actual, string label)
    {
        Assert.True(expected == actual, $"{label}: expected={expected} actual={actual}");
    }

    private static void AssertString(string expected, string actual, string label)
    {
        Assert.True(string.Equals(expected, actual, StringComparison.Ordinal),
            $"{label}: expected=\"{Esc(expected)}\" actual=\"{Esc(actual)}\"");
    }

    private static string Esc(string? s) =>
        s?.Replace("\0", "\\0", StringComparison.Ordinal)
         .Replace("\n", "\\n", StringComparison.Ordinal)
         .Replace("\r", "\\r", StringComparison.Ordinal)
         .Replace("\t", "\\t", StringComparison.Ordinal) ?? "(null)";
}

public sealed class StringExtensionsUnitTests
{
    [Fact]
    public void Replace_NullOldValue_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => StringExtensions.Replace("input", null!, "new", StringComparison.Ordinal));
    }

    [Fact]
    public void Replace_EmptyOldValue_Throws()
    {
        Assert.Throws<ArgumentException>(() => StringExtensions.Replace("input", "", "new", StringComparison.Ordinal));
    }

    [Fact]
    public void Replace_NullNewValue_DoesNotThrow()
    {
        string result = StringExtensions.Replace("input", "in", null, StringComparison.Ordinal);
        Assert.Equal("input".Replace("in", null), result);
    }

    [Fact]
    public void Replace_UnsupportedComparison_Throws()
    {
        Assert.Throws<ArgumentException>(() => StringExtensions.Replace("input", "in", "new", (StringComparison)999));
    }
}

#endif

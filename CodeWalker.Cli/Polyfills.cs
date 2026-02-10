#if (!NETCOREAPP2_1_OR_GREATER && !NETSTANDARD2_1_OR_GREATER) || TESTING
using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
#endif

namespace CodeWalker.Cli;

#pragma warning disable IDE0079 // Remove unnecessary suppression

#if (!NETCOREAPP2_1_OR_GREATER && !NETSTANDARD2_1_OR_GREATER) || TESTING

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
            _ = result.Append(searchSpace[..index]);

            // append the replacement
            _ = result.Append(newValue);

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

        _ = result.Append(searchSpace);
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

#pragma warning restore IDE0079 // Remove unnecessary suppression

// Source - https://stackoverflow.com/a/74447498
// Posted by Matthew Watson
// Retrieved 2026-02-06, License - CC BY-SA 4.0

#pragma warning disable IDE0130 // Namespace does not match folder structure

#if !NETCOREAPP
using System;
using System.Text;
#endif

#if !NET5_0_OR_GREATER
using System.ComponentModel;
#endif

namespace System.Runtime.CompilerServices
{
#if !NET5_0_OR_GREATER

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit { }

#endif // !NET5_0_OR_GREATER

#if !NET7_0_OR_GREATER

    [AttributeUsage(
        AttributeTargets.Class
            | AttributeTargets.Struct
            | AttributeTargets.Field
            | AttributeTargets.Property,
        AllowMultiple = false,
        Inherited = false
    )]
    internal sealed class RequiredMemberAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute(string featureName) : Attribute
    {
        public string FeatureName { get; } = featureName;
        public bool IsOptional { get; init; }

        public const string RefStructs = nameof(RefStructs);
        public const string RequiredMembers = nameof(RequiredMembers);
    }

#endif // !NET7_0_OR_GREATER
}

namespace System.Diagnostics.CodeAnalysis
{
#if !NET7_0_OR_GREATER
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    internal sealed class SetsRequiredMembersAttribute : Attribute { }
#endif
}

namespace CodeWalker.Cli.Polyfills
{
#if !NETCOREAPP
    internal static class StringExtensions
    {
        public static bool Contains(this string s, char value)
        {
            return s.IndexOf(value) >= 0;
        }

        public static bool Contains(this string s, char value, StringComparison comparisonType)
        {
            return s.IndexOf(value.ToString(), comparisonType) >= 0;
        }

        public static bool Contains(this string s, string value, StringComparison comparisonType)
        {
            return s.IndexOf(value, comparisonType) >= 0;
        }

        public static bool StartsWith(this string s, char value)
        {
            return s.Length > 0 && s[0] == value;
        }

        private static string ReplaceInternal(
            this string s,
            string oldValue,
            string? newValue,
            StringComparison comparisonType
        )
        {
            StringBuilder? sb = new();
            int start = 0;
            int index;
            while ((index = s.IndexOf(oldValue, start, comparisonType)) >= 0)
            {
                sb.Append(s, start, index - start);
                if (newValue != null)
                    sb.Append(newValue);
                start = index + oldValue.Length;
            }
            sb.Append(s, start, s.Length - start);
            return sb.ToString();
        }

        public static string Replace(this string s, string oldValue, string? newValue, StringComparison comparisonType) =>
            comparisonType switch
            {
                StringComparison.Ordinal => s.Replace(oldValue, newValue),
                _ => s.ReplaceInternal(oldValue, newValue, comparisonType),
            };
    }
#endif
}

#pragma warning restore IDE0130 // Namespace does not match folder structure

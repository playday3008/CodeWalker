// Source - https://stackoverflow.com/a/74447498
// Posted by Matthew Watson
// Retrieved 2026-02-06, License - CC BY-SA 4.0

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
    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName)
        {
            FeatureName = featureName;
        }

        public string FeatureName { get; }
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

#if !NETCOREAPP
namespace CodeWalker.Cli.Polyfills
{
    internal static class StringExtensions
    {
        public static bool Contains(this string s, char value)
        {
            return s.IndexOf(value) >= 0;
        }
    }
}
#endif

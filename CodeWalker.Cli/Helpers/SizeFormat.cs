using System;

#if TESTING
using Xunit;
#endif

namespace CodeWalker.Cli.Helpers;

/// <summary>
/// Defines size formatting options for human-readable file sizes.
/// </summary>
internal enum SizeFormat
{
    /// <summary>IEC format: 1024-based (KiB, MiB, GiB)</summary>
    IEC = 0,

    /// <summary>SI format: 1000-based (KB, MB, GB)</summary>
    SI = 1,
}

/// <summary>
/// Extension methods for SizeFormat to format byte sizes into human-readable strings.
/// </summary>
internal static class SizeFormatExtensions
{
    private static readonly string[] SiSuffixes = ["B", "KB", "MB", "GB", "TB", "PB"];
    private static readonly string[] IecSuffixes = ["B", "KiB", "MiB", "GiB", "TiB", "PiB"];

    private static double GetDivisor(this SizeFormat format) =>
        format switch
        {
            SizeFormat.SI => 1000.0,
            SizeFormat.IEC => 1024.0,
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

    private static string[] GetSuffixes(this SizeFormat format) =>
        format switch
        {
            SizeFormat.SI => SiSuffixes,
            SizeFormat.IEC => IecSuffixes,
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

    /// <summary>
    /// Formats the given byte size into a human-readable string based on the size format.
    /// </summary>
    public static string ToFormattedString(this SizeFormat format, long bytes)
    {
        double divisor = format.GetDivisor();
        string[] suffixes = format.GetSuffixes();
        int i = 0;
        double size = Math.Abs((double)bytes);
        while (size >= divisor && i < suffixes.Length - 1)
        {
            size /= divisor;
            i++;
        }
        if (bytes < 0) size = -size;
        return $"{size:0.##} {suffixes[i]}";
    }
}

#if TESTING
public sealed class SizeFormatTests
{
    [Fact]
    public void IEC_ZeroBytes() =>
        Assert.Equal("0 B", SizeFormat.IEC.ToFormattedString(0));

    [Fact]
    public void IEC_ExactBoundaries()
    {
        Assert.Equal("1 B", SizeFormat.IEC.ToFormattedString(1));
        Assert.Equal("1 KiB", SizeFormat.IEC.ToFormattedString(1024));
        Assert.Equal("1 MiB", SizeFormat.IEC.ToFormattedString(1024 * 1024));
        Assert.Equal("1 GiB", SizeFormat.IEC.ToFormattedString(1024L * 1024 * 1024));
        Assert.Equal("1 TiB", SizeFormat.IEC.ToFormattedString(1024L * 1024 * 1024 * 1024));
        Assert.Equal("1 PiB", SizeFormat.IEC.ToFormattedString(1024L * 1024 * 1024 * 1024 * 1024));
        Assert.Equal("1024 PiB", SizeFormat.IEC.ToFormattedString(1024L * 1024 * 1024 * 1024 * 1024 * 1024));
    }

    [Fact]
    public void IEC_FractionalValues()
    {
        Assert.Equal("1.5 KiB", SizeFormat.IEC.ToFormattedString(1536));
        Assert.Equal("1.5 MiB", SizeFormat.IEC.ToFormattedString(1536 * 1024));
        Assert.Equal("1.5 GiB", SizeFormat.IEC.ToFormattedString(1536L * 1024 * 1024));
        Assert.Equal("1.5 TiB", SizeFormat.IEC.ToFormattedString(1536L * 1024 * 1024 * 1024));
        Assert.Equal("1.5 PiB", SizeFormat.IEC.ToFormattedString(1536L * 1024 * 1024 * 1024 * 1024));
        Assert.Equal("1536 PiB", SizeFormat.IEC.ToFormattedString(1536L * 1024 * 1024 * 1024 * 1024 * 1024));
    }

    [Fact]
    public void SI_ExactBoundaries()
    {
        Assert.Equal("1 B", SizeFormat.SI.ToFormattedString(1));
        Assert.Equal("1 KB", SizeFormat.SI.ToFormattedString(1000));
        Assert.Equal("1 MB", SizeFormat.SI.ToFormattedString(1_000_000));
        Assert.Equal("1 GB", SizeFormat.SI.ToFormattedString(1_000_000_000));
        Assert.Equal("1 TB", SizeFormat.SI.ToFormattedString(1_000_000_000_000));
        Assert.Equal("1 PB", SizeFormat.SI.ToFormattedString(1_000_000_000_000_000));
        Assert.Equal("1000 PB", SizeFormat.SI.ToFormattedString(1_000_000_000_000_000_000));
    }

    [Fact]
    public void SI_FractionalValues()
    {
        Assert.Equal("1.5 KB", SizeFormat.SI.ToFormattedString(1500));
        Assert.Equal("1.5 MB", SizeFormat.SI.ToFormattedString(1_500_000));
        Assert.Equal("1.5 GB", SizeFormat.SI.ToFormattedString(1_500_000_000));
        Assert.Equal("1.5 TB", SizeFormat.SI.ToFormattedString(1_500_000_000_000));
        Assert.Equal("1.5 PB", SizeFormat.SI.ToFormattedString(1_500_000_000_000_000));
        Assert.Equal("1500 PB", SizeFormat.SI.ToFormattedString(1_500_000_000_000_000_000));
    }

    [Fact]
    public void SmallBytes_NoSuffix()
    {
        Assert.Equal("1023 B", SizeFormat.IEC.ToFormattedString(1023));
        Assert.Equal("999 B", SizeFormat.SI.ToFormattedString(999));
    }

    [Fact]
    public void NegativeBytes_FormatsCorrectly()
    {
        Assert.Equal("-1 B", SizeFormat.IEC.ToFormattedString(-1));
        Assert.Equal("-1 KiB", SizeFormat.IEC.ToFormattedString(-1024));
        Assert.Equal("-1 PiB", SizeFormat.IEC.ToFormattedString(-1024L * 1024 * 1024 * 1024 * 1024));
        Assert.Equal("-1024 PiB", SizeFormat.IEC.ToFormattedString(-1024L * 1024 * 1024 * 1024 * 1024 * 1024));

        Assert.Equal("-1 B", SizeFormat.SI.ToFormattedString(-1));
        Assert.Equal("-1 KB", SizeFormat.SI.ToFormattedString(-1000));
        Assert.Equal("-1 PB", SizeFormat.SI.ToFormattedString(-1_000_000_000_000_000));
        Assert.Equal("-1000 PB", SizeFormat.SI.ToFormattedString(-1_000_000_000_000_000_000));
    }

    [Fact]
    public void InvalidFormat_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ((SizeFormat)999).ToFormattedString(1024));
}
#endif

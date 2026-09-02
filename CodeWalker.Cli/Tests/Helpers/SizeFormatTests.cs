using System;

using CodeWalker.Cli.Helpers;

using Xunit;

namespace CodeWalker.Cli.Tests.Helpers;

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

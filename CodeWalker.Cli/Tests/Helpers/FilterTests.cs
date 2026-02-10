using CodeWalker.Cli.Helpers;

using Xunit;

namespace CodeWalker.Cli.Tests.Helpers;

public sealed class FilterTests
{
    [Fact]
    public void Normalize_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(Filter.Normalize(null));
        Assert.Empty(Filter.Normalize([]));
    }

    [Fact]
    public void Normalize_TrimsAndLowercases()
    {
        string[] result = Filter.Normalize(["  .YDR  ", "Foo"]);
        Assert.Equal([".ydr", "foo"], result);
    }

    [Fact]
    public void Normalize_StripsBlankEntries()
    {
        string[] result = Filter.Normalize(["a", "", "  ", "b"]);
        Assert.Equal(["a", "b"], result);
    }

    [Fact]
    public void Matches_NoFilters_MatchesEverything()
    {
        Assert.True(Filter.Matches("anything.ydr", null));
        Assert.True(Filter.Matches("anything.ydr", []));
    }

    [Fact]
    public void Matches_ExtensionWithDot()
    {
        string[] filters = [".ydr"];
        Assert.True(Filter.Matches("model.ydr", filters));
        Assert.False(Filter.Matches("model.ytd", filters));
    }

    [Fact]
    public void Matches_ExtensionWithoutDot()
    {
        string[] filters = ["ydr"];
        Assert.True(Filter.Matches("model.ydr", filters));
        Assert.False(Filter.Matches("model.ytd", filters));
    }

    [Fact]
    public void Matches_WildcardPattern()
    {
        string[] filters = ["*.ydr"];
        Assert.True(Filter.Matches("model.ydr", filters));
        Assert.True(Filter.Matches("dir/model.ydr", filters));
        Assert.False(Filter.Matches("model.ytd", filters));
    }

    [Fact]
    public void Matches_PathPattern()
    {
        string[] filters = ["vehicles/*.ydr"];
        Assert.True(Filter.Matches("vehicles/car.ydr", filters));
        Assert.False(Filter.Matches("peds/ped.ydr", filters));
    }

    [Fact]
    public void Matches_GlobstarPattern()
    {
        string[] filters = ["**/vehicles/*.ydr"];
        Assert.True(Filter.Matches("x64/dlcpacks/vehicles/car.ydr", filters));
        Assert.True(Filter.Matches("vehicles/car.ydr", filters));
    }

    [Fact]
    public void Matches_CaseInsensitive()
    {
        string[] filters = [".ydr"];
        Assert.True(Filter.Matches("MODEL.YDR", filters));
    }

    [Fact]
    public void Matches_BackslashNormalized()
    {
        string[] filters = ["vehicles\\*.ydr"];
        Assert.True(Filter.Matches("vehicles/car.ydr", filters));
    }
}

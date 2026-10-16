using Microsoft.Extensions.Primitives;
using Xapi.Lrs.Http;

namespace Xapi.Lrs.Tests;

public class EntityTagListTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void An_absent_or_empty_header_is_no_precondition(string? header)
    {
        Assert.Null(EntityTagList.Parse(header is null ? StringValues.Empty : new StringValues(header)));
    }

    [Fact]
    public void A_star_matches_any_tag()
    {
        var list = EntityTagList.Parse(" * ")!;
        Assert.True(list.IsAny);
    }

    [Theory]
    [InlineData("\"abc\"", "abc", true, true)]
    [InlineData("W/\"abc\"", "abc", false, true)]
    [InlineData("abc", "abc", true, true)]
    [InlineData("\"x\", \"abc\"", "abc", true, true)]
    [InlineData("\"x\",W/\"abc\"", "abc", false, true)]
    [InlineData("\"ABC\"", "abc", false, false)]
    [InlineData("\"abc", "abc", true, true)]
    [InlineData("\"a,b\"", "a,b", true, true)]
    public void Tags_compare_strongly_or_weakly(string header, string etag, bool strong, bool weak)
    {
        var list = EntityTagList.Parse(header)!;
        Assert.False(list.IsAny);
        Assert.Equal(strong, list.Contains(etag, weak: false));
        Assert.Equal(weak, list.Contains(etag, weak: true));
    }

    [Fact]
    public void Several_header_lines_form_one_list()
    {
        var list = EntityTagList.Parse(new StringValues(["\"a\"", "\"b\""]))!;
        Assert.True(list.Contains("a", weak: false));
        Assert.True(list.Contains("b", weak: false));
    }
}

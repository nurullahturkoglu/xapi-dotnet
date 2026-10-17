namespace Xapi.Core.Tests.Values;

public class LanguageMapTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("en-US")]
    [InlineData("tlh")]
    [InlineData("und")]
    [InlineData("zh-Hant-TW")]
    [InlineData("zh-yue-HK")]
    [InlineData("sr-Latn-RS")]
    [InlineData("es-419")]
    [InlineData("de-CH-1996")]
    [InlineData("sl-rozaj-biske")]
    [InlineData("en-US-u-islamcal")]
    [InlineData("en-a-bbb-x-a-ccc")]
    [InlineData("x-whatever")]
    [InlineData("i-klingon")]
    [InlineData("zh-min-nan")]
    [InlineData("EN-us")]
    public void Accepts_well_formed_rfc5646_tags(string tag) => Assert.True(LanguageMap.IsValidLanguageTag(tag));

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("a12345678")]
    [InlineData("en-")]
    [InlineData("en--US")]
    [InlineData("en_US")]
    [InlineData("123")]
    [InlineData("en-US-x")]
    [InlineData("en-a")]
    [InlineData("toolongtag")]
    [InlineData("en-US-123456789")]
    [InlineData("en-U")]
    [InlineData("en-U1")]
    [InlineData("en-41")]
    [InlineData("en-41a")]
    [InlineData("e1-US")]
    public void Rejects_malformed_tags(string tag) => Assert.False(LanguageMap.IsValidLanguageTag(tag));

    [Fact]
    public void Lookup_ignores_case_and_keeps_the_original_key()
    {
        var map = LanguageMap.Create("en-US", "completed").With("tr", "tamamladı");
        Assert.Equal("completed", map["EN-us"]);
        Assert.True(map.ContainsKey("TR"));
        Assert.Equal(["en-US", "tr"], map.Keys);
        Assert.Equal(2, map.Count);
    }

    [Fact]
    public void With_replaces_an_existing_tag_ignoring_case()
    {
        var map = LanguageMap.Create("en-US", "a").With("en-us", "b");
        Assert.Equal("b", Assert.Single(map).Value);
    }

    [Fact]
    public void Equality_ignores_order_and_tag_case_but_not_value_case()
    {
        var a = new LanguageMap([new("en", "x"), new("de", "y")]);
        var b = new LanguageMap([new("DE", "y"), new("en", "x")]);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, LanguageMap.Create("en", "X").With("de", "y"));
        Assert.True(a == b);
    }

    [Fact]
    public void Constructor_rejects_invalid_entries()
    {
        Assert.Throws<ArgumentException>(() => LanguageMap.Create("not a tag", "x"));
        Assert.Throws<ArgumentException>(() => new LanguageMap([new("en", "a"), new("EN", "b")]));
        Assert.Throws<ArgumentException>(() => LanguageMap.Create("en", null!));
    }
}

namespace Xapi.Core.Tests.Values;

public class IriTests
{
    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com/path?query=1#fragment")]
    [InlineData("urn:uuid:fd41c918-b88b-4b20-a0a5-a4c32391aaa0")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("http://例え.テスト/パス")]
    [InlineData("http://[::1]:8080/x")]
    [InlineData("tag:example.com,2026:x%20y")]
    [InlineData("http://example.com/\U0001F600")]
    public void Accepts_absolute_iris(string value)
    {
        Assert.True(Iri.TryParse(value, out var iri));
        Assert.Equal(value, iri.Value);
        Assert.Equal(value, new Iri(value).ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("example.com")]
    [InlineData("/relative/path")]
    [InlineData("1http://example.com")]
    [InlineData("http:")]
    [InlineData("http://example.com/with space")]
    [InlineData("http://example.com/<tag>")]
    [InlineData("http://example.com/%zz")]
    [InlineData("http://example.com/#a#b")]
    [InlineData("http://example.com/[x]")]
    [InlineData("should fail")]
    public void Rejects_non_iris(string value)
    {
        Assert.False(Iri.TryParse(value, out _));
        Assert.Throws<FormatException>(() => new Iri(value));
    }

    [Fact]
    public void Equality_is_ordinal_and_nothing_is_normalized()
    {
        Assert.Equal(new Iri("http://example.com/a"), new Iri("http://example.com/a"));
        Assert.NotEqual(new Iri("http://Example.com/a"), new Iri("http://example.com/a"));
        Assert.Equal("http://www.example.com", Iri.FromUri(new Uri("http://www.example.com")).Value);
    }

    [Fact]
    public void Default_value_is_empty()
    {
        Assert.True(default(Iri).IsEmpty);
        Assert.Equal(string.Empty, default(Iri).Value);
        Assert.Throws<ArgumentException>(() => new Activity(default));
    }

    [Fact]
    public void FromUri_rejects_relative_uris() =>
        Assert.Throws<FormatException>(() => Iri.FromUri(new Uri("/x", UriKind.Relative)));
}

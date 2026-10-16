using System.Text.Json;

namespace Xapi.Core.Tests.Values;

public class ExtensionsTests
{
    private static readonly Iri Key = new("http://example.com/ext");

    private static JsonElement Value(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Equality_compares_values_deeply()
    {
        var a = Extensions.Create(Key, Value("""{"a":1,"b":[1,2,{"c":"d"}]}"""));
        var b = Extensions.Create(Key, Value("""{"b":[1,2,{"c":"d"}],"a":1.0}"""));
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Theory]
    [InlineData("""{"a":1}""", """{"a":2}""")]
    [InlineData("""[1,2]""", """[2,1]""")]
    [InlineData("""{"a":1}""", """{"a":1,"b":2}""")]
    [InlineData("\"x\"", "\"X\"")]
    [InlineData("true", "false")]
    [InlineData("null", "0")]
    public void Different_values_are_not_equal(string left, string right) =>
        Assert.NotEqual(Extensions.Create(Key, Value(left)), Extensions.Create(Key, Value(right)));

    [Fact]
    public void Values_are_cloned_from_their_document()
    {
        Extensions extensions;
        using (var document = JsonDocument.Parse("""{"n":1.50}"""))
        {
            extensions = Extensions.Create(Key, document.RootElement);
        }

        Assert.Equal("""{"n":1.50}""", extensions[Key].GetRawText());
    }

    [Fact]
    public void Constructor_rejects_duplicate_and_default_entries()
    {
        Assert.Throws<ArgumentException>(() => new Extensions([new(Key, Value("1")), new(Key, Value("2"))]));
        Assert.Throws<ArgumentException>(() => Extensions.Create(default, Value("1")));
        Assert.Throws<ArgumentException>(() => Extensions.Create(Key, default));
    }

    [Fact]
    public void With_adds_or_replaces()
    {
        var other = new Iri("http://example.com/other");
        var extensions = Extensions.Create(Key, Value("1")).With(other, Value("2")).With(Key, Value("3"));
        Assert.Equal(2, extensions.Count);
        Assert.Equal(3, extensions[Key].GetInt32());
        Assert.True(extensions.ContainsKey(other));
    }
}

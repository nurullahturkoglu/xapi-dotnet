using Xapi.Internal;

namespace Xapi.Core.Tests.Json;

public class JsonPathStackTests
{
    [Fact]
    public void Paths_are_written_as_JsonPath_writes_them()
    {
        var context = new JsonPathStack().Root.Index(2).Property("context");
        Assert.Equal("$[2].context", context.ToString());
        Assert.Equal(
            "$[2].context.extensions['http://example.com/x']['it\\'s']",
            context.Property("extensions").Property("http://example.com/x").Property("it's").ToString());
    }

    [Fact]
    public void A_path_used_after_a_sibling_replaced_part_of_it_throws()
    {
        var root = new JsonPathStack().Root;
        var actor = root.Property("actor");
        var name = actor.Property("name");
        _ = root.Property("verb");
        Assert.Throws<InvalidOperationException>(() => actor.Property("mbox"));
        Assert.Throws<InvalidOperationException>(() => name.ToString());
        Assert.Equal("$", root.ToString());
    }
}

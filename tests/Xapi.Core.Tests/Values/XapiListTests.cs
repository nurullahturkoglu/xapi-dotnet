namespace Xapi.Core.Tests.Values;

public class XapiListTests
{
    [Fact]
    public void Equality_is_structural_and_ordered()
    {
        var a = XapiList.Create(1, 2, 3);
        XapiList<int> b = [1, 2, 3];
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, XapiList.Create(3, 2, 1));
        Assert.True(a == b);
        Assert.False(a == null);
    }

    [Fact]
    public void Records_holding_lists_compare_by_value()
    {
        var left = new ContextAgent(Agent.FromMbox("a@example.com")) { RelevantTypes = [new Iri("http://example.com/t")] };
        var right = new ContextAgent(Agent.FromMbox("a@example.com")) { RelevantTypes = XapiList.Create(new Iri("http://example.com/t")) };
        Assert.Equal(left, right);
    }

    [Fact]
    public void Create_copies_its_input()
    {
        var source = new[] { "a", "b" };
        var list = XapiList.Create(source);
        source[0] = "changed";
        Assert.Equal("a", list[0]);
    }

    [Fact]
    public void Empty_and_Add()
    {
        Assert.Same(XapiList<string>.Empty, XapiList.CreateRange(Array.Empty<string>()));
        var list = XapiList<string>.Empty.Add("x");
        Assert.Equal(["x"], list);
        Assert.True(XapiList<string>.Empty.IsEmpty);
        Assert.Equal("[x]", list.ToString());
    }

    [Fact]
    public void Null_init_becomes_empty()
    {
        var statement = new Statement(Agent.FromMbox("a@example.com"), Verbs.Completed, new Activity(new Iri("http://example.com/a"))) { Attachments = null! };
        Assert.True(statement.Attachments.IsEmpty);
    }
}

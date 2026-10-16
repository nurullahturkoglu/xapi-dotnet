using Xapi.Internal;

namespace Xapi;

/// <summary>One item of an interaction component list (<c>choices</c>, <c>scale</c>, <c>source</c>, <c>target</c>, <c>steps</c>).</summary>
/// <remarks>IEEE 9274.1.1, 4.2.2.3 "Interaction Components, Expanded".</remarks>
public sealed record InteractionComponent
{
    private readonly string _id;

    /// <summary>Creates an interaction component.</summary>
    /// <param name="id">Identifies the component within its list; should not contain whitespace.</param>
    public InteractionComponent(string id) => _id = Guard.NotNull(id);

    /// <summary>Identifies the component within its list.</summary>
    public string Id
    {
        get => _id;
        init => _id = Guard.NotNull(value);
    }

    /// <summary>A description, for example the text of a choice.</summary>
    public LanguageMap? Description { get; init; }
}

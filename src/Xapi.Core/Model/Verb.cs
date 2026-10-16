using Xapi.Internal;

namespace Xapi;

/// <summary>The action an Actor took, identified by an IRI, with optional display names.</summary>
/// <remarks>IEEE 9274.1.1, 4.2.2.2. The display names are not part of the Statement's meaning.</remarks>
public sealed record Verb
{
    private readonly Iri _id;

    /// <summary>Creates a Verb.</summary>
    /// <param name="id">The Verb's IRI.</param>
    public Verb(Iri id) => _id = Guard.NotDefault(id);

    /// <summary>The Verb's IRI.</summary>
    public Iri Id
    {
        get => _id;
        init => _id = Guard.NotDefault(value);
    }

    /// <summary>Human-readable names by language.</summary>
    public LanguageMap? Display { get; init; }
}

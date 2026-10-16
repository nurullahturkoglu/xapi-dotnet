using Xapi.Internal;

namespace Xapi;

/// <summary>An Activity: something an Actor interacted with, identified by an IRI.</summary>
/// <remarks>IEEE 9274.1.1, 4.2.2.3 "Object As Activity Table".</remarks>
public sealed record Activity : StatementObject
{
    private readonly Iri _id;

    /// <summary>Creates an Activity.</summary>
    /// <param name="id">The Activity's IRI.</param>
    public Activity(Iri id) => _id = Guard.NotDefault(id);

    /// <summary>The Activity's IRI.</summary>
    public Iri Id
    {
        get => _id;
        init => _id = Guard.NotDefault(value);
    }

    /// <summary>Metadata about the Activity.</summary>
    public ActivityDefinition? Definition { get; init; }
}

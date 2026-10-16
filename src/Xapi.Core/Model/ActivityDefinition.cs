namespace Xapi;

/// <summary>Metadata about an Activity, including the interaction properties of assessment items.</summary>
/// <remarks>IEEE 9274.1.1, 4.2.2.3 "Activity Definition Table" and "Interaction Activities Table".</remarks>
public sealed record ActivityDefinition
{
    /// <summary>The human-readable name.</summary>
    public LanguageMap? Name { get; init; }

    /// <summary>A description.</summary>
    public LanguageMap? Description { get; init; }

    /// <summary>The type of Activity.</summary>
    public Iri? Type { get; init; }

    /// <summary>An IRL to a human-readable document about the Activity.</summary>
    public Iri? MoreInfo { get; init; }

    /// <summary>The interaction type; required when any other interaction property is present.</summary>
    public InteractionType? InteractionType { get; init; }

    /// <summary>
    /// Patterns for correct responses. <see langword="null"/> means absent, which is different from an empty list.
    /// </summary>
    public XapiList<string>? CorrectResponsesPattern { get; init; }

    /// <summary>Choices, for <see cref="Xapi.InteractionType.Choice"/> and <see cref="Xapi.InteractionType.Sequencing"/>.</summary>
    public XapiList<InteractionComponent>? Choices { get; init; }

    /// <summary>The scale, for <see cref="Xapi.InteractionType.Likert"/>.</summary>
    public XapiList<InteractionComponent>? Scale { get; init; }

    /// <summary>Sources, for <see cref="Xapi.InteractionType.Matching"/>.</summary>
    public XapiList<InteractionComponent>? Source { get; init; }

    /// <summary>Targets, for <see cref="Xapi.InteractionType.Matching"/>.</summary>
    public XapiList<InteractionComponent>? Target { get; init; }

    /// <summary>Steps, for <see cref="Xapi.InteractionType.Performance"/>.</summary>
    public XapiList<InteractionComponent>? Steps { get; init; }

    /// <summary>Extensions.</summary>
    public Extensions? Extensions { get; init; }
}

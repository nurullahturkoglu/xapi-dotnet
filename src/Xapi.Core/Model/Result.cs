namespace Xapi;

/// <summary>A measured outcome of the experience a Statement describes.</summary>
/// <remarks>IEEE 9274.1.1, 4.2.2.4 "Result Table".</remarks>
public sealed record Result
{
    /// <summary>The score.</summary>
    public Score? Score { get; init; }

    /// <summary>Whether the attempt was successful.</summary>
    public bool? Success { get; init; }

    /// <summary>Whether the Activity was completed.</summary>
    public bool? Completion { get; init; }

    /// <summary>The learner's response, formatted for the Activity's interaction type.</summary>
    public string? Response { get; init; }

    /// <summary>How long the experience took.</summary>
    public XapiDuration? Duration { get; init; }

    /// <summary>Extensions.</summary>
    public Extensions? Extensions { get; init; }
}

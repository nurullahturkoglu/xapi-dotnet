namespace Xapi;

/// <summary>The score of an Agent for an Activity.</summary>
/// <remarks>IEEE 9274.1.1, 4.2.2.4 "Score Table": <c>scaled</c> is within -1..1 and <c>raw</c> within <c>min</c>..<c>max</c>.</remarks>
public sealed record Score
{
    /// <summary>The score scaled to -1..1.</summary>
    public double? Scaled { get; init; }

    /// <summary>The unscaled score, between <see cref="Min"/> and <see cref="Max"/> when they are present.</summary>
    public double? Raw { get; init; }

    /// <summary>The lowest possible score.</summary>
    public double? Min { get; init; }

    /// <summary>The highest possible score.</summary>
    public double? Max { get; init; }
}

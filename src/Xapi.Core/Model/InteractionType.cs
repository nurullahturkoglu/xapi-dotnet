namespace Xapi;

/// <summary>The kind of interaction an Activity represents (<c>interactionType</c>).</summary>
/// <remarks>IEEE 9274.1.1, 4.2.2.3 "Interaction Types". The JSON values are the lowercase hyphenated names.</remarks>
public enum InteractionType
{
    /// <summary><c>true-false</c>: two possible responses, true or false.</summary>
    TrueFalse,

    /// <summary><c>choice</c>: select one or more items from a list (<c>choices</c>).</summary>
    Choice,

    /// <summary><c>fill-in</c>: a short free-text response.</summary>
    FillIn,

    /// <summary><c>long-fill-in</c>: a long free-text response.</summary>
    LongFillIn,

    /// <summary><c>matching</c>: match items from <c>source</c> to <c>target</c>.</summary>
    Matching,

    /// <summary><c>performance</c>: a task with multiple <c>steps</c>.</summary>
    Performance,

    /// <summary><c>sequencing</c>: order the items in <c>choices</c>.</summary>
    Sequencing,

    /// <summary><c>likert</c>: select from a discrete <c>scale</c>.</summary>
    Likert,

    /// <summary><c>numeric</c>: a numeric response or range.</summary>
    Numeric,

    /// <summary><c>other</c>: any other interaction.</summary>
    Other,
}

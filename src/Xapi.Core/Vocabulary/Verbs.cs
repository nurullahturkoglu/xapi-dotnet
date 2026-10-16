namespace Xapi;

/// <summary>Common Verbs from the ADL vocabulary (<c>http://adlnet.gov/expapi/verbs/</c>), with an <c>en-US</c> display name.</summary>
public static class Verbs
{
    private const string Prefix = "http://adlnet.gov/expapi/verbs/";

    /// <summary>The IRI of the reserved <c>voided</c> Verb (IEEE 9274.1.1, 4.2.5).</summary>
    public static Iri VoidedId { get; } = new(Prefix + "voided");

    /// <summary><c>voided</c>: the reserved Verb that voids the Statement referenced by the object.</summary>
    public static Verb Voided { get; } = Create("voided");

    /// <summary><c>completed</c>.</summary>
    public static Verb Completed { get; } = Create("completed");

    /// <summary><c>passed</c>.</summary>
    public static Verb Passed { get; } = Create("passed");

    /// <summary><c>failed</c>.</summary>
    public static Verb Failed { get; } = Create("failed");

    /// <summary><c>attempted</c>.</summary>
    public static Verb Attempted { get; } = Create("attempted");

    /// <summary><c>experienced</c>.</summary>
    public static Verb Experienced { get; } = Create("experienced");

    /// <summary><c>answered</c>.</summary>
    public static Verb Answered { get; } = Create("answered");

    /// <summary><c>initialized</c>.</summary>
    public static Verb Initialized { get; } = Create("initialized");

    /// <summary><c>terminated</c>.</summary>
    public static Verb Terminated { get; } = Create("terminated");

    private static Verb Create(string name) =>
        new(new Iri(Prefix + name)) { Display = LanguageMap.Create("en-US", name) };
}

using Xapi.Internal;

namespace Xapi;

/// <summary>An xAPI Statement: an Actor, a Verb and an object, with optional result, context and attachments.</summary>
/// <remarks>
/// IEEE 9274.1.1, 4.2.2. <see cref="Stored"/> and <see cref="Authority"/> are normally set by the LRS, and a
/// Learning Record Provider should leave <see cref="Version"/> unset. Record equality is exact structural
/// equality; use <see cref="Validation.StatementEquivalence"/> for the standard's immutability comparison.
/// </remarks>
public sealed record Statement
{
    private readonly Actor _actor;
    private readonly Verb _verb;
    private readonly StatementObject _object;
    private readonly XapiList<Attachment> _attachments = XapiList<Attachment>.Empty;

    /// <summary>Creates a Statement.</summary>
    /// <param name="actor">Whom the Statement is about.</param>
    /// <param name="verb">The action.</param>
    /// <param name="object">What the action was done to.</param>
    public Statement(Actor actor, Verb verb, StatementObject @object)
    {
        _actor = Guard.NotNull(actor);
        _verb = Guard.NotNull(verb);
        _object = Guard.NotNull(@object);
    }

    /// <summary>The Statement's id. Assigned by the LRS when absent; Learning Record Providers should set it.</summary>
    public Guid? Id { get; init; }

    /// <summary>Whom the Statement is about.</summary>
    public Actor Actor
    {
        get => _actor;
        init => _actor = Guard.NotNull(value);
    }

    /// <summary>The action.</summary>
    public Verb Verb
    {
        get => _verb;
        init => _verb = Guard.NotNull(value);
    }

    /// <summary>What the action was done to.</summary>
    public StatementObject Object
    {
        get => _object;
        init => _object = Guard.NotNull(value);
    }

    /// <summary>The measured outcome.</summary>
    public Result? Result { get; init; }

    /// <summary>Contextual information.</summary>
    public Context? Context { get; init; }

    /// <summary>When the described experience happened. The LRS sets it to <see cref="Stored"/> when absent.</summary>
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>When the LRS recorded the Statement. Set by the LRS.</summary>
    public DateTimeOffset? Stored { get; init; }

    /// <summary>Who asserts the Statement is true. Normally set by the LRS from the credentials used.</summary>
    public Actor? Authority { get; init; }

    /// <summary>The xAPI version the Statement was accepted with, for example <c>1.0.3</c> or <c>2.0.0</c>. Set by the LRS.</summary>
    public string? Version { get; init; }

    /// <summary>Attachment headers.</summary>
    public XapiList<Attachment> Attachments
    {
        get => _attachments;
        init => _attachments = value ?? XapiList<Attachment>.Empty;
    }
}

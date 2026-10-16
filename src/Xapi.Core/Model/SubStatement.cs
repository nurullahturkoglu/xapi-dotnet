using Xapi.Internal;

namespace Xapi;

/// <summary>
/// A Statement used as the object of another Statement, for example to record something an Actor plans to do.
/// It has no id, stored, version or authority, and its object cannot be another SubStatement.
/// </summary>
/// <remarks>IEEE 9274.1.1, 4.2.2.3 "Object As Sub-Statement Table".</remarks>
public sealed record SubStatement : StatementObject
{
    private readonly Actor _actor;
    private readonly Verb _verb;
    private readonly StatementObject _object;
    private readonly XapiList<Attachment> _attachments = XapiList<Attachment>.Empty;

    /// <summary>Creates a SubStatement.</summary>
    /// <param name="actor">Whom the SubStatement is about.</param>
    /// <param name="verb">The action.</param>
    /// <param name="object">What the action was done to.</param>
    public SubStatement(Actor actor, Verb verb, StatementObject @object)
    {
        _actor = Guard.NotNull(actor);
        _verb = Guard.NotNull(verb);
        _object = Guard.NotNull(@object);
    }

    /// <summary>Whom the SubStatement is about.</summary>
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

    /// <summary>What the action was done to. Must not be another <see cref="SubStatement"/>.</summary>
    public StatementObject Object
    {
        get => _object;
        init => _object = Guard.NotNull(value);
    }

    /// <summary>The measured outcome.</summary>
    public Result? Result { get; init; }

    /// <summary>Contextual information.</summary>
    public Context? Context { get; init; }

    /// <summary>When the described experience happened (may be in the future for a SubStatement).</summary>
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>Attachment headers.</summary>
    public XapiList<Attachment> Attachments
    {
        get => _attachments;
        init => _attachments = value ?? XapiList<Attachment>.Empty;
    }
}

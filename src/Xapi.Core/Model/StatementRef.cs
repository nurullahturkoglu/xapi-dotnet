namespace Xapi;

/// <summary>A reference to another Statement by its id, used as an object (for example when voiding) or as <c>context.statement</c>.</summary>
/// <remarks>IEEE 9274.1.1, 4.2.2.3 "Object As Statement Table".</remarks>
public sealed record StatementRef : StatementObject
{
    /// <summary>Creates a Statement reference.</summary>
    /// <param name="id">The referenced Statement's id.</param>
    public StatementRef(Guid id) => Id = id;

    /// <summary>The referenced Statement's id.</summary>
    public Guid Id { get; init; }
}

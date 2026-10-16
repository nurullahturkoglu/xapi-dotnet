namespace Xapi;

/// <summary>An Agent or a Group: who a Statement is about, its authority, or an instructor or team.</summary>
/// <remarks>IEEE 9274.1.1, 4.2.2.1.</remarks>
public abstract record Actor : StatementObject
{
    private protected Actor()
    {
    }

    /// <summary>The full name of the Agent or the name of the Group.</summary>
    public string? Name { get; init; }
}

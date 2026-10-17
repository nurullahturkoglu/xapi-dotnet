namespace Xapi.Lrs;

/// <summary>
/// The names of the permissions a credential can be granted, for <see cref="XapiLrsCredential.Scopes"/> and the
/// <see cref="XapiLrsOptions.ScopeClaimType"/> claim. They are the scopes of xAPI 1.0.3 (Communication, 4.2,
/// "OAuth Authorization Scope"); IEEE 9274.1.1 leaves authorization to the LRS.
/// </summary>
/// <remarks>
/// Names are matched exactly, with case. New scopes may be added in a minor release.
/// </remarks>
public static class XapiScopes
{
    /// <summary><c>statements/write</c>: store statements.</summary>
    public const string StatementsWrite = "statements/write";

    /// <summary><c>statements/read/mine</c>: read statements stored with the same authority.</summary>
    public const string StatementsReadMine = "statements/read/mine";

    /// <summary><c>statements/read</c>: read any statement.</summary>
    public const string StatementsRead = "statements/read";

    /// <summary><c>state</c>: read and write State documents.</summary>
    public const string State = "state";

    /// <summary><c>define</c>: update the canonical definitions of Activities and Agents from stored statements.</summary>
    public const string Define = "define";

    /// <summary><c>profile</c>: read and write Activity and Agent Profile documents, and read the Agents and Activities Resources.</summary>
    public const string Profile = "profile";

    /// <summary><c>all/read</c>: read everything.</summary>
    public const string AllRead = "all/read";

    /// <summary><c>all</c>: unrestricted access.</summary>
    public const string All = "all";
}

namespace Xapi.Lrs;

/// <summary>
/// A permission granted to a credential. The names follow the scopes of xAPI 1.0.3 (Communication, 4.2,
/// "OAuth Authorization Scope"); IEEE 9274.1.1 leaves authorization to the LRS.
/// </summary>
public enum XapiScope
{
    /// <summary><c>statements/write</c>: store statements.</summary>
    StatementsWrite = 1,

    /// <summary><c>statements/read/mine</c>: read statements stored with the same authority.</summary>
    StatementsReadMine = 2,

    /// <summary><c>statements/read</c>: read any statement.</summary>
    StatementsRead = 3,

    /// <summary><c>state</c>: read and write State documents.</summary>
    State = 4,

    /// <summary><c>define</c>: update the canonical definitions of Activities and Agents from stored statements.</summary>
    Define = 5,

    /// <summary><c>profile</c>: read and write Activity and Agent Profile documents.</summary>
    Profile = 6,

    /// <summary><c>all/read</c>: read everything.</summary>
    AllRead = 7,

    /// <summary><c>all</c>: unrestricted access.</summary>
    All = 8,
}

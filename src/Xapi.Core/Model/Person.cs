namespace Xapi;

/// <summary>
/// The response of the Agents resource: everything an LRS knows about a person, where each property is a list
/// and several identifiers may appear together.
/// </summary>
/// <remarks>IEEE 9274.1.1, 4.1.6.3 "Person Object Details".</remarks>
public sealed record Person
{
    private readonly XapiList<string> _names = XapiList<string>.Empty;
    private readonly XapiList<Mbox> _mboxes = XapiList<Mbox>.Empty;
    private readonly XapiList<MboxSha1Sum> _mboxSha1Sums = XapiList<MboxSha1Sum>.Empty;
    private readonly XapiList<OpenId> _openIds = XapiList<OpenId>.Empty;
    private readonly XapiList<Account> _accounts = XapiList<Account>.Empty;

    /// <summary>Names (JSON <c>name</c>).</summary>
    public XapiList<string> Names
    {
        get => _names;
        init => _names = value ?? XapiList<string>.Empty;
    }

    /// <summary>E-mail identifiers (JSON <c>mbox</c>).</summary>
    public XapiList<Mbox> Mboxes
    {
        get => _mboxes;
        init => _mboxes = value ?? XapiList<Mbox>.Empty;
    }

    /// <summary>Hashed e-mail identifiers (JSON <c>mbox_sha1sum</c>).</summary>
    public XapiList<MboxSha1Sum> MboxSha1Sums
    {
        get => _mboxSha1Sums;
        init => _mboxSha1Sums = value ?? XapiList<MboxSha1Sum>.Empty;
    }

    /// <summary>OpenID identifiers (JSON <c>openid</c>).</summary>
    public XapiList<OpenId> OpenIds
    {
        get => _openIds;
        init => _openIds = value ?? XapiList<OpenId>.Empty;
    }

    /// <summary>Account identifiers (JSON <c>account</c>).</summary>
    public XapiList<Account> Accounts
    {
        get => _accounts;
        init => _accounts = value ?? XapiList<Account>.Empty;
    }
}

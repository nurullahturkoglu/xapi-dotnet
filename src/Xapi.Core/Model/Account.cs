using Xapi.Internal;

namespace Xapi;

/// <summary>An <c>account</c> identifier: a user account on an existing system, such as an LMS.</summary>
/// <remarks>IEEE 9274.1.1, 4.2.2.1 "Account Table".</remarks>
public sealed record Account : InverseFunctionalIdentifier
{
    private readonly Iri _homePage;
    private readonly string _name;

    /// <summary>Creates an account identifier.</summary>
    /// <param name="homePage">The canonical home page of the system the account is on.</param>
    /// <param name="name">The unique id or login name of the account on that system.</param>
    public Account(Iri homePage, string name)
    {
        _homePage = Guard.NotDefault(homePage);
        _name = Guard.NotNull(name);
    }

    /// <summary>The canonical home page of the system the account is on.</summary>
    public Iri HomePage
    {
        get => _homePage;
        init => _homePage = Guard.NotDefault(value);
    }

    /// <summary>The unique id or login name of the account.</summary>
    public string Name
    {
        get => _name;
        init => _name = Guard.NotNull(value);
    }
}

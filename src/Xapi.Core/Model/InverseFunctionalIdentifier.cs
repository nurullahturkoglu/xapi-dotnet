namespace Xapi;

/// <summary>
/// An inverse functional identifier (IFI) that identifies an Agent or Group: <see cref="Mbox"/>,
/// <see cref="MboxSha1Sum"/>, <see cref="OpenId"/> or <see cref="Account"/>.
/// </summary>
/// <remarks>IEEE 9274.1.1, 4.2.2.1: exactly one IFI is required on an Agent and on an identified Group.</remarks>
public abstract record InverseFunctionalIdentifier
{
    private protected InverseFunctionalIdentifier()
    {
    }
}

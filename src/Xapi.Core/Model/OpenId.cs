namespace Xapi;

/// <summary>An <c>openid</c> identifier: an OpenID URI that identifies the Agent.</summary>
public sealed record OpenId : InverseFunctionalIdentifier
{
    private readonly Iri _value;

    /// <summary>Creates an <c>openid</c> identifier.</summary>
    /// <param name="value">The OpenID URI.</param>
    public OpenId(Iri value) => _value = Internal.Guard.NotDefault(value);

    /// <summary>The OpenID URI.</summary>
    public Iri Value
    {
        get => _value;
        init => _value = Internal.Guard.NotDefault(value);
    }
}

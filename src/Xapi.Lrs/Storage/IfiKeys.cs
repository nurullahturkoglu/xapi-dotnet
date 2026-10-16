namespace Xapi.Lrs.Storage;

// Text keys for Inverse Functional Identifiers, used in link tables and the actor table. Each IFI type gets a
// prefix, so equal keys mean equal IFIs (IEEE 9274.1.1, 4.1.6.1: Agents are equal when they use the same IFI
// with equal values). Values are kept as sent: IRIs compare by simple string comparison (RFC 3987, 5.3.1).
internal static class IfiKeys
{
    public static string For(InverseFunctionalIdentifier ifi) => ifi switch
    {
        Mbox mbox => "mbox:" + mbox.Value,
        MboxSha1Sum sha1 => "mbox_sha1sum:" + sha1.Value,
        OpenId openId => "openid:" + openId.Value.Value,
        // An IRI cannot contain a space, so the first space ends the home page and the name may contain anything
        Account account => "account:" + account.HomePage.Value + " " + account.Name,
        _ => throw new ArgumentOutOfRangeException(nameof(ifi), ifi.GetType().Name, "Unknown identifier type."),
    };

    // The key that identifies an authority: its IFI, or for an anonymous Group (the OAuth form of 1.0.3) the
    // sorted keys of its members.
    public static string ForAuthority(Actor authority) => authority switch
    {
        Agent agent => For(agent.Ifi),
        Group { Ifi: { } ifi } => For(ifi),
        Group group => string.Join('\n', group.Members.Select(m => For(m.Ifi)).Order(StringComparer.Ordinal)),
        _ => throw new ArgumentOutOfRangeException(nameof(authority), authority.GetType().Name, "Unknown actor type."),
    };
}

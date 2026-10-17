using System.Collections.ObjectModel;

namespace Xapi.Lrs;

/// <summary>The Basic credentials an LRS accepts, see <see cref="XapiLrsOptions.Credentials"/>.</summary>
public sealed class XapiLrsCredentialList : Collection<XapiLrsCredential>
{
    /// <summary>Adds a credential.</summary>
    /// <param name="key">The Basic user name.</param>
    /// <param name="secret">The Basic password.</param>
    /// <param name="scopes">The scopes granted, as <see cref="XapiScopes"/> names. At least one is required.</param>
    /// <returns>The added credential.</returns>
    public XapiLrsCredential Add(string key, string secret, params string[] scopes)
    {
        var credential = new XapiLrsCredential(key, secret, scopes);
        Add(credential);
        return credential;
    }

    /// <inheritdoc/>
    protected override void InsertItem(int index, XapiLrsCredential item)
    {
        ArgumentNullException.ThrowIfNull(item);
        base.InsertItem(index, item);
    }

    /// <inheritdoc/>
    protected override void SetItem(int index, XapiLrsCredential item)
    {
        ArgumentNullException.ThrowIfNull(item);
        base.SetItem(index, item);
    }
}

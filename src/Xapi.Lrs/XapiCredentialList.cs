using System.Collections;

namespace Xapi.Lrs;

/// <summary>The Basic credentials an LRS accepts, see <see cref="XapiLrsOptions.Credentials"/>.</summary>
public sealed class XapiCredentialList : IReadOnlyList<XapiCredential>
{
    private readonly List<XapiCredential> _items = [];

    /// <inheritdoc/>
    public int Count => _items.Count;

    /// <inheritdoc/>
    public XapiCredential this[int index] => _items[index];

    /// <summary>Adds a credential.</summary>
    /// <param name="key">The Basic user name.</param>
    /// <param name="secret">The Basic password.</param>
    /// <param name="scopes">The scopes granted. At least one is required.</param>
    /// <returns>The added credential.</returns>
    /// <exception cref="ArgumentException">The credential is invalid or the key is already in the list.</exception>
    public XapiCredential Add(string key, string secret, params XapiScope[] scopes)
    {
        var credential = new XapiCredential(key, secret, scopes);
        Add(credential);
        return credential;
    }

    /// <summary>Adds a credential.</summary>
    /// <param name="credential">The credential.</param>
    /// <exception cref="ArgumentException">A credential with the same key is already in the list.</exception>
    public void Add(XapiCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        if (_items.Exists(c => string.Equals(c.Key, credential.Key, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"A credential with key '{credential.Key}' was already added.", nameof(credential));
        }

        _items.Add(credential);
    }

    /// <inheritdoc/>
    public IEnumerator<XapiCredential> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

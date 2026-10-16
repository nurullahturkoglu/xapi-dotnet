using System.Net.Http.Headers;
using System.Text;
using Xapi.Client.Internal;

namespace Xapi.Client;

/// <summary>How the client authenticates: HTTP Basic or a bearer token.</summary>
/// <remarks>
/// IEEE 9274.1.1, 4.1.8 leaves the scheme to the LRS. Other schemes can be added with a
/// <see cref="System.Net.Http.DelegatingHandler"/> and <see cref="XapiClientOptions.Credentials"/> left empty.
/// </remarks>
public sealed class XapiCredentials
{
    private readonly AuthenticationHeaderValue? _basic;
    private readonly Func<CancellationToken, ValueTask<string>>? _tokenProvider;

    private XapiCredentials(AuthenticationHeaderValue? basic, Func<CancellationToken, ValueTask<string>>? tokenProvider)
    {
        _basic = basic;
        _tokenProvider = tokenProvider;
    }

    /// <summary>HTTP Basic authentication with an LRS key and secret (UTF-8 encoded).</summary>
    /// <param name="key">The key (user name).</param>
    /// <param name="secret">The secret (password).</param>
    /// <returns>The credentials.</returns>
    public static XapiCredentials Basic(string key, string secret)
    {
        Guard.NotNull(key);
        Guard.NotNull(secret);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(key + ":" + secret));
        return new XapiCredentials(new AuthenticationHeaderValue("Basic", encoded), null);
    }

    /// <summary>A bearer token, asked for before every request so the provider can cache and refresh it.</summary>
    /// <param name="tokenProvider">Returns the current token, without the <c>Bearer</c> prefix.</param>
    /// <returns>The credentials.</returns>
    public static XapiCredentials Bearer(Func<CancellationToken, ValueTask<string>> tokenProvider) =>
        new(null, Guard.NotNull(tokenProvider));

    internal async ValueTask<AuthenticationHeaderValue> GetHeaderAsync(CancellationToken cancellationToken)
    {
        if (_basic is not null)
        {
            return _basic;
        }

        var token = await _tokenProvider!(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException("The bearer token provider returned an empty token.");
        }

        return new AuthenticationHeaderValue("Bearer", token);
    }
}

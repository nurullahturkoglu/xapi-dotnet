using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Xapi.Lrs.Security;

internal static class XapiClaimTypes
{
    public const string Scope = "xapi:scope";
    public const string Authority = "xapi:authority";
}

// HTTP Basic (RFC 7617) against the credentials in xapi_credential.
internal sealed class BasicAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    CredentialStore credentials) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Challenge = "Basic realm=\"xAPI\", charset=\"UTF-8\"";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (string.IsNullOrEmpty(header))
        {
            return AuthenticateResult.NoResult();
        }

        const string prefix = "Basic ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.Fail("The Authorization header is not a Basic credential.");
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[prefix.Length..].Trim()));
        }
        catch (FormatException)
        {
            return AuthenticateResult.Fail("The Basic credential is not valid base64.");
        }

        var colon = decoded.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            return AuthenticateResult.Fail("The Basic credential has no key.");
        }

        var credential = await credentials.ValidateAsync(decoded[..colon], decoded[(colon + 1)..], Context.RequestAborted).ConfigureAwait(false);
        if (credential is null)
        {
            return AuthenticateResult.Fail("The key or secret is not valid.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, credential.Key),
            new(ClaimTypes.Name, credential.Key),
            new(XapiClaimTypes.Authority, credential.AuthorityPayload),
        };
        claims.AddRange(credential.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => new Claim(XapiClaimTypes.Scope, s)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers[HeaderNames.WWWAuthenticate] = Challenge;
        return Task.CompletedTask;
    }
}

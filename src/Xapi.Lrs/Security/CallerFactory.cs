using System.Security.Claims;
using Microsoft.Extensions.Options;
using Xapi.Json;
using Xapi.Lrs.Storage;

namespace Xapi.Lrs.Security;

// Turns an authenticated principal into the authority and scopes of the request.
internal sealed class CallerFactory(IOptions<XapiLrsOptions> options)
{
    public bool IsDelegated => options.Value.AuthenticationScheme is not null;

    public string Scheme => options.Value.AuthenticationScheme ?? XapiLrsAuthenticationDefaults.AuthenticationScheme;

    public XapiCaller? Create(ClaimsPrincipal principal)
    {
        var settings = options.Value;
        if (!IsDelegated)
        {
            var payload = principal.FindFirstValue(XapiClaimTypes.Authority);
            var authority = payload is null ? null : XapiJson.ParseActor(payload, XapiVersion.V2_0_0, XapiReadMode.Lenient);
            if (authority is not { IsSuccess: true })
            {
                return null;
            }

            var scopes = Scopes.ParseList(principal.FindAll(XapiClaimTypes.Scope).Select(c => c.Value));
            return new XapiCaller(authority.Value, IfiKeys.ForAuthority(authority.Value), scopes);
        }

        Agent? agent;
        if (settings.AuthorityFactory is { } factory)
        {
            agent = factory(principal);
        }
        else
        {
            var name = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub") ?? principal.Identity?.Name;
            agent = string.IsNullOrEmpty(name) ? null : Agent.FromAccount(settings.AuthorityHomePage, name);
        }

        if (agent is null)
        {
            return null;
        }

        var delegatedScopes = Scopes.ParseList(principal.FindAll(settings.ScopeClaimType).Select(c => c.Value));
        return new XapiCaller(agent, IfiKeys.ForAuthority(agent), delegatedScopes);
    }
}

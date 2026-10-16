namespace Xapi.Lrs.Security;

// The authenticated client of a request: the authority its statements get and what it may do.
internal sealed record XapiCaller(Actor Authority, string AuthorityKey, ScopeSet Scopes);

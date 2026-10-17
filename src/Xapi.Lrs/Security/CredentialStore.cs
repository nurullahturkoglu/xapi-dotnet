using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Xapi.Json;
using Xapi.Lrs.Storage;

namespace Xapi.Lrs.Security;

internal sealed record ValidatedCredential(string Key, string Scopes, string AuthorityPayload);

// Basic credentials live in xapi_credential with PBKDF2 secret hashes. PBKDF2 is deliberately slow, and Basic
// sends the secret with every request, so a successful check is remembered for a minute as a SHA-256 digest.
internal sealed class CredentialStore(IDbContextFactory<XapiLrsDbContext> contextFactory, TimeProvider timeProvider)
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(1);

    // Checked against when the key is unknown, so that an unknown key costs the same PBKDF2 work as a wrong
    // secret and response times do not tell which keys exist
    private static readonly Lazy<string> UnknownKeyHash = new(() => SecretHasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

    private readonly ConcurrentDictionary<string, CacheEntry> _verified = new(StringComparer.Ordinal);

    public async Task<ValidatedCredential?> ValidateAsync(string key, string secret, CancellationToken cancellationToken)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        var now = timeProvider.GetUtcNow();
        if (_verified.TryGetValue(key, out var entry) && entry.Expires > now && CryptographicOperations.FixedTimeEquals(entry.SecretDigest, digest))
        {
            return entry.Credential;
        }

        CredentialEntity? row;
        var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            row = await context.Credentials.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Key == key, cancellationToken)
                .ConfigureAwait(false);
        }

        if (row is null)
        {
            _ = SecretHasher.Verify(secret, UnknownKeyHash.Value);
            return null;
        }

        if (!SecretHasher.Verify(secret, row.SecretHash))
        {
            return null;
        }

        var credential = new ValidatedCredential(row.Key, row.Scopes, row.AuthorityPayload);
        _verified[key] = new CacheEntry(digest, credential, now + CacheLifetime);
        return credential;
    }

    // Makes xapi_credential mirror the configured credentials, so removing one from the configuration revokes it.
    public async Task SeedAsync(IReadOnlyList<XapiCredential> credentials, Iri authorityHomePage, CancellationToken cancellationToken)
    {
        var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var rows = await context.Credentials.ToDictionaryAsync(c => c.Key, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);
            foreach (var credential in credentials)
            {
                var authority = credential.Authority ?? Agent.FromAccount(authorityHomePage, credential.Key);
                var scopes = string.Join(' ', credential.Scopes.Select(Scopes.Name));
                var authorityPayload = XapiJson.SerializeActor(authority, XapiVersion.V2_0_0);
                if (rows.Remove(credential.Key, out var row))
                {
                    if (!SecretHasher.Verify(credential.Secret, row.SecretHash))
                    {
                        row.SecretHash = SecretHasher.Hash(credential.Secret);
                    }

                    row.Scopes = scopes;
                    row.AuthorityPayload = authorityPayload;
                }
                else
                {
                    context.Credentials.Add(new CredentialEntity
                    {
                        Key = credential.Key,
                        SecretHash = SecretHasher.Hash(credential.Secret),
                        Scopes = scopes,
                        AuthorityPayload = authorityPayload,
                    });
                }
            }

            context.Credentials.RemoveRange(rows.Values);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        _verified.Clear();
    }

    private sealed record CacheEntry(byte[] SecretDigest, ValidatedCredential Credential, DateTimeOffset Expires);
}

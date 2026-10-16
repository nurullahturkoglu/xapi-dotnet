using System.Net;
using System.Text.Json;
using Xapi.Client.Internal;

namespace Xapi.Client;

public sealed partial class XapiClient
{
    internal const string StatesResource = "activities/state";
    internal const string ActivityProfilesResource = "activities/profile";
    internal const string AgentProfilesResource = "agents/profile";

    /// <inheritdoc/>
    public async Task<XapiDocument?> GetDocumentAsync(DocumentKey key, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(key);
        var version = await GetVersionAsync(cancellationToken).ConfigureAwait(false);
        var query = new QueryBuilder();
        key.AddParameters(query, version);
        using var response = await SendAsync(HttpMethod.Get, key.Resource, query, null, null, version, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var body = await ReadBytesAsync(response, cancellationToken).ConfigureAwait(false);
        return new XapiDocument(body, ContentTypeOf(response)?.ToString() ?? "application/octet-stream")
        {
            ETag = response.Headers.TryGetValues("ETag", out var etags) ? etags.FirstOrDefault() : null,
            LastModified = ReadLastModified(response),
        };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetStateIdsAsync(Iri activityId, Agent agent, Guid? registration = null, DateTimeOffset? since = null, CancellationToken cancellationToken = default)
    {
        Guard.NotDefault(activityId);
        Guard.NotNull(agent);
        var version = await GetVersionAsync(cancellationToken).ConfigureAwait(false);
        var query = new QueryBuilder()
            .Add("activityId", activityId)
            .Add("agent", agent, version)
            .Add("registration", registration)
            .Add("since", since);
        return await GetIdsAsync(StatesResource, query, version, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetActivityProfileIdsAsync(Iri activityId, DateTimeOffset? since = null, CancellationToken cancellationToken = default)
    {
        Guard.NotDefault(activityId);
        var version = await GetVersionAsync(cancellationToken).ConfigureAwait(false);
        var query = new QueryBuilder().Add("activityId", activityId).Add("since", since);
        return await GetIdsAsync(ActivityProfilesResource, query, version, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetAgentProfileIdsAsync(Agent agent, DateTimeOffset? since = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(agent);
        var version = await GetVersionAsync(cancellationToken).ConfigureAwait(false);
        var query = new QueryBuilder().Add("agent", agent, version).Add("since", since);
        return await GetIdsAsync(AgentProfilesResource, query, version, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task PutDocumentAsync(DocumentKey key, XapiDocument document, DocumentPrecondition? precondition = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(key);
        Guard.NotNull(document);
        return WriteDocumentAsync(HttpMethod.Put, key, () => CreateContent(document.Content, document.ContentType), precondition, cancellationToken);
    }

    /// <inheritdoc/>
    public Task MergeDocumentAsync(DocumentKey key, JsonElement json, DocumentPrecondition? precondition = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(key);
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"Only JSON objects can be merged, not {json.ValueKind}.", nameof(json));
        }

        var bytes = JsonBytes.From(json);
        return WriteDocumentAsync(HttpMethod.Post, key, () => CreateContent(bytes, JsonMediaType), precondition, cancellationToken);
    }

    /// <inheritdoc/>
    public Task DeleteDocumentAsync(DocumentKey key, DocumentPrecondition? precondition = null, CancellationToken cancellationToken = default) =>
        WriteDocumentAsync(HttpMethod.Delete, Guard.NotNull(key), null, precondition, cancellationToken);

    /// <inheritdoc/>
    public async Task DeleteStatesAsync(Iri activityId, Agent agent, Guid? registration = null, CancellationToken cancellationToken = default)
    {
        Guard.NotDefault(activityId);
        Guard.NotNull(agent);
        var version = await GetVersionAsync(cancellationToken).ConfigureAwait(false);
        var query = new QueryBuilder()
            .Add("activityId", activityId)
            .Add("agent", agent, version)
            .Add("registration", registration);
        using var response = await SendAsync(HttpMethod.Delete, StatesResource, query, null, null, version, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteDocumentAsync(HttpMethod method, DocumentKey key, Func<HttpContent>? content, DocumentPrecondition? precondition, CancellationToken cancellationToken)
    {
        var version = await GetVersionAsync(cancellationToken).ConfigureAwait(false);
        var query = new QueryBuilder();
        key.AddParameters(query, version);
        using var response = await SendAsync(method, key.Resource, query, content?.Invoke(), precondition, version, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> GetIdsAsync(string resource, QueryBuilder query, XapiVersion version, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, resource, query, null, null, version, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadStringArrayAsync(response, cancellationToken).ConfigureAwait(false);
    }
}

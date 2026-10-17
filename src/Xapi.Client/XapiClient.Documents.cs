using System.Net;
using System.Text.Json;
using Xapi.Client.Internal;

namespace Xapi.Client;

public partial class XapiClient
{
    internal const string StatesResource = "activities/state";
    internal const string ActivityProfilesResource = "activities/profile";
    internal const string AgentProfilesResource = "agents/profile";

    /// <summary>Fetches one State, Activity Profile or Agent Profile document.</summary>
    /// <param name="key">Which document.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The document with its ETag, or <see langword="null"/> when the LRS answers 404.</returns>
    public virtual async Task<XapiDocument?> GetDocumentAsync(DocumentKey key, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(key);
        var version = await VersionAsync(cancellationToken).ConfigureAwait(false);
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

    /// <summary>Lists the State ids for an Activity and Agent (and registration).</summary>
    /// <param name="activityId">The Activity.</param>
    /// <param name="agent">The Agent.</param>
    /// <param name="registration">The registration, if the states are tied to one.</param>
    /// <param name="since">Only ids of documents stored or changed after this time.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The <c>stateId</c> values.</returns>
    public virtual async Task<IReadOnlyList<string>> GetStateIdsAsync(Iri activityId, Agent agent, Guid? registration = null, DateTimeOffset? since = null, CancellationToken cancellationToken = default)
    {
        Guard.NotDefault(activityId);
        Guard.NotNull(agent);
        var version = await VersionAsync(cancellationToken).ConfigureAwait(false);
        var query = new QueryBuilder()
            .Add("activityId", activityId)
            .Add("agent", agent, version)
            .Add("registration", registration)
            .Add("since", since);
        return await GetIdsAsync(StatesResource, query, version, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists the Activity Profile ids for an Activity.</summary>
    /// <param name="activityId">The Activity.</param>
    /// <param name="since">Only ids of documents stored or changed after this time.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The <c>profileId</c> values.</returns>
    public virtual async Task<IReadOnlyList<string>> GetActivityProfileIdsAsync(Iri activityId, DateTimeOffset? since = null, CancellationToken cancellationToken = default)
    {
        Guard.NotDefault(activityId);
        var version = await VersionAsync(cancellationToken).ConfigureAwait(false);
        var query = new QueryBuilder().Add("activityId", activityId).Add("since", since);
        return await GetIdsAsync(ActivityProfilesResource, query, version, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lists the Agent Profile ids for an Agent.</summary>
    /// <param name="agent">The Agent.</param>
    /// <param name="since">Only ids of documents stored or changed after this time.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The <c>profileId</c> values.</returns>
    public virtual async Task<IReadOnlyList<string>> GetAgentProfileIdsAsync(Agent agent, DateTimeOffset? since = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(agent);
        var version = await VersionAsync(cancellationToken).ConfigureAwait(false);
        var query = new QueryBuilder().Add("agent", agent, version).Add("since", since);
        return await GetIdsAsync(AgentProfilesResource, query, version, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stores a document, replacing any existing one (PUT).</summary>
    /// <remarks>
    /// Without a <paramref name="precondition"/>, xAPI 2.0 LRSs answer 409 when the document already exists
    /// (IEEE 9274.1.1, 4.1.4). Use <see cref="DocumentPrecondition.IfNoneMatchAny"/> to create and
    /// <see cref="DocumentPrecondition.IfMatch"/> to replace.
    /// </remarks>
    /// <param name="key">Which document.</param>
    /// <param name="document">The content and content type. Its ETag and Last-Modified are not sent.</param>
    /// <param name="precondition">An <c>If-Match</c> or <c>If-None-Match</c> condition.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the LRS has stored the document.</returns>
    /// <exception cref="XapiPreconditionFailedException">The precondition failed.</exception>
    /// <exception cref="XapiConflictException">The document exists and no precondition was sent.</exception>
    public virtual Task PutDocumentAsync(DocumentKey key, XapiDocument document, DocumentPrecondition? precondition = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(key);
        Guard.NotNull(document);
        return WriteDocumentAsync(HttpMethod.Put, key, () => CreateContent(document.Content, document.ContentType), precondition, cancellationToken);
    }

    /// <summary>
    /// Merges a JSON object into a document (POST): each top-level property replaces the one in the stored JSON
    /// object. Creates the document if it does not exist.
    /// </summary>
    /// <param name="key">Which document.</param>
    /// <param name="json">A JSON object.</param>
    /// <param name="precondition">An <c>If-Match</c> or <c>If-None-Match</c> condition.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the LRS has stored the result.</returns>
    /// <exception cref="ArgumentException"><paramref name="json"/> is not an object.</exception>
    /// <exception cref="XapiPreconditionFailedException">The precondition failed.</exception>
    public virtual Task MergeDocumentAsync(DocumentKey key, JsonElement json, DocumentPrecondition? precondition = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(key);
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"Only JSON objects can be merged, not {json.ValueKind}.", nameof(json));
        }

        var bytes = JsonBytes.From(json);
        return WriteDocumentAsync(HttpMethod.Post, key, () => CreateContent(bytes, JsonMediaType), precondition, cancellationToken);
    }

    /// <summary>Deletes one document.</summary>
    /// <param name="key">Which document.</param>
    /// <param name="precondition">An <c>If-Match</c> condition, to delete only the version last read.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the LRS has deleted the document.</returns>
    /// <exception cref="XapiPreconditionFailedException">The precondition failed.</exception>
    public virtual Task DeleteDocumentAsync(DocumentKey key, DocumentPrecondition? precondition = null, CancellationToken cancellationToken = default) =>
        WriteDocumentAsync(HttpMethod.Delete, Guard.NotNull(key), null, precondition, cancellationToken);

    /// <summary>Deletes every State document of an Activity and Agent (and registration).</summary>
    /// <param name="activityId">The Activity.</param>
    /// <param name="agent">The Agent.</param>
    /// <param name="registration">Only the states of this registration; all registrations when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the LRS has deleted the documents.</returns>
    public virtual async Task DeleteStatesAsync(Iri activityId, Agent agent, Guid? registration = null, CancellationToken cancellationToken = default)
    {
        Guard.NotDefault(activityId);
        Guard.NotNull(agent);
        var version = await VersionAsync(cancellationToken).ConfigureAwait(false);
        var query = new QueryBuilder()
            .Add("activityId", activityId)
            .Add("agent", agent, version)
            .Add("registration", registration);
        using var response = await SendAsync(HttpMethod.Delete, StatesResource, query, null, null, version, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteDocumentAsync(HttpMethod method, DocumentKey key, Func<HttpContent>? content, DocumentPrecondition? precondition, CancellationToken cancellationToken)
    {
        var version = await VersionAsync(cancellationToken).ConfigureAwait(false);
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

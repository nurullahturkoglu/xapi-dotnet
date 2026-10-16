using System.Text.Json;
using Xapi.Json;

namespace Xapi.Client;

/// <summary>
/// The xAPI resources of one LRS: Statements, State, Activity Profile, Agent Profile, Agents, Activities and
/// About (IEEE 9274.1.1, 4.1.6). <see cref="XapiClient"/> implements it over <see cref="HttpClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// Error responses throw <see cref="XapiHttpException"/>, or its subclasses <see cref="XapiConflictException"/>
/// (409) and <see cref="XapiPreconditionFailedException"/> (412). Methods that fetch one thing by id return
/// <see langword="null"/> on 404 instead. Network failures and timeouts surface unchanged as
/// <see cref="HttpRequestException"/> and <see cref="TaskCanceledException"/>. Nothing is retried.
/// </para>
/// <para>Responses are read leniently (<see cref="XapiReadMode.Lenient"/>): properties the client does not know are ignored.</para>
/// </remarks>
public interface IXapiClient
{
    /// <summary>
    /// The xAPI version used for requests: <see cref="XapiClientOptions.Version"/>, or the version negotiated with
    /// the About resource on first use.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait for the negotiation.</param>
    /// <returns>The version.</returns>
    /// <exception cref="NotSupportedException">The LRS supports neither 2.0.x nor 1.0.x.</exception>
    Task<XapiVersion> GetVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores one Statement: PUT with <c>statementId</c> when <see cref="Statement.Id"/> is set, POST otherwise.</summary>
    /// <param name="statement">The Statement.</param>
    /// <param name="attachments">
    /// Data for the Statement's Attachments that have no <c>fileUrl</c>. When there is any, the request is sent as
    /// <c>multipart/mixed</c>.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Statement id: <see cref="Statement.Id"/>, or the id the LRS assigned.</returns>
    /// <exception cref="XapiValidationException">The Statement is invalid and <see cref="XapiClientOptions.ValidateBeforeSend"/> is on.</exception>
    /// <exception cref="XapiConflictException">The LRS has a different Statement with the same id.</exception>
    Task<Guid> SaveStatementAsync(Statement statement, IReadOnlyList<AttachmentPayload>? attachments = null, CancellationToken cancellationToken = default);

    /// <summary>Stores a batch of Statements with one POST. The LRS stores all or none of them.</summary>
    /// <remarks>An empty batch returns an empty list without a request.</remarks>
    /// <param name="statements">The Statements.</param>
    /// <param name="attachments">Data for Attachments that have no <c>fileUrl</c>, in any of the Statements.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Statement ids, in the order of <paramref name="statements"/>.</returns>
    /// <exception cref="XapiValidationException">A Statement is invalid, or two share an id, and <see cref="XapiClientOptions.ValidateBeforeSend"/> is on.</exception>
    /// <exception cref="XapiConflictException">The LRS has a different Statement with the id of one in the batch.</exception>
    Task<IReadOnlyList<Guid>> SaveStatementsAsync(IReadOnlyList<Statement> statements, IReadOnlyList<AttachmentPayload>? attachments = null, CancellationToken cancellationToken = default);

    /// <summary>Fetches one Statement by id (<c>statementId</c>). Voided Statements are not returned.</summary>
    /// <param name="id">The Statement id.</param>
    /// <param name="format">How much of Agents, Activities and Verbs to return.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Statement, or <see langword="null"/> when the LRS answers 404.</returns>
    Task<Statement?> GetStatementAsync(Guid id, StatementFormat format = StatementFormat.Exact, CancellationToken cancellationToken = default);

    /// <summary>Fetches one voided Statement by id (<c>voidedStatementId</c>).</summary>
    /// <param name="id">The id of the voided Statement (not of the voiding one).</param>
    /// <param name="format">How much of Agents, Activities and Verbs to return.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Statement, or <see langword="null"/> when the LRS answers 404.</returns>
    Task<Statement?> GetVoidedStatementAsync(Guid id, StatementFormat format = StatementFormat.Exact, CancellationToken cancellationToken = default);

    /// <summary>Fetches the first page of a Statement query.</summary>
    /// <param name="query">Filters and options.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The page; follow <see cref="StatementResult.More"/> with <see cref="GetMoreStatementsAsync"/>.</returns>
    Task<StatementResult> QueryStatementsAsync(StatementQuery query, CancellationToken cancellationToken = default);

    /// <summary>Fetches the next page of a Statement query.</summary>
    /// <remarks>
    /// Only the path and query of <paramref name="more"/> are used; they are sent to the scheme, host and port of
    /// <see cref="XapiClientOptions.Endpoint"/>, so credentials never go to another host. A path without a leading
    /// slash is resolved against the endpoint.
    /// </remarks>
    /// <param name="more">The <see cref="StatementResult.More"/> of the previous page.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The page.</returns>
    Task<StatementResult> GetMoreStatementsAsync(Uri more, CancellationToken cancellationToken = default);

    /// <summary>Runs a Statement query and follows <c>more</c> until the last page, one page request at a time.</summary>
    /// <remarks>Attachment data is not returned; use <see cref="QueryStatementsAsync"/> for that.</remarks>
    /// <param name="query">Filters and options.</param>
    /// <param name="cancellationToken">Cancels the enumeration.</param>
    /// <returns>Every matching Statement.</returns>
    IAsyncEnumerable<Statement> QueryAllStatementsAsync(StatementQuery query, CancellationToken cancellationToken = default);

    /// <summary>Voids a Statement by storing a Statement with the <c>voided</c> Verb and a StatementRef to it.</summary>
    /// <remarks>IEEE 9274.1.1, 4.2.5. The voided Statement is then only returned by <see cref="GetVoidedStatementAsync"/>.</remarks>
    /// <param name="statementId">The id of the Statement to void.</param>
    /// <param name="actor">Who voids it.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The id of the voiding Statement.</returns>
    Task<Guid> VoidStatementAsync(Guid statementId, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>Fetches one State, Activity Profile or Agent Profile document.</summary>
    /// <param name="key">Which document.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The document with its ETag, or <see langword="null"/> when the LRS answers 404.</returns>
    Task<XapiDocument?> GetDocumentAsync(DocumentKey key, CancellationToken cancellationToken = default);

    /// <summary>Lists the State ids for an Activity and Agent (and registration).</summary>
    /// <param name="activityId">The Activity.</param>
    /// <param name="agent">The Agent.</param>
    /// <param name="registration">The registration, if the states are tied to one.</param>
    /// <param name="since">Only ids of documents stored or changed after this time.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The <c>stateId</c> values.</returns>
    Task<IReadOnlyList<string>> GetStateIdsAsync(Iri activityId, Agent agent, Guid? registration = null, DateTimeOffset? since = null, CancellationToken cancellationToken = default);

    /// <summary>Lists the Activity Profile ids for an Activity.</summary>
    /// <param name="activityId">The Activity.</param>
    /// <param name="since">Only ids of documents stored or changed after this time.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The <c>profileId</c> values.</returns>
    Task<IReadOnlyList<string>> GetActivityProfileIdsAsync(Iri activityId, DateTimeOffset? since = null, CancellationToken cancellationToken = default);

    /// <summary>Lists the Agent Profile ids for an Agent.</summary>
    /// <param name="agent">The Agent.</param>
    /// <param name="since">Only ids of documents stored or changed after this time.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The <c>profileId</c> values.</returns>
    Task<IReadOnlyList<string>> GetAgentProfileIdsAsync(Agent agent, DateTimeOffset? since = null, CancellationToken cancellationToken = default);

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
    Task PutDocumentAsync(DocumentKey key, XapiDocument document, DocumentPrecondition? precondition = null, CancellationToken cancellationToken = default);

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
    Task MergeDocumentAsync(DocumentKey key, JsonElement json, DocumentPrecondition? precondition = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes one document.</summary>
    /// <param name="key">Which document.</param>
    /// <param name="precondition">An <c>If-Match</c> condition, to delete only the version last read.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the LRS has deleted the document.</returns>
    /// <exception cref="XapiPreconditionFailedException">The precondition failed.</exception>
    Task DeleteDocumentAsync(DocumentKey key, DocumentPrecondition? precondition = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes every State document of an Activity and Agent (and registration).</summary>
    /// <param name="activityId">The Activity.</param>
    /// <param name="agent">The Agent.</param>
    /// <param name="registration">Only the states of this registration; all registrations when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the LRS has deleted the documents.</returns>
    Task DeleteStatesAsync(Iri activityId, Agent agent, Guid? registration = null, CancellationToken cancellationToken = default);

    /// <summary>Fetches the Person object the LRS knows for an Agent (Agents resource).</summary>
    /// <param name="agent">The Agent.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Person; at least the identifiers of <paramref name="agent"/>.</returns>
    Task<Person> GetPersonAsync(Agent agent, CancellationToken cancellationToken = default);

    /// <summary>Fetches the LRS's definition of an Activity (Activities resource).</summary>
    /// <param name="activityId">The Activity id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Activity, or <see langword="null"/> when the LRS answers 404.</returns>
    Task<Activity?> GetActivityAsync(Iri activityId, CancellationToken cancellationToken = default);

    /// <summary>Fetches the About resource: the xAPI versions the LRS supports, and extensions.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The About object.</returns>
    Task<About> GetAboutAsync(CancellationToken cancellationToken = default);
}

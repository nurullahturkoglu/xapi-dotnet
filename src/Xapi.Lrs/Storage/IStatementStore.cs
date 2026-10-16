using System.Diagnostics.CodeAnalysis;

namespace Xapi.Lrs.Storage;

/// <summary>Stores and reads statements. The default implementation uses <see cref="XapiLrsDbContext"/>.</summary>
[Experimental(XapiLrsDiagnostics.ExperimentalStorage)]
public interface IStatementStore
{
    /// <summary>
    /// Stores a batch atomically. A statement whose id is already stored is skipped when the stored statement
    /// is equivalent (IEEE 9274.1.1, 4.2 "Statement Immutability") and fails the whole batch otherwise.
    /// Storing also voids targets of voiding statements, records which actors, activities and statements each
    /// statement refers to, and merges activity and agent definitions when <paramref name="updateDefinitions"/>
    /// is set.
    /// </summary>
    /// <param name="statements">The prepared statements, with distinct ids.</param>
    /// <param name="updateDefinitions">Whether the caller may update canonical Activity and Agent definitions (the <c>define</c> scope).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see cref="StatementStoreResult.Stored"/>, or a conflict when nothing was written.</returns>
    Task<StatementStoreResult> StoreAsync(IReadOnlyList<StatementRecord> statements, bool updateDefinitions, CancellationToken cancellationToken = default);

    /// <summary>Reads one statement by id, whether or not it is voided.</summary>
    /// <param name="statementId">The statement id.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The statement, or <see langword="null"/> when no statement has that id.</returns>
    Task<StoredStatement?> GetAsync(Guid statementId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one page of the statements that match a query, ordered by storage key (stored time). Pages are
    /// keyed on the storage key, so statements stored while a client pages through the results neither repeat
    /// nor shift earlier pages.
    /// </summary>
    /// <param name="filter">The query.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The page, possibly empty.</returns>
    Task<StatementPage> QueryAsync(StatementFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the LRS's canonical form of Activities: the definitions merged from the statements that mentioned
    /// them (see <see cref="StoreAsync"/>), as returned by the <c>canonical</c> statement format.
    /// </summary>
    /// <param name="activityIds">The Activity ids.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The known Activities by id; unknown ids are left out.</returns>
    Task<IReadOnlyDictionary<Iri, Activity>> GetActivitiesAsync(IReadOnlyCollection<Iri> activityIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads what the store knows about the person behind an Agent, for the Agents Resource. The default
    /// implementation knows one persona per identifier: the Person holds the Agent's identifier and the latest
    /// name seen with it in a statement stored with the <c>define</c> scope.
    /// </summary>
    /// <param name="agent">The Agent. Only its identifier is used for the lookup.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The Person. For an Agent the store has never seen, a Person with only the Agent's identifier
    /// (IEEE 9274.1.1, 4.1.6.3).
    /// </returns>
    Task<Person> GetPersonAsync(Agent agent, CancellationToken cancellationToken = default);
}

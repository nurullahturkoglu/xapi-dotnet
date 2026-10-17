using System.Diagnostics.CodeAnalysis;

namespace Xapi.Lrs.Storage;

/// <summary>One page of statements returned by <see cref="IStatementStore.QueryAsync"/>.</summary>
[Experimental(XapiLrsDiagnostics.ExperimentalStorage, UrlFormat = XapiLrsDiagnostics.ExperimentalStorageUrl)]
public sealed class StatementPage
{
    /// <summary>Creates a page.</summary>
    /// <param name="statements">The statements, in the requested order.</param>
    /// <param name="continuationToken">See <see cref="ContinuationToken"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="continuationToken"/> is empty.</exception>
    public StatementPage(IReadOnlyList<StoredStatement> statements, string? continuationToken)
    {
        ArgumentNullException.ThrowIfNull(statements);
        if (continuationToken is { Length: 0 })
        {
            throw new ArgumentException("The continuation token must not be empty.", nameof(continuationToken));
        }

        Statements = statements;
        ContinuationToken = continuationToken;
    }

    /// <summary>The statements, in the requested order.</summary>
    public IReadOnlyList<StoredStatement> Statements { get; }

    /// <summary>
    /// When more statements match, an opaque token that marks the end of this page, to pass as
    /// <see cref="StatementFilter.ContinuationToken"/> with the same query for the next page;
    /// <see langword="null"/> on the last page. Only the store that returned it can read it.
    /// </summary>
    /// <remarks>
    /// The LRS hands the token to clients inside the <c>more</c> URL and reads it back from there, so it should be
    /// short. It is not secret and not protected: a client can read it and send a different one.
    /// </remarks>
    public string? ContinuationToken { get; }
}

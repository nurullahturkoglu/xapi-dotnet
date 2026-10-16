using System.Diagnostics.CodeAnalysis;

namespace Xapi.Lrs.Storage;

/// <summary>One page of statements returned by <see cref="IStatementStore.QueryAsync"/>.</summary>
[Experimental(XapiLrsDiagnostics.ExperimentalStorage)]
public sealed class StatementPage
{
    /// <summary>Creates a page.</summary>
    /// <param name="statements">The statements, in the requested order.</param>
    /// <param name="continuation">See <see cref="Continuation"/>.</param>
    public StatementPage(IReadOnlyList<StoredStatement> statements, Guid? continuation)
    {
        ArgumentNullException.ThrowIfNull(statements);
        Statements = statements;
        Continuation = continuation;
    }

    /// <summary>The statements, in the requested order.</summary>
    public IReadOnlyList<StoredStatement> Statements { get; }

    /// <summary>
    /// The storage key of the last statement on this page when more statements match, to pass as
    /// <see cref="StatementFilter.After"/> for the next page; <see langword="null"/> on the last page.
    /// </summary>
    public Guid? Continuation { get; }
}

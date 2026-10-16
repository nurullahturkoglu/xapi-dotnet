using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Xapi.Client.Outbox.Internal;

internal static class Guard
{
    public static T NotNull<T>([NotNull] T? value, [CallerArgumentExpression(nameof(value))] string? parameterName = null)
        where T : class
    {
#if NET
        ArgumentNullException.ThrowIfNull(value, parameterName);
        return value;
#else
        return value ?? throw new ArgumentNullException(parameterName);
#endif
    }

    public static Statement WithId(Statement statement, [CallerArgumentExpression(nameof(statement))] string? parameterName = null) =>
        NotNull(statement, parameterName).Id is null
            ? throw new ArgumentException("The Statement must have an id.", parameterName)
            : statement;
}

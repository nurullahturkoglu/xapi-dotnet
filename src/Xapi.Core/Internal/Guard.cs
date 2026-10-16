using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Xapi.Internal;

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

    public static Iri NotDefault(Iri value, [CallerArgumentExpression(nameof(value))] string? parameterName = null) =>
        value.IsEmpty ? throw new ArgumentException("The IRI must not be the default (empty) value.", parameterName) : value;
}

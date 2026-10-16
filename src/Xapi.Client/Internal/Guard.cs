using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Xapi.Client.Internal;

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

    public static string NotEmpty([NotNull] string? value, [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        NotNull(value, parameterName);
        return value.Length == 0 ? throw new ArgumentException("The value must not be empty.", parameterName) : value;
    }

    public static Iri NotDefault(Iri value, [CallerArgumentExpression(nameof(value))] string? parameterName = null) =>
        value.IsEmpty ? throw new ArgumentException("The IRI must not be the default (empty) value.", parameterName) : value;
}

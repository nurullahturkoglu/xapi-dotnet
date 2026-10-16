using System.Globalization;
using System.Text;
using Xapi.Json;

namespace Xapi.Client.Internal;

/// <summary>Builds a percent-encoded query string. Every value goes through <see cref="Uri.EscapeDataString(string)"/>.</summary>
internal sealed class QueryBuilder
{
    private readonly StringBuilder _text = new();

    public QueryBuilder Add(string name, string? value)
    {
        if (value is null)
        {
            return this;
        }

        _text.Append(_text.Length == 0 ? '?' : '&')
            .Append(name)
            .Append('=')
            .Append(Uri.EscapeDataString(value));
        return this;
    }

    public QueryBuilder Add(string name, Iri? value) => Add(name, value?.Value);

    public QueryBuilder Add(string name, Guid? value) => Add(name, value?.ToString("D"));

    public QueryBuilder Add(string name, DateTimeOffset? value) =>
        Add(name, value is { } v ? XapiTimestamp.Format(v) : null);

    public QueryBuilder Add(string name, int? value) =>
        Add(name, value?.ToString(CultureInfo.InvariantCulture));

    /// <summary>Booleans are JSON literals in xAPI (IEEE 9274.1.1, 4.1.6.1); <see langword="false"/> is the default and is left out.</summary>
    public QueryBuilder AddFlag(string name, bool value) => value ? Add(name, "true") : this;

    public QueryBuilder Add(string name, Actor? value, XapiVersion version) =>
        Add(name, value is null ? null : XapiJson.SerializeActor(value, version));

    public QueryBuilder Add(string name, StatementFormat format) => format switch
    {
        StatementFormat.Exact => this,
        StatementFormat.Ids => Add(name, "ids"),
        StatementFormat.Canonical => Add(name, "canonical"),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown statement format."),
    };

    public override string ToString() => _text.ToString();
}

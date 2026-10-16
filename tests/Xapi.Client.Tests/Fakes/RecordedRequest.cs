using System.Text;

namespace Xapi.Client.Tests.Fakes;

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string? ContentType, byte[]? Body)
{
    /// <summary>The URI without the query.</summary>
    public string Path => Uri.GetLeftPart(UriPartial.Path);

    /// <summary>The query exactly as sent, percent-encoding included.</summary>
    public string RawQuery => Uri.Query.Length > 0 ? Uri.Query.Substring(1) : string.Empty;

    public string BodyText => Body is null ? string.Empty : Encoding.UTF8.GetString(Body);

    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;

    /// <summary>The decoded query parameters, in order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> QueryParameters =>
        [.. RawQuery.Split(['&'], StringSplitOptions.RemoveEmptyEntries).Select(pair =>
        {
            var parts = pair.Split(['='], 2);
            return new KeyValuePair<string, string>(Uri.UnescapeDataString(parts[0]), Uri.UnescapeDataString(parts[1]));
        })];

    public string? Query(string name) => QueryParameters.Where(p => p.Key == name).Select(p => p.Value).SingleOrDefault();
}

using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Xapi.Lrs.Http;

internal static class RequestReading
{
    // IEEE 9274.1.1, 4.1.5: reject parameters the LRS does not recognize, and known parameters spelled in
    // another case. ASP.NET Core merges query keys case-insensitively, so a name in two spellings shows up as
    // one key with two values.
    public static string? CheckParameters(IQueryCollection query, IReadOnlyCollection<string> allowed)
    {
        foreach (var (name, values) in query)
        {
            if (!allowed.Contains(name))
            {
                var match = allowed.FirstOrDefault(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
                return match is null
                    ? $"The parameter '{name}' is not recognized here."
                    : $"The parameter '{name}' must be spelled '{match}'.";
            }

            if (values.Count != 1)
            {
                return $"The parameter '{name}' must appear exactly once.";
            }
        }

        return null;
    }

    // UUIDs must be in the standard string form (4.2.7)
    public static bool TryParseUuid(string? value, out Guid uuid) => Guid.TryParseExact(value, "D", out uuid);

    public static bool IsMediaType(HttpRequest request, string mediaType) =>
        MediaTypeHeaderValue.TryParse(request.ContentType, out var parsed) &&
        parsed.MediaType.Equals(mediaType, StringComparison.OrdinalIgnoreCase);

    public static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }
}

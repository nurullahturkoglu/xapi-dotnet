using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Xapi.Lrs.Storage;

namespace Xapi.Lrs.Http;

// The If-Match and If-None-Match headers of a document request (IEEE 9274.1.1, 4.1.4; RFC 9110, 13.1.1 and
// 13.1.2). Unquoted tags are accepted as well, since some clients send the bare SHA-1 they computed.
internal sealed class DocumentPreconditions
{
    private DocumentPreconditions(EntityTagList? ifMatch, EntityTagList? ifNoneMatch)
    {
        IfMatch = ifMatch;
        IfNoneMatch = ifNoneMatch;
    }

    public EntityTagList? IfMatch { get; }

    public EntityTagList? IfNoneMatch { get; }

    public bool Any => IfMatch is not null || IfNoneMatch is not null;

    public static DocumentPreconditions From(HttpRequest request) => new(
        EntityTagList.Parse(request.Headers[HeaderNames.IfMatch]),
        EntityTagList.Parse(request.Headers[HeaderNames.IfNoneMatch]));

    // Evaluated in the order of RFC 9110, 13.2.2. If-Match compares strongly, If-None-Match weakly.
    public bool HoldFor(StoredDocument? current)
    {
        if (IfMatch is { } ifMatch && !(current is not null && (ifMatch.IsAny || ifMatch.Contains(current.ETag, weak: false))))
        {
            return false;
        }

        return IfNoneMatch is not { } ifNoneMatch || current is null || !(ifNoneMatch.IsAny || ifNoneMatch.Contains(current.ETag, weak: true));
    }
}

internal sealed class EntityTagList
{
    private readonly List<(string Tag, bool IsWeak)> _tags;

    private EntityTagList(bool isAny, List<(string Tag, bool IsWeak)> tags)
    {
        IsAny = isAny;
        _tags = tags;
    }

    public bool IsAny { get; }

    public bool Contains(string etag, bool weak) =>
        _tags.Exists(t => (weak || !t.IsWeak) && string.Equals(t.Tag, etag, StringComparison.Ordinal));

    // Null when the header is absent or empty
    public static EntityTagList? Parse(StringValues values)
    {
        var text = string.Join(',', values.Where(v => !string.IsNullOrWhiteSpace(v))).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (text == "*")
        {
            return new EntityTagList(isAny: true, []);
        }

        var tags = new List<(string, bool)>();
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] is ',' or ' ' or '\t')
            {
                i++;
                continue;
            }

            var weak = false;
            if (string.CompareOrdinal(text, i, "W/", 0, 2) == 0)
            {
                weak = true;
                i += 2;
            }

            int end;
            string tag;
            if (i < text.Length && text[i] == '"')
            {
                end = text.IndexOf('"', i + 1);
                end = end < 0 ? text.Length : end;
                tag = text[(i + 1)..end];
                i = end + 1;
            }
            else
            {
                end = text.IndexOf(',', i);
                end = end < 0 ? text.Length : end;
                tag = text[i..end].Trim();
                i = end;
            }

            tags.Add((tag, weak));
        }

        return new EntityTagList(isAny: false, tags);
    }
}

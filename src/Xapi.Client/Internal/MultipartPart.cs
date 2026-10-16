namespace Xapi.Client.Internal;

/// <summary>One part of a multipart body: its headers and its raw bytes.</summary>
internal sealed record MultipartPart(IReadOnlyDictionary<string, string> Headers, ReadOnlyMemory<byte> Body)
{
    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}

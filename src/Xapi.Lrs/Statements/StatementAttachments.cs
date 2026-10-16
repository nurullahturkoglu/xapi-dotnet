using Xapi.Lrs.Storage;

namespace Xapi.Lrs.Statements;

// One attachment part of a multipart/mixed statement request. ContentType is null when the part had none.
internal sealed record AttachmentPart(string Sha2, string? ContentType, byte[] Data);

// Matches attachment parts to the Attachment objects of a batch (IEEE 9274.1.1, 4.1.3).
internal static class StatementAttachments
{
    /// <summary>
    /// Returns, for each statement, the attachment data it carries, or an error when an Attachment object has
    /// neither a fileUrl nor a part, or a part belongs to no Attachment object.
    /// </summary>
    public static string? Match(IReadOnlyList<Statement> statements, IReadOnlyDictionary<string, AttachmentPart> parts, out List<IReadOnlyList<AttachmentContent>> perStatement)
    {
        perStatement = new List<IReadOnlyList<AttachmentContent>>(statements.Count);
        var contents = new Dictionary<string, AttachmentContent>(StringComparer.Ordinal);
        foreach (var statement in statements)
        {
            var carried = new List<AttachmentContent>();
            foreach (var attachment in AllAttachments(statement))
            {
                var sha2 = AttachmentHash.Normalize(attachment.Sha2);
                if (parts.TryGetValue(sha2, out var part))
                {
                    if (!contents.TryGetValue(sha2, out var content))
                    {
                        // A part without its own Content-Type takes the one the Attachment object declares
                        content = new AttachmentContent(sha2, part.ContentType ?? NonEmpty(attachment.ContentType), part.Data);
                        contents[sha2] = content;
                    }

                    carried.Add(content);
                }
                else if (attachment.FileUrl is null)
                {
                    return parts.Count == 0
                        ? $"The Attachment with sha2 {attachment.Sha2} has no fileUrl; send its data as a part of a multipart/mixed request."
                        : $"The Attachment with sha2 {attachment.Sha2} has no fileUrl and no part with that X-Experience-API-Hash was sent.";
                }
            }

            perStatement.Add(carried);
        }

        foreach (var sha2 in parts.Keys)
        {
            if (!contents.ContainsKey(sha2))
            {
                return $"The part with X-Experience-API-Hash {sha2} matches no Attachment object of the statements.";
            }
        }

        return null;
    }

    // SubStatements may carry Attachments of their own; their data travels in the same request
    private static IEnumerable<Attachment> AllAttachments(Statement statement) =>
        statement.Object is SubStatement sub ? statement.Attachments.Concat(sub.Attachments) : statement.Attachments;

    private static string NonEmpty(string contentType) =>
        string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;
}

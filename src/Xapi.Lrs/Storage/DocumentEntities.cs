namespace Xapi.Lrs.Storage;

// Rows of the three document tables. The key columns differ per resource; the content columns are shared.
internal abstract class DocumentEntity
{
    // stateId or profileId
    public string DocumentId { get; set; } = "";

    public string ContentType { get; set; } = "";

    public byte[] Content { get; set; } = [];

    // SHA-1 of Content in lower-case hex (ADR-0012); also the EF Core concurrency token
    public string ETag { get; set; } = "";

    public DateTimeOffset Updated { get; set; }
}

internal sealed class StateDocumentEntity : DocumentEntity
{
    public string ActivityIri { get; set; } = "";

    public string AgentIfi { get; set; } = "";

    // Guid.Empty for a document stored without a registration: key columns cannot be null (ADR-0012)
    public Guid Registration { get; set; }
}

internal sealed class ActivityProfileEntity : DocumentEntity
{
    public string ActivityIri { get; set; } = "";
}

internal sealed class AgentProfileEntity : DocumentEntity
{
    public string AgentIfi { get; set; } = "";
}

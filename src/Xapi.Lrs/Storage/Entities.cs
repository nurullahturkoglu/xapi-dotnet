namespace Xapi.Lrs.Storage;

// Table shapes follow the design's storage schema (adapted from lrsql): the statement is stored once as JSON
// and every filter runs on narrow link tables keyed by the ordering key.

internal sealed class StatementEntity
{
    // Monotonic UUIDv7; its time part is the statement's stored time. Ordering key and paging cursor.
    public Guid Pk { get; set; }

    public Guid StatementId { get; set; }

    public DateTimeOffset Stored { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public string VerbIri { get; set; } = "";

    public Guid? Registration { get; set; }

    // object.id when the object is a StatementRef: voiding and "statements targeting a match"
    public Guid? RefStatementId { get; set; }

    public bool IsVoided { get; set; }

    // The statement's version property as stored ("1.0.0", "1.0.3", "2.0.0", ...)
    public string Version { get; set; } = "";

    // Identifier of the authority, for statements/read/mine
    public string? AuthorityKey { get; set; }

    // The exact JSON returned for format=exact
    public string Payload { get; set; } = "";
}

internal sealed class StatementActorEntity
{
    public Guid StatementPk { get; set; }

    public short Usage { get; set; }

    public string ActorIfi { get; set; } = "";

    public short ActorType { get; set; }
}

internal sealed class StatementActivityEntity
{
    public Guid StatementPk { get; set; }

    public short Usage { get; set; }

    public string ActivityIri { get; set; } = "";
}

internal sealed class StatementRefEntity
{
    // The referencing statement
    public Guid AncestorPk { get; set; }

    // A statement id it targets directly or through other StatementRefs
    public Guid DescendantId { get; set; }
}

internal sealed class ActorEntity
{
    public string Ifi { get; set; } = "";

    public short Type { get; set; }

    public string Payload { get; set; } = "";
}

internal sealed class ActivityEntity
{
    public string Iri { get; set; } = "";

    // Canonical Activity JSON, merged from the definitions in stored statements
    public string Payload { get; set; } = "";
}

// Attachment data, content-addressed: one row per SHA-2 however many statements carry it
internal sealed class AttachmentContentEntity
{
    // Lower-case hex
    public string Sha2 { get; set; } = "";

    public string ContentType { get; set; } = "";

    public long Length { get; set; }

    public byte[] Content { get; set; } = [];
}

internal sealed class StatementAttachmentEntity
{
    public Guid StatementPk { get; set; }

    public string Sha2 { get; set; } = "";
}

internal sealed class CredentialEntity
{
    public string Key { get; set; } = "";

    public string SecretHash { get; set; } = "";

    // Space-separated scope names
    public string Scopes { get; set; } = "";

    public string AuthorityPayload { get; set; } = "";
}

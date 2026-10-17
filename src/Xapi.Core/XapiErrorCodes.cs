namespace Xapi;

/// <summary>
/// The stable error codes used in <see cref="XapiError.Code"/>. Codes name the rule; <see cref="XapiError.Path"/>
/// says where it was broken. Section numbers refer to IEEE 9274.1.1 (LRS book unless stated).
/// </summary>
public static class XapiErrorCodes
{
    /// <summary>The input is not well-formed JSON, or has content after the first value.</summary>
    public const string JsonSyntax = "json.syntax";

    /// <summary>A value has the wrong JSON type, for example a string where a number or an object is required (4.2.1).</summary>
    public const string JsonType = "json.type";

    /// <summary>An explicit <c>null</c> outside extension values (4.2.1). Strict mode only.</summary>
    public const string JsonNull = "json.null";

    /// <summary>A property the standard does not define at this location (4.2.1). Strict mode only.</summary>
    public const string PropertyUnknown = "json.property.unknown";

    /// <summary>A property name that matches a defined property only when case is ignored (4.2.1). Strict mode only.</summary>
    public const string PropertyCase = "json.property.case";

    /// <summary>The same property appears more than once in one object (4.2.1). Strict mode only.</summary>
    public const string PropertyDuplicate = "json.property.duplicate";

    /// <summary>A property that exists only in a later xAPI version, such as <c>contextAgents</c> under 1.0.3.</summary>
    public const string PropertyVersion = "json.property.version";

    /// <summary>A required property is missing.</summary>
    public const string PropertyRequired = "json.property.required";

    /// <summary>Not an absolute IRI, or the default (empty) <see cref="Iri"/> (4.2.7 "IRIs").</summary>
    public const string Iri = "format.iri";

    /// <summary>Not a UUID in the standard 8-4-4-4-12 hexadecimal form (4.2.7 "UUIDs").</summary>
    public const string Uuid = "format.uuid";

    /// <summary>Not an RFC 3339 timestamp with a UTC offset (4.2.7 "Timestamps").</summary>
    public const string Timestamp = "format.timestamp";

    /// <summary>Not an ISO 8601:2004 4.4.3.2 duration (4.2.7 "Duration").</summary>
    public const string Duration = "format.duration";

    /// <summary>Not a well-formed RFC 5646 language tag (4.2.7 "Language Maps", <c>context.language</c>).</summary>
    public const string LanguageTag = "format.language-tag";

    /// <summary>Not a <c>mailto:</c> IRI with an e-mail address (4.2.2.1).</summary>
    public const string Mbox = "format.mbox";

    /// <summary>Not a 40-character hexadecimal SHA-1 digest (<c>mbox_sha1sum</c>, 4.2.2.1).</summary>
    public const string Sha1 = "format.sha1";

    /// <summary>Not a hexadecimal SHA-2 digest (<c>sha2</c>, 4.2.2.6).</summary>
    public const string Sha2 = "format.sha2";

    /// <summary>Not an Internet media type (<c>contentType</c>, 4.2.2.6).</summary>
    public const string MediaType = "format.media-type";

    /// <summary>A statement <c>version</c> the profile does not accept: 1.0.x for 1.0.3; 1.0.x or 2.0.0 for 2.0.0.</summary>
    public const string Version = "format.version";

    /// <summary>An <c>objectType</c> value that is not allowed at this location, or has the wrong case (4.2.1).</summary>
    public const string ObjectType = "format.object-type";

    /// <summary>An <c>interactionType</c> outside the controlled vocabulary (4.2.2.3).</summary>
    public const string InteractionType = "format.interaction-type";

    /// <summary>An Agent or identified Group without an inverse functional identifier (4.2.2.1).</summary>
    public const string IfiMissing = "actor.ifi.missing";

    /// <summary>An Agent or Group with more than one inverse functional identifier (4.2.4.2).</summary>
    public const string IfiMultiple = "actor.ifi.multiple";

    /// <summary>An anonymous Group without members (4.2.2.1, "member" is required).</summary>
    public const string GroupMemberMissing = "group.member.missing";

    /// <summary>A Group used inside the <c>member</c> property of a Group (4.2.4.2).</summary>
    public const string GroupMemberNotAgent = "group.member.not-agent";

    /// <summary>
    /// A Group <c>authority</c> that is not an anonymous Group of exactly two Agents (the OAuth 3-legged form of
    /// xAPI 1.0.3, Data 2.4.9; still tested by the conformance suite for 2.0).
    /// </summary>
    public const string AuthorityGroup = "authority.group";

    /// <summary>A Statement using the <c>voided</c> Verb whose object is not a StatementRef (4.2.5).</summary>
    public const string VoidingObject = "statement.voiding.object";

    /// <summary>A SubStatement whose object is another SubStatement (4.2.4.2).</summary>
    public const string SubStatementNested = "substatement.nested";

    /// <summary>
    /// <c>context.revision</c> on a Statement whose object is not an Activity. A 1.0.3 rule (Data 2.4.6) that the
    /// conformance suite also applies to 2.0 (ADL CTS issue #278); enforced in both versions.
    /// </summary>
    public const string ContextRevision = "context.revision.object";

    /// <summary><c>context.platform</c> on a Statement whose object is not an Activity. Same background as <see cref="ContextRevision"/>.</summary>
    public const string ContextPlatform = "context.platform.object";

    /// <summary><c>relevantTypes</c> present but empty; it must hold one or more IRIs (4.2.2.5).</summary>
    public const string RelevantTypesEmpty = "context.relevant-types.empty";

    /// <summary><c>contextAgents</c> or <c>contextGroups</c> used with the 1.0.3 profile.</summary>
    public const string ContextVersion = "context.version";

    /// <summary><c>score.scaled</c> outside -1..1 (4.2.2.4).</summary>
    public const string ScoreScaled = "score.scaled.range";

    /// <summary><c>score.raw</c> below <c>min</c> or above <c>max</c> (4.2.2.4).</summary>
    public const string ScoreRaw = "score.raw.range";

    /// <summary><c>score.min</c> not less than <c>score.max</c> (4.2.2.4).</summary>
    public const string ScoreMinMax = "score.min-max";

    /// <summary>A score value that is NaN or infinite.</summary>
    public const string ScoreNotFinite = "score.not-finite";

    /// <summary>Interaction properties (<c>correctResponsesPattern</c>, component lists) without an <c>interactionType</c> (4.2.4.2).</summary>
    public const string InteractionTypeMissing = "interaction.type.missing";

    /// <summary>An interaction component list that the <c>interactionType</c> does not support (4.2.2.3).</summary>
    public const string InteractionComponentsNotAllowed = "interaction.components.not-allowed";

    /// <summary>Two interaction components in the same list share an <c>id</c>.</summary>
    public const string InteractionComponentDuplicate = "interaction.component.duplicate";

    /// <summary>An attachment <c>length</c> below zero.</summary>
    public const string AttachmentLength = "attachment.length.negative";

    /// <summary>
    /// An Attachment without a <c>fileUrl</c> and without attachment data of the same SHA-2 to send with it
    /// (4.1.3). Reported by <c>Xapi.Client</c> and <c>Xapi.Client.Outbox</c> before sending, not by the
    /// statement validator; <see cref="XapiError.Path"/> is the Attachment, for example <c>$.attachments[0]</c>.
    /// </summary>
    public const string AttachmentPayloadMissing = "attachment.payload.missing";

    /// <summary>
    /// Attachment data that no Attachment of the Statements refers to by SHA-2 (4.1.3). Reported by
    /// <c>Xapi.Client</c> before sending. The data is not part of the JSON, so <see cref="XapiError.Path"/> is its
    /// index in the list of attachment data passed with the Statements, for example <c>attachments[0]</c>, without
    /// the <c>$</c> of a JSON path.
    /// </summary>
    public const string AttachmentPayloadUnreferenced = "attachment.payload.unreferenced";

    /// <summary>Two statements in one batch share an <c>id</c> (4.1.6.1).</summary>
    public const string BatchDuplicateId = "batch.id.duplicate";
}

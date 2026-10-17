namespace Xapi.Client;

/// <summary>Settings for <see cref="XapiClient"/>. The client copies them when it is created.</summary>
public sealed class XapiClientOptions
{
    /// <summary>
    /// The LRS endpoint, the base IRL that the resource names (<c>statements</c>, <c>activities/state</c>, …) are
    /// appended to, for example <c>https://lrs.example.com/xapi/</c>. A missing trailing slash is added.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// The xAPI version to speak. When <see langword="null"/> (the default), the client asks the About resource
    /// once, before its first request, and uses the highest version both sides support: 2.0.0, then 1.0.3.
    /// </summary>
    public XapiVersion? Version { get; set; }

    /// <summary>
    /// The credentials sent in the <c>Authorization</c> header of every request, or <see langword="null"/> to send
    /// none (for example when a <see cref="System.Net.Http.DelegatingHandler"/> adds them).
    /// </summary>
    public XapiCredentials? Credentials { get; set; }

    /// <summary>
    /// Whether Statements are validated with <see cref="Validation.StatementValidator"/> before they are sent.
    /// Defaults to <see langword="true"/>. Invalid Statements then fail with <see cref="XapiValidationException"/>
    /// without a request, instead of a 400 from the LRS.
    /// </summary>
    /// <remarks>
    /// The check also matches attachment data with the Statements: every Attachment without a <c>fileUrl</c> needs
    /// an <see cref="AttachmentPayload"/> with the same SHA-2 (<see cref="XapiErrorCodes.AttachmentPayloadMissing"/>),
    /// and every payload must belong to an Attachment (<see cref="XapiErrorCodes.AttachmentPayloadUnreferenced"/>).
    /// </remarks>
    public bool ValidateBeforeSend { get; set; } = true;
}

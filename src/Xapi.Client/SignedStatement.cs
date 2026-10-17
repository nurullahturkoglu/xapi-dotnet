using Xapi.Client.Internal;

namespace Xapi.Client;

/// <summary>A Statement signed by <see cref="StatementSigner"/>, ready to send with its signature.</summary>
/// <example>
/// <code>
/// var signed = StatementSigner.Sign(statement, rsa, XapiVersion.V2_0_0);
/// await client.SaveStatementAsync(signed.Statement, [signed.Signature]);
/// </code>
/// </example>
public sealed record SignedStatement
{
    private readonly Statement _statement;
    private readonly AttachmentPayload _signature;

    /// <summary>Creates a signed Statement.</summary>
    /// <param name="statement">The Statement with the signature Attachment object added to its <see cref="Statement.Attachments"/>.</param>
    /// <param name="signature">The JWS, to send as attachment data with the Statement.</param>
    public SignedStatement(Statement statement, AttachmentPayload signature)
    {
        _statement = Guard.NotNull(statement);
        _signature = Guard.NotNull(signature);
    }

    /// <summary>The Statement with the signature Attachment object added to its <see cref="Statement.Attachments"/>.</summary>
    public Statement Statement
    {
        get => _statement;
        init => _statement = Guard.NotNull(value);
    }

    /// <summary>The JWS, to send as attachment data with the Statement.</summary>
    public AttachmentPayload Signature
    {
        get => _signature;
        init => _signature = Guard.NotNull(value);
    }
}

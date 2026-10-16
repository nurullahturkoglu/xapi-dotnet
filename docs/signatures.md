# Signed statements

A statement can carry a digital signature: a JSON Web Signature (JWS, RFC 7515) of the statement, sent as an
attachment (IEEE 9274.1.1, 4.2.6 "Statement Signing"; xAPI 1.0.3 Data 2.6). This page covers signing with
`Xapi.Client` and what the `Xapi.Lrs` LRS checks.

## Signing

`StatementSigner.Sign` takes the complete statement, an RSA private key and the xAPI version, and returns the
statement with a signature Attachment object added, plus the signature itself as an `AttachmentPayload`:

```csharp
using var certificate = X509CertificateLoader.LoadPkcs12FromFile("signer.pfx", password);
using var key = certificate.GetRSAPrivateKey()!;

var signed = StatementSigner.Sign(statement, key, XapiVersion.V2_0_0, HashAlgorithmName.SHA256, [certificate]);
await client.SaveStatementAsync(signed.Statement, [signed.Signature]);
```

- The JWS uses the Compact Serialization. The algorithm is RS256, RS384 or RS512, chosen by the hash
  algorithm (SHA-256 by default).
- The payload is the statement exactly as `XapiJson` serializes it for that version, before the signature
  attachment is added. Other attachments of the statement are part of the payload.
- The attachment has usage type `http://adlnet.gov/expapi/attachments/signature` and content type
  `application/octet-stream`, as the standard requires.
- When a certificate chain is given, it goes in the JWS `x5c` header, signer's certificate first. The standard
  recommends this, so that a reader can find the signer's key.

Set the statement `id` before signing if you want to refer to the statement later; the LRS assigns one
otherwise, and the comparison below ignores it.

`StatementSigner` is in `Xapi.Client` because its result is an `AttachmentPayload`, the type the client sends
attachment data with. It uses only `System.Security.Cryptography`, so it works on every target of the package,
Unity and Native AOT included.

## What the LRS checks

For every attachment with the signature usage type that arrives as a part of a `multipart/mixed` request, the
LRS rejects the request with 400 Bad Request when:

- the attachment's content type is not `application/octet-stream`;
- the data is not a JWS in Compact Serialization, a segment is not base64url, or the header is not a JSON
  object;
- the algorithm is not RS256, RS384 or RS512, or the header lists critical extensions (`crit`);
- the payload is not a valid statement for the request's xAPI version;
- the signed statement is not equivalent to the statement it is attached to. The comparison is the one the
  standard uses for statement immutability (4.2): `id`, `stored`, `timestamp`, `authority`, `version`,
  `attachments` and Activity definitions are ignored, case is ignored, and durations are compared to 0.01
  second (4.2.7);
- the header has an `x5c` certificate and the signature does not verify with that certificate's RSA key.

## Limitations

- The `x5c` chain is not validated against trusted roots, and a signature without `x5c` is checked for form
  only. The standard describes these checks as a way to catch mistakes, not as authentication; deciding
  whether to trust the signer is up to whoever reads the statement. Clients must not assume a signature is
  valid because an LRS accepted it (xAPI 1.0.3 Data 2.6).
- A signature attachment that is only referenced by `fileUrl` is not fetched, so it is not checked.
- Only the JWS Compact Serialization is supported; the standard discourages the JSON Serialization.

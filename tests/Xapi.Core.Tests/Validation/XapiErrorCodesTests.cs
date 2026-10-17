using System.Reflection;
using System.Text.RegularExpressions;

namespace Xapi.Core.Tests.Validation;

public class XapiErrorCodesTests
{
    private static readonly string[] Codes = [.. typeof(XapiErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral)
        .Select(f => (string)f.GetRawConstantValue()!)];

    [Fact]
    public void Codes_are_unique_lowercase_and_dotted()
    {
        Assert.Equal(Codes.Length, Codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(Codes, code => Assert.Matches(new Regex("^[a-z0-9-]+(\\.[a-z0-9-]+)+$"), code));
    }

    // Codes are stable; these two shipped as string literals in Xapi.Client and Xapi.Client.Outbox
    [Fact]
    public void Attachment_payload_codes_keep_their_values()
    {
        Assert.Equal("attachment.payload.missing", XapiErrorCodes.AttachmentPayloadMissing);
        Assert.Equal("attachment.payload.unreferenced", XapiErrorCodes.AttachmentPayloadUnreferenced);
    }
}

using Xapi.Lrs.Storage;

namespace Xapi.Lrs.Telemetry;

// Tag names and values shared by the metrics and the activities (documented in docs/lrs.md)
internal static class XapiTags
{
    public const string Resource = "xapi.resource";
    public const string Version = "xapi.version";
    public const string StatementCount = "xapi.statements.count";
    public const string StatementConflict = "xapi.statements.conflict";
    public const string DocumentOperation = "xapi.document.operation";

    // A value outside the known set, as OpenTelemetry spells it for http.request.method
    public const string Other = "_OTHER";

    public static string ResourceOf(DocumentResource resource) => resource switch
    {
        DocumentResource.State => "activities/state",
        DocumentResource.ActivityProfile => "activities/profile",
        DocumentResource.AgentProfile => "agents/profile",
        _ => Other,
    };
}

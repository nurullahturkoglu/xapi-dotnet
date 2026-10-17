using System.Diagnostics;
using Xapi.Lrs.Storage;

namespace Xapi.Lrs.Telemetry;

// The Xapi.Lrs activity source (ADR-014). StartActivity returns null, at the cost of one check, when nothing
// listens. Activities nest under ASP.NET Core's request activity, and EF Core's database activities under them.
// System.Diagnostics.Activity is spelled out: Xapi.Activity, the xAPI object, wins over the using directive.
internal static class XapiLrsActivities
{
    public static readonly ActivitySource Source = new(XapiLrsDiagnostics.ActivitySourceName);

    public static System.Diagnostics.Activity? StartStoreStatements(int count, XapiVersion version)
    {
        var activity = Source.StartActivity(XapiLrsDiagnostics.StoreStatementsActivity);
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag(XapiTags.Version, version.HeaderValue);
            activity.SetTag(XapiTags.StatementCount, count);
        }

        return activity;
    }

    public static System.Diagnostics.Activity? StartQueryStatements(XapiVersion version)
    {
        var activity = Source.StartActivity(XapiLrsDiagnostics.QueryStatementsActivity);
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag(XapiTags.Version, version.HeaderValue);
        }

        return activity;
    }

    // operation: replace (PUT), merge (POST) or delete
    public static System.Diagnostics.Activity? StartWriteDocument(DocumentResource resource, string operation)
    {
        var activity = Source.StartActivity(XapiLrsDiagnostics.WriteDocumentActivity);
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag(XapiTags.Resource, XapiTags.ResourceOf(resource));
            activity.SetTag(XapiTags.DocumentOperation, operation);
        }

        return activity;
    }

    // For an exception filter: marks the activity as failed and lets the exception through. Only the type is
    // recorded; database messages can quote stored values.
    public static bool Failed(System.Diagnostics.Activity? activity, Exception exception)
    {
        if (activity is not null)
        {
            activity.SetStatus(ActivityStatusCode.Error);
            activity.SetTag("error.type", exception.GetType().FullName);
        }

        return false;
    }
}

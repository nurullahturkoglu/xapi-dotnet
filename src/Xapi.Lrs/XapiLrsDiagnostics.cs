namespace Xapi.Lrs;

/// <summary>
/// The names of the LRS's traces and metrics, for OpenTelemetry's <c>AddSource</c> and <c>AddMeter</c> or a
/// <see cref="System.Diagnostics.ActivityListener"/> or <see cref="System.Diagnostics.Metrics.MeterListener"/>.
/// The names are part of the public contract: they change only in a major release.
/// </summary>
/// <remarks>docs/lrs.md lists the tags of each instrument and activity.</remarks>
public static class XapiLrsDiagnostics
{
    /// <summary>The name of the <see cref="System.Diagnostics.ActivitySource"/> of the LRS: <c>Xapi.Lrs</c>.</summary>
    public const string ActivitySourceName = "Xapi.Lrs";

    /// <summary>The name of the <see cref="System.Diagnostics.Metrics.Meter"/> of the LRS: <c>Xapi.Lrs</c>.</summary>
    public const string MeterName = "Xapi.Lrs";

    /// <summary>
    /// Counter of the Statements accepted by Statement PUT and POST requests (unit <c>{statement}</c>). A Statement
    /// sent again that is equivalent to the stored one is accepted and counted again.
    /// </summary>
    public const string StatementsStored = "xapi.lrs.statements.stored";

    /// <summary>Histogram of the time the LRS endpoints take to handle a request, in seconds (unit <c>s</c>).</summary>
    public const string RequestDuration = "xapi.lrs.request.duration";

    /// <summary>Counter of the requests the LRS endpoints answered with a 4xx or 5xx status (unit <c>{error}</c>).</summary>
    public const string Errors = "xapi.lrs.errors";

    /// <summary>The activity that stores a batch of Statements.</summary>
    public const string StoreStatementsActivity = "xapi.lrs.statements.store";

    /// <summary>The activity that reads a page of Statements for a query.</summary>
    public const string QueryStatementsActivity = "xapi.lrs.statements.query";

    /// <summary>The activity that writes or deletes a State, Activity Profile or Agent Profile document.</summary>
    public const string WriteDocumentActivity = "xapi.lrs.documents.write";

    // [Experimental] id of the storage seams (ADR-011): public for the provider packages and custom stores, but
    // not covered by semantic versioning, also after 1.0, until each seam is declared stable.
    internal const string ExperimentalStorage = "XAPI0001";

    // UrlFormat of the [Experimental] attributes: the compiler links the XAPI0001 error to this page
    internal const string ExperimentalStorageUrl = "https://nurullahturkoglu.github.io/xapi-dotnet/lrs.html#experimental-storage-api";
}

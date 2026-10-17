using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Xapi.Lrs.Telemetry;

// The instruments of the Xapi.Lrs meter (ADR-014), created once per service provider through IMeterFactory. Tag
// values come from small fixed sets; nothing taken from a request (agents, IRIs, ids, credential keys) is a tag.
internal sealed class XapiLrsMetrics
{
    // The buckets ASP.NET Core gives http.server.request.duration, so the two histograms line up
    private static readonly double[] DurationBuckets = [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10];

    private readonly Counter<long> _statementsStored;
    private readonly Histogram<double> _requestDuration;
    private readonly Counter<long> _errors;

    public XapiLrsMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(XapiLrsDiagnostics.MeterName);
        _statementsStored = meter.CreateCounter<long>(
            XapiLrsDiagnostics.StatementsStored,
            unit: "{statement}",
            description: "Statements accepted by Statement PUT and POST requests.");
        _requestDuration = meter.CreateHistogram(
            XapiLrsDiagnostics.RequestDuration,
            unit: "s",
            description: "Time the LRS endpoints take to handle a request.",
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBuckets });
        _errors = meter.CreateCounter<long>(
            XapiLrsDiagnostics.Errors,
            unit: "{error}",
            description: "Requests the LRS endpoints answered with a 4xx or 5xx status.");
    }

    public bool MeasuresRequests => _requestDuration.Enabled || _errors.Enabled;

    public void StatementsStored(int count, XapiVersion version)
    {
        if (_statementsStored.Enabled)
        {
            _statementsStored.Add(count, new KeyValuePair<string, object?>(XapiTags.Version, version.HeaderValue));
        }
    }

    // exceptionType: the exception that ended the request, when it did not end with a response the LRS wrote
    public void RequestCompleted(HttpContext http, string resource, long startTimestamp, string? exceptionType)
    {
        var status = http.Response.StatusCode;
        var tags = new TagList
        {
            { XapiTags.Resource, resource },
            { "http.request.method", Method(http.Request.Method) },
            { "http.response.status_code", status },
        };
        if (http.Features.Get<Http.XapiRequestFeature>() is { } request)
        {
            tags.Add(XapiTags.Version, request.Version.HeaderValue);
        }

        if (_requestDuration.Enabled)
        {
            // As http.server.request.duration: a 4xx is the client's error, not the server's
            var durationTags = tags;
            if ((exceptionType ?? (status >= 500 ? Status(status) : null)) is { } errorType)
            {
                durationTags.Add("error.type", errorType);
            }

            _requestDuration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, durationTags);
        }

        if (_errors.Enabled && (status >= 400 || exceptionType is not null))
        {
            tags.Add("error.type", exceptionType ?? Status(status));
            _errors.Add(1, tags);
        }
    }

    // OpenTelemetry semantic conventions: known methods by name, anything else as _OTHER
    private static string Method(string method) =>
        HttpMethods.IsGet(method) ? HttpMethods.Get
        : HttpMethods.IsHead(method) ? HttpMethods.Head
        : HttpMethods.IsPut(method) ? HttpMethods.Put
        : HttpMethods.IsPost(method) ? HttpMethods.Post
        : HttpMethods.IsDelete(method) ? HttpMethods.Delete
        : HttpMethods.IsOptions(method) ? HttpMethods.Options
        : HttpMethods.IsPatch(method) ? HttpMethods.Patch
        : HttpMethods.IsTrace(method) ? HttpMethods.Trace
        : HttpMethods.IsConnect(method) ? HttpMethods.Connect
        : XapiTags.Other;

    private static string Status(int status) => status.ToString(CultureInfo.InvariantCulture);
}

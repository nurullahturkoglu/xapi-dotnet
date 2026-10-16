using Microsoft.AspNetCore.Http;
using Xapi.Json;
using Xapi.Lrs.Storage;

namespace Xapi.Lrs.Http;

// The Agents Resource (IEEE 9274.1.1, 4.1.6.3; xAPI 1.0.3 Communication 2.4).
internal static class AgentsEndpoint
{
    private static readonly string[] Parameters = ["agent"];

    public static async Task<IResult> GetAsync(HttpContext http, IStatementStore store)
    {
        var query = http.Request.Query;
        if (RequestReading.CheckParameters(query, Parameters) is { } parameterError)
        {
            return XapiResults.Problem(StatusCodes.Status400BadRequest, parameterError);
        }

        var version = XapiRequestFeature.Get(http).Version;
        if (!RequestReading.TryReadAgent(query, version, out var agent, out var error))
        {
            return error;
        }

        var person = await store.GetPersonAsync(agent, http.RequestAborted).ConfigureAwait(false);
        return XapiResults.Json(XapiJson.SerializePerson(person, version));
    }
}

// The Activities Resource (IEEE 9274.1.1, 4.1.6.4; xAPI 1.0.3 Communication 2.5).
internal static class ActivitiesEndpoint
{
    private static readonly string[] Parameters = ["activityId"];

    public static async Task<IResult> GetAsync(HttpContext http, IStatementStore store)
    {
        var query = http.Request.Query;
        if (RequestReading.CheckParameters(query, Parameters) is { } parameterError)
        {
            return XapiResults.Problem(StatusCodes.Status400BadRequest, parameterError);
        }

        if (!query.TryGetValue("activityId", out var value))
        {
            return XapiResults.Problem(StatusCodes.Status400BadRequest, "The activityId parameter is required.");
        }

        if (!Iri.TryParse(value.ToString(), out var activityId))
        {
            return XapiResults.Problem(StatusCodes.Status400BadRequest, $"activityId '{value}' is not an IRI.");
        }

        // The id is looked up exactly as sent (simple string comparison, RFC 3987 5.3.1, as 4.2.7 allows). An
        // Activity the LRS has no definition for is still returned, with just its id (4.1.6.4).
        var known = await store.GetActivitiesAsync([activityId], http.RequestAborted).ConfigureAwait(false);
        var activity = known.TryGetValue(activityId, out var found) ? found : new Activity(activityId);
        return XapiResults.Json(XapiJson.SerializeActivity(activity, XapiRequestFeature.Get(http).Version));
    }
}

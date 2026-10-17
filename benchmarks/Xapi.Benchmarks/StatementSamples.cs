using System.Globalization;
using System.Text;

namespace Xapi.Benchmarks;

// Statements shaped like what an LMS or a course player sends: a named learner, a verb with a display, an activity
// with a definition, a scored result and a context with a registration, a parent course and a category. About
// 1 KB each.
public static class StatementSamples
{
    public const string CourseIri = "https://lms.example.com/courses/xapi-101";

    public static readonly string[] Verbs =
    [
        "http://adlnet.gov/expapi/verbs/attempted",
        "http://adlnet.gov/expapi/verbs/completed",
        "http://adlnet.gov/expapi/verbs/passed",
        "http://adlnet.gov/expapi/verbs/failed",
        "http://adlnet.gov/expapi/verbs/answered",
        "http://adlnet.gov/expapi/verbs/experienced",
        "http://adlnet.gov/expapi/verbs/interacted",
        "http://adlnet.gov/expapi/verbs/progressed",
        "http://adlnet.gov/expapi/verbs/launched",
        "http://adlnet.gov/expapi/verbs/terminated",
    ];

    // n picks the learner, verb and lesson, so a sequence of n gives a spread of each. A null id leaves it out.
    public static string Statement(Guid? id, int n)
    {
        var verb = Verbs[n % Verbs.Length];
        var verbName = verb[(verb.LastIndexOf('/') + 1)..];
        var lesson = $"{CourseIri}/lessons/{n % 40}";
        var learner = n % 500;
        var score = (n % 100) / 100.0;
        var timestamp = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero).AddSeconds(n);

        var idMember = id is { } value ? $"\"id\":\"{value}\"," : "";
        var success = score >= 0.5 ? "true" : "false";
        return string.Create(CultureInfo.InvariantCulture,
            $"{{{idMember}\"actor\":{{\"objectType\":\"Agent\",\"name\":\"Learner {learner}\",\"mbox\":\"mailto:learner{learner}@example.com\"}}," +
            $"\"verb\":{{\"id\":\"{verb}\",\"display\":{{\"en-US\":\"{verbName}\"}}}}," +
            $"\"object\":{{\"objectType\":\"Activity\",\"id\":\"{lesson}\",\"definition\":{{\"name\":{{\"en-US\":\"Lesson {n % 40}\"}}," +
            $"\"description\":{{\"en-US\":\"A lesson of the introductory xAPI course, with a short quiz at the end.\"}},\"type\":\"http://adlnet.gov/expapi/activities/lesson\"}}}}," +
            $"\"result\":{{\"score\":{{\"scaled\":{score},\"raw\":{n % 100},\"min\":0,\"max\":100}},\"success\":{success},\"completion\":true,\"duration\":\"PT{n % 3600}S\"}}," +
            $"\"context\":{{\"registration\":\"{DeterministicGuid(learner)}\",\"contextActivities\":{{\"parent\":[{{\"id\":\"{CourseIri}\",\"objectType\":\"Activity\"}}]," +
            $"\"category\":[{{\"id\":\"https://w3id.org/xapi/cmi5/context/categories/cmi5\",\"objectType\":\"Activity\"}}]}},\"platform\":\"Example LMS\",\"language\":\"en-US\"}}," +
            $"\"timestamp\":\"{timestamp:yyyy-MM-ddTHH:mm:ss.fffZ}\"}}");
    }

    public static string Batch(int count, int start = 0)
    {
        var builder = new StringBuilder("[");
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(Statement(Guid.NewGuid(), start + i));
        }

        return builder.Append(']').ToString();
    }

    public static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);

    // A version 4, RFC 4122 variant UUID per learner, so registrations repeat across runs
    private static Guid DeterministicGuid(int n) => Guid.Parse($"00000000-0000-4000-8000-{n:D12}", CultureInfo.InvariantCulture);
}

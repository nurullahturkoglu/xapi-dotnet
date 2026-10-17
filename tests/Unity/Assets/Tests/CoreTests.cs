using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Xapi.Json;
using Xapi.Validation;

namespace Xapi.Unity.Tests
{
    public class CoreTests
    {
        private const string StatementJson = @"{
  ""id"": ""0192a3b4-5c6d-7e8f-9a0b-1c2d3e4f5a6b"",
  ""actor"": { ""objectType"": ""Agent"", ""name"": ""Ada Learner"", ""mbox"": ""mailto:ada@example.com"" },
  ""verb"": { ""id"": ""http://adlnet.gov/expapi/verbs/completed"", ""display"": { ""en-US"": ""completed"" } },
  ""object"": {
    ""objectType"": ""Activity"",
    ""id"": ""https://example.com/games/level-1"",
    ""definition"": {
      ""name"": { ""en-US"": ""Level 1"" },
      ""type"": ""http://adlnet.gov/expapi/activities/assessment"",
      ""interactionType"": ""choice"",
      ""correctResponsesPattern"": [ ""red"" ],
      ""choices"": [ { ""id"": ""red"", ""description"": { ""en-US"": ""Red"" } }, { ""id"": ""blue"", ""description"": { ""en-US"": ""Blue"" } } ]
    }
  },
  ""result"": {
    ""score"": { ""scaled"": 0.75, ""raw"": 75, ""min"": 0, ""max"": 100 },
    ""success"": true,
    ""completion"": true,
    ""duration"": ""PT4M30.5S"",
    ""extensions"": { ""https://example.com/xapi/ext/attempts"": 3 }
  },
  ""context"": {
    ""registration"": ""0192a3b4-0000-7000-8000-000000000001"",
    ""platform"": ""Unity"",
    ""language"": ""en-US"",
    ""extensions"": { ""https://example.com/xapi/ext/device"": { ""os"": ""macOS"", ""fps"": 60 } }
  },
  ""timestamp"": ""2026-10-17T10:15:30.123Z""
}";

        [Test]
        public void Statement_is_read_validated_and_written()
        {
            var parsed = XapiJson.ParseStatement(StatementJson, XapiVersion.V2_0_0);
            Assert.That(parsed.IsSuccess, Is.True, string.Join("; ", parsed.Errors));

            var statement = parsed.Value;
            Assert.That(statement.Verb.Id, Is.EqualTo(Verbs.Completed.Id));
            Assert.That(statement.Result!.Score!.Scaled, Is.EqualTo(0.75));
            Assert.That(statement.Result.Duration!.Value.ToString(), Is.EqualTo("PT4M30.5S"));
            Assert.That(StatementValidator.Validate(statement, XapiVersion.V1_0_3), Is.Empty);
            Assert.That(StatementValidator.Validate(statement, XapiVersion.V2_0_0), Is.Empty);

            foreach (var version in new[] { XapiVersion.V1_0_3, XapiVersion.V2_0_0 })
            {
                var json = XapiJson.SerializeStatement(statement, version);
                var again = XapiJson.ParseStatement(json, version);
                Assert.That(again.IsSuccess, Is.True, string.Join("; ", again.Errors));
                Assert.That(again.Value, Is.EqualTo(statement), json);
            }
        }

        [Test]
        public void Invalid_statement_reports_errors()
        {
            var parsed = XapiJson.ParseStatement(
                @"{ ""actor"": { ""mbox"": ""ada@example.com"" }, ""verb"": { ""id"": ""http://adlnet.gov/expapi/verbs/completed"" }, ""object"": { ""id"": ""https://example.com/a"" } }",
                XapiVersion.V2_0_0);

            Assert.That(parsed.IsSuccess, Is.False);
            Assert.That(parsed.Errors.Select(e => (e.Code, e.Path)), Is.EqualTo(new[] { (XapiErrorCodes.Mbox, "$.actor.mbox") }));
            Assert.Throws<XapiValidationException>(() => parsed.GetValueOrThrow());
        }

        [Test]
        public void UuidV7_ids_are_version_7_and_ordered()
        {
            var before = DateTimeOffset.UtcNow.AddSeconds(-1);
            var ids = Enumerable.Range(0, 1000).Select(_ => UuidV7Generator.Shared.NewGuid()).ToList();
            var after = DateTimeOffset.UtcNow.AddSeconds(1);

            foreach (var id in ids)
            {
                var text = id.ToString();
                Assert.That(text[14], Is.EqualTo('7'), text);
                Assert.That("89ab", Does.Contain(text[19]), text);
            }

            Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Count));
            Assert.That(ids.Select(i => i.ToString()), Is.Ordered.Using((IComparer)StringComparer.Ordinal));
            Assert.That(UuidV7Generator.GetTimestamp(ids[0]), Is.InRange(before, after));
        }
    }
}

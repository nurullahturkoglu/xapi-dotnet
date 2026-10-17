using System;
using System.Collections;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEngine.TestTools;
using Xapi.Client;

namespace Xapi.Unity.Tests
{
    [TestFixture(Transport.HttpClientHandler)]
    [TestFixture(Transport.UnityWebRequest)]
    public class ClientTests
    {
        private readonly Transport transport;

        public ClientTests(Transport transport) => this.transport = transport;

        [SetUp]
        public void SetUp() => TestLrs.IgnoreIfUnsupported(transport);

        [UnityTest]
        public IEnumerator Statement_is_sent_and_read_back() => TestLrs.Run(async () =>
        {
            var client = TestLrs.CreateClient(transport);
            var registration = UuidV7Generator.Shared.NewGuid();
            var statement = new Statement(
                Agent.FromMbox("ada@example.com", "Ada Learner"),
                Verbs.Completed,
                new Activity(new Iri("https://example.com/games/level-1")))
            {
                Result = new Result { Score = new Score { Scaled = 0.5 }, Success = true },
                Context = new Context { Registration = registration, Platform = "Unity" },
            };

            var id = await client.SaveStatementAsync(statement);
            var stored = await client.GetStatementAsync(id);
            var page = await client.QueryStatementsAsync(new StatementQuery { Registration = registration });

            Assert.That(await client.GetVersionAsync(), Is.EqualTo(XapiVersion.V2_0_0));
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored!.Verb.Id, Is.EqualTo(Verbs.Completed.Id));
            Assert.That(stored.Result!.Score!.Scaled, Is.EqualTo(0.5));
            Assert.That(stored.Context!.Platform, Is.EqualTo("Unity"));
            Assert.That(page.Statements, Has.Count.EqualTo(1));
            Assert.That(page.Statements[0].Id, Is.EqualTo(id));
        });

        // The LRS verifies the JWS, so this covers RSA signing and the multipart/mixed request.
        [UnityTest]
        public IEnumerator Signed_statement_is_accepted() => TestLrs.Run(async () =>
        {
            var client = TestLrs.CreateClient(transport);
            var statement = new Statement(
                Agent.FromMbox("ada@example.com"),
                Verbs.Passed,
                new Activity(new Iri("https://example.com/games/final-exam")))
            {
                Id = UuidV7Generator.Shared.NewGuid(),
            };

            using var key = RSA.Create(2048);
            var signed = StatementSigner.Sign(statement, key, await client.GetVersionAsync());
            var id = await client.SaveStatementAsync(signed.Statement, new[] { signed.Signature });
            var stored = await client.GetStatementAsync(id);

            Assert.That(id, Is.EqualTo(statement.Id));
            Assert.That(stored!.Attachments.Select(a => a.UsageType), Is.EqualTo(new[] { StatementSigner.SignatureUsageType }));
        });

        [UnityTest]
        public IEnumerator Wrong_credentials_fail_with_401() => TestLrs.Run(async () =>
        {
            var client = new XapiClient(TestLrs.Http(transport), new XapiClientOptions
            {
                Endpoint = TestLrs.Endpoint,
                Credentials = XapiCredentials.Basic("unity_key", "wrong"),
                Version = XapiVersion.V2_0_0,
            });

            var error = await AssertThrowsAsync<XapiHttpException>(() => client.GetStatementAsync(Guid.NewGuid()));
            Assert.That((int)error.StatusCode, Is.EqualTo(401));
        });

        private static async System.Threading.Tasks.Task<T> AssertThrowsAsync<T>(Func<System.Threading.Tasks.Task> action)
            where T : Exception
        {
            try
            {
                await action();
            }
            catch (T e)
            {
                return e;
            }

            Assert.Fail($"Expected {typeof(T).Name}");
            return null!;
        }
    }
}

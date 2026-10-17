using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Xapi.Client;
using Xapi.Client.Outbox;

namespace Xapi.Unity.Tests
{
    [TestFixture(Transport.HttpClientHandler)]
    [TestFixture(Transport.UnityWebRequest)]
    public class OutboxTests
    {
        private readonly Transport transport;
        private string directory = null!;

        public OutboxTests(Transport transport) => this.transport = transport;

        [SetUp]
        public void SetUp()
        {
            TestLrs.IgnoreIfUnsupported(transport);
            directory = Path.Combine(Application.temporaryCachePath, "xapi-outbox-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [UnityTest]
        public IEnumerator Statements_queued_offline_are_delivered_after_a_restart() => TestLrs.Run(async () =>
        {
            var registration = UuidV7Generator.Shared.NewGuid();
            var ids = new List<Guid>();

            // Nothing listens on port 1, so every request fails as it does without a network.
            var offline = TestLrs.CreateClient(transport, new Uri("http://127.0.0.1:1/xapi/"));
            using (var store = await FileOutboxStore.OpenAsync(directory))
            using (var outbox = new StatementOutbox(offline, store))
            {
                for (var level = 1; level <= 3; level++)
                {
                    ids.Add(await outbox.EnqueueAsync(Completed(level, registration)));
                }

                var result = await outbox.FlushAsync();

                Assert.That(result.Delivered, Is.EqualTo(0));
                Assert.That(result.Status.PendingCount, Is.EqualTo(3));
                Assert.That(result.Status.State, Is.EqualTo(OutboxState.WaitingToRetry));
            }

            using (var store = await FileOutboxStore.OpenAsync(directory))
            using (var outbox = new StatementOutbox(TestLrs.CreateClient(transport), store))
            {
                Assert.That((await outbox.GetStatusAsync()).PendingCount, Is.EqualTo(3));

                var result = await outbox.FlushAsync();

                Assert.That(result.Delivered, Is.EqualTo(3));
                Assert.That(result.Status.PendingCount, Is.EqualTo(0));
            }

            var page = await TestLrs.CreateClient(transport).QueryStatementsAsync(
                new StatementQuery { Registration = registration, Ascending = true });
            Assert.That(page.Statements.Select(s => s.Id!.Value), Is.EqualTo(ids));
        });

        private static Statement Completed(int level, Guid registration) => new Statement(
            Agent.FromMbox("ada@example.com"),
            Verbs.Completed,
            new Activity(new Iri($"https://example.com/games/level-{level}")))
        {
            Context = new Context { Registration = registration },
        };
    }
}

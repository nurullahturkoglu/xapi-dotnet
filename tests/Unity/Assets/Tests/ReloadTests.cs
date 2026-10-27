#nullable enable

using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Xapi.Client.Outbox;

namespace Xapi.Unity.Tests
{
    // Runs in a WebGL player that tests/Unity/WebGL/run.mjs loads twice. With ?xapi-reload=enqueue it queues
    // statements under Application.persistentDataPath while the LRS cannot be reached; after the reload, with
    // ?xapi-reload=deliver, it checks that the queue is still there and delivers it.
    public class ReloadTests
    {
        private static readonly string QueueDirectory = Path.Combine(Application.persistentDataPath, "xapi-outbox-reload");

        // Never disposed, as a page that is reloaded or closed does not dispose it either
        private static FileOutboxStore? openStore;

        private static string? Phase
        {
            get
            {
                const string key = "xapi-reload=";
                var url = Application.absoluteURL;
                var index = url.IndexOf(key, StringComparison.Ordinal);
                if (index < 0)
                {
                    return null;
                }

                var value = url.Substring(index + key.Length);
                var end = value.IndexOfAny(new[] { '&', '#' });
                return end < 0 ? value : value.Substring(0, end);
            }
        }

        [SetUp]
        public void SetUp()
        {
            if (Phase == null)
            {
                Assert.Ignore("Runs in a WebGL player loaded by tests/Unity/WebGL/run.mjs");
            }
        }

        [UnityTest]
        public IEnumerator Queue_under_persistent_data_survives_a_page_reload() => TestLrs.Run(async () =>
        {
            if (Phase == "enqueue")
            {
                if (Directory.Exists(QueueDirectory))
                {
                    Directory.Delete(QueueDirectory, recursive: true);
                }

                // Nothing listens on port 1, so the statements stay queued
                var offline = TestLrs.CreateClient(Transport.UnityWebRequest, new Uri("http://127.0.0.1:1/xapi/"));
                openStore = await FileOutboxStore.OpenAsync(QueueDirectory);
                var outbox = new StatementOutbox(offline, openStore);
                for (var level = 1; level <= 3; level++)
                {
                    await outbox.EnqueueAsync(new Statement(
                        Agent.FromMbox("ada@example.com"),
                        Verbs.Completed,
                        new Activity(new Iri($"https://example.com/games/level-{level}"))));
                }

                WebGLFileSystem.Persist();
                return;
            }

            using (var store = await FileOutboxStore.OpenAsync(QueueDirectory))
            using (var outbox = new StatementOutbox(TestLrs.CreateClient(Transport.UnityWebRequest), store))
            {
                Assert.That((await outbox.GetStatusAsync()).PendingCount, Is.EqualTo(3));

                var result = await outbox.FlushAsync();

                Assert.That(result.Delivered, Is.EqualTo(3));
                Assert.That(result.Status.PendingCount, Is.EqualTo(0));
            }
        });
    }
}

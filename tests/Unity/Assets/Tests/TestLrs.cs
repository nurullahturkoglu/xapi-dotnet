using System;
using System.Collections;
using System.Net.Http;
using System.Threading.Tasks;
using Xapi.Client;

namespace Xapi.Unity.Tests
{
    // The LRS the tests talk to: the xapi-lrs image, started with
    //   docker run -p 8080:8080 -e LrsHost__Key=unity_key -e LrsHost__Secret=unity_secret ghcr.io/nurullahturkoglu/xapi-lrs:0.1.0-preview.1
    internal static class TestLrs
    {
        public static readonly HttpClient Http = new HttpClient();

        public static Uri Endpoint =>
            new Uri(Environment.GetEnvironmentVariable("XAPI_UNITY_LRS") ?? "http://localhost:8080/xapi/");

        public static XapiClient CreateClient() => CreateClient(Endpoint);

        public static XapiClient CreateClient(Uri endpoint) => new XapiClient(Http, new XapiClientOptions
        {
            Endpoint = endpoint,
            Credentials = XapiCredentials.Basic("unity_key", "unity_secret"),
        });

        // Unity tests are coroutines. Waiting with yield, not Task.Wait, keeps them working on WebGL, which has
        // one thread.
        public static IEnumerator Run(Func<Task> test)
        {
            var task = test();
            while (!task.IsCompleted)
            {
                yield return null;
            }

            task.GetAwaiter().GetResult();
        }
    }
}

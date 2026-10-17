#nullable enable

using System;
using System.Collections;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Xapi.Client;

namespace Xapi.Unity.Tests
{
    public enum Transport
    {
        HttpClientHandler,
        UnityWebRequest,
    }

    // The LRS the tests talk to: the xapi-lrs image, started with
    //   docker run -p 8080:8080 -e LrsHost__Key=unity_key -e LrsHost__Secret=unity_secret -e "LrsHost__AllowedOrigins=*" \
    //     ghcr.io/nurullahturkoglu/xapi-lrs:0.1.0-preview.1
    // WebGL players need AllowedOrigins, because the browser checks CORS.
    internal static class TestLrs
    {
        private static HttpClient? httpClientHandler;
        private static HttpClient? unityWebRequest;

        public static Uri Endpoint =>
            new Uri(Environment.GetEnvironmentVariable("XAPI_UNITY_LRS") ?? "http://localhost:8080/xapi/");

        // Created on first use, which is on the main thread, as UnityWebRequestHandler requires
        public static HttpClient Http(Transport transport) => transport == Transport.UnityWebRequest
            ? unityWebRequest ??= new HttpClient(new UnityWebRequestHandler())
            : httpClientHandler ??= new HttpClient();

        public static XapiClient CreateClient(Transport transport) => CreateClient(transport, Endpoint);

        public static XapiClient CreateClient(Transport transport, Uri endpoint) => new XapiClient(Http(transport), new XapiClientOptions
        {
            Endpoint = endpoint,
            Credentials = XapiCredentials.Basic("unity_key", "unity_secret"),
        });

        public static void IgnoreIfUnsupported(Transport transport)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (transport == Transport.HttpClientHandler)
            {
                Assert.Ignore("HttpClientHandler cannot open connections in a browser");
            }
#endif
        }

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

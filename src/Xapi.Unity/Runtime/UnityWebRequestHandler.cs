#nullable enable

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Xapi.Unity
{
    /// <summary>
    /// An <see cref="HttpMessageHandler"/> that sends requests with <see cref="UnityWebRequest"/>. WebGL players
    /// need it because <see cref="HttpClientHandler"/> cannot open connections in a browser:
    /// <c>new HttpClient(new UnityWebRequestHandler())</c>. It works on the other platforms too.
    /// </summary>
    /// <remarks>
    /// <see cref="UnityWebRequest"/> only runs on Unity's main thread, so create the handler there. Requests sent
    /// from other threads are posted to the main thread, which therefore must not block waiting for them (with
    /// <c>.Result</c> or <c>.Wait()</c>).
    /// </remarks>
    public sealed class UnityWebRequestHandler : HttpMessageHandler
    {
        // UnityWebRequest sets these itself, and browsers refuse most of them
        private static readonly HashSet<string> SkippedRequestHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Accept-Encoding", "Connection", "Content-Length", "Cookie", "Date", "Expect", "Host", "Keep-Alive",
            "Transfer-Encoding", "User-Agent",
        };

        // The body is already decoded, and ByteArrayContent sets its own length
        private static readonly HashSet<string> SkippedResponseHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Content-Encoding", "Content-Length", "Transfer-Encoding",
        };

        private readonly SynchronizationContext mainThread;
        private readonly int mainThreadId;

        /// <summary>Creates a handler. Call it on Unity's main thread.</summary>
        /// <exception cref="InvalidOperationException">The current thread has no synchronization context.</exception>
        public UnityWebRequestHandler()
        {
            mainThread = SynchronizationContext.Current
                ?? throw new InvalidOperationException("Create UnityWebRequestHandler on Unity's main thread.");
            mainThreadId = Environment.CurrentManagedThreadId;
        }

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var body = request.Content == null ? null : await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

#if UNITY_WEBGL && !UNITY_EDITOR
            // One thread: continuations must run inline, there is no thread pool to queue them to
            var completion = new TaskCompletionSource<HttpResponseMessage>();
#else
            // Keep the caller's continuation off the main thread
            var completion = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
#endif

            if (Environment.CurrentManagedThreadId == mainThreadId)
            {
                Start(request, body, completion, cancellationToken);
            }
            else
            {
                mainThread.Post(_ => Start(request, body, completion, cancellationToken), null);
            }

            return await completion.Task.ConfigureAwait(false);
        }

        private void Start(HttpRequestMessage request, byte[]? body, TaskCompletionSource<HttpResponseMessage> completion, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }

            // Errors go to the task: when Start was posted from another thread, nothing else would see them
            UnityWebRequest? created = null;
            try
            {
                created = new UnityWebRequest(request.RequestUri, request.Method.Method)
                {
                    downloadHandler = new DownloadHandlerBuffer(),
                };
                if (body != null)
                {
                    created.uploadHandler = new UploadHandlerRaw(body);
                }

                SetHeaders(created, request.Headers);
                if (request.Content != null)
                {
                    SetHeaders(created, request.Content.Headers);
                }
            }
            catch (Exception e)
            {
                created?.Dispose();
                completion.TrySetException(e);
                return;
            }

            var web = created;

            var finished = false;
            var registration = cancellationToken.Register(() => Post(() =>
            {
                if (!finished)
                {
                    web.Abort();
                }
            }));

            web.SendWebRequest().completed += _ =>
            {
                finished = true;
                registration.Dispose();
#if UNITY_WEBGL && !UNITY_EDITOR
                // .NET runs a ConfigureAwait(false) continuation inline only on a thread without a custom
                // SynchronizationContext, and queues it to the thread pool otherwise. A WebGL player has no thread
                // pool, so without this the caller's await would never resume.
                var context = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(null);
#endif
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        completion.TrySetCanceled(cancellationToken);
                    }
                    else if (web.result == UnityWebRequest.Result.ConnectionError || web.result == UnityWebRequest.Result.DataProcessingError)
                    {
                        completion.TrySetException(new HttpRequestException(web.error));
                    }
                    else
                    {
                        completion.TrySetResult(CreateResponse(web, request));
                    }
                }
                catch (Exception e)
                {
                    completion.TrySetException(e);
                }
                finally
                {
                    web.Dispose();
#if UNITY_WEBGL && !UNITY_EDITOR
                    SynchronizationContext.SetSynchronizationContext(context);
#endif
                }
            };
        }

        private void Post(Action action)
        {
            if (Environment.CurrentManagedThreadId == mainThreadId)
            {
                action();
            }
            else
            {
                mainThread.Post(_ => action(), null);
            }
        }

        private static void SetHeaders(UnityWebRequest web, System.Net.Http.Headers.HttpHeaders headers)
        {
            foreach (var header in headers)
            {
                if (!SkippedRequestHeaders.Contains(header.Key))
                {
                    web.SetRequestHeader(header.Key, string.Join(", ", header.Value));
                }
            }
        }

        private static HttpResponseMessage CreateResponse(UnityWebRequest web, HttpRequestMessage request)
        {
            var response = new HttpResponseMessage((HttpStatusCode)web.responseCode)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(web.downloadHandler.data ?? Array.Empty<byte>()),
            };

            var headers = web.GetResponseHeaders();
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    if (!SkippedResponseHeaders.Contains(header.Key)
                        && !response.Headers.TryAddWithoutValidation(header.Key, header.Value))
                    {
                        response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }
            }

            return response;
        }
    }
}

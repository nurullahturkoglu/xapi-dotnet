# Unity

`Xapi.Core`, `Xapi.Client` and `Xapi.Client.Outbox` use their `netstandard2.0` builds in Unity. WebGL players
also need `UnityWebRequestHandler` from the Unity package in `src/Xapi.Unity`. This page says what has been
tested, how to install the packages, and what to watch for.

## What is tested

The tests in [tests/Unity](https://github.com/nurullahturkoglu/xapi-dotnet/tree/main/tests/Unity) read,
validate and write statements, generate UUIDv7 ids, send statements to an LRS and read them back, sign a
statement with RSA and send it as multipart/mixed, check the 401 for wrong credentials, and queue statements in
a `FileOutboxStore` while the LRS cannot be reached, then deliver them after the store is reopened. The client
and outbox tests run with `HttpClientHandler` and with `UnityWebRequestHandler`.

| Where | Scripting backend | Result |
| --- | --- | --- |
| Editor (play mode), macOS | Mono | Passed |
| macOS player, Apple silicon | IL2CPP, Managed Stripping Level High | Passed |
| Editor (play mode), Linux (CI) | Mono | Passed |
| Linux player, x86-64 (CI) | IL2CPP | Passed |
| WebGL, headless Chromium (local) and Chrome (CI) | IL2CPP | Passed with `UnityWebRequestHandler` |
| Android 15 emulator, arm64 | IL2CPP | Passed |
| iOS 26 Simulator, arm64 | IL2CPP | Passed |

Tested with Unity 6.3 LTS (6000.3.26f1), API Compatibility Level .NET Standard 2.1 (the default) and
NuGetForUnity 4.5.0. The macOS editor row ran with 0.2.0 from nuget.org, the macOS player row with
0.1.0-preview.1 from nuget.org, and the Android and iOS rows with 0.2.0 from nuget.org. iOS has run in the
Simulator only, not on a device. The local WebGL run used packages
packed from the repository just before 0.2.0, which added the WebGL fixes. CI runs the Linux and WebGL rows on
every change to the packages, with packages packed from the commit; the macOS, Android and iOS rows run locally
([CONTRIBUTING.md](https://github.com/nurullahturkoglu/xapi-dotnet/blob/main/CONTRIBUTING.md#unity-tests)).

The packages do not use reflection, so they need no `link.xml`: the player tests pass with the highest stripping
level.

## Install

Unity does not read NuGet packages by itself.
[NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) installs them and their dependencies into
`Assets/Packages`. Add it to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.github-glitchenzo.nugetforunity": "https://github.com/GlitchEnzo/NuGetForUnity.git?path=/src/NuGetForUnity#v4.5.0"
  }
}
```

Then open **NuGet > Manage NuGet Packages** and install `Xapi.Client` (or
`Xapi.Client.Outbox`, which brings `Xapi.Client` and `Xapi.Core`). NuGetForUnity also installs
System.Text.Json 10.0.0, System.Text.Encodings.Web, System.IO.Pipelines, Microsoft.Bcl.AsyncInterfaces,
Microsoft.Bcl.TimeProvider, System.Diagnostics.DiagnosticSource and System.Runtime.CompilerServices.Unsafe. It
lists System.Memory, System.Buffers and System.Threading.Tasks.Extensions too, but adds no DLLs for them,
because Unity's .NET Standard 2.1 profile has those types.

If another package in the project brings an older System.Text.Json, NuGetForUnity keeps the newer one;
`Xapi.Core` needs 10.0.0 or later.

## Sending statements

The code is the same as on .NET (see [getting-started.md](getting-started.md) and [outbox.md](outbox.md)). A
sketch of a component that queues statements and delivers them in the background:

```csharp
public class XapiTracker : MonoBehaviour
{
    static readonly HttpClient Http = new HttpClient();

    FileOutboxStore store;
    StatementOutbox outbox;
    readonly CancellationTokenSource shutdown = new CancellationTokenSource();

    async void Start()
    {
        var client = new XapiClient(Http, new XapiClientOptions
        {
            Endpoint = new Uri("https://lrs.example.com/xapi/"),
            Credentials = XapiCredentials.Basic(key, secret),
        });
        store = await FileOutboxStore.OpenAsync(Path.Combine(Application.persistentDataPath, "xapi-outbox"));
        outbox = new StatementOutbox(client, store);
        _ = outbox.RunAsync(shutdown.Token);
    }

    public async void Completed(string levelId)
    {
        await outbox.EnqueueAsync(new Statement(
            Agent.FromMbox(playerEmail),
            Verbs.Completed,
            new Activity(new Iri("https://example.com/games/" + levelId))));
    }

    async void OnApplicationPause(bool paused)
    {
        if (paused && outbox != null)
        {
            await outbox.FlushAsync();
        }
    }

    void OnApplicationQuit()
    {
        shutdown.Cancel();
        outbox?.Dispose();
        store?.Dispose();
    }
}
```

- Use one `HttpClient` for the life of the app, as on .NET.
- Keep the store under `Application.persistentDataPath`, which survives app updates.
- The library awaits with `ConfigureAwait(false)`, so its work does not run on the main thread, but an `await`
  in a `MonoBehaviour` method still resumes there. The outbox's `Delivered`, `DeadLettered` and `Paused` events
  are raised on the thread that runs the flush, often a thread-pool thread; do not touch Unity objects from
  them.
- Do not block on a task with `.Result` or `.Wait()` on the main thread.

## WebGL

Browsers do not give WebAssembly sockets, so `HttpClient` with its default handler sends nothing in a WebGL
player: the request never starts and the task never completes. `UnityWebRequestHandler` sends the requests with
`UnityWebRequest`, which hands them to the browser. It ships from version 0.2.0 on. Install it from this
repository with the Package Manager (**Add package from git URL**), or in `Packages/manifest.json`:

```json
{
  "dependencies": {
    "io.github.nurullahturkoglu.xapi.unity": "https://github.com/nurullahturkoglu/xapi-dotnet.git?path=/src/Xapi.Unity#v0.2.1"
  }
}
```

Then give the client an `HttpClient` that uses it. Create the handler on the main thread, for example in
`Awake`:

```csharp
http = new HttpClient(new UnityWebRequestHandler());
```

It works on the other platforms too, so one `HttpClient` can serve every build. Requests sent from another
thread are posted to the main thread, which therefore must not block waiting for them.

The LRS has to allow the page's origin with CORS, because the browser checks it. The `xapi-lrs` image does that
with `LrsHost__AllowedOrigins` ([lrs.md](lrs.md)).

A WebGL player has one thread and no thread pool. In the tests, `Task.Delay` and `Task.Run` never completed
there. That changes how the outbox is used:

- Do not start `RunAsync`, which waits with timers. Call `FlushAsync` from a coroutine instead, for example
  every few seconds and after enqueueing.
- Unity keeps `Application.persistentDataPath` in memory in WebGL and writes it to the browser's IndexedDB only
  when asked, so a queue there is gone after a page reload. Call `WebGLFileSystem.Persist()` from the Unity
  package after `EnqueueAsync` and after each `FlushAsync`; it does nothing on other platforms. The browser
  writes the directory after the current frame, so a statement enqueued in the frame the tab closes can still be
  lost. Setting `config.autoSyncPersistentDataPath = true` in the page's `createUnityInstance` call is not enough
  on its own: Unity then saves a file when it is closed, and `FileOutboxStore` keeps its journal open.

```csharp
await outbox.EnqueueAsync(statement);
WebGLFileSystem.Persist();
```

The WebGL tests queue statements under `Application.persistentDataPath` while the LRS cannot be reached, call
`Persist`, reload the page, and deliver the queue.

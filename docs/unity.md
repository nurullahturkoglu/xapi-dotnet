# Unity

`Xapi.Core`, `Xapi.Client` and `Xapi.Client.Outbox` use their `netstandard2.0` builds in Unity. This page says
what has been tested, how to install the packages, and what to watch for.

## What is tested

The tests in [tests/Unity](https://github.com/nurullahturkoglu/xapi-dotnet/tree/main/tests/Unity) read,
validate and write statements, generate UUIDv7 ids, send statements to an LRS and read them back, sign a
statement with RSA and send it as multipart/mixed, check the 401 for wrong credentials, and queue statements in
a `FileOutboxStore` while the LRS cannot be reached, then deliver them after the store is reopened.

| Where | Scripting backend | Result |
| --- | --- | --- |
| Editor (play mode), macOS | Mono | Passed |
| macOS player, Apple silicon | IL2CPP, Managed Stripping Level High | Passed |
| Editor (play mode), Linux (CI) | Mono | Passed |
| Linux player, x86-64 (CI) | IL2CPP | Passed |
| WebGL | IL2CPP | Not tested yet |
| Android, iOS | IL2CPP | Not tested yet |

Tested with Unity 6.3 LTS (6000.3.26f1), API Compatibility Level .NET Standard 2.1 (the default),
NuGetForUnity 4.5.0 and the packages at 0.1.0-preview.1 from nuget.org. CI runs the Linux rows on every change to
the three packages, with packages packed from the commit.

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

Then open **NuGet > Manage NuGet Packages**, turn on **Show Prerelease**, and install `Xapi.Client` (or
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

Browsers do not give WebAssembly sockets, and `HttpClient` is not expected to work in a WebGL player. This is
not tested yet. [ADR-0008](adr/0008-http-client.md) records the plan: an `HttpMessageHandler` that sends
through `UnityWebRequest`, or a transport abstraction if a handler is not enough. Until then, use the packages
on desktop and mobile players, and not in WebGL builds.

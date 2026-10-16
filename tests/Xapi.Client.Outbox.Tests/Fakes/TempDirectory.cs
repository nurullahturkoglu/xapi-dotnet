namespace Xapi.Client.Outbox.Tests.Fakes;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "xapi-outbox-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Journal => System.IO.Path.Combine(Path, "outbox.v1.jsonl");

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A store that a failing test did not dispose may still hold a file open on Windows
        }
    }
}

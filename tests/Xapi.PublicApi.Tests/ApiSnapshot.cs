using System.Reflection;
using System.Text;

namespace Xapi.PublicApi.Tests;

/// <summary>
/// Compares generated text with <c>ApiSnapshots/&lt;name&gt;.approved.txt</c>. On a mismatch it writes
/// <c>&lt;name&gt;.received.txt</c> next to it and fails, so the change shows up as a file diff.
/// </summary>
internal static class ApiSnapshot
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static string SnapshotDirectory { get; } = typeof(ApiSnapshot).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "ApiSnapshotDirectory")
        .Value!;

    public static void Match(string actual, string name)
    {
        var approvedPath = Path.Combine(SnapshotDirectory, name + ".approved.txt");
        var receivedPath = Path.Combine(SnapshotDirectory, name + ".received.txt");

        var received = Normalize(actual);
        var approved = File.Exists(approvedPath) ? Normalize(File.ReadAllText(approvedPath)) : null;

        if (string.Equals(received, approved, StringComparison.Ordinal))
        {
            File.Delete(receivedPath);
            return;
        }

        Directory.CreateDirectory(SnapshotDirectory);
        File.WriteAllText(receivedPath, received, Utf8NoBom);

        Assert.Fail(approved is null
            ? $"""
              No approved public API snapshot exists for {name}.
              The generated API was written to {receivedPath}.
              Review it, rename it to {name}.approved.txt and commit it.
              """
            : $"""
              The public API of {name} no longer matches the approved snapshot.
              {DescribeFirstDifference(approved, received)}
              Review the diff between {approvedPath} and {receivedPath}.
              If the change is intended, copy the received file over the approved file and commit it with your change.
              """);
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n') + "\n";

    private static string DescribeFirstDifference(string approved, string received)
    {
        var approvedLines = approved.Split('\n');
        var receivedLines = received.Split('\n');
        var count = Math.Max(approvedLines.Length, receivedLines.Length);

        for (var i = 0; i < count; i++)
        {
            var a = i < approvedLines.Length ? approvedLines[i] : "<end of file>";
            var r = i < receivedLines.Length ? receivedLines[i] : "<end of file>";
            if (!string.Equals(a, r, StringComparison.Ordinal))
            {
                return $"""
                    First difference at line {i + 1}:
                      approved: {a}
                      received: {r}
                    """;
            }
        }

        return string.Empty;
    }
}

using System.Text.RegularExpressions;

namespace Xapi.Documentation.Tests;

// A code block in a Markdown file is checked when the line before it is "<!-- snippet: name -->": its text must equal
// the region between "// snippet: name" and "// end snippet" in Snippets/*.cs, which this project compiles.
public partial class SnippetTests
{
    // Every C# block in these must be a snippet. outbox.md and signatures.md are not checked yet, and
    // migrating-from-tincan.md shows TinCan.NET code, which cannot compile here.
    private static readonly string[] FullyChecked =
    [
        "README.md",
        "docs/getting-started.md",
        "docs/client.md",
        "docs/lrs.md",
        "docs/versions.md",
        "docs/conformance.md",
    ];

    private static readonly string Root = FindRoot();

    [Fact]
    public void Every_marked_code_block_matches_its_snippet()
    {
        var snippets = ReadSnippets();
        var problems = new List<string>();
        foreach (var block in ReadBlocks().Where(b => b.Snippet is not null))
        {
            if (!snippets.TryGetValue(block.Snippet!, out var snippet))
            {
                problems.Add($"{block.File}:{block.Line}: no snippet named '{block.Snippet}' in tests/Xapi.Documentation.Tests/Snippets.");
            }
            else if (!snippet.SequenceEqual(block.Code))
            {
                problems.Add($"{block.File}:{block.Line}: the block differs from snippet '{block.Snippet}', which is:\n{string.Join('\n', snippet)}");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n\n", problems));
    }

    [Fact]
    public void Every_snippet_is_used_in_the_documentation()
    {
        var used = ReadBlocks().Select(b => b.Snippet).ToHashSet(StringComparer.Ordinal);

        var unused = ReadSnippets().Keys.Except(used!, StringComparer.Ordinal).ToList();

        Assert.Empty(unused);
    }

    [Fact]
    public void Checked_documents_have_no_unmarked_csharp_blocks()
    {
        var unmarked = ReadBlocks()
            .Where(b => b.Language == "csharp" && b.Snippet is null && FullyChecked.Contains(b.File))
            .Select(b => $"{b.File}:{b.Line}");

        Assert.Empty(unmarked);
    }

    private static Dictionary<string, string[]> ReadSnippets()
    {
        var snippets = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(Path.Combine(Root, "tests", "Xapi.Documentation.Tests", "Snippets"), "*.cs"))
        {
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                var start = SnippetStart().Match(lines[i]);
                if (!start.Success)
                {
                    continue;
                }

                var end = Array.FindIndex(lines, i + 1, l => SnippetEnd().IsMatch(l));
                Assert.True(end > i, $"{Path.GetFileName(path)}:{i + 1}: snippet '{start.Groups[1].Value}' has no '// end snippet'.");
                Assert.True(snippets.TryAdd(start.Groups[1].Value, Dedent(lines[(i + 1)..end])), $"Snippet '{start.Groups[1].Value}' is defined twice.");
                i = end;
            }
        }

        return snippets;
    }

    private static List<CodeBlock> ReadBlocks()
    {
        var files = new[] { "README.md" }
            .Concat(Directory.EnumerateFiles(Path.Combine(Root, "docs"), "*.md").Select(p => "docs/" + Path.GetFileName(p)));
        var blocks = new List<CodeBlock>();
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(Path.Combine(Root, file));
            string? marker = null;
            for (var i = 0; i < lines.Length; i++)
            {
                if (SnippetMarker().Match(lines[i]) is { Success: true } match)
                {
                    marker = match.Groups[1].Value;
                    continue;
                }

                if (lines[i].StartsWith("```", StringComparison.Ordinal))
                {
                    var end = Array.FindIndex(lines, i + 1, l => l.StartsWith("```", StringComparison.Ordinal));
                    Assert.True(end > i, $"{file}:{i + 1}: the code block is not closed.");
                    blocks.Add(new CodeBlock(file, i + 1, lines[i][3..].Trim(), marker, Dedent(lines[(i + 1)..end])));
                    i = end;
                }

                if (lines[i].Trim().Length > 0)
                {
                    marker = null;
                }
            }
        }

        return blocks;
    }

    private static string[] Dedent(string[] lines)
    {
        var indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart(' ').Length).DefaultIfEmpty(0).Min();
        return [.. lines.Select(l => l.Trim().Length == 0 ? string.Empty : l[indent..].TrimEnd())];
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "xapi-dotnet.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("xapi-dotnet.slnx was not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex(@"^<!-- snippet: ([a-z0-9-]+) -->$")]
    private static partial Regex SnippetMarker();

    [GeneratedRegex(@"^\s*// snippet: ([a-z0-9-]+)\s*$")]
    private static partial Regex SnippetStart();

    [GeneratedRegex(@"^\s*// end snippet\s*$")]
    private static partial Regex SnippetEnd();

    private sealed record CodeBlock(string File, int Line, string Language, string? Snippet, string[] Code);
}

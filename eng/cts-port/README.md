# CTS template export

`export-templates.js` turns the template-generated statement cases of the ADL
[LRS Conformance Test Suite](https://github.com/adlnet/lrs-conformance-test-suite)
into the JSON files in `tests/Xapi.Core.Tests/Conformance/`.

## Regenerate

Requires Node 22. No npm install is needed; the script uses only built-in modules.

```sh
git clone https://github.com/adlnet/lrs-conformance-test-suite.git /tmp/cts
git -C /tmp/cts checkout 5bc232d349c60faded8240da698f195106091638
node eng/cts-port/export-templates.js /tmp/cts
```

Options: `--out <dir>` writes somewhere else, `--commit <sha>` sets the commit
when the suite directory is not a git checkout. The output is deterministic, so
rerunning against the same commit gives byte-identical files. The script fails
if a battery no longer has 950 (1.0.3) or 972 (2.0.0) cases; update the counts
in the script and in `ConformanceDataTests` when moving to a new commit on purpose.

## What it does

The suite's test files call `templatingSelection.createTemplate('<config>.js')`.
For every entry in that config, the suite merges JSON fragments from
`test/<battery>/templates/` with literal override objects and POSTs the result
once to `/statements`, expecting 200 or 400. The script repeats that merge:

- Test files are read in the order the runner loads them, and `createTemplate`
  calls and their enclosing `describe` titles are found by scanning the source.
  The test files are never executed.
- Each `configs/*.js` file is evaluated in a `node:vm` context whose `require`
  only returns a stub for `helper.js` (`generateUUID`). Nothing else from the
  suite is loaded. This keeps the suite's dependencies out of the process, but
  `node:vm` is not a security boundary, so only point the script at a suite
  checkout you have looked at.
- Merging copies the suite's semantics exactly: the `extend` package's deep
  merge (arrays merge index by index), later fragments merged into earlier ones,
  in-place mutation of shared override objects, and one shared template object
  when a test references the same template twice.

Values the suite generates at run time are pinned:

| Where | Suite value | Exported value |
|---|---|---|
| `formatting.js` "should fail when not using \"id\"" (`iD`) | `uuid.v4()` | `00000000-0000-4000-8000-000000000000` |
| `formatting.js` `timeStamp` / `STOred` | `new Date("…")` parsed in the runner's local time zone | parsed with `TZ=UTC` |
| `timestamp_property.js` "future date" (statement and substatement) | now + 5 years | `2031-01-01T00:00:00.000Z` (clock fixed at 2026-01-01) |

Against a recorded run of both batteries at this commit, every exported
statement matched the request body the suite sent byte for byte, apart from
these five cases per battery.

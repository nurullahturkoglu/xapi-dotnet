# CTS template cases

Derived from adlnet/lrs-conformance-test-suite (MIT), commit
5bc232d349c60faded8240da698f195106091638.

`cts-templates-1.0.3.json` (950 cases) and `cts-templates-2.0.0.json` (972 cases)
hold every template-generated test of the suite's 1.0.3 and 2.0.0 batteries. Each
of those tests POSTs one JSON statement to `/statements` and expects 200 (`valid`)
or 400 (`invalid`). The hand-written tests of the suite are not included.

Each case has:

- `id`: `<config>-<requirement index>-<test index>`, stable for a given suite commit.
  Ids repeat across the two files, so pair them with the version.
- `path`: the suite's describe titles down to the test title, as shown in its reports.
- `requirement`: the first `XAPI-NNNNN` id in the innermost title that has one, or `null`
  (the 2.0-only context agent and group tests and three older tests have none).
- `kind`: `statement-validity` when the outcome depends only on the statement, or
  `time-dependent` for the two "future date" timestamp tests per battery, whose
  timestamp is fixed at 2031-01-01 instead of "now + 5 years".
- `expect` and `statement`: the expected outcome and the exact body the suite sends.

`ConformanceCases` loads the files from the repository (it walks up from the test
output folder to `xapi-dotnet.slnx`) and provides xUnit theory data:

```csharp
[Theory]
[MemberData(nameof(ConformanceCases.V2_0_0), MemberType = typeof(ConformanceCases))]
public void Parser_matches_the_suite(ConformanceCase testCase) { ... }
```

Expectations that differ from IEEE 9274.1.1 (kept as the suite has them):

- `contexts-006-*` and `contexts-008-*` (XAPI-00084/00085) expect 400 for
  `context.revision` or `context.platform` when the object is not an Activity. That
  rule is in xAPI 1.0.3 but not in 9274.1.1 (suite issue #278).
- `version-001-003` to `version-001-005` expect 400 for a statement `version` of
  `should fail`, `0.9.9` and `1.1.0` in the 2.0.0 battery too. 9274.1.1 says an LRS
  should not reject an otherwise valid statement because of its version.

## Regenerate

```sh
node eng/cts-port/export-templates.js <path to a suite checkout>
```

See `eng/cts-port/README.md` for details and the values pinned for determinism.

## License of the source material

```text
The MIT License (MIT)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

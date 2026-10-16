# Third-party notices

xapi-dotnet uses material from the projects below. This file is included in
every NuGet package built from this repository.

## IEEE xAPI Base Standard documentation

- Source: https://opensource.ieee.org/xapi/xapi-base-standard-documentation (commit 5fefb85, 28 Sep 2026)
- License: Apache License, Version 2.0
- Copyright: Copyright 2022 xAPI Authors. The documentation describes itself as
  "licensed under the terms of the Apache 2.0 License and copyright the IEEE
  XAPI Authors". The upstream repository has no NOTICE file at that commit.
- Use: short excerpts of the standard's text are quoted in XML documentation
  comments and in validation error messages. Excerpts may be shortened or
  reformatted. Where a requirement is cited, the section number of
  IEEE 9274.1.1 is given.

The full text of the Apache License, Version 2.0 is in the LICENSE file of
this repository and at https://www.apache.org/licenses/LICENSE-2.0.

## ADL LRS Conformance Test Suite

- Source: https://github.com/adlnet/lrs-conformance-test-suite (commit 5bc232d, 4 Sep 2025)
- License: MIT. The upstream LICENSE file has no copyright line; the suite is
  published by the Advanced Distributed Learning (ADL) Initiative.
- Use: the suite is run against this project in CI, and test cases in this
  repository are derived from its test configurations. Derived files say so in
  their header. This material is used only for testing and is not shipped in
  the NuGet packages.

License text, as published by the upstream project:

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

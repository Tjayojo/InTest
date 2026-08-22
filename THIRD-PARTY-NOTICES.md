# Third-Party Notices

InTest ships two NuGet packages with different packaging models, and this file's contents
follow directly from that difference:

- **`InTest.Cli`** sets `PackAsTool: true`. A dotnet tool package bundles its dependencies'
  managed assemblies directly into the package (`tools/<tfm>/any/`) so the tool runs standalone
  against the shared framework — it does not declare them as ordinary NuGet dependencies for the
  installer to resolve. Redistributing those assemblies in binary form is what triggers the
  notice obligation MIT and BSD-2-Clause both carry.
- **`InTest.Runtime`** is an ordinary library package. Its `.nupkg` contains only
  `InTest.Runtime.dll`; its dependencies are declared in the `.nuspec` and resolved separately by
  NuGet when a consumer restores the package. Nothing of theirs is embedded in this package's own
  binary.

**How this list was derived:** `dotnet pack -c Release` was run for both projects into a scratch
output directory, each resulting `.nupkg` was unzipped, and the actual file list was inspected —
not the `PackageReference` list in the `.csproj`, which would have missed transitive dependencies
(`NJsonSchema.Annotations`, `Namotion.Reflection`, `Newtonsoft.Json`) and wrongly included
`System.Text.Json`, which `NJsonSchema` also depends on but which never appears in the bundled
tool output because it ships as part of the `net10.0` shared framework instead. Each package's
declared licence and copyright were read from its own `.nuspec` in the local NuGet cache
(`~/.nuget/packages/<id>/<version>/<id>.nuspec`), not assumed from memory. `Shouldly` and
`Microsoft.NET.Test.Sdk` are referenced only by the `tests/` projects — confirmed by `git grep` —
and are not listed below because they never ship in either published package.

---

## `InTest.Cli` — bundled inside the tool package

These assemblies are copied into `InTest.Cli`'s `.nupkg` and redistributed in binary form with
every install of the tool.

### Microsoft.OpenApi 3.10.0 (MIT)

> © Microsoft Corporation. All rights reserved.

### System.CommandLine 2.0.11 (MIT)

> © Microsoft Corporation. All rights reserved.

### NJsonSchema 11.6.1 (MIT)

> Copyright © Rico Suter, 2025

### NJsonSchema.Annotations 11.6.1 (MIT) — transitive dependency of NJsonSchema

> Copyright © Rico Suter, 2025

### Namotion.Reflection 3.5.0 (MIT) — transitive dependency of NJsonSchema

> Copyright © Rico Suter, 2025

### Newtonsoft.Json 13.0.3 (MIT) — transitive dependency of NJsonSchema

> Copyright © James Newton-King 2008

### Scriban 7.2.6 (BSD-2-Clause)

> Copyright (c) Alexandre Mutel. All rights reserved.

---

## `InTest.Runtime` — declared NuGet dependencies, not bundled

`InTest.Runtime`'s own `.nupkg` contains only `InTest.Runtime.dll`. The packages below are listed
in its `.nuspec` as ordinary dependencies and are downloaded and placed by NuGet when a consumer
restores the package — this project never embeds their binaries. They are included here for
transparency about what installing `InTest.Runtime` pulls in, not because this package
redistributes them itself.

| Package | Version | Licence | Copyright |
|---|---|---|---|
| MSTest.TestFramework | 4.3.3 | MIT | © Microsoft Corporation. All rights reserved. |
| Microsoft.Extensions.Http | 10.0.11 | MIT | © Microsoft Corporation. All rights reserved. |
| Microsoft.Extensions.Configuration | 10.0.11 | MIT | © Microsoft Corporation. All rights reserved. |
| Microsoft.Extensions.Configuration.Json | 10.0.11 | MIT | © Microsoft Corporation. All rights reserved. |
| Microsoft.Extensions.Configuration.EnvironmentVariables | 10.0.11 | MIT | © Microsoft Corporation. All rights reserved. |
| NJsonSchema | 11.6.1 | MIT | Copyright © Rico Suter, 2025 |

All six are MIT — see the licence text below.

---

## Licence texts

### MIT License

Applies to: Microsoft.OpenApi, System.CommandLine, NJsonSchema, NJsonSchema.Annotations,
Namotion.Reflection, Newtonsoft.Json, MSTest.TestFramework, Microsoft.Extensions.Http,
Microsoft.Extensions.Configuration, Microsoft.Extensions.Configuration.Json,
Microsoft.Extensions.Configuration.EnvironmentVariables — with the copyright line for each taken
from its own package above.

```
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

### BSD 2-Clause License

Applies to: Scriban.

```
Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDER AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

---

## Excluded — test-only, never shipped

`Shouldly` (BSD-3-Clause) and `Microsoft.NET.Test.Sdk`, `MSTest.TestAdapter`, `MSTest.Analyzers`
(all MIT) are referenced only by projects under `tests/`. None is a dependency of `InTest.Cli` or
`InTest.Runtime`, bundled or declared, and none appears in either published `.nupkg`. Listed here
explicitly so their absence above reads as verified rather than as an oversight.

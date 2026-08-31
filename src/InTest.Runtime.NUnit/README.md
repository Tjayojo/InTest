# InTest.Runtime.NUnit

The NUnit adapter for [`InTest.Cli`](https://www.nuget.org/packages/InTest.Cli)-generated API
integration tests: `TestHost`, the assembly-scope composition root a generated project's
`[SetUpFixture]` delegates to, and `ApiTestBase`, the base class generated test classes derive
from — `[SetUp]`/`[TearDown]` wiring plus `Assert.Ignore`-based skips for the auth cases that need
a second identity or a scope the current one lacks.

This package brings in [`InTest.Runtime`](https://www.nuget.org/packages/InTest.Runtime)
transitively — the framework-neutral fixture handling, schema validation, and identity/auth
plumbing both types delegate to. Both packages' types live in the same `namespace InTest.Runtime`,
so referencing this package instead of a hypothetical all-in-one package changes only the
`PackageReference` in your `.csproj`, never a `using` or a type name in your own code.

You don't reference this package by hand: `intest init --framework nunit` adds it to the generated
project's `.csproj` automatically, pinned to a version compatible with the CLI that generated the
project.

Full documentation, including the compatibility contract between `InTest.Cli` and the runtime
packages, lives in the [InTest repository](https://github.com/Tjayojo/intest) — start with
[`docs/getting-started.md`](https://github.com/Tjayojo/intest/blob/main/docs/getting-started.md).

MIT licensed.

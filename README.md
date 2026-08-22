# InTest

Generates a complete, owned .NET test project that exercises a deployed API over real HTTP,
from its OpenAPI document.

The output is a normal MSTest project. You commit it, edit it, and run it with `dotnet test`
like any other test project. InTest is a development-time tool — it generates on your machine
or in a pull request, never as part of the deployment pipeline.

> **Status: v0. Working, but early — and nothing is published to NuGet yet.**
>
> `intest init`, `intest generate`, `intest generate --check` and `intest upgrade` work: together
> they produce a compiling MSTest project whose contract tests pass against a live API, and keep
> committed output honest in CI. That has been verified against three sample APIs, one per
> OpenAPI producer — see [`docs/v0-acceptance.md`](docs/v0-acceptance.md). Catalog and
> Inventory pass in full, twice each, against an unreset store. Orders — the one sample with
> declared `security` — generates 24 tests: **0 failed, 4 skipped, 20 passed**. The 4 skips are
> the wrong-scope 403 cases whose secondary identity already holds the scope the operation
> requires — a runtime guard (`RequireSecondaryIdentityLacks`) skips each with a stated reason
> instead of letting it run against a request that identity is genuinely authorized for
> (**F11, closed**). The 3 write-scope 403s — the cases the sample's identity pair can actually
> prove — ran and passed. `intest fixtures repair` now exists too: it creates and maintains the
> fixture files under `fixtures/` that supply request bodies and path/query parameters, so
> operations with a request body no longer generate a test that cannot send one — see "What day
> one actually looks like" below.
>
> **Not yet built:** variation tests, `intest survey`, `intest assertions add`, YAML input, and
> **a URL `spec.source`** — the OpenAPI document must be a local path today. `init` and
> `generate` both refuse a URL outright rather than letting it fail as a mangled path; the
> `spec.json` snapshot that would make a URL source work is designed (§9) and not written.
> Packages are unpublished and the IDs are not reserved, so you cannot install this yet —
> build from source.
>
> The design spec is still the source of truth and is worth reading before the code:
> [`docs/superpowers/specs/2026-08-16-intest-api-test-generator-design.md`](docs/superpowers/specs/2026-08-16-intest-api-test-generator-design.md)

## What it is for

Post-deployment gates. You deploy to an environment, and you want to know the API actually
works there — that routes resolve, responses match their declared schemas, auth is wired up,
and nothing 500s. That is a different job from unit tests, and it is usually either skipped or
hand-written once and left to rot.

InTest generates that suite from the OpenAPI document you already produce, and then gets out of
the way: the code is yours, it's committed, and you edit it like any other code you own.

## What it is not

- **Not a unit test generator.** It needs a deployed, reachable service.
- **Not a load or performance tool.**
- **Not a stateful flow tester.** Create → read → update → delete has no ordering model here and
  stays hand-written.
- **Not a mocking framework.** Real HTTP, real environment, real data.

## Requirements

Read these before evaluating — they are firm for v1, and they rule InTest out for some teams.

| | |
|---|---|
| Test project TFM | `net10.0`. Independent of your API's TFM — an API on `net8.0` is fine |
| Test framework | **MSTest only in v1.** xUnit and NUnit are the highest-priority v2 work, and the architecture is built to keep them additive — but today, if you are standardised on either, InTest is not for you yet |
| Spec | OpenAPI 3.x, JSON or YAML, local file or URL. **Today: JSON, local file only** |
| Target | A deployed, reachable API |

## What day one actually looks like

Worth knowing before you start, because it surprises people.

```bash
intest generate          # reports missing/stale fixtures, exits non-zero
intest fixtures repair   # creates and updates them
```

`intest fixtures repair` creates a fixture for every operation that needs a request body or a
required path/query parameter. Where your spec provides an `example` or a `default`, that value
is real. Where it does not, InTest emits an obvious `TODO:` placeholder — **and the test fails
until a human replaces it.**

That is deliberate. The alternative is filling in plausible-looking junk (`"string"`, `0`),
which a permissive endpoint accepts, so the suite passes while asserting nothing. A red test
gets fixed; a green test that proves nothing never does.

In practice that means, on an API with lots of POSTs and few spec examples, your first run after
`fixtures repair` is mostly red and there is real work to do. Two things make that manageable:

- Run `intest survey` **before** adopting — it will tell you what fraction of operations carry
  examples, so you can size the work in advance instead of discovering it. (Designed, not yet
  built.)
- A useful suite runs immediately with no fixture work at all: every GET and DELETE contract
  test, every declared-error test (404s), and every no-token 401 test needs no body.

## Using it

Working today:

```bash
intest init --name Orders.ApiTests --spec ../Orders/bin/Debug/net10.0/orders.json
intest generate
intest fixtures repair
dotnet test
intest generate --check            # CI: fail if committed output is stale
intest upgrade                     # adopt a new tool version deliberately
```

Designed, not yet built:

```bash
intest survey "specs/**/*.json"    # size the work before adopting
```

Generated code lands in `Generated/` and is regenerated wholesale. Your code lives in
same-named partial classes outside it, and InTest never touches those. Request bodies and
path/query parameters live in `fixtures/`, which only `fixtures repair` writes to.

**Full walkthrough:** [docs/getting-started.md](docs/getting-started.md) — from an existing API
to a suite running as a post-deployment gate, including CI wiring and the things that bite.

## Design principles

1. **You own the output.** A full test project, committed, readable, editable.
2. **Generation happens at pull-request time**, never in the deployment pipeline. Bad output
   fails on the PR where someone can fix it.
3. **Fail loudly.** Placeholder data causes a clear failure with a name attached. There are no
   skip flags and no silent green.
4. **Prefer the framework's own mechanism.** Parallelization, timeouts, retries and filtering
   are MSTest's job, not InTest's.
5. **Stable dependencies only.** No preview packages, and no dependency carrying a licence
   obligation you would inherit.

## Contributing

Issues and pull requests are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md). A careful
reading of the spec remains among the most valuable contributions: prior review rounds caught
contradictions, a build-breaking interaction, and a correlation identifier that silently
collapsed across data-driven test rows, all before any of it was written.

## Security

To report a vulnerability, see [SECURITY.md](SECURITY.md). Please do not open a public issue
for one.

## Licence

MIT — see [LICENSE](LICENSE).

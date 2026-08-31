# InTest

[![Build and test](https://github.com/Tjayojo/intest/actions/workflows/build-and-test.yml/badge.svg?branch=main)](https://github.com/Tjayojo/intest/actions/workflows/build-and-test.yml)

Generates a complete, owned .NET test project that exercises a deployed API over real HTTP,
from its OpenAPI document.

The output is a normal MSTest, xUnit or NUnit project — your choice, made once at `init` and
frozen per project. You commit it and edit it like any other test project; an MSTest or NUnit
project runs with `dotnet test`, an xUnit v3 one runs as a built executable (see
[`docs/getting-started.md`](docs/getting-started.md) Phase 6 for why xUnit differs). InTest is a
development-time tool — it generates on your machine or in a pull request, never as part of the
deployment pipeline.

> **Status: v0. Working, but early — `0.1.0-preview.1` is published to nuget.org as a prerelease.**
>
> `intest init`, `intest generate` and `intest fixtures repair` work: together they produce a
> compiling MSTest project whose contract tests pass against a live API. That has been verified
> against three sample APIs, one per OpenAPI producer — see
> [`docs/v0-acceptance.md`](docs/v0-acceptance.md). Catalog and Inventory pass in full, twice
> each, against an unreset store. Orders — the one sample with declared `security` — generates 24
> tests: **0 failed, 4 skipped, 20 passed**. The 4 skips are the wrong-scope 403 cases whose
> secondary identity already holds the scope the operation requires — a runtime guard
> (`RequireSecondaryIdentityLacks`) skips each with a stated reason instead of letting it run
> against a request that identity is genuinely authorized for (**F11, closed**). The 3
> write-scope 403s — the cases the sample's identity pair can actually prove — ran and passed.
> `intest fixtures repair` creates and maintains the fixture files under `fixtures/` that supply
> request bodies and path/query parameters, so an operation that needs one no longer generates a
> test that cannot send it — see "What day one actually looks like" below.
>
> `intest generate --check` and `intest upgrade` also work end to end, but neither has its own
> entry in `docs/v0-acceptance.md` yet — that acceptance run predates both commands, and
> extending it to cover them is still open work. Until then, "verified against three sample APIs"
> is a claim about `init`/`generate`/`fixtures repair` only.
>
> **A URL `spec.source` now works** (§9): `generate` fetches the document, writes it into the
> project as a committed `spec.json` snapshot, and everything downstream — `generate --check`,
> `fixtures repair` — reads that snapshot rather than the network, so CI stays hermetic. JSON
> only; the fetch is anonymous, so an authenticated Swagger endpoint still needs the fetch-it-
> yourself route below. This has unit and end-to-end coverage but has **not** been through an
> acceptance run against a live Swagger endpoint, so it is not in `docs/v0-acceptance.md`.
>
> **Not yet built:** variation tests, `intest survey`, `intest fixtures promote`,
> `intest assertions add`, `intest generate --emit-plan`, and YAML input — from a file or a URL
> alike; a URL serving YAML is refused by name rather than failing as a parse error.
>
> **Installable today:** `InTest.Cli` and `InTest.Runtime` `0.1.0-preview.1` are live on
> nuget.org — `dotnet tool install -g InTest.Cli --version 0.1.0-preview.1` resolves and
> installs cleanly (verified by installing it into a scratch directory right after the push
> went live). This is a v0 prerelease: breaking changes are still expected before a `0.1.0`
> stable release. Building from source is still how you get anything past that tag. The three
> adapter packages a generated project actually references — `InTest.Runtime.MSTest`,
> `InTest.Runtime.xUnit` and `InTest.Runtime.NUnit`, split out of `InTest.Runtime` so no adapter
> ever pulls another test framework in transitively — are not published yet; build from source to
> try any of them.
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
| Test framework | **MSTest, xUnit v3 or NUnit**, chosen with `init --framework` (defaults to `mstest`) and frozen per project — a suite cannot be migrated in place |
| Spec | OpenAPI 3.x, JSON or YAML, local file or URL. **Today: JSON only** — a local file, or a URL InTest can reach anonymously |
| Target | A deployed, reachable API |

## What day one actually looks like

Worth knowing before you start, because it surprises people.

```bash
dotnet intest generate          # reports missing/stale fixtures, exits non-zero
dotnet intest fixtures repair   # creates and updates them
```

Shown as `dotnet intest …`, not bare `intest`, because these run inside a project `init` already
scaffolded — see "Using it" below for why the invocation changes at that point.

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
- A useful suite runs on day one with the least hand-editing: every GET and DELETE contract
  test, every declared-error test (404s), and every no-token 401 test needs no request body, and
  any parameter the spec already gives an `example` or `default` for arrives filled, with no
  `TODO:` sentinel to fix by hand. `fixtures repair` still creates a fixture **file** for every
  operation that has a parameter at all, whether or not that file ends up needing an edit.

## Using it

Working today:

```bash
intest init --name Orders.ApiTests --spec ../Orders/bin/Debug/net10.0/orders.json
dotnet intest generate                    # exits non-zero on a first run — see below
dotnet intest fixtures repair
dotnet intest generate                    # regenerate now that fixtures exist
dotnet test
dotnet intest generate --check            # CI: fail if committed output is stale
dotnet intest upgrade                     # adopt a new tool version deliberately
```

`init` is the one command above shown bare: it is what creates the local tool manifest
(`.config/dotnet-tools.json`) every command after it restores against, so there is nothing yet
for `init` itself to resolve through. From `generate` on, `dotnet intest …` is what actually
runs — bare `intest` is only on `PATH` after a **global** install, and `init` scaffolds a
**local** one instead, so CI and your machine restore the identical pinned version (`dotnet tool
restore`, confirmed against a real restore, cross-shell — see
[`docs/getting-started.md`](docs/getting-started.md), Phase 2 and Phase 8, for the full
explanation and evidence).

Designed, not yet built:

```bash
intest survey "specs/**/*.json"    # size the work before adopting
```

Shown bare, unlike everything in "Using it" above: there is no project yet at this point, so no
local manifest either — `survey` runs pre-adoption against whatever a **global**
`dotnet tool install -g InTest.Cli` put on `PATH`, the same shape shown in
[`docs/getting-started.md`](docs/getting-started.md)'s Phase 0.

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
   are the chosen framework's job, not InTest's.
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

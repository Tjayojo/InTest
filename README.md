# InTest

[![Build and test](https://github.com/Tjayojo/intest/actions/workflows/build-and-test.yml/badge.svg?branch=main)](https://github.com/Tjayojo/intest/actions/workflows/build-and-test.yml)

Generates a complete, owned .NET test project that exercises a deployed API over real HTTP,
from its OpenAPI document.

The output is a normal MSTest, xUnit or NUnit project — your choice, made once at `init` and
frozen per project. You commit it and edit it like any other test project. InTest is a
development-time tool: it generates on your machine or in a pull request, never as part of the
deployment pipeline.

```bash
dotnet tool install -g InTest.Cli --prerelease
```

> **Status: v0 prerelease.** Working, but early — breaking changes are expected before a `0.1.0`
> stable release. See [Status and limits](#status-and-limits) below for what is proven, what is
> unproven, and what does not exist yet.

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

## Quickstart

```bash
intest init --name Orders.ApiTests --spec ../Orders/bin/Debug/net10.0/orders.json
dotnet intest generate                    # exits non-zero on a first run — see below
dotnet intest fixtures repair             # create the fixtures it asked for
dotnet intest generate                    # regenerate now that fixtures exist
dotnet test                               # an xUnit v3 suite runs as an executable instead
dotnet intest generate --check            # CI: fail if committed output is stale
dotnet intest upgrade                     # adopt a new tool version deliberately
```

An MSTest or NUnit suite runs with `dotnet test`; an xUnit v3 one runs as a built executable —
[`docs/getting-started.md`](docs/getting-started.md) Phase 6 explains why, and what to run.

**Why `init` is bare and everything after it is `dotnet intest`.** `init` scaffolds a *local*
tool manifest (`.config/dotnet-tools.json`), so from `generate` on, commands resolve through it —
that is what pins CI and your machine to the identical version. Bare `intest` is only on `PATH`
after a **global** install. Full explanation in
[`docs/getting-started.md`](docs/getting-started.md), Phases 2 and 8.

Generated code lands in `Generated/` and is regenerated wholesale. Your code lives in same-named
partial classes outside it, and InTest never touches those. Request bodies and path/query
parameters live in `fixtures/`, which only `fixtures repair` writes to.

## What day one actually looks like

Worth knowing before you start, because it surprises people.

`intest fixtures repair` creates a fixture for every operation that needs a request body or a
required path/query parameter. Where your spec provides an `example` or a `default`, that value
is real. Where it does not, InTest emits an obvious `TODO:` placeholder — **and the test fails
until a human replaces it.**

That is deliberate. The alternative is filling in plausible-looking junk (`"string"`, `0`),
which a permissive endpoint accepts, so the suite passes while asserting nothing. A red test
gets fixed; a green test that proves nothing never does.

In practice that means, on an API with lots of POSTs and few spec examples, your first run after
`fixtures repair` is mostly red and there is real work to do.

What still lands green on day one, with no hand-editing: every GET and DELETE contract test,
every declared-error test (404s), and every no-token 401 test needs no request body, and any
parameter the spec already gives an `example` or `default` for arrives filled. `fixtures repair`
still creates a fixture **file** for every operation that has a parameter at all, whether or not
that file ends up needing an edit.

## Learn more

- **[Full walkthrough](docs/getting-started.md)** — from an existing API to a suite running as a
  post-deployment gate, including CI wiring and the things that bite.
- **[Worked examples](examples/)** — the generated output of two sample APIs, Catalog and Orders,
  each committed under all three frameworks. Every one references its adapter package from
  nuget.org rather than this repository's source, and CI builds and `--check`s all six on every
  pull request and every push to `main`.

## Status and limits

**Published.** All five packages — `InTest.Cli`, `InTest.Runtime`, and the three adapters
`InTest.Runtime.MSTest`, `InTest.Runtime.xUnit` and `InTest.Runtime.NUnit` — are on nuget.org at
`0.1.0-preview.2`. A generated project references whichever adapter matches its framework and
gets `InTest.Runtime` transitively at the same version. Building from source is how you get
anything past that tag.

**Proven against live APIs.** `init`, `generate` and `fixtures repair` produce a compiling project
whose contract tests pass over real HTTP against three sample APIs, one per OpenAPI producer.
Under MSTest, Catalog and Inventory pass in full; Orders — the one sample declaring `security` —
generates 24 tests, of which 20 pass and 4 skip with a stated reason, because the sample's
identity pair cannot produce the 403 those four assert. xUnit and NUnit reproduce the Catalog and
Orders results exactly. The full record, including every defect found and how, is in
[`docs/v0-acceptance.md`](docs/v0-acceptance.md).

**Works, but not yet covered by an acceptance run.** `generate --check`, `intest upgrade`, and a
URL `spec.source`. The URL path fetches the document once and commits it as a `spec.json`
snapshot, so `--check` and `fixtures repair` read that snapshot rather than the network and CI
stays hermetic. JSON only, and the fetch is anonymous — an authenticated Swagger endpoint still
needs you to fetch the document yourself.

**Not built yet.** Variation tests, `intest survey`, `intest fixtures promote`,
`intest assertions add`, `intest generate --emit-plan`, and YAML input from a file or a URL alike
(a URL serving YAML is refused by name rather than failing as a parse error).

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

Issues and pull requests are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md). The
[design spec](docs/superpowers/specs/2026-08-16-intest-api-test-generator-design.md) is the
source of truth for why things are built the way they are, and a careful reading of it remains
among the most valuable contributions: prior review rounds caught contradictions, a
build-breaking interaction, and a correlation identifier that silently collapsed across
data-driven test rows, all before any of it was written.

## Security

To report a vulnerability, see [SECURITY.md](SECURITY.md). Please do not open a public issue
for one.

## Licence

MIT — see [LICENSE](LICENSE).

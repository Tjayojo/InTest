# URL `spec.source` — §9's committed `spec.json` snapshot

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `spec.source` accept an `http(s)://` URL, as §2 has always promised and §9 has always
designed. `generate` fetches the document, writes it into the project as a committed,
generator-owned `spec.json`, and everything downstream reads that snapshot. Remove the refusal
(`SpecLoader.IsUrl`/`UrlReason` at `InitCommand.cs:166` and `ConfigLoader.cs:129`) that stands in
for the capability today, and remove the "not built" hedges from the four documents that carry them.

**Scope:** JSON only, over an anonymous GET. YAML — from a file *or* a URL — stays unbuilt and is
explicitly out of scope; `SpecLoader.LoadFromTextAsync` already hard-codes `"json"` as the parse
format, and a URL serving YAML gets a targeted refusal rather than a generic parse error
(`[json-only]`).

**Architecture:** The refusal was always a placeholder for one seam. `SpecLoader` reads a spec from
text and from a file; this plan adds fetching as a sibling concern (`SpecFetcher`) rather than a
third `LoadFrom*` overload, because HTTP policy — timeout, size cap, status handling, content-type
sniffing — is not parsing and does not belong in the parser. A second new type (`SpecSnapshot`)
owns the on-disk snapshot: its name, its byte shape, and the one reprint function that makes
`--check` stable. Everything else is a routing change at four call sites that today all read
`Path.Combine(projectRoot, config.SpecSource)`.

**Tech Stack:** .NET 10 / C#, `System.Net.Http` (BCL — **no new package**, so the dependency policy
is untouched), `System.Text.Json`, MSTest, Shouldly.

**Prerequisite:** this branch at `2c51c3a` or later. Measure the baseline yourself before starting
(`dotnet test InTest.sln`) — Cli was **410 passing, 0 failing** at time of writing; do not trust
that number if anything else has landed since.

---

## Decisions

Named with slugs per `CONTRIBUTING.md`'s "Writing plans" — insertion and reordering cannot break a
slug. All five were put to the repository owner before implementation began and confirmed; the
alternatives recorded here are the ones actually considered, not hypotheticals invented afterwards.

### `[snapshot-is-input]` — the snapshot is written on parse, *before* the fixture-drift gate

`CLAUDE.md` states that `generate` "detects fixture drift **before** writing anything and exits
`1`". Taken literally, that invariant deadlocks a URL source:

```
spec changes upstream
  → generate fetches, sees new required property, reports drift, exits 1  (snapshot NOT written)
  → fixtures repair reads the OLD snapshot, repairs against the OLD spec
  → generate fetches, sees the same drift, exits 1                        (forever)
```

So the snapshot is written as soon as the fetched document **parses**, before `TestPlanBuilder.Build`
and before the drift check. The invariant narrows rather than breaks, and the narrowing is the point:
`generate` writes no *generated output* — nothing under `Generated/`, no `coverage-report.json` —
before the drift gate. `spec.json` is not generated output. It is the **materialized input**: the
bytes the rest of the run reasons from, which for a local source already exist on disk before
`generate` is invoked at all. Writing it early puts a URL source in exactly the state a local source
is always in.

Two alternatives were considered and rejected:

- **Write it with the other outputs; have `fixtures repair` fetch too.** Keeps the invariant intact
  word-for-word, but gives `repair` a network dependency it has never had, doubles the fetches per
  cycle, and introduces a window where `generate` and `repair` plan against two *different* upstream
  revisions — a skew that would surface as drift that repair cannot fix.
- **Write it with the other outputs; let repair read the stale snapshot.** Simplest code, ships the
  deadlock, and its only escape hatch is "delete `spec.json` and re-run", which no message could
  reasonably be expected to teach.

### `[no-refetch]` — only write-mode `generate` ever performs a fetch

`generate --check` and `fixtures repair` read the committed snapshot and never open a socket. §9 is
explicit for `--check` ("CI stays hermetic and does not depend on the service being reachable"), and
the same reasoning covers `repair`: a command that only writes `fixtures/` has no business deciding
what the spec now says. Refreshing is `generate`'s job, deliberately, on a branch, where the
resulting `spec.json` diff is reviewable.

This also keeps `--check`'s [no-write] seam honest. `BuildOutputs` deliberately does **not** gain a
`spec.json` entry: in check mode the snapshot *is* the input the outputs were rendered from, so
comparing it against itself would be a tautology dressed up as a guarantee. The orphan sweep is
unaffected — it walks `Generated/` only, and `spec.json` sits at the project root next to
`coverage-report.json`.

### `[fail-closed]` — a failed fetch is exit 2, never a silent fall back to the committed snapshot

If `generate` cannot reach the URL, it fails with exit 2 and writes nothing, even when a perfectly
good `spec.json` is sitting there. Falling back would make "I regenerated against the current spec"
and "I regenerated against whatever I had lying around" produce identical output and identical exit
codes — the quiet-green failure mode `README.md`'s "Fail loudly" principle exists to reject. The
message names the URL, says what went wrong, and points at `--check` for the path that is *supposed*
to be hermetic.

No `--no-fetch` flag ships with this plan. It is a real want for an air-gapped build, but it needs
its own row in §5's command table and its own exit-code semantics, and adding it speculatively
alongside the capability it modifies is how a flag ends up with no test behind it.

### `[anonymous]` — unauthenticated GET only

No headers, no token, no credential path through the CLI. A `401`/`403` gets its own message telling
the adopter to fetch the document by hand and commit it — which is exactly the workaround
`docs/getting-started.md` Phase 1 documents today, so the fallback is already written and already
true. Rejected for now: an `INTEST_SPEC_TOKEN` env var (a credential path deserves its own change,
with its own "never log this" review) and a `spec.headers` map in `intest.json` (invites secrets into
a committed file, cutting directly against getting-started's own "anything with a credential in it"
ignore guidance).

### `[any-address]` — no address is blocked, and the containment is stated rather than assumed

`spec.source` may name `localhost`, a private range, or a link-local address such as a cloud
metadata endpoint, and `generate` will fetch it. Blocking those was considered and rejected:
`http://localhost:5001/swagger.json` is the single most common shape this feature will ever be
pointed at, so a private-range block would break the loopback and intranet cases that are the whole
reason most teams want a URL source.

The reason that is acceptable is worth writing down, because "we thought about it" and "it happened
to be fine" are indistinguishable a year later:

- `spec.source` is a value the adopter writes into their own `intest.json`. It is not
  attacker-supplied input, and only a developer running `generate` on a branch triggers a fetch —
  `--check` and `fixtures repair` never do (`[no-refetch]`).
- The response must parse as an OpenAPI document declaring at least one operation *before* anything
  is written (`[snapshot-is-input]` puts the parse strictly before the write), so a metadata
  endpoint's credentials response cannot come to rest in a committed `spec.json`.
- No failure message echoes the response body; a failed fetch reports a status code.

This stops holding the moment InTest gains authenticated fetching (`[anonymous]` is the current
decision) or fetches from a value it did not get from the adopter. At that point an allowlist is
the right answer, and this decision should be reopened deliberately rather than rediscovered.

### `[reprinted]` — the snapshot is re-emitted indented, CRLF, with the relaxed encoder

The fetched bytes are not written verbatim. They are parsed and re-emitted through a dedicated
`JsonSerializerOptions`, because §9's entire justification for the snapshot is that "a spec change
arrives as a reviewable diff" — and Swagger endpoints overwhelmingly serve **minified** JSON. A
200 KB single-line `spec.json` has a diff of exactly zero review value, which would leave the feature
technically working and practically pointless.

Four properties were confirmed by direct experiment (not inferred from the API surface) before this
was chosen — see `SpecSnapshotTests`, which pins each one:

| Property | Why it matters | Result |
|---|---|---|
| Number fidelity | `1.0`, `0.1000`, `1e10` and a 23-digit integer must not be rewritten — a spec's `multipleOf`/`maximum` are load-bearing | Preserved exactly; `JsonElement.WriteTo` emits raw number text |
| Idempotence | reprint(reprint(x)) == reprint(x), or `--check` oscillates forever | Holds |
| String round-trip | no character is lost or altered | Holds |
| Readability | `café & <tag>` must stay `café & <tag>` | Needs `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` — the **default** encoder produces `café & <tag>` |

That last row is why this cannot simply reuse `CommittedJsonOptions.Value`. It is also not a new
argument: `InitCommand.JsonSpecSource` already chose the relaxed encoder for the identical reason
(a value adopters read by hand), and its doc comment already carries the reasoning. The second
instance therefore lives **in `CommittedJsonOptions.cs` itself**, beside the first — which keeps
`JsonWritingOptionsGuardTests` green *by construction* rather than by adding an exemption, and keeps
"one home for committed-JSON writer options" true. Do **not** change `CommittedJsonOptions.Value`'s
own encoder: that would re-encode `coverage-report.json`, `spec-schemas.json`, `spec-paths.json` and
every fixture, breaking golden files for a reason unrelated to this plan.

---

## What does *not* change

- **`BuildOutputs` and the [no-write] seam.** No new entry, no signature change. See `[no-refetch]`.
- **`CommittedJsonOptions.Value`.** A sibling instance is added in the same file; the existing one is
  untouched. See `[reprinted]`.
- **`<Content Include="$(InTestSpecSource)" Link="spec.json" …>`.** §9's *build-time copy* of the
  spec to the output directory is **not built today** — the scaffolded `.csproj` sets the
  `<InTestSpecSource>` property but no `Content` item consumes it, and `TestHost` reads
  `spec-schemas.json`, never `spec.json`. That gap is real but pre-existing and orthogonal; this plan
  does not close it. It only makes the property *correct* for a URL project (Task 4) so that
  whenever the copy does land, it needs no second fix.
- **`scripts/ci/dogfood.ps1`.** Static-only, three local sample specs, no live API. A URL case would
  give CI a network dependency for no coverage a unit test cannot provide.
- **`SpecLoader.LoadFromTextAsync`'s `"json"` parse format.** Out of scope per `[json-only]`.
- **`docs/v0-acceptance.md`.** A dated record of what was measured under a build that refused URLs.
  Leave it; this plan is the record for the capability landing.

---

## Task 1: `SpecFetcher` — the HTTP half

**Files:** `src/InTest.Cli/Spec/SpecFetcher.cs` (new),
`tests/InTest.Cli.Tests/SpecFetcherTests.cs` (new).

- [ ] **Step 1: Write `SpecFetcher`**

A static class in `InTest.Cli.Spec`. One entry point:

```csharp
public static async Task<string> FetchAsync(
    string url, HttpMessageHandler? transport, CancellationToken cancellationToken)
```

`transport` is the `[transport]` seam: `null` in production (construct a
`SocketsHttpHandler`/`HttpClientHandler` and own it), injected by tests. Document it as test-only.
Do not reach for `IHttpClientFactory` — the CLI has no DI container and one fetch happens per
process.

Policy, each with a named constant and a comment saying why the number is what it is:

- **Timeout** 30 s. `HttpClient`'s 100 s default is a very long time to look hung.
- **Max size** 32 MiB, enforced against `Content-Length` *and* while reading (a chunked response
  declares no length). Roughly 10× the largest real-world OpenAPI documents.
- **Redirects** follow (default). .NET refuses `https:` → `http:` redirects on its own; state that
  as the reason no custom policy is written, and pin it in a test.

Every failure throws `SpecLoadException` — already caught by `generate` and mapped to exit 2 — with
an adopter-facing message in the shape every refusal in this repository uses: name the setting, quote
what was written, say what is wrong, then the remedy. Cases, each with its own sentence:

| Case | Message must |
|---|---|
| Not a well-formed absolute http(s) URI | quote it, say what a URL looks like |
| DNS / connection / TLS failure | name the URL and the underlying reason |
| Timeout | name the URL and the timeout, and distinguish itself from cancellation |
| `401` / `403` | say auth is not supported yet and give the `curl -o` + local-path workaround verbatim from getting-started Phase 1 |
| `404` and other non-2xx | name the status code and reason phrase |
| Body over the cap | name the cap and the observed size |
| Empty body | say so — an empty 200 is otherwise a confusing parse error |
| Content-Type names YAML, or body starts `openapi:`/`swagger:` at column 0 | name YAML explicitly and say it is not supported yet (`[json-only]`) |

Cancellation (`cancellationToken` actually signalled) must propagate as `OperationCanceledException`,
**not** be relabelled as a timeout. `HttpClient` surfaces both as `TaskCanceledException`; branch on
`cancellationToken.IsCancellationRequested`.

- [ ] **Step 2: Write `SpecFetcherTests`**

Drive every row above through a stub `HttpMessageHandler`. Assert the *message*, not just the throw —
and assert `ShouldNotContain("unexpected failure")` on each, matching `ConfigLoaderTests`' house rule
for the same reason: exit code alone would pass against the defect these messages exist to fix.

Add the happy path (200 + JSON body returns the body verbatim), the `https`→`http` redirect refusal,
and a cancellation test proving it is not reported as a timeout.

---

## Task 2: `SpecSnapshot` — the on-disk half

**Files:** `src/InTest.Cli/Spec/SpecSnapshot.cs` (new),
`src/InTest.Cli/Json/CommittedJsonOptions.cs` (edit),
`tests/InTest.Cli.Tests/SpecSnapshotTests.cs` (new).

- [ ] **Step 1: Add the sibling options instance**

In `CommittedJsonOptions.cs`, beside `Value`:

```csharp
public static readonly JsonSerializerOptions SpecSnapshot = new()
{
    WriteIndented = true,
    NewLine = "\r\n",
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};
```

Document why it is a second instance rather than a change to `Value` (it would re-encode four other
committed artefacts) and why it lives in this file rather than next to its caller (so
`JsonWritingOptionsGuardTests` stays green by construction — the guard allows the `WriteIndented`
marker in this file and nowhere else). Extend the existing type-level doc comment to say there are
now two instances and what separates them.

- [ ] **Step 2: Write `SpecSnapshot`**

```csharp
public const string FileName = "spec.json";
public static string Reprint(string json)   // throws SpecLoadException if not JSON at all
```

`Reprint` parses with `JsonDocument`, re-emits via
`JsonSerializer.Serialize(document.RootElement, CommittedJsonOptions.SpecSnapshot)`, and appends the
trailing `"\r\n"` by hand — `WriteIndented` never emits a line ending after the final closing brace,
the same footnote every other writer in this repo carries.

A `JsonException` here means the response was not JSON. Route it into the same YAML-aware message
`SpecFetcher` uses, so a YAML endpoint gets the same sentence regardless of which layer noticed.

- [ ] **Step 3: Write `SpecSnapshotTests`**

Pin all four experimentally-confirmed properties from `[reprinted]`'s table as named tests — number
fidelity (`1.0`/`0.1000`/`1e10`/23-digit int survive as raw text), idempotence, string round-trip,
and readability (`café & <tag>` stays literal; assert `ShouldNotContain("\\u00E9")`, which is the
assertion that fails if someone later "simplifies" this onto `CommittedJsonOptions.Value`).

Add: minified input becomes multi-line; interior line endings are CRLF; the file ends in exactly one
CRLF; a non-JSON body throws with a message naming YAML.

---

## Task 3: `ConfigLoader` — accept a URL, and carry the verdict

**Files:** `src/InTest.Cli/Configuration/ConfigLoader.cs`,
`src/InTest.Cli/Configuration/LoadedConfig.cs`,
`tests/InTest.Cli.Tests/ConfigLoaderTests.cs`.

- [ ] **Step 1: Replace the refusal with validation**

Delete the `SpecLoader.IsUrl(specSource)` → `throw` block (`ConfigLoader.cs:129`). Replace it with a
*well-formedness* check on the URL branch only: a value that starts `http(s)://` but is not a
well-formed absolute URI (`https://` alone, say) is still refused — it just gets a message about
being a malformed URL rather than about URLs being unsupported. Keep the empty-value check exactly as
it is, and keep its comment.

Update `SpecSourceRule` and `SpecSectionRule` to say a path **or** an `http(s)://` URL is accepted,
with an example of each.

- [ ] **Step 2: Carry the verdict on `LoadedConfig`**

Add `bool SpecSourceIsUrl` to the record, computed once in `Load`. Every consumer reads the flag;
none re-derives it by calling `SpecLoader.IsUrl` again.

This is `CLAUDE.md`'s stated rule, not a style preference: "`TestCasePlan` deliberately **carries**
verdicts computed elsewhere rather than letting downstream code re-derive them. Re-deriving is the
recurring defect in this codebase — don't." Three call sites need this answer; one place decides it.

- [ ] **Step 3: Update the tests**

`ExplainsAUrlSpecSourceRatherThanReportingAMangledPathAsAMissingSpec` is now testing a refusal that no
longer exists — **replace** it (do not delete the coverage) with a test that a URL `spec.source` loads
and sets `SpecSourceIsUrl`. Rewrite its doc comment: the historical defect it records is still worth
keeping as the reason the value is validated at all, but it must no longer read as though the refusal
is current.

Keep `LoadsASpecSourceThatIsNotAUrl` and its four rows exactly as they are — the Windows-path false
positive it guards is *more* load-bearing now, not less, because `IsUrl` has been promoted from
"which error message" to "which code path". Extend it to assert `SpecSourceIsUrl` is `false`. Add a
malformed-URL refusal test.

---

## Task 4: `InitCommand` — scaffold a URL project

**Files:** `src/InTest.Cli/Commands/InitCommand.cs`, `src/InTest.Cli/Program.cs`,
`tests/InTest.Cli.Tests/InitCommandTests.cs`.

- [ ] **Step 1: Replace the refusal**

Delete the `SpecLoader.IsUrl(normalizedSpecSource)` → refuse block (`InitCommand.cs:166`), keeping
its long comment's *historical* half (the measured "exited 0 and wrote the whole scaffold" defect is
why the value is validated before the first write at all) and dropping its "not built" half. Refuse
a malformed URL through the same shared validator Task 3 Step 1 uses.

`init` does **not** fetch. It records the URL and exits; `generate` takes the snapshot. This keeps
`init` offline, which is what makes scaffolding work before the API is reachable.

- [ ] **Step 2: Point `<InTestSpecSource>` at the snapshot**

For a URL source the property must hold `spec.json` — the local snapshot — not the URL. MSBuild
cannot copy from `https://`, and §9 is explicit that the `.csproj` copies "that local file … exactly
as above". `intest.json`'s `spec.source` still holds the URL: that is the *source*, and `spec.json`
is the *materialization*.

The `\\`→`/` normalization must not run on a URL branch value in a way that alters it; a backslash is
not valid unescaped in a URL path, but assert the value round-trips unchanged rather than assuming.

- [ ] **Step 3: Pin the snapshot in `.gitattributes`**

Add `spec.json text eol=crlf` to `GitattributesContent`. It is a committed, InTest-written, pure-CRLF
file — exactly the category the existing three lines cover.

Note in the comment (and in Task 7's docs) the one gap this leaves: `UpgradeCommand` writes
`.gitattributes` only when the project has none, never overwriting. A project scaffolded before this
change that later switches to a URL source keeps a `.gitattributes` with no `spec.json` line. That is
tolerable precisely because nothing byte-compares `spec.json` (`[no-refetch]` keeps it out of
`BuildOutputs`) — a checkout that flattens it to LF costs a noisy diff, not a wrong result. Say so;
do not silently rely on it.

- [ ] **Step 4: Fix the help text and the remedy**

`Program.cs`'s `--spec` description currently reads "Path of the OpenAPI document, relative to the
test project directory." It becomes path **or** URL. This sentence has history — it said "Path or
URL" while URLs were refused, which is the documentation-ahead-of-the-build defect
`SpecLoader.UrlReason` was written to apologise for. It is finally true; make it true carefully.
Update `InitCommand.SpecRemedy` the same way, with both example shapes.

- [ ] **Step 5: Update the tests**

Replace `InitCommandTests`' URL-refusal rows (`https` / `http` / `HTTPS://EXAMPLE.COM/...`) with
acceptance: exit 0, `intest.json` carries the URL verbatim, `<InTestSpecSource>` is `spec.json`,
`.gitattributes` carries the new line. Keep a refusal test for a *malformed* URL. Keep every existing
escaping test untouched — Task 4 must not disturb `MSBuildPropertyValue` behaviour for path sources.

---

## Task 5: `GenerateCommand` — fetch, snapshot, and never re-fetch under `--check`

**Files:** `src/InTest.Cli/Commands/GenerateCommand.cs`,
`tests/InTest.Cli.Tests/GenerateCommandTests.cs`,
`tests/InTest.Cli.Tests/GenerateCheckCommandTests.cs`.

- [ ] **Step 1: Replace the single spec load with a resolve step**

`GenerateCommand.cs:176` is one line today. It becomes a branch on `config.SpecSourceIsUrl`:

- **local source** — unchanged, byte for byte.
- **URL + `check`** — read `spec.json`. If absent, report
  `spec.json is missing. Run 'intest generate' to fetch it.` and return `WorkOutstanding` (1), not 2:
  a missing snapshot is outstanding work a human must do, and it is reported in the same voice as
  every other `--check` difference. **No socket is opened on this path** (`[no-refetch]`).
- **URL + write** — fetch, reprint, parse the *reprinted* text, then write the snapshot.

Order within the write branch matters and must be commented as load-bearing: **fetch → reprint →
parse → write**. Parsing the reprinted text rather than the raw response is what guarantees the
document the plan is built from is byte-identical to what lands on disk; parsing *before* writing is
what guarantees an unparseable response never overwrites a good snapshot.

The write goes here — before `TestPlanBuilder.Build`, before `DetectFixtureDrift`. Comment it as
`[snapshot-is-input]` with the deadlock it prevents spelled out, because a future reader will
otherwise "fix" it back down beside the other writes and reintroduce the loop.

- [ ] **Step 2: Thread the `[transport]` seam**

Add `HttpMessageHandler? specTransport = null` as a trailing optional parameter on `RunAsync`,
documented as test-only, in the same style as the existing `TextWriter? report` seam. `UpgradeCommand`
calls `RunAsync` and passes nothing — it inherits the fetch, which is correct: `upgrade` regenerates,
and regenerating against a URL source means refreshing it.

- [ ] **Step 3: Tests — `GenerateCommandTests`**

- A URL source writes `spec.json`, indented, CRLF, one trailing newline.
- **The regression test for `[snapshot-is-input]`:** a run that exits 1 on fixture drift has
  *still written* the snapshot. Delete this behaviour and this test fails — it is the whole reason
  the write sits where it does.
- A fetch failure returns 2 and leaves an existing `spec.json` byte-identical (`[fail-closed]`).
- An unparseable response leaves an existing `spec.json` byte-identical.
- A local source never opens a socket: pass a transport that throws if called.

- [ ] **Step 4: Tests — `GenerateCheckCommandTests`**

- `--check` on a URL source with a transport that throws if invoked → passes, proving `[no-refetch]`
  mechanically rather than by reading the code.
- `--check` with no `spec.json` → exit 1 and the message above.
- `--check` still writes nothing on the URL path (the existing before/after snapshot assertions
  extended to cover `spec.json`).

---

## Task 6: `FixturesRepairCommand` — read the snapshot

**Files:** `src/InTest.Cli/Commands/FixturesRepairCommand.cs`,
`tests/InTest.Cli.Tests/FixturesRepairCommandTests.cs`.

- [ ] **Step 1: Resolve the spec path through the same rule**

`FixturesRepairCommand.cs:47` gets the same branch: URL → `spec.json`, local → the path. `repair`
never fetches (`[no-refetch]`).

When the source is a URL and no snapshot exists, throw `SpecLoadException` with a message that says
what to do — `intest generate` first, because it is the command that takes the snapshot — rather than
letting `LoadFromFileAsync` report `Spec file not found: <projectRoot>/spec.json`, which names a file
the adopter never wrote and never asked for. This is the same defect class the deleted `UrlReason`
existed to fix; do not reintroduce it one file over.

- [ ] **Step 2: Tests**

Repair against a URL project with a snapshot present creates fixtures normally; with no snapshot it
exits 2 with a message naming `intest generate` and **not** containing `Spec file not found`.

---

## Task 7: Documentation

**Files:** `README.md`, `docs/getting-started.md`, `CLAUDE.md`,
`docs/superpowers/specs/2026-08-16-intest-api-test-generator-design.md`.

Per `CLAUDE.md`'s working conventions the spec is the source of truth and is updated in the same
change as the behaviour; getting-started is updated whenever the adoption path changes. Both apply.

- [ ] **Step 1: `README.md`** — remove "**and a URL `spec.source`**" from "Not yet built" along with
  its two-sentence explanation. Change the Spec requirements row from "Today: JSON, local file only"
  to JSON, local file or URL — YAML still unbuilt. Do not overclaim: this has not been through an
  acceptance run against a live Swagger endpoint, so say what was tested.

- [ ] **Step 2: `docs/getting-started.md`** — rewrite Phase 1's "Different repository, or only a URL"
  block (line ~97): the "**Not built — do not do this yet**" admonition becomes the actual
  instructions, keeping the manual `curl` route as the documented fallback for an authenticated
  endpoint (`[anonymous]`). Fix line ~28 (roadmap list), line ~48 (requirements table), the commit
  table at ~496 (`spec.json` is committed — drop "not built yet"), and the paragraph at ~501 that
  states `spec.json` is never created. Add the `[fail-closed]` and `[no-refetch]` behaviours to
  Phase 8's CI section: `--check` needs no network, `generate` does.

- [ ] **Step 3: `CLAUDE.md`** — remove URL input from the "do not assume they do" list; add
  `spec.json` to the ownership table as generator-owned, written by `generate` on a URL source only;
  and narrow the "detects fixture drift **before** writing anything" sentence to name the
  `[snapshot-is-input]` exception, pointing at this plan for why.

- [ ] **Step 4: The design spec** — §9's "When `spec.source` is a URL" section loses its
  designed-not-built framing and gains what was actually decided: reprinting (`[reprinted]`),
  fail-closed (`[fail-closed]`), anonymous-only (`[anonymous]`), and JSON-only (`[json-only]`). §2's
  input row and §5's command table get the same treatment. Where §9 describes the build-time
  `Content Include` copy, add a one-line note that it remains unbuilt — the section currently reads
  as though it ships, and this plan is the first thing to depend on that distinction.

---

## Verification

Run from a directory **outside** the repository if the local SDK's feature band is below
`global.json`'s pin — the resolver searches upward from the working directory, so an outside cwd
bypasses it without editing a tracked file. Never edit `global.json` to make a local build work.

- [ ] `dotnet build InTest.sln` — clean, no new warnings (`TreatWarningsAsErrors=true`).
- [ ] `dotnet test tests/InTest.Cli.Tests` — 410 + new, 0 failing.
- [ ] `dotnet test tests/InTest.Architecture.Tests` — `PackageVersionCouplingTests` and
      `JsonWritingOptionsGuardTests` in particular, since Task 2 touches a guarded file.
- [ ] `dotnet test InTest.sln` — all four suites, Golden included. Golden is slow (~90–107 s) and is
      the only suite proving generated code compiles *and* runs; do not skip it.
- [ ] `git status` — confirm `global.json` is unmodified before every commit.

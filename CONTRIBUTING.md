# Contributing to InTest

Thanks for looking. InTest is a working tool with an incomplete command surface. `intest init`,
`generate`, `fixtures repair`, `generate --check` and `upgrade` run end to end today, with a
documented walkthrough in [`docs/getting-started.md`](docs/getting-started.md), and all five are
verified against live sample APIs — `init`/`generate`/`fixtures repair` by the v0/v1-a/v1-b/v1-c
runs, `generate --check`/`upgrade` by v1-e Task 6 — see
[`docs/v0-acceptance.md`](docs/v0-acceptance.md) for both. `survey`, `fixtures promote`,
`assertions add` and `generate --emit-plan` don't exist yet — that doc's own preamble tracks the
gap precisely, and is the source of truth if this file and it ever disagree.
`InTest.Cli`/`InTest.Runtime` `0.1.0-preview.1` are published to nuget.org as a prerelease
(see "Branching and how a release is cut" below); building from source is still how you try
anything past that tag. A third package, `InTest.Runtime.MSTest` — the MSTest adapter split out
of `InTest.Runtime`, described in "Releases" below — has not been published at all yet, so
`examples/` still pins the neutral `InTest.Runtime` package directly at `0.1.0-preview.1` rather
than the adapter it would reference once one exists on nuget.org. The
[design spec](docs/superpowers/specs/2026-08-16-intest-api-test-generator-design.md) remains the
reference for why things are built the way they are.

## The most useful contribution today

**Read the [design spec](docs/superpowers/specs/2026-08-16-intest-api-test-generator-design.md)
and tell us where it is wrong.**

It is long, and that is deliberate: it records not just decisions but the evidence behind them
and the alternatives rejected. Claims marked *measured* were established by building and
running code, not by reading documentation. If one of them is wrong, we want to know, and a
reproduction beats an assertion.

Reviews have already caught a build-breaking interaction between two documented MSTest
mechanisms, a correlation identifier that collapsed to one value across every data-driven test
row, and a validator gap that would have passed invalid responses silently. That kind of reading
is still the highest-leverage contribution: it catches defects a fresh implementation would only
rediscover later, and it costs nothing to build or run first.

## Ground rules for changes to the spec

The spec has conventions worth keeping:

- **Back claims with evidence.** If you assert a library behaves a certain way, say how you
  know. "The docs say" and "I ran this and got that" are both fine; they are just not the same
  thing, and the spec distinguishes them.
- **Record what was rejected and why.** §19 exists so decisions are not silently relitigated.
- **Prefer deletion.** Several revisions made the design smaller. Removing a contradiction beats
  documenting a workaround for it.
- **No capability may be gated on any one organisation's spec population.** InTest is used by
  people whose specs we cannot see. A survey informs priority; it never decides whether a
  feature exists. This has been violated twice and corrected twice — please do not reintroduce
  it.

## Writing plans

New implementation plans (`docs/superpowers/plans/`) name their decisions with short slugs —
`[containment]`, `[descriptor]` — rather than numbering them. Numbered decisions drifted three
times during v1-c, twice inside a single document: inserting a decision silently invalidates
every reference after it. A related failure has already cost a commit of its own: `1448570`
("docs: disambiguate v1-a and v1-b decision references") had to qualify every bare "decision N"
in `src/` and `tests/` as "v1-b decision N", because decision numbering restarts in each plan and
a bare number does not say which plan it belongs to. That is a different failure mode than
reference drift within one document — but it is the same class, numbered decision references
going wrong, and it is an additional argument for slugs rather than a restatement of the first
one: a slug is unique across plans in a way `3` never is.

F11's plan named its decisions instead — `[containment]`, `[descriptor]`,
`[unknown-runs]`, `[counted]`, `[sample-unchanged]` — and had zero reference drift across 29
commits and several rounds of insertions. A slug is a word that insertion and reordering cannot
break; a number is not.

**Do not retrofit this onto plans that are already done.** `2026-08-17-intest-v1a-fixtures.md`,
`2026-08-18-intest-v1b-fixture-lifecycle.md` and `2026-08-19-intest-v1c-error-and-auth-tests.md`
still number their decisions, and that is correct as-is — leave them numbered. The drift risk
only exists while a plan is still being edited; a finished plan is never renumbered again, so the
risk it closed against is already zero, and renaming its decisions now would be pure churn
against a document whose entire value is being an accurate record of what was decided when. This
is the same reasoning that kept F11's closure from rewriting the v1-c run record. Treat this rule
as governing plans not yet written, never as a mandate to clean up the ones already closed.

## One canonical explanation

**One canonical explanation, pointers elsewhere.** When the same reasoning needs to appear in
more than one place, one copy is authoritative and the others point at it — never two copies
that must agree by discipline. The rule bites when the *reasoning itself* is duplicated, not
when text merely looks alike: two comments explaining two different things that happen to share
vocabulary are fine.

`CoverageReport.cs` already does this: its `notFoundWithoutPathParameter` key is matched against
`TestPlanBuilder.NoPathParameterNoteReason` rather than a hand-copied literal, so the count
"cannot drift from the message a reader ... actually sees, because both are the same object in
memory, not two copies that happen to agree today." The failure this avoids has shown up
elsewhere without the fix — a doc comment on `RequireSecondaryIdentityLacks` once contradicted
itself two paragraphs apart about what runs before it, because the two paragraphs stated the same
fact independently instead of one deferring to the other.

## Where validation lives

**A guard placed at a read site is bounded by what that read site can observe.** If validation
sits downstream of parsing, a parse failure pre-empts it before it ever runs — so it looks
complete while covering only the cases parsing lets through. Validate a document where the whole
document is available, not at each point a value is read. This does not ban local checks; it
applies specifically when validation is positioned after a step that can itself throw or bail out
on the same input.

`intest.json`'s `project.rootNamespace` was validated by `CSharpIdentifier.TryValidateDottedName`
at the point `GenerateCommand` read it. That guard could only ever see a string or null, because
`GetProperty` and `GetString` throw on a missing key or a non-string value first — so it closed
one of three cases and looked complete from where it stood. The fix moved validation to
`ConfigLoader.Load`, which both `GenerateCommand` and `FixturesRepairCommand` now call against the
whole document before either command writes anything.

## Ask the thing that decides

**Ask the thing that decides.** When you need to know how something behaves, ask the component
that actually decides it — not the specification, not the documentation, and not your reading of
either. This holds even when you reach for a framework predicate instead of hand-rolling one: a
reassuring name is not evidence, and it should be checked against the same real behavior you would
demand of your own code.

`MSBuildPropertyValue.TryEscape`'s XML-representability check was first hand-rolled from the XML
1.0 `Char` production and missed the two noncharacters U+FFFE and U+FFFF — invisible to a check
that only asked "is this a control character." It was replaced with `XmlConvert.IsXmlChar`, which
was then itself verified against `XDocument.Parse` rather than trusted on its name. The ordering
comment above the surrogate check in the same method is there for the same reason: `IsXmlChar`
alone would misdiagnose a valid surrogate pair as unrepresentable, so surrogates are checked first.

**A test is bounded by what its assertions can discriminate.** "I mutated this and it failed" is
not "this decision is pinned" — the mutation only reaches what the assertion can tell apart. Ask
what the test would still pass under.

`InitCommand`'s choice of JSON encoder for `intest.json` is such a case: both candidate encoders
emit valid, equivalent JSON for the same input, so any test that round-trips and compares is blind
to which one ran. It was pinned only once a test asserted on the raw file text instead.

**The assertion's own default can be the blind spot.** Shouldly's `ShouldContain`,
`ShouldNotContain`, `ShouldStartWith`, `ShouldNotStartWith`, `ShouldEndWith` and
`ShouldNotEndWith` all take `Case caseSensitivity = Case.Insensitive` on their string overloads,
so `reason.ShouldContain("project.rootNamespace")` passes against a message that says
`Project.RootNamespace`.

When writing one, ask whether anything else matches that string ordinally. Setting paths,
`schemaVersion`, `intestVersion`, token names like `{{runId}}`, fixture keys, `operationId`, paths
under `Generated/`, CLI flags, filenames, the `intest fixtures repair` command line — all are, by
`JsonElement.TryGetProperty`, by `StringComparer.Ordinal`, by git, or by a shell. Naming one in the
wrong case sends the adopter to an edit that cannot work, so the assertion has to say
`Case.Sensitive`. A literal that only describes a condition — "is empty", "not valid JSON", "an
object" — is not such a name: leave it, and write `Case.Insensitive` explicitly wherever a reader
would otherwise wonder which was meant. Nor is a literal with no letters in it — a separator, a
punctuation mark, a line ending — which has no casing to be sensitive about, so annotating one
states a claim it cannot check. The annotation is the only thing distinguishing an
assertion that depends on casing from one that does not, which is why annotating everything would
be worse than annotating nothing.

Negatives run the other way, which is worth knowing before "fixing" one: a case-insensitive
`ShouldNotContain` rejects *more* than it says, not less, so it fails spuriously rather than
passing vacuously. The suite's are deliberately left un-annotated.

The cost of the default was measured, not assumed. Rewriting `spec.source` to `Spec.Source`,
`schemaVersion` to `SchemaVersion`, `operationId` to `OperationId`, `--spec` to `--Spec`,
`` `intest fixtures repair` `` to `` `InTest Fixtures Repair` ``, `U+{c:X4}` to `U+{c:x4}`, the
`generate --check` report's paths to lowercase and the published-key listings to lowercase — eight
regressions any reviewer would catch on sight — left 614 of the 615 `InTest.Cli.Tests` and
`InTest.Runtime.Tests` cases green. The one that failed,
`TokenResolverTests.TheAvailableKeyListIsOrdinalSortedRegardlessOfPublishOrder`, is the one that
does not use Shouldly's string helpers at all: it compares with
`IndexOf(..., StringComparison.Ordinal)`. Annotating the 126 sites where casing is the claim takes
that to 67 failures. `ShouldlyStringDefaultsTests` pins the six defaults themselves, because every
one of those annotations stops being load-bearing the moment Shouldly changes them.

**Line endings are the same trap in a different costume, and this repository's first real CI run
caught it.** `UpgradeCommandTests.NeverBumpsTheManifestFormatVersionOrAnotherToolsPin` set up
`.config/dotnet-tools.json` from a C# raw string literal and then asserted on the result with
`after.ShouldContain("\"version\": 1,\n  \"isRoot\": true", ...)` — a hard-coded `\n` standing in
for "these two values are unchanged." A raw string literal's line endings are whatever bytes sit in
the `.cs` file at that point, not a fixed constant: on this project's own machines
`core.autocrlf=input` keeps the checkout LF, so the literal carried LF and the embedded `\n`
matched. `windows-latest`'s default `core.autocrlf=true` checks the same file out CRLF, the literal
carried CRLF, and the same bytes the assertion meant to find — `"version": 1` immediately followed
by `"isRoot": true` — were there, just not joined by a bare `\n` any more. The fix was the same
shape as the casing fix: split into two `ShouldContain` calls, one per value, so the assertion
states the claim it actually means ("these two values didn't change") rather than a stronger one it
does not ("...and stayed joined by this exact byte"). `.gitattributes` also gained a `*.cs
text eol=crlf` entry (originally `eol=lf`; see `docs/superpowers/plans/2026-08-23-crlf-everywhere.md`
for why the letter later flipped) so the checkout itself stops varying by platform — the mechanical
scan behind that decision (grep every `\n`/`\r\n` literal compared against source-file-derived
content) found this to be the only test in the suite with the hazard; the rest either used regular escape
sequences (fixed bytes regardless of checkout) or compared against output a renderer or JSON writer
already normalizes.

**A proof living outside the repository is indistinguishable, to everyone downstream, from a
proof that was never run.** Running the real check once, by hand, and reporting the result in a
message is genuine evidence in the moment — but it leaves nothing anyone else can find, re-run,
or trust later. Only a proof committed as a test survives past the conversation that produced it.

`MSBuildPropertyValue`'s `?` escaping is such a case. The claim that an unescaped `?` makes
`Include="$(InTestSpecSource)"` silently glob-match a different file was verified against a real
`dotnet msbuild` evaluation — but only in a terminal, reported in a message. It existed nowhere in
source, tests, docs, or commit messages. A later session writing this very section went looking
for that evaluation to cite it here and could not find it anywhere in the repository, so it
correctly declined to cite it as fact rather than trust the claim on reputation. The gap stayed
open — every assertion for this escaping still went through `XDocument`, which cannot see a glob
resolving to the wrong file at all — until it was closed by a test in
`MSBuildEvaluationTests.cs` that runs the same `dotnet msbuild` evaluation itself and asserts on
its output.

## Working alongside other branches

Two habits, both earned by real cost today.

**`main` is not the whole picture.** An unmerged branch is invisible to it.
Before starting work, check whether a branch already carries it, not just
whether `main` does — `git branch --no-merged main` is the check. Four
parallel efforts were dispatched against work already merged, and one was
nearly dispatched twice because the finished implementation sat on a branch
`main` hadn't absorbed yet.

**Re-check your base before committing and before claiming done.** A
baseline measured once can't see `main` move afterward — that's a reason to
repeat the check, not skip it:

```bash
git merge-base --is-ancestor <your-base> main
```

A branch's base went stale mid-task; the author only found out because
someone told them.

## Dependency policy

New dependencies are held to a hard line, because adopters inherit whatever we take on.

- **No preview or prerelease packages**, in the tool or in generated output.
- **No licence surface.** Permissive licences only. A package that is technically excellent but
  charges commercial users is excluded on that ground alone — this is why `JsonSchema.Net` and
  FluentAssertions v8 are not used, both documented in §4.
- **No assumed vendor.** No cloud SDK, no identity library. If a capability needs one, it
  belongs behind an interface the adopter implements.
- **Deprecated or vulnerable versions are disqualifying.** Check nuget.org's deprecation and
  vulnerability metadata, not just the version number. The entire `Microsoft.OpenApi` 2.x line
  is deprecated, which an earlier revision missed.
- **Third-party GitHub Actions are dependencies too, and inherit this policy — pinned harder.**
  An action executes with repository secrets in scope, a strictly larger supply-chain surface
  than a NuGet package sitting in a lockfile. Every action
  `.github/workflows/build-and-test.yml` uses is pinned by **commit SHA**, never a tag — a tag
  is mutable and can be repointed after review. See "Continuous integration" below for how each
  pin was resolved and verified.

### Automated dependency updates (Dependabot)

`.github/dependabot.yml` opens weekly pull requests, capped at five open at a time per ecosystem,
for two ecosystems: `nuget` (reading `Directory.Packages.props` directly — this repository uses
central package management with no `packages.lock.json`, which Dependabot's `nuget` updater
handles natively) and `github-actions` (reading the `uses:` lines in
`.github/workflows/build-and-test.yml`). Four groups of packages that were each confirmed, by
checking `.nuspec` repository metadata and nuget.org version history, to ship from one upstream
release train and move together are collapsed into a single PR apiece, each expressed as the
narrowest name-prefix glob that covers exactly its members and nothing else in
`Directory.Packages.props` (checked mechanically, package by package — not eyeballed): `MSTest.*`
(`MSTest.TestFramework`, `MSTest.TestAdapter`, `MSTest.Analyzers` — microsoft/testfx),
`openapi-net`'s `Microsoft.OpenApi*` (`Microsoft.OpenApi`, `Microsoft.OpenApi.YamlReader` —
Microsoft/OpenAPI.NET), `dotnet-servicing`'s `Microsoft.Extensions.*` /
`Microsoft.AspNetCore.*` / `Microsoft.EntityFrameworkCore.*` (eight packages built from the same
commit in the unified `dotnet/dotnet` monorepo — see the file for the full list), and `nswag`'s
`NSwag.*` (`NSwag.AspNetCore`, `NSwag.MSBuild` — RicoSuter/NSwag). Every other package gets its
own PR. The file itself carries the fuller reasoning for each group, including the packages that
were considered and deliberately excluded (`Microsoft.NET.Test.Sdk`, `Shouldly`, `NJsonSchema`,
`System.CommandLine`) and how CPM support and SHA-pin support were confirmed rather than assumed.

**`dotnet-servicing`'s globs accept a named future risk in exchange for brevity.**
`Microsoft.Extensions.*` matches more than the five packages it groups today — if a package like
`Microsoft.Extensions.AI` or `Microsoft.Extensions.Http.Resilience` (both real packages on their
own, unrelated release cadence, not in `Directory.Packages.props` today) is ever added, this glob
would silently sweep it into the group and delay its updates behind the VMR servicing train's.
The file's own comment on `dotnet-servicing` names this trade explicitly and says what to check
before adding a `Microsoft.Extensions.*` package. `openapi-net` and `nswag`'s globs do not carry
this risk today: nothing else in `Directory.Packages.props` starts with `Microsoft.OpenApi` or
`NSwag.`, and the file's comments explain why (`Microsoft.AspNetCore.OpenApi` shares the substring
"OpenApi" but not the prefix; `NJsonSchema` does not start with `NSwag.` at all).

**What Dependabot enforces mechanically:** a version bump proposal against
`Directory.Packages.props`, nothing more. For `github-actions`, it keeps an action pinned to a
commit SHA (it does not rewrite the pin to a mutable tag) and updates the trailing `# vX.Y.Z`
comment to match — confirmed against GitHub's own changelog entries for that feature, so
[pin-actions-by-sha] survives Dependabot being turned on rather than being silently undone by it.

**What stays a human review step, because Dependabot cannot check it:** every other line of the
dependency policy above — licence, deprecation status, and vulnerability metadata beyond what
GitHub's own security-alert pipeline already covers (a separate, repository-level setting, not
something `dependabot.yml` controls). Whether the `nuget` ecosystem withholds prerelease versions
by default was not independently confirmed while writing this config (see the file's own header
for exactly what was and was not established); every package pinned today is a stable release, so
it does not currently matter in practice, but do not rely on Dependabot to enforce "no preview or
prerelease" — check it by hand the same way you would for a manually-proposed dependency. The pull
request template's "If this adds or changes a dependency" checklist is exactly this list, and it
applies to a Dependabot PR the same as any other.

**A Dependabot PR that bumps `MSTest.TestFramework`, `MSTest.TestAdapter`,
`Microsoft.NET.Test.Sdk`, `MSTest.Analyzers`, or `Shouldly` will fail CI.** This is expected, not
a bug in the config. `PackageVersionCouplingTests`
(`tests/InTest.Architecture.Tests/PackageVersionCouplingTests.cs`, described in CLAUDE.md's
"Build configuration" section) enforces that those versions stay identical across
`Directory.Packages.props`, the scaffold string in `InitCommand.cs`, and — for the three-way
subset — the hand-written project in `CompileVerificationTests.cs`. Dependabot only ever edits
`Directory.Packages.props`, so any of those five packages moving puts the other site(s) out of
sync by construction, and the guard exists precisely to make that failure loud instead of silent.
To merge such a PR: read the failing test's message (it names both files and both versions),
hand-edit the scaffold string(s) it points at to match, and re-run
`dotnet test tests/InTest.Architecture.Tests` before pushing. This is the behaviour the guard was
built for — see `PackageVersionCouplingTests`' own doc comment, which names Dependabot explicitly
as the reason the coupling needed to become mechanical rather than a matter of discipline.

**Not covered by any ecosystem:** `global.json` pins the .NET SDK at `10.0.400`
([pin-the-sdk], `docs/superpowers/plans/2026-08-22-intest-ci.md`). Dependabot has no
`dotnet-sdk`/`global.json` ecosystem, so that pin is not part of this automation at all — bumping
it remains a deliberate, hand-made edit, same as before this file existed.

## Scope requests

Two are expected often enough to answer up front:

**xUnit or NUnit support.** Reasonable ask, genuinely not free. The lifecycle, parameterization
and parallelism models differ enough that generated code, the runtime base class and the
frozen-axis machinery all change. It is the most likely v2 feature. Open an issue describing
your setup rather than a PR.

**Targeting below `net10.0`.** .NET 8 and 9 both reach end of support on 10 November 2026, and
MSTest v4's own floor is .NET 8. The test project's TFM is independent of your API's, so an API
on `net8.0` works today. If the SDK requirement is the blocker for you, say so in an issue —
that is useful data.

## Pull requests

- One logical change per PR, with a description saying what it changes and why.
- Tests for behaviour changes. §16 lists the suites the project commits to, including several
  that guard failures which are otherwise invisible until they reach production.
- Follow the existing style; do not reformat unrelated code.
- Update the spec in the same PR when a change alters documented behaviour. The spec is the
  source of truth, not an afterthought.
- Update [`docs/getting-started.md`](docs/getting-started.md) when a change alters the adoption
  path. It is deliberately a full end-to-end trace rather than a summary, because walking it is
  what catches gaps — reading it top to bottom is how the unowned initial-fixture creation was
  found, after the design had already been through several review rounds.
- Add an entry under `CHANGELOG.md`'s `Unreleased` section for anything a changelog reader
  would want to know — see "Changelog" below for what qualifies and where it goes.

## Continuous integration

`.github/workflows/build-and-test.yml` runs on every push to `main` and on every pull request,
matrixed over `ubuntu-latest` and `windows-latest` — three jobs, six runs per trigger:

- **`fast`** — `InTest.Architecture.Tests`, `InTest.Cli.Tests` and `InTest.Runtime.Tests`, each
  built and run from its own `.csproj` rather than the solution, so this job never incidentally
  rebuilds `InTest.Golden.Tests` or a `samples/` project. Measured cold-cache, three repeats per
  platform: ~33.5–35.5s end to end. Unchanged by the `InTest.Runtime`/`InTest.Runtime.MSTest`
  split — `InTest.Runtime.Tests` exercises both projects (an `InternalsVisibleTo` grant from each),
  and no new test project was added for the adapter, so this job's composition and timing are not
  expected to move.
- **`golden`** — `InTest.Golden.Tests` alone, in its own parallel job specifically so its
  ~90–107s cannot delay the verdict `fast` gives in a fraction of that time. CLAUDE.md: it is
  "the only suite that proves generated code both compiles and runs."
- **`dogfood`** — `scripts/ci/dogfood.ps1` runs `init` → `generate` (exit 1, fixtures missing —
  designed behaviour, not a failure) → `fixtures repair` → `generate` → `generate --check`
  against the three sample specs (`samples/Catalog.Api/Catalog.Api.json`,
  `samples/Inventory.Api/Inventory.Api.json`, `samples/Orders.Api/Orders.Api.json` —
  `samples/Identity.Server` is a Duende provider with no spec, so three, not four). Deliberately
  static: no `dotnet build` of a scaffold and no live API, because starting the samples over real
  HTTP needs the port/issuer/environment pairing `samples/README.md` documents, and getting that
  wrong produces 500s and silent 404s rather than an obvious CI failure. Scaffolds under the
  runner's temp directory, outside the checkout, so nothing this job does can dirty your PR's
  working tree — the workflow confirms that afterward with `git status --porcelain` rather than
  trusting the isolation silently.

Both `fast` and `golden` run `scripts/ci/assert-trx-results.ps1` after `dotnet test`: it parses
the `.trx` and requires each expected assembly's file to exist, report more than zero executed
tests, report zero failures, and contain a `<TestMethod codeBase>` ending in that assembly's
`.dll`. A green `dotnet test` step does not by itself prove anything ran — a wrong path or a
typo'd filter can match nothing and still exit 0 — so this is a second, independent check rather
than trusting the step's own exit code.

**What a pull request should expect:** six required job runs, all matrixed the same way. Both
platforms run rather than one standing in for the other — see "Line endings are the same trap in
a different costume" above for the first thing that distinction actually caught: a hard-coded
`\n` in a test assertion that passed on every contributor's machine and failed only on
`windows-latest`.

**What has and has not been verified about this workflow itself — different claims, stated
separately:** every job's exact command sequence has been run locally on both platforms by hand,
and the workflow file has been checked with `actionlint`. The GitHub Actions runtime proper —
scheduling, cache save/restore, matrix fan-out, trigger firing — is a different thing from the
commands it runs, and is proven only by real runs on GitHub's infrastructure, not by either of
those checks.

## Changelog

`CHANGELOG.md` follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/): an
`Unreleased` section at the top, version sections below it once cut, each grouped into
`Added`/`Changed`/`Deprecated`/`Removed`/`Fixed`/`Security` subsections (only the ones that
apply — an entry with nothing to say under a heading omits the heading, it does not leave it
empty).

**What goes in `Unreleased`.** Anything a changelog reader — someone deciding whether to
upgrade, not someone reading the diff — would want to know: a new command or flag, a changed
exit code or JSON shape, a fixed defect that shipped in a previous release, a removed or
deprecated capability. Add the entry in the **same PR** as the change, the same discipline
this file already asks for the spec and `docs/getting-started.md` above — a changelog
written after the fact from `git log` is exactly the kind of proof-outside-the-repository
this file's "One canonical explanation" section already warns against; it survives only if
the PR that made the change is also the PR that records it. What does **not** qualify:
internal refactors, test-only changes, and anything already covered by "Not covered by
semver" in "Releases" below (failure message text, the internal `TestPlan` JSON, template
internals) — those are free to change without a changelog reader needing to know.

**When `Unreleased` becomes a version heading.** At release-cut time, **before** the tag is
pushed — see "Cutting a release, end to end" below, where this is its own numbered step. The
tag is the release ([tag-is-the-release]), so the changelog entry for a version has to exist
before that version does, not be backfilled once nuget.org already has it. Renaming
`Unreleased` to the version and date, and opening a fresh empty `Unreleased` above it, is the
entire operation — entries do not move or get rewritten, the heading above them does.

## Releases

Three packages now ship together and their majors move together: `InTest.Cli`, `InTest.Runtime`
(the framework-neutral fixture/schema/identity runtime — no test-framework dependency), and
`InTest.Runtime.MSTest` (the MSTest adapter — `TestHost` and `ApiTestBase` — depending on
`InTest.Runtime` at the exact same version plus `MSTest.TestFramework`;
`docs/superpowers/plans/2026-08-25-runtime-framework-split.md` is the record of that split). A
generated project references `InTest.Runtime.MSTest` directly and gets `InTest.Runtime`
transitively. The compatibility contract is in §3 of the spec and is public API:

- `InTest.Runtime`/`InTest.Runtime.MSTest` **N.x** accept code generated by `InTest.Cli` **N.y**
  for any `y`.
- Majors may change generated code shape, the `intest.json` schema, or either runtime package's
  public surface. They require `intest upgrade` and a reviewed diff.
- The previous major is supported for **12 months** after its successor ships.

Covered by semver: the runtime's exported types, the `intest.json` schema, CLI commands, flags
and exit codes, and the coverage report's JSON shape. Not covered: failure message text, the
internal `TestPlan` JSON, and template internals.

## Branching and how a release is cut

One branch, `main`, protected and continuous
(`docs/superpowers/plans/2026-08-23-trunk-based-versioning.md`, `[tag-is-the-release]`). There is
no `develop` branch and none is planned: a second long-lived branch would just encode, a second
time, the same fact a tag already encodes, and the two can disagree about whether something has
shipped. This supersedes an earlier `develop`/`main` decision recorded in the design spec's §7 —
see that section for the fuller argument and the measurements behind it.

**What a merge produces.** Every push to `main` is packed and verified in CI
(`.github/workflows/pack.yml`, via `scripts/ci/pack-and-verify.ps1`). The version is derived by
MinVer from git tags and commit height, with no CI-injected suffix and no counter to maintain:
`0.1.0-preview.0.<height>` today, since nothing has been tagged yet. **That artifact is not
published anywhere.** It exists only as a downloadable file attached to the Actions run that built
it. This distinction is load-bearing, not pedantic — nuget.org versions are permanent and
undeletable once pushed, so treating a merge artifact as published would burn the version space of
a package that has not shipped once. Publishing stays a separate, manual, deliberate act
(`[publish-stays-manual]`); nothing in this repository has ever pushed a package to nuget.org.

**What a tag produces.** `git tag 0.1.0-preview.1` (a publishable preview) or `git tag 0.1.0` (the
first stable release), pushed with `git push origin <tag>`, makes the build at that commit exact —
no prerelease height, because MinVer only appends `.<height>` to a commit that is not itself an
exact tag match. `pack.yml` also runs on any tag push, on any branch, and its tag-match check
(`scripts/ci/pack-and-verify.ps1 -ExpectedTag`) fails the build if the packed version and the
pushed tag ever disagree.

**A tag matching a SemVer-ish shape also publishes**, automatically, via a second, narrower
workflow: `.github/workflows/release.yml`. `[publish-stays-manual]`
(`docs/superpowers/plans/2026-08-23-trunk-based-versioning.md`) recorded publishing as a deferred,
manual step — that decision is **superseded**; see that section's own record for why two of its
three premises turned out not to hold. What changed is NuGet Trusted Publishing (OIDC):
`release.yml`'s `publish` job exchanges a GitHub-issued OIDC token for a short-lived (~1 hour)
nuget.org API key via `NuGet/login`, scoped by a GitHub Environment (`nuget-release`) that only
the repository owner can configure — no `NUGET_API_KEY` secret exists anywhere in this repository,
and none needs to. See `release.yml`'s own header comment for the full security reasoning,
including why this had to be a separate workflow file from `pack.yml` rather than a fourth job in
it.

**Cutting a release, end to end, as this repository defines it today:**

1. Merge whatever should ship into `main`.
2. Move `CHANGELOG.md`'s `Unreleased` section to a new version heading ("Changelog" above) and
   commit it to `main` — **before** tagging, since the tag is the release and the changelog
   entry has to exist before the version it describes does.
3. Tag that commit and push the tag: `git tag 0.1.0-preview.1 && git push origin 0.1.0-preview.1`
   (or `0.1.0` for a stable release). Use a plain SemVer shape — `release.yml`'s trigger only fires
   on `X.Y.Z` or `X.Y.Z-<label>`; see its header comment for exactly what its tag-filter glob does
   and does not accept.
4. `pack.yml` packs and verifies all three packages at exactly that tag's version and uploads them
   as workflow artifacts on the resulting Actions run — unchanged from before, still never
   publishes.
5. `release.yml` runs in parallel (same tag push triggers both workflows independently): its own
   `pack` job re-packs and re-verifies the same commit — including the artifact-content assertions
   below — then its `publish` job, gated on the `nuget-release` environment, pushes each package's
   `.nupkg` and `.snupkg` to nuget.org. Once `publish` succeeds, a third job, `release`, creates
   the GitHub Release for the tag — notes drawn from the `CHANGELOG.md` section step 2 just cut,
   all three packages' `.nupkg`/`.snupkg` attached (six assets — `release.yml` itself asserts the
   count). It runs after `publish`, in `contents: write` only, never in the same job as the
   `id-token: write` `publish` carries — see `release.yml`'s own header comment for why that
   separation is non-negotiable.
6. The Publishing checklist below still names real one-time and per-release human steps that
   automation does not perform — ID-prefix reservation, account hygiene, flipping the README
   status callout, and (recommended, not yet configured) a required-reviewer gate on the
   `nuget-release` environment so a human still looks before an irreversible push happens.

**Closed by the `0.1.0-preview.1` tag push:** both `pack.yml` and `release.yml` have now
completed real runs on GitHub Actions — trigger firing, matrix fan-out, cross-job artifact
upload/download, the OIDC token exchange and the `nuget-release` environment gate all fired for
real, both jobs went green, and nuget.org accepted all four artifacts (`InTest.Cli` and
`InTest.Runtime`, `.nupkg` plus `.snupkg` each). See `docs/v0-acceptance.md`'s publish record for
the full account. **What this one run does not prove:** that every future tag push behaves
identically, that a stable (non-preview) tag packs and publishes the same way, or anything about
the `publish` job on a second OS — it runs `ubuntu-latest` only, by design (see `release.yml`'s
own comment on that choice). Treat the next tag push as still worth watching, not as a foregone
conclusion. **Also unproven by this run:** the `InTest.Runtime`/`InTest.Runtime.MSTest` split
landed after it, so no tag push has yet published the three-package, six-artifact shape described
above — the next tag push is the first real test of that.

**Patching an old major.** There are zero shipped releases today, so nothing needs this yet.
Once one exists, cut `release/N.x` **on demand**, from the relevant tag, rather than maintaining a
permanent branch against a need that has not arrived — the same practice `dotnet/runtime` follows,
and the reason a second long-lived branch was rejected above.

**Two things `versioning.md` recommends and this scheme deliberately does not do**, recorded so a
future reader sees a decision rather than an oversight — full reasoning in the versioning plan's
`[version-from-git]` section:

- *"CONSIDER only including a major version in the `AssemblyVersion`."* MinVer already gives
  `{Major}.0.0.0` (measured `0.0.0.0` today, since nothing is tagged past `0.x`) — satisfied for
  free, nothing to decide.
- *"CONSIDER including a continuous integration build number as the `AssemblyFileVersion`
  revision."* **Not done, and not planned under this scheme** — MinVer's commit-height suffix lives
  in the prerelease label, not in a version component, and there is no CI build number to put there
  in the first place. Both recommendations exist chiefly to reduce .NET Framework binding-redirect
  pain; InTest is not strong-named and targets `net10.0`, where that pain does not apply.

### The shallow-clone guard

MinVer needs real tag history to compute a version at all. In a **shallow clone** (the default
depth for `actions/checkout`, and for a plain `git clone --depth 1`) it sees no tags, computes
height zero, and silently produces `0.1.0-preview.0` for *every* commit — no warning, no error.
`MINVER1001` does not fire for this case at all, and even where it fires for a different reason it
stays only a warning under `TreatWarningsAsErrors` (it is an MSBuild task warning, not a compiler
one), so neither mechanism catches a shallow clone on its own.

A plausible-looking wrong version is worse than a build failure: a failure stops at the point of
the mistake, while a wrong-but-plausible version ships silently and only surfaces later, if ever,
as a confusing report from whoever installed it — exactly the anti-pattern CLAUDE.md names, "never
substitute plausible defaults that let a suite pass while asserting nothing." Two independent
things guard against it here:

- Every `actions/checkout` step across both workflow files sets `fetch-depth: 0`, so CI never
  clones shallow in the first place.
- `Directory.Build.props`'s `InTestEnsureNotShallowClone` target asks git directly instead of
  trusting either signal above — a `.git` present *and* reporting the checkout as shallow — and
  fails the build before it can produce a version at all, on both `dotnet build` and `dotnet pack`.
  This is deliberately independent of `fetch-depth: 0`: that setting is one line per checkout step
  that a future edit can drop without anyone noticing, and this guard exists to catch exactly that
  silently.

**If this guard fires,** the checkout is shallow: run `git fetch --unshallow`, or re-clone without
`--depth`, then rebuild. It does **not** fire for an ordinary (non-shallow) clone, and it does not
fire outside a git repository at all — `scripts/local-e2e-test.ps1` packs from a non-git copy of
`src/` by design, so it hits a plain `MINVER1001` warning instead, and that fallback is accepted
for that harness specifically (see the script's own header comment for why).

## Publishing checklist

The actual `dotnet nuget push` step is now automated by `.github/workflows/release.yml` on a tag
push (`[publish-stays-manual]` is superseded — see "Branching and how a release is cut" above and
that section's own record in
`docs/superpowers/plans/2026-08-23-trunk-based-versioning.md`). Everything below that isn't the
push itself remains a real, one-time or per-release, human step — see `docs/superpowers/specs/
2026-08-23-nuget-publish-readiness-design.md` for the full reasoning behind each.

**One-time setup, before the first tag push can succeed:**

1. Confirm the branching/versioning model above is in effect. The scaffold defect that model
   exposed is already fixed — `InitCommand.cs` interpolates `CliVersion.Current` rather than a
   literal, guarded by `PackageVersionCouplingTests` — so this step is just "tests are green", not
   a manual check.
2. Reserve the `InTest.` NuGet ID prefix. The IDs are unclaimed today; the first push claims them.
   Unrelated to trusted publishing — nuget.org's trusted-publishing policy binds a package *owner*,
   not a reserved ID, so this step is only about the prefix-reservation benefit itself (protecting
   `InTest.*` from being claimed by an unrelated package), not a publishing prerequisite.
3. One-time nuget.org account hygiene: sign in with a Microsoft account, enable two-factor
   authentication, enable "email me when a package is published".
4. **Create the NuGet Trusted Publishing policy** on the nuget.org account that will own these
   packages (Trusted Publishing is a gradual rollout — **confirmed available on this account**:
   the policy exists and the `0.1.0-preview.1` push exchanged its OIDC token successfully; if the
   menu item is ever missing on a different account, that gates everything below it). The policy
   needs:
   Repository Owner and Repository set to this repo, **Workflow File set to the file name only**
   (`release.yml` — not a path, not a job name), **Environment set to `nuget-release`**, and a
   Package owner selected from the dropdown. Every field must match `release.yml` exactly — the
   Environment field in particular is the control that makes an attacker unable to self-grant a
   publish key by editing the workflow file; see that workflow's own header comment for why.
5. **Create the `nuget-release` GitHub Environment** (repo Settings → Environments). At minimum
   its name must match the policy's Environment field above exactly. Strongly recommended:
   configure **required reviewers** on it — since the workflow itself performs no manual
   verification step before pushing (see "Verify, then tag" below for where that check now lives
   instead), a required-reviewer gate on this environment is the only remaining place a human
   looks at a release before it becomes irreversible.
6. **Add the `NUGET_TRUSTED_PUBLISHING_USER` repository or environment variable** (not a secret —
   it is a nuget.org profile *name*, not a credential; `release.yml` reads it as
   `vars.NUGET_TRUSTED_PUBLISHING_USER`). Scope it to the `nuget-release` environment created above
   if you want it to only be readable by that job.

**Per release:**

7. **Clear the local NuGet cache** (`dotnet nuget locals global-packages --clear`, or delete
   `~/.nuget/packages/intest.*`) before installing or testing any package built during this
   release. NuGet caches by exact version and never re-fetches, so a stale local-pack entry
   silently shadows the real one.
8. **Verify, then tag.** Merge the release candidate to `main` and let `.github/workflows/pack.yml`
   pack and verify it — this is the ordinary path (see "Branching and how a release is cut" above)
   and it now runs the same artifact-content assertions release.yml's own pack job will run
   (`README.md`, `icon.png`, `THIRD-PARTY-NOTICES.md` presence/absence, a non-empty `<repository …
   commit="…">` — `scripts/ci/pack-and-verify.ps1`'s `Assert-PackageArtifactContents`). Download
   that run's artifacts and do the one check nothing automated performs:
   `dotnet tool install --global --add-source <dir> InTest.Cli --version <v>` and run
   `intest --help`. Only once that looks right, tag the commit and push the tag — the trigger for
   `release.yml`'s actual publish. This ordering matters: it is what puts a human verification step
   *before* an irreversible push under an otherwise-automated flow, using an artifact `pack.yml`
   already produced from the same commit, rather than trying to intercept `release.yml`'s own pack
   job mid-run.
9. `release.yml` packs and pushes each package's `.nupkg` and `.snupkg` automatically — three
   packages, six files, no manual `dotnet nuget push` needed or expected. Confirm the run went
   green and every version now appears on nuget.org. **Confirmed by the `0.1.0-preview.1` push**
   (`InTest.Cli` and `InTest.Runtime` only — before the `InTest.Runtime.MSTest` split):
   nuget.org accepts a `.snupkg` whose PDBs sit under `tools/` (a tool package) rather than
   `lib/` — all four artifacts (two packages times `.nupkg`/`.snupkg`) were pushed and accepted;
   see `docs/v0-acceptance.md`. The next tag push is the first to exercise the three-package,
   six-file shape.
10. Flip `README.md`'s status callout to name the just-published version. **Done for
    `0.1.0-preview.1`** — the banner now reads "`0.1.0-preview.1` is published to nuget.org as a
    prerelease" instead of "nothing is published yet"; a future release only needs the version
    string bumped, not this rewritten from scratch.
11. Starting with the release *after* each package's first publish: add
    `<PackageValidationBaselineVersion>` to that package's project file, pointing at the version
    just published — `InTest.Runtime` and, once it has shipped a first version,
    `InTest.Runtime.MSTest` too. (`InTest.Cli` never participates in package validation — the SDK
    hard-disables it for tool packages.)
12. **Regenerate `examples/Catalog.ApiTests` and `examples/Orders.ApiTests`, and move each
    `PackageReference` from `InTest.Runtime` to `InTest.Runtime.MSTest`.** Both are required and
    both are easy to forget, because **no test enforces either**.
    `ExampleProjectVersionMarkerTests` compares the three version markers *to each other*, never
    against `CliVersion.Current`, and its package-reference regex deliberately matches either id
    — so stale examples stay green indefinitely. Nothing under `.github/` or `scripts/` builds
    them and neither is in `InTest.sln`.
    This is a human step by necessity, not by omission: the trigger is "at the next publish",
    and a test encoding it would go red on `main` the moment the CLI version moves ahead and
    stay red for the whole development cycle — which is pressure to migrate `examples/`
    preemptively, exactly what `ExampleProjectVersionMarkerTests`' own comment forbids.

## Testing against a local build

`InTest.Cli`/`InTest.Runtime` `0.1.0-preview.1` are published to nuget.org, but only for that
exact tagged commit, and `InTest.Runtime.MSTest` is not published at all yet. Trying the
documented adoption path (`docs/getting-started.md` Phase 8 — `dotnet tool restore`,
`generate --check`, `upgrade`) against anything you changed locally — which, while contributing,
is the common case — still means packing all three packages yourself and restoring a scaffolded
project against them. **Use `scripts/local-e2e-test.ps1` for this. Do not improvise a
`dotnet pack` + `dotnet restore` by hand.** The script already packs `InTest.Cli`, `InTest.Runtime`
and `InTest.Runtime.MSTest`, and the scaffolded project it builds references the adapter package
(`InTest.Runtime.MSTest`), matching what `intest init` actually writes.

**Why this is a rule and not a suggestion:** NuGet's package cache (`~/.nuget/packages/`) is
keyed by exact version, machine-wide, and is never invalidated by a newer local build carrying
the same version number. A locally-packed `0.1.0` becomes indistinguishable from a real
published `0.1.0` forever — the next restore silently keeps using whatever is already cached,
with no error and no visible reason for the mismatch. This has already cost real time on this
project, twice, for the identical reason:

- **v1-e Task 5.** A stale `InTest.Runtime 0.1.0`, built one commit behind `HEAD`, shadowed a
  fresh build and produced `CS0103` on members (`RequireFixture`, `FixtureBody` and similar) that
  plainly existed in the source just built. Confirmed by direct experiment: deleting the cache
  entry and rebuilding is what fixed it, not any change to the generated code — see
  `docs/getting-started.md`'s "Things that will bite you" for the fuller writeup.
- **The v1-e Task 6 acceptance run** (`docs/v0-acceptance.md`, "Second trap, found during Task 5
  and confirmed here"). The identical defect recurred in a *different* run, despite the first
  occurrence already being fully diagnosed — because the safe procedure existed only as prose in
  a run report, never as anything a later run would actually pick up and use.

**What does not fix this, measured rather than assumed:** packing alone is not the hazard.
`dotnet pack` on its own never populates `~/.nuget/packages/` — verified by packing a throwaway
version and confirming the cache stayed empty. It is specifically a *restore* resolving from a
local feed that writes into the global cache, and the adoption path cannot be exercised without
one. "Don't pack" is therefore not a fix; the fix has to make the restore itself harmless.

`scripts/local-e2e-test.ps1` does exactly that, two ways, both required:

1. **Redirects `NUGET_PACKAGES`** to a scratch directory for its entire run, so no restore it
   triggers — pack, build, `dotnet run`, or `dotnet tool restore` — can reach the global cache.
   Measured: a restore with `NUGET_PACKAGES` pointed at a scratch directory lands entirely there,
   confirmed empty in the real cache both before and after.
2. **Packs at a version that can never collide with a real release** —
   `0.1.0-local.<timestamp>.pid<pid>`, applied as a `-p:Version=` MSBuild global property, which
   overrides `Directory.Build.props`'s `<Version>0.1.0</Version>` for the whole build without
   editing that file (its pin to `0.1.0` is deliberate — see `Directory.Build.props`'s own
   comment — and is not this script's to change). Defence in depth: even if redirection 1 were
   ever bypassed by a manual step outside the script, a `0.1.0-local.*` package cannot shadow a
   published `0.1.0`.

It also exercises the whole local adoption path against `samples/Catalog.Api` in the process —
`init`, `generate`, `fixtures repair`, `generate --check`, `upgrade`, and a real `dotnet build`
against the packed `InTest.Runtime`, which is the step that would have caught both incidents
above (`generate --check` alone never compiles anything, it only compares text). See the
script's own header comment for the full design, including why it is one PowerShell script
rather than a PowerShell-plus-Bash pair, and what is deliberately out of scope
(`dotnet test` against a live sample API — a different, separately-flaky concern from the NuGet
hazard this script exists to close).

## Code of conduct

Be decent. Assume good faith, disagree about the work rather than the person, and accept that
maintainers may decline a change without it being a judgement on you. Behaviour that makes
people not want to participate is not welcome, and maintainers will act on it.

## Licence

Contributions are accepted under the MIT licence covering this repository.

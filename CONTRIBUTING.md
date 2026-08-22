# Contributing to InTest

Thanks for looking. InTest is a working tool with an incomplete command surface. `intest init`,
`generate`, `fixtures repair`, `generate --check` and `upgrade` run end to end today, with a
documented walkthrough in [`docs/getting-started.md`](docs/getting-started.md); `init`,
`generate` and `fixtures repair` are also verified against three sample APIs
([`docs/v0-acceptance.md`](docs/v0-acceptance.md)) — `generate --check` and `upgrade` shipped
after that acceptance run and are not yet covered by it. `survey`, `fixtures promote`,
`assertions add` and `generate --emit-plan` don't exist yet — that doc's own preamble tracks the
gap precisely, and is the source of truth if this file and it ever disagree. Nothing is published
to NuGet, so building from source is still how anyone tries it. The
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

## Releases

Both packages follow semantic versioning and their majors move together. The compatibility
contract is in §3 of the spec and is public API:

- `InTest.Runtime` **N.x** accepts code generated by `InTest.Cli` **N.y** for any `y`.
- Majors may change generated code shape, the `intest.json` schema, or the runtime's public
  surface. They require `intest upgrade` and a reviewed diff.
- The previous major is supported for **12 months** after its successor ships.

Covered by semver: the runtime's exported types, the `intest.json` schema, CLI commands, flags
and exit codes, and the coverage report's JSON shape. Not covered: failure message text, the
internal `TestPlan` JSON, and template internals.

## Code of conduct

Be decent. Assume good faith, disagree about the work rather than the person, and accept that
maintainers may decline a change without it being a judgement on you. Behaviour that makes
people not want to participate is not welcome, and maintainers will act on it.

## Licence

Contributions are accepted under the MIT licence covering this repository.

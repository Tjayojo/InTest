# CRLF everywhere — reversing `[lf-everywhere]`

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Switch every line-ending convention this project controls — its own tracked source, and everything `generate`/`fixtures repair`/`init`/`upgrade` write into an adopter's project — from LF to CRLF, without reintroducing the platform-dependent bugs `[lf-everywhere]` (`docs/superpowers/plans/2026-08-21-intest-v1e-check-and-upgrade.md`) was built to close.

**Architecture:** This is a mechanical direction-flip of a single, well-factored decision, not a redesign. Every writer routes through one of two seams — `TemplateRenderer.Normalize` for generated `.g.cs` classes, `InTest.Cli.Json.CommittedJsonOptions` for every JSON writer — plus one `.gitattributes` pin (this repo's own, and the one `InitCommand` scaffolds into adopter projects) that makes the checked-out bytes match what those seams produce. Flip both seams from `\n`/`eol=lf` to `\r\n`/`eol=crlf`, keep the seams themselves (one shared instance, mechanically guarded — do not duplicate them), and update every test and comment that currently asserts or explains the LF direction. Name this decision `[crlf-everywhere]` in new/updated comments so it is greppable the same way `[lf-everywhere]` was.

**Tech Stack:** .NET 10 / C#, `System.Text.Json`, Scriban, MSTest, Shouldly, git attributes.

**Prerequisite:** `main`/this branch at `b0cf98d` or later. **658 passing, 0 failing** (Architecture 8, Cli 410, Runtime 205, Golden 35). Measure it yourself before starting (`dotnet test InTest.sln`) — do not trust this number if any other change has landed since.

---

## Revision note — Task 6's code quality review

Code quality review of Task 6 found that its `docs/getting-started.md` sentence, and an already-merged Task 4 doc comment in `InitCommand.cs` (lines 53-55) carrying the identical claim, both overclaim relative to what Task 5's own investigation established: they name `core.autocrlf=false` alongside `core.autocrlf=input` as both causing a checkout to flatten CRLF to LF. Task 5 proved `false` does not do this — it applies no conversion in either direction, so a file committed as CRLF (which every InTest writer now produces) round-trips as CRLF regardless of the pin, the same "coincidentally safe" shape `core.autocrlf=true` has under this convention. Only `core.autocrlf=input` demonstrably flattens (normalizes on add, does not re-expand on checkout — verified with a hex-dumped before/after in Task 5). "Linux/macOS" as a named risk has the same problem: its typical default is `core.autocrlf=false`, which is the coincidentally-safe case, not a risk. Both mentions are corrected below to name only `core.autocrlf=input`, matching what was actually demonstrated.

---

## Revision note — Task 5's code quality review

Code quality review of Task 5 found a defect in the plan's own prescribed text (not an execution error — the implementer copied it correctly): `GitattributesSurvivesAnAutocrlfTrueCheckout` forces `core.autocrlf=true` on both the source commit and the destination clone. Under `[lf-everywhere]`, that was the correct hostile condition — `core.autocrlf=true`'s own checkout-time expansion (LF stored → CRLF checkout) fought the desired LF outcome, so the test proved the `eol=lf` pin was doing real work by overriding it. Under `[crlf-everywhere]`, `core.autocrlf=true`'s expansion (LF stored → CRLF checkout) now *coincides* with the desired CRLF outcome — so the test as migrated passes whether or not `.gitattributes` pins anything at all. Verified directly: deleting the `fixtures/**/*.json text eol=crlf` line from `InitCommand.GitattributesContent` and re-running the test, it still passed.

**First correction attempt (`core.autocrlf=false`) was itself wrong, and the implementer caught it before shipping it.** `false` disables conversion entirely for any path with no matching attribute — so with the pin deleted, a file committed as CRLF is stored as CRLF (no add-time normalization) and checked out as CRLF (no checkout-time conversion either): the round trip stays byte-identical whether or not the pin exists, because `false` never touches the bytes in either direction. Re-verified with the pin deleted under `autocrlf=false`: still passed — proving `false` is exactly as blind as `true` was, just via the opposite mechanism (no conversion at all, instead of conversion that happens to land on the right answer anyway).

**The setting that actually distinguishes pinned from unpinned is `core.autocrlf=input`.** `input` normalizes CRLF→LF on add (same text auto-detection as `true`), but — unlike `true` — does *not* re-expand LF back to CRLF on checkout. So a path with no `eol=crlf` pin silently flattens to LF on the clone; a path *with* the pin gets forced back to CRLF regardless of `input`'s own checkout behavior, because an explicit `eol` attribute always overrides `core.autocrlf`. Re-verified with the pin deleted under `autocrlf=input`: the test now genuinely fails, with a hex-dumped before/after showing exactly the CRLF→LF flattening (`fixtures\sample.json`: 3 CRLF sequences before the checkout, 0 after). Restoring the pin: passes again. Fixed below using `core.autocrlf=input`, and renamed the test to `GitattributesSurvivesAnAutocrlfInputCheckout` (not `...FalseCheckout` — that name was proposed, then disproven, in the same investigation) since a test whose name states the wrong precondition is its own defect in a codebase this precise about naming.

---

## Revision note

Task 2's implementer caught a gap the plan missed on first write: `tests/InTest.Cli.Tests/TemplateRendererTests.cs` asserts against `TemplateRenderer.Render`'s output using seven hard-coded-`\n` checks across seven test methods, none of which were in this plan's original file list for any task. Flipping `Normalize` to CRLF makes five of them fail outright (an exact-match `ShouldContain` with an embedded `\n` no longer occurs) and silently defangs two more (`ShouldNotContain("\n\n\n")` and `ShouldNotContain("\n\n    }")` become vacuously true against CRLF content — they stop catching the regression they exist to catch, without failing). Folded into Task 2 Step 6 below rather than a separate task, since it is a direct, same-file-family consequence of Step 1's change and leaving it for a later task would mean Task 2's own commit leaves the Cli suite red.

---

## What does *not* change

- `UpgradeCommand.SetIntestVersion` / `DetectFileNewline` — already convention-agnostic: it reads whichever newline the adopter's own `intest.json` already uses and matches it. `intest.json` and `.config/dotnet-tools.json` stay outside `[crlf-everywhere]`'s scope entirely; they are adopter-owned and never pinned by `.gitattributes` (per `docs/superpowers/plans/2026-08-21-intest-v1e-check-and-upgrade.md`'s own Task 2 Step 3 reasoning — only InTest-owned paths are pinned). **Do not touch `UpgradeCommand.cs`, `UpgradeCommandTests.cs`'s `InsertsIntestVersionWhenAbsent`/`InsertsIntestVersionUsingTheConfigsOwnCrlfLineEnding`/`NeverBumpsTheManifestFormatVersionOrAnotherToolsPin` tests, or `SetIntestVersionInserts*`/`SetIntestVersionReplaces*`/`DoesNotCorruptANestedKeyNamedIntestVersion` in this plan** — they already pass unchanged in either direction and touching them is scope creep.
- `docs/v0-acceptance.md` and `docs/superpowers/plans/2026-08-21-intest-v1e-check-and-upgrade.md` — both are dated historical records of what was actually measured under `[lf-everywhere]`. Rewriting them to claim CRLF was measured back then would be dishonest. Leave them alone; this plan is the historical record for the reversal.
- `mstest-class.scriban` and any other embedded template/resource files — `TemplateRenderer.Normalize` already collapses `\r\n` to `\n` before re-emitting, so the template's own checked-out line endings never reach the rendered output. No content edit needed there, only the `.gitattributes` pin that controls how the template file itself checks out (Task 1).

---

## Task 1: This repo's own `.gitattributes`

**Files:** `.gitattributes` (repo root).

- [ ] **Step 1: Edit the file**

Replace the full contents with:

```
# Normalize to CRLF in the working tree; the repository object database still stores LF
# internally (git's `text` attribute always normalizes storage to LF — `eol` only controls
# checkout), but every checkout on every platform gets CRLF here, deliberately and
# unconditionally, rather than varying by the checking-out machine's core.autocrlf. See
# [crlf-everywhere] below for why CRLF was chosen and what breaks if a pin below is removed.
* text=auto eol=crlf

# Golden files are compared byte-for-byte against renderer output, and TemplateRenderer
# normalizes to CRLF ([crlf-everywhere] — see TemplateRenderer.Normalize). A clone whose
# checkout would otherwise diverge from that (e.g. core.autocrlf=input, or any non-Windows
# default) would rewrite these to LF on checkout and fail the comparison on every line.
*.g.cs.txt text eol=crlf

# Scriban templates are rendered verbatim into the golden output, so their line endings
# are part of the generated artifact rather than a local editing preference.
*.scriban text eol=crlf

# [crlf-everywhere] pinned generated artifacts (above) but never our own source. A C# raw string
# literal's line endings are whatever bytes sit in the .cs file at that point — they are data, not
# editing preference, the same way a Scriban template's are. This project was previously bitten by
# exactly this in the other direction: local dev on this project historically ran
# core.autocrlf=input (checkout stays whatever the object database already normalizes to), while
# CI's windows-latest ran core.autocrlf=true (checkout expands to CRLF) — the two disagreed, and a
# raw string literal in UpgradeCommandTests.cs carried different bytes on each, breaking a
# hard-coded "\n" assertion on one but not the other. See CONTRIBUTING.md for the full account.
#
# The fix was — and, after this reversal, still is — pinning eol explicitly so every checkout
# agrees regardless of core.autocrlf: only the letter changed, from eol=lf to eol=crlf. Pinning
# eol=crlf changes nothing already committed — the object database was, and remains, LF-normalized
# by `* text=auto` above; only checkout was ever platform-dependent, and this makes it uniform.
*.cs text eol=crlf
```

- [ ] **Step 2: Renormalize the working tree**

Run:

```bash
git add --renormalize .
git status --short
```

Expected: no staged changes report new *content* (the object database is still LF internally either way), but `git status` should not error. This step exists so the index is not left stale relative to the new attributes; the working-tree files on this machine will show CRLF only after a fresh checkout of the affected paths (a known git limitation — `.gitattributes` changes take effect for *new* checkouts, not silently rewriting files already on disk). Do not force a checkout of every tracked file as part of this step; that is out of scope and CI's fresh checkout on every run is what actually proves the pin.

- [ ] **Step 3: Commit**

```bash
git add .gitattributes
git commit -m "chore: pin this repo's own checkout to CRLF (reverses [lf-everywhere] for source files)"
```

---

## Task 2: `TemplateRenderer.Normalize` and the golden file

**Files:** `src/InTest.Cli/Rendering/TemplateRenderer.cs:124-126`; `tests/InTest.Golden.Tests/Expected/OrdersTests.g.cs.txt` (regenerated, not hand-edited).

- [ ] **Step 1: Flip `Normalize`**

In `src/InTest.Cli/Rendering/TemplateRenderer.cs`, replace:

```csharp
    /// <summary>Normalizes line endings so golden files compare identically on every OS.</summary>
    private static string Normalize(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";
```

with:

```csharp
    /// <summary>
    /// Normalizes line endings so golden files compare identically on every OS. [crlf-everywhere]:
    /// collapse to LF first so any already-CRLF input (e.g. a template file checked out CRLF) does
    /// not double up, then re-expand to CRLF — the direction this project standardizes on for
    /// every generated artifact. See TemplateRenderer's own callers and CommittedJsonOptions for
    /// the JSON half of the same decision.
    /// </summary>
    private static string Normalize(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd()
                .Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n";
```

- [ ] **Step 2: Rebuild**

```bash
dotnet build InTest.sln
```

Expected: builds with no warnings (this project has `TreatWarningsAsErrors=true`).

- [ ] **Step 3: Regenerate the golden file**

```bash
INTEST_UPDATE_GOLDEN=1 dotnet test tests/InTest.Golden.Tests --filter "FullyQualifiedName~OutputMatchesTheGoldenFile"
```

Expected: the test reports `Inconclusive` (per CLAUDE.md, this is the documented behavior of the update path — it writes the source copy at `tests/InTest.Golden.Tests/Expected/OrdersTests.g.cs.txt` and does not itself assert a pass).

- [ ] **Step 4: Verify the regenerated file actually contains CRLF**

```bash
dotnet build InTest.sln
dotnet test tests/InTest.Golden.Tests
```

Expected: full `InTest.Golden.Tests` suite passes (35 tests) **without** `INTEST_UPDATE_GOLDEN` set — this is the real verification; Step 3 alone only proves the write happened, not that a fresh render matches it.

- [ ] **Step 5: Do not commit yet — Step 6 below touches a test file in the same logical change**

- [ ] **Step 6: Fix `TemplateRendererTests.cs`'s hard-coded LF assertions**

In `tests/InTest.Cli.Tests/TemplateRendererTests.cs`, every one of the following lines embeds a bare `\n` that either exact-matches against `Render(...)`'s output or checks for its *absence* — both need the `\n` changed to `\r\n` so the assertion means the same thing against CRLF output that it meant against LF output. There are 20 lines across 7 test methods; find them with:

```bash
grep -n '\\n\\n\|{\\n\|)\]\\n' tests/InTest.Cli.Tests/TemplateRendererTests.cs
```

Apply these exact replacements (shown as old → new; every other character on each line — comments, `customMessage` text, method names — stays unchanged):

In `EmitsNoStrayBlankLines` (~line 180-185):
```csharp
        rendered.ShouldNotContain("\n\n\n");        // no double blank line anywhere
        rendered.ShouldNotContain("\n\n    }");     // no blank line before a closing brace

        rendered.ShouldContain(httpMethod == "POST"
            ? "then 200\")]\n    [DoNotParallelize]\n    public async Task"
            : "then 200\")]\n    public async Task");
```
becomes:
```csharp
        rendered.ShouldNotContain("\r\n\r\n\r\n");        // no double blank line anywhere
        rendered.ShouldNotContain("\r\n\r\n    }");       // no blank line before a closing brace

        rendered.ShouldContain(httpMethod == "POST"
            ? "then 200\")]\r\n    [DoNotParallelize]\r\n    public async Task"
            : "then 200\")]\r\n    public async Task");
```

In `EmitsNoStrayBlankLinesForADeclaredErrorCase` (~line 325-328):
```csharp
        rendered.ShouldNotContain("\n\n\n");
        rendered.ShouldNotContain("\n\n    }");
        rendered.ShouldContain("    {\n        using var request",
            customMessage: "no RequireFixture line and no leftover blank line ahead of it");
```
becomes:
```csharp
        rendered.ShouldNotContain("\r\n\r\n\r\n");
        rendered.ShouldNotContain("\r\n\r\n    }");
        rendered.ShouldContain("    {\r\n        using var request",
            customMessage: "no RequireFixture line and no leftover blank line ahead of it");
```

In `EmitsNoStrayBlankLinesForAWrongScopeAuthCase` (~line 433-437):
```csharp
        rendered.ShouldNotContain("\n\n\n");
        rendered.ShouldNotContain("\n\n    }");
        rendered.ShouldContain(
            "    {\n        RequireMultipleIdentities();\n        using var _ = UseIdentity(IdentitySlot.Secondary);\n\n        using var request",
            customMessage: "guard and override must sit on adjacent lines, with exactly one blank line before the request");
```
becomes:
```csharp
        rendered.ShouldNotContain("\r\n\r\n\r\n");
        rendered.ShouldNotContain("\r\n\r\n    }");
        rendered.ShouldContain(
            "    {\r\n        RequireMultipleIdentities();\r\n        using var _ = UseIdentity(IdentitySlot.Secondary);\r\n\r\n        using var request",
            customMessage: "guard and override must sit on adjacent lines, with exactly one blank line before the request");
```

In `EmitsNoStrayBlankLinesForANoTokenAuthCase` (~line 445-449):
```csharp
        rendered.ShouldNotContain("\n\n\n");
        rendered.ShouldNotContain("\n\n    }");
        rendered.ShouldContain(
            "    {\n        using var _ = UseIdentity(IdentitySlot.None);\n\n        using var request",
            customMessage: "no guard line for a 401 case, and no leftover blank line ahead of the override");
```
becomes:
```csharp
        rendered.ShouldNotContain("\r\n\r\n\r\n");
        rendered.ShouldNotContain("\r\n\r\n    }");
        rendered.ShouldContain(
            "    {\r\n        using var _ = UseIdentity(IdentitySlot.None);\r\n\r\n        using var request",
            customMessage: "no guard line for a 401 case, and no leftover blank line ahead of the override");
```

In `EmitsNoStrayBlankLinesForAWrongScopeCaseWithRequiredScopes` (~line 543-547):
```csharp
        rendered.ShouldNotContain("\n\n\n");
        rendered.ShouldNotContain("\n\n    }");
        rendered.ShouldContain(
            "    {\n        RequireMultipleIdentities();\n        RequireSecondaryIdentityLacks(\"orders.write\");\n        using var _ = UseIdentity(IdentitySlot.Secondary);\n\n        using var request",
            customMessage: "both guards and the override sit on adjacent lines, with exactly one blank line before the request");
```
becomes:
```csharp
        rendered.ShouldNotContain("\r\n\r\n\r\n");
        rendered.ShouldNotContain("\r\n\r\n    }");
        rendered.ShouldContain(
            "    {\r\n        RequireMultipleIdentities();\r\n        RequireSecondaryIdentityLacks(\"orders.write\");\r\n        using var _ = UseIdentity(IdentitySlot.Secondary);\r\n\r\n        using var request",
            customMessage: "both guards and the override sit on adjacent lines, with exactly one blank line before the request");
```

In `EmitsNoStrayBlankLinesWithABodyOrQueryParameters` (~line 558-563) — this one does not fail today (its checks are all `ShouldNotContain`, which is vacuously true against CRLF content and so silently stops testing anything — fix it anyway, since a test that can never fail is a test-coverage regression this codebase's own conventions do not tolerate):
```csharp
        var withBody = Render(PlanWithBody());
        withBody.ShouldNotContain("\n\n\n");
        withBody.ShouldNotContain("\n\n    }");

        var withQuery = Render(PlanWithQueryParameters("page", "sort"));
        withQuery.ShouldNotContain("\n\n\n");
        withQuery.ShouldNotContain("\n\n    }");
```
becomes:
```csharp
        var withBody = Render(PlanWithBody());
        withBody.ShouldNotContain("\r\n\r\n\r\n");
        withBody.ShouldNotContain("\r\n\r\n    }");

        var withQuery = Render(PlanWithQueryParameters("page", "sort"));
        withQuery.ShouldNotContain("\r\n\r\n\r\n");
        withQuery.ShouldNotContain("\r\n\r\n    }");
```

After making all of the above, re-run:

```bash
dotnet build InTest.sln
dotnet test tests/InTest.Cli.Tests
```

**Expected: 409 passing, 1 failing — not 410/0.** The one failure is `InitCommandTests.GitattributesSurvivesAnAutocrlfTrueCheckout`, and it is *expected* at this checkpoint: that test scaffolds a project via `InitCommand` and round-trips it through a simulated `core.autocrlf=true` checkout, but `InitCommand.GitattributesContent` still pins `Generated/** text eol=lf` (Task 4 has not run yet), while `Normalize` (this task) now emits CRLF — the scaffolded pin and the renderer's actual output disagree until Task 4 lands. Confirm the failure is *only* that one test, by name, before proceeding — a different or additional failure is a real regression, not this known gap. No test was added or removed in this task; the 7 methods above already existed and already ran, only their assertion literals changed.

- [ ] **Step 7: Commit**

```bash
git add src/InTest.Cli/Rendering/TemplateRenderer.cs tests/InTest.Golden.Tests/Expected/OrdersTests.g.cs.txt tests/InTest.Cli.Tests/TemplateRendererTests.cs
git commit -m "feat: generate .g.cs classes with CRLF line endings"
```

---

## Task 3: `CommittedJsonOptions` and its four call sites

**Files:** `src/InTest.Cli/Json/CommittedJsonOptions.cs`; `src/InTest.Cli/Coverage/CoverageReport.cs:151-156`; `src/InTest.Cli/Fixtures/FixtureDocument.cs:132-138`; `src/InTest.Cli/Schemas/SchemaBundleBuilder.cs:40-50`; `src/InTest.Cli/Commands/GenerateCommand.cs:112-115`; `tests/InTest.Cli.Tests/JsonWritingOptionsGuardTests.cs`; `tests/InTest.Cli.Tests/SchemaBundleBuilderTests.cs:84-100`.

- [ ] **Step 1: Flip `CommittedJsonOptions`**

In `src/InTest.Cli/Json/CommittedJsonOptions.cs`, replace the whole file's summary and value with:

```csharp
using System.Text.Json;

namespace InTest.Cli.Json;

/// <summary>
/// The one <see cref="JsonSerializerOptions"/> instance behind every JSON file `generate` or
/// `fixtures repair` writes to disk: <c>CoverageReport</c>, <c>FixtureDocument</c>,
/// <c>SchemaBundleBuilder</c>, and <c>GenerateCommand</c>'s spec-paths.json manifest. All four
/// are committed artefacts — three are `--check`-compared, the fourth (fixtures/) is hand-edited
/// and diffed by adopters — where a stray LF is not cosmetic; see [crlf-everywhere]
/// (`docs/superpowers/plans/2026-08-23-crlf-everywhere.md`), which reverses the v1-e line-endings
/// task's LF choice for the same reason it was made: one fixed convention, chosen deliberately,
/// beats one that tracks whatever the writing platform's default happens to be.
/// <para>
/// <see cref="JsonSerializerOptions.NewLine"/> pins the <em>interior</em> line endings a writer
/// emits between properties to CRLF; without it, System.Text.Json defaults to
/// <see cref="Environment.NewLine"/>, which is LF on Linux/macOS — so a writer that skipped this
/// would still vary by platform, only now it would happen to match on Windows and diverge
/// everywhere else. Each call site still appends its own trailing <c>"\r\n"</c> by hand —
/// <c>WriteIndented</c> never emits a line ending after the final closing brace, and one call site
/// (<c>SchemaBundleBuilder</c>) also needs its own <c>.Replace(...)</c> pass afterwards — so the
/// trailing newline is not folded into this shared instance; there would be nothing left to share
/// if it were.
/// </para>
/// <para>
/// One instance, not four inline copies: <see cref="JsonSerializerOptions"/> keeps a per-options
/// reflection/metadata cache (documented on the type itself), so four structurally-equal
/// instances paid for that cache four times over for no reason. A single instance also means the
/// "why NewLine" reasoning above lives once, not once per call site with three of the four
/// restating "same fix, same reasoning" instead of the reasoning itself — and
/// JsonWritingOptionsGuardTests (InTest.Cli.Tests) enforces mechanically that no fifth writer
/// reintroduces an inline copy and silently forgets NewLine.
/// </para>
/// </summary>
internal static class CommittedJsonOptions
{
    public static readonly JsonSerializerOptions Value = new() { WriteIndented = true, NewLine = "\r\n" };
}
```

- [ ] **Step 2: Flip the four call sites**

In `src/InTest.Cli/Coverage/CoverageReport.cs`, replace:

```csharp
        // CommittedJsonOptions pins interior line endings to LF; see its own doc comment for why.
        // The trailing "+ \"\\n\"" below is separate — indented JSON serialization never emits a
        // line ending after the final closing brace, so that final newline is still added by
        // hand here. This is a committed, `--check`-compared artefact, so one line ending
        // throughout matters for the same reason it matters in any file a human diffs.
        return report.ToJsonString(CommittedJsonOptions.Value) + "\n";
```

with:

```csharp
        // CommittedJsonOptions pins interior line endings to CRLF; see its own doc comment for
        // why. The trailing "+ \"\r\n\"" below is separate — indented JSON serialization never
        // emits a line ending after the final closing brace, so that final newline is still added
        // by hand here. This is a committed, `--check`-compared artefact, so one line ending
        // throughout matters for the same reason it matters in any file a human diffs.
        return report.ToJsonString(CommittedJsonOptions.Value) + "\r\n";
```

In `src/InTest.Cli/Fixtures/FixtureDocument.cs`, replace:

```csharp
        // CommittedJsonOptions pins interior line endings to LF; see its own doc comment for why.
        // The trailing "+ \"\\n\"" is the final newline after the closing brace, which indented
        // JSON serialization never emits on its own. fixtures/ is written only by `fixtures repair`,
        // never generated wholesale like Generated/, so a hand-edited value here is read closely
        // — mixed line endings would bury the one changed line in a whole-file diff, which is the
        // failure this fix removes.
        return root.ToJsonString(CommittedJsonOptions.Value) + "\n";
```

with:

```csharp
        // CommittedJsonOptions pins interior line endings to CRLF; see its own doc comment for
        // why. The trailing "+ \"\r\n\"" is the final newline after the closing brace, which
        // indented JSON serialization never emits on its own. fixtures/ is written only by
        // `fixtures repair`, never generated wholesale like Generated/, so a hand-edited value
        // here is read closely — mixed line endings would bury the one changed line in a
        // whole-file diff, which is the failure this fix removes.
        return root.ToJsonString(CommittedJsonOptions.Value) + "\r\n";
```

In `src/InTest.Cli/Schemas/SchemaBundleBuilder.cs`, replace:

```csharp
        var bundle = new JsonObject { ["definitions"] = definitions };
        // CommittedJsonOptions pins interior line endings to LF; see its own doc comment for why.
        // Unlike the other three call sites, this one previously appended nothing after
        // ToJsonString, so the file had no trailing newline at all; the appended "\n" below is a
        // deliberate addition, not a preserved behaviour, chosen so every JSON file `generate`
        // writes ends the same way — a single trailing LF — rather than leaving spec-schemas.json
        // as the one file in Generated/ without one. Pinned by
        // SchemaBundleBuilderTests.EndsWithASingleTrailingLineFeed.
        return bundle.ToJsonString(CommittedJsonOptions.Value)
                     .Replace(ComponentPrefix, DefinitionPrefix, StringComparison.Ordinal)
                     + "\n";
```

with:

```csharp
        var bundle = new JsonObject { ["definitions"] = definitions };
        // CommittedJsonOptions pins interior line endings to CRLF; see its own doc comment for
        // why. Unlike the other three call sites, this one previously appended nothing after
        // ToJsonString, so the file had no trailing newline at all; the appended "\r\n" below is a
        // deliberate addition, not a preserved behaviour, chosen so every JSON file `generate`
        // writes ends the same way — a single trailing CRLF — rather than leaving
        // spec-schemas.json as the one file in Generated/ without one. Pinned by
        // SchemaBundleBuilderTests.EndsWithASingleTrailingCarriageReturnLineFeed.
        return bundle.ToJsonString(CommittedJsonOptions.Value)
                     .Replace(ComponentPrefix, DefinitionPrefix, StringComparison.Ordinal)
                     + "\r\n";
```

In `src/InTest.Cli/Commands/GenerateCommand.cs`, replace:

```csharp
        // CommittedJsonOptions pins interior line endings to LF; see its own doc comment for why.
        // The trailing "+ \"\\n\"" is the final newline after the closing brace, which indented
        // JSON serialization never emits on its own.
        outputs["Generated/spec-paths.json"] = pathManifest.ToJsonString(CommittedJsonOptions.Value) + "\n";
```

with:

```csharp
        // CommittedJsonOptions pins interior line endings to CRLF; see its own doc comment for
        // why. The trailing "+ \"\r\n\"" is the final newline after the closing brace, which
        // indented JSON serialization never emits on its own.
        outputs["Generated/spec-paths.json"] = pathManifest.ToJsonString(CommittedJsonOptions.Value) + "\r\n";
```

- [ ] **Step 3: Update `JsonWritingOptionsGuardTests.cs`**

In `tests/InTest.Cli.Tests/JsonWritingOptionsGuardTests.cs`, in the class doc comment, replace:

```
/// constructing its own <c>System.Text.Json.JsonSerializerOptions { WriteIndented = true }</c>
/// inline, silently defaulting <c>NewLine</c> to <c>Environment.NewLine</c> (CRLF on Windows)
/// instead of routing through <c>InTest.Cli.Json.CommittedJsonOptions</c>, which pins it to
/// <c>"\n"</c>. Before that fix three of the four writers repeated the same ~7-line comment
```

with:

```
/// constructing its own <c>System.Text.Json.JsonSerializerOptions { WriteIndented = true }</c>
/// inline, silently defaulting <c>NewLine</c> to <c>Environment.NewLine</c> (LF on Linux/macOS)
/// instead of routing through <c>InTest.Cli.Json.CommittedJsonOptions</c>, which pins it to
/// <c>"\r\n"</c>. Before that fix three of the four writers repeated the same ~7-line comment
```

And in `NoWriterConstructsItsOwnWriteIndentedOptions`'s failure message, replace:

```csharp
        offenders.ShouldBeEmpty(
            "These files mention 'WriteIndented' outside InTest.Cli.Json.CommittedJsonOptions: " +
            string.Join(", ", offenders.OrderBy(n => n, StringComparer.Ordinal)) + ". A JSON " +
            "writer that constructs its own JsonSerializerOptions here silently defaults NewLine " +
            "to Environment.NewLine (CRLF on Windows) instead of the pinned \"\\n\" — reference " +
            "InTest.Cli.Json.CommittedJsonOptions.Value instead, or add the file to " +
            "JsonWritingOptionsGuardTests.Allowed with a one-line reason if it genuinely is not " +
            "one of the committed artefacts CommittedJsonOptions exists for.");
```

with:

```csharp
        offenders.ShouldBeEmpty(
            "These files mention 'WriteIndented' outside InTest.Cli.Json.CommittedJsonOptions: " +
            string.Join(", ", offenders.OrderBy(n => n, StringComparer.Ordinal)) + ". A JSON " +
            "writer that constructs its own JsonSerializerOptions here silently defaults NewLine " +
            "to Environment.NewLine (LF on Linux/macOS) instead of the pinned \"\\r\\n\" — " +
            "reference InTest.Cli.Json.CommittedJsonOptions.Value instead, or add the file to " +
            "JsonWritingOptionsGuardTests.Allowed with a one-line reason if it genuinely is not " +
            "one of the committed artefacts CommittedJsonOptions exists for.");
```

- [ ] **Step 4: Update and rename the `SchemaBundleBuilderTests` trailing-newline test**

In `tests/InTest.Cli.Tests/SchemaBundleBuilderTests.cs`, replace:

```csharp
    /// <summary>
    /// Pins the trailing "\n" the v1-e line-endings task added to Build(): before that fix it
    /// appended nothing at all, so spec-schemas.json was the one file in Generated/ with no
    /// final newline. GitattributesSurvivesAnAutocrlfTrueCheckout's before/after byte comparison
    /// cannot catch a regression back to that — both sides of that round trip are written by
    /// the same call to Build(), so an unconditional absence would compare equal to itself. This
    /// test is the only thing asserting the newline is there at all, and also that it is exactly
    /// one LF, not CRLF and not doubled.
    /// </summary>
    [TestMethod]
    public async Task EndsWithASingleTrailingLineFeed()
    {
        var bundle = await BuildAsync();
        bundle.ShouldEndWith("\n");
        bundle.ShouldNotEndWith("\r\n");
        bundle.ShouldNotEndWith("\n\n");
    }
```

with:

```csharp
    /// <summary>
    /// Pins the trailing "\r\n" the v1-e line-endings task added to Build() (LF at the time;
    /// [crlf-everywhere] flips the direction, not the reasoning): before that fix it appended
    /// nothing at all, so spec-schemas.json was the one file in Generated/ with no final newline.
    /// GitattributesSurvivesAnAutocrlfTrueCheckout's before/after byte comparison cannot catch a
    /// regression back to that — both sides of that round trip are written by the same call to
    /// Build(), so an unconditional absence would compare equal to itself. This test is the only
    /// thing asserting the newline is there at all, and also that it is exactly one CRLF, not a
    /// bare LF and not doubled.
    /// </summary>
    [TestMethod]
    public async Task EndsWithASingleTrailingCarriageReturnLineFeed()
    {
        var bundle = await BuildAsync();
        bundle.ShouldEndWith("\r\n");
        bundle.ShouldNotEndWith("\r\n\r\n");
    }
```

- [ ] **Step 5: Check for stray references to the renamed test**

```bash
grep -rn "EndsWithASingleTrailingLineFeed" --include="*.cs" --include="*.md" .
```

Expected: no matches outside the file just edited. If any turn up, update them.

- [ ] **Step 6: Build and run the Cli suite**

```bash
dotnet build InTest.sln
dotnet test tests/InTest.Cli.Tests
```

Expected: all 410 tests still pass (no count change — this task only changes byte content and comments, not test count).

- [ ] **Step 7: Commit**

```bash
git add src/InTest.Cli/Json/CommittedJsonOptions.cs src/InTest.Cli/Coverage/CoverageReport.cs \
        src/InTest.Cli/Fixtures/FixtureDocument.cs src/InTest.Cli/Schemas/SchemaBundleBuilder.cs \
        src/InTest.Cli/Commands/GenerateCommand.cs tests/InTest.Cli.Tests/JsonWritingOptionsGuardTests.cs \
        tests/InTest.Cli.Tests/SchemaBundleBuilderTests.cs
git commit -m "feat: write coverage-report.json, fixtures/*.json and spec-*.json with CRLF"
```

---

## Task 4: `InitCommand`'s scaffolded content

**Files:** `src/InTest.Cli/Commands/InitCommand.cs:29-68` (`GitattributesContent`), `:397-398` (`Write`).

- [ ] **Step 1: Flip `GitattributesContent`**

Replace:

```csharp
    /// <summary>
    /// The exact bytes `init` scaffolds at <c>.gitattributes</c> — hoisted to a named constant,
    /// internal rather than private, so <c>UpgradeCommand</c> can write the identical file for a
    /// project scaffolded before <c>[lf-everywhere]</c> shipped (v1-e plan, Task 2 Step 3 /
    /// Task 4 Step 1b) without a second hand-copied literal that could silently drift from this
    /// one. Modeled on this repository's own <c>.gitattributes</c>, which pins the identical case
    /// (*.g.cs.txt golden files, *.scriban templates) for the identical reason — with one
    /// deliberate difference: no <c>* text=auto</c> line. That line normalizes *every* path under
    /// wherever this file lives, not just the three patterns below, and a .gitattributes in a
    /// subdirectory outranks one at the adopting team's repo root for paths beneath it — so it
    /// would silently reverse a deliberate root policy such as `* -text` for TestStartup.cs,
    /// appsettings*.json, this project's own .csproj, and anything the team adds later (the
    /// "everything else | the adopting team | InTest never touches" row of CLAUDE.md's ownership
    /// table). `eol=lf` on its own already implies `text` for the paths it names, so it needs no
    /// help from a blanket normalization line — confirmed by mutation: deleting `* text=auto`
    /// from this scaffold leaves GitattributesSurvivesAnAutocrlfTrueCheckout passing; the three
    /// `eol=lf` lines carry the fix alone.
    /// <para>
    /// Every path pinned here is InTest-owned: `generate` deletes and rewrites Generated/
    /// wholesale and writes coverage-report.json, `fixtures repair` writes fixtures/**/*.json —
    /// base fixtures and every profile overlay subdirectory alike, since FixtureStore.Load deep-
    /// merges fixtures/{profile}/*.json over fixtures/*.json and both are committed, hand-edited
    /// files — and all of it is now pure-LF content (TemplateRenderer.Normalize for the .g.cs
    /// classes, CommittedJsonOptions.NewLine for the JSON writers). Without this file, a clone
    /// with core.autocrlf=true — the Git-for-Windows default — rewrites every one of them to CRLF
    /// on checkout, because nothing else tells git these particular paths must stay LF. That
    /// checkout-time rewrite is invisible to `fixtures repair` (FixtureDrift.Compare works on
    /// parsed FixtureDocument objects, not bytes) but not to a byte-for-byte comparison such as
    /// `generate --check`.
    /// </para>
    /// </summary>
    internal const string GitattributesContent = """
    # InTest writes these files with LF interior line endings (a template Normalize step for
    # generated .g.cs classes, JsonSerializerOptions.NewLine = "\n" for the JSON files). A
    # clone with core.autocrlf=true (the Git-for-Windows default) would otherwise rewrite
    # them to CRLF on checkout, with nothing on disk to show why.
    Generated/** text eol=lf
    coverage-report.json text eol=lf
    fixtures/**/*.json text eol=lf
    """;
```

with:

```csharp
    /// <summary>
    /// The exact bytes `init` scaffolds at <c>.gitattributes</c> — hoisted to a named constant,
    /// internal rather than private, so <c>UpgradeCommand</c> can write the identical file for a
    /// project scaffolded before <c>[crlf-everywhere]</c> shipped without a second hand-copied
    /// literal that could silently drift from this one. Modeled on this repository's own
    /// <c>.gitattributes</c>, which pins the identical case (*.g.cs.txt golden files, *.scriban
    /// templates) for the identical reason — with one deliberate difference: no
    /// <c>* text=auto</c> line. That line normalizes *every* path under wherever this file lives,
    /// not just the three patterns below, and a .gitattributes in a subdirectory outranks one at
    /// the adopting team's repo root for paths beneath it — so it would silently reverse a
    /// deliberate root policy such as `* -text` for TestStartup.cs, appsettings*.json, this
    /// project's own .csproj, and anything the team adds later (the "everything else | the
    /// adopting team | InTest never touches" row of CLAUDE.md's ownership table). `eol=crlf` on
    /// its own already implies `text` for the paths it names, so it needs no help from a blanket
    /// normalization line — confirmed by mutation under the LF-direction predecessor of this
    /// scaffold: deleting `* text=auto` left GitattributesSurvivesAnAutocrlfTrueCheckout passing;
    /// the three `eol=` lines carry the fix alone, and that mutation result does not depend on
    /// which letter `eol` names.
    /// <para>
    /// Every path pinned here is InTest-owned: `generate` deletes and rewrites Generated/
    /// wholesale and writes coverage-report.json, `fixtures repair` writes fixtures/**/*.json —
    /// base fixtures and every profile overlay subdirectory alike, since FixtureStore.Load deep-
    /// merges fixtures/{profile}/*.json over fixtures/*.json and both are committed, hand-edited
    /// files — and all of it is now pure-CRLF content (TemplateRenderer.Normalize for the .g.cs
    /// classes, CommittedJsonOptions.NewLine for the JSON writers). Without this file, a clone
    /// whose checkout would otherwise diverge from CRLF (e.g. a Linux/macOS clone, or a Windows
    /// clone with core.autocrlf=false or =input) rewrites every one of them to LF on checkout,
    /// because nothing else tells git these particular paths must stay CRLF. That checkout-time
    /// rewrite is invisible to `fixtures repair` (FixtureDrift.Compare works on parsed
    /// FixtureDocument objects, not bytes) but not to a byte-for-byte comparison such as
    /// `generate --check`.
    /// </para>
    /// </summary>
    internal const string GitattributesContent = """
    # InTest writes these files with CRLF interior line endings (a template Normalize step for
    # generated .g.cs classes, JsonSerializerOptions.NewLine = "\r\n" for the JSON files). A
    # clone whose checkout would otherwise diverge from CRLF (e.g. a non-Windows default, or
    # core.autocrlf=input) would rewrite them on checkout, with nothing on disk to show why.
    Generated/** text eol=crlf
    coverage-report.json text eol=crlf
    fixtures/**/*.json text eol=crlf
    """;
```

- [ ] **Step 2: Flip `Write`**

Replace:

```csharp
    /// <summary>
    /// Internal rather than private: <c>UpgradeCommand</c> reuses this exact normalization
    /// (<c>ReplaceLineEndings("\n") + "\n"</c>, matching every other file `init` scaffolds) to
    /// write <see cref="GitattributesContent"/> for a project `init` itself refuses to touch —
    /// see that field's doc comment for why the two commands must share the constant.
    /// </summary>
    internal static void Write(string root, string relativePath, string content)
        => File.WriteAllText(Path.Combine(root, relativePath), content.ReplaceLineEndings("\n") + "\n");
```

with:

```csharp
    /// <summary>
    /// Internal rather than private: <c>UpgradeCommand</c> reuses this exact normalization
    /// (<c>ReplaceLineEndings("\r\n") + "\r\n"</c>, matching every other file `init` scaffolds) to
    /// write <see cref="GitattributesContent"/> for a project `init` itself refuses to touch —
    /// see that field's doc comment for why the two commands must share the constant.
    /// [crlf-everywhere]: this normalizes every file `init` writes, not only the three paths
    /// `GitattributesContent` pins — intest.json, the .csproj, TestStartup.cs and the rest are
    /// scaffolded once, at write time, so this call site is their only source of line-ending
    /// truth; nothing in `.gitattributes` needs to pin them separately for the initial write to
    /// be CRLF (a subsequent checkout of the adopter's own repo is the adopting team's own
    /// `.gitattributes`/core.autocrlf concern from that point on, per CLAUDE.md's ownership
    /// table).
    /// </summary>
    internal static void Write(string root, string relativePath, string content)
        => File.WriteAllText(Path.Combine(root, relativePath), content.ReplaceLineEndings("\r\n") + "\r\n");
```

- [ ] **Step 3: Build**

```bash
dotnet build InTest.sln
```

- [ ] **Step 4 (added by revision): fix `UpgradeCommandTests.ScaffoldsGitattributesWhenAbsent`**

Task 4's implementer found a gap the plan missed on first write, the same class of gap Task 2 found: `tests/InTest.Cli.Tests/UpgradeCommandTests.cs:686` hard-codes the OLD scaffolded `.gitattributes` content. `UpgradeCommand` writes a project's `.gitattributes` (when absent) using the exact same `InitCommand.GitattributesContent` constant Step 1 above just changed, so this assertion breaks the moment Step 1 lands. Find:

```csharp
        File.ReadAllText(Path.Combine(_root, ".gitattributes")).ShouldContain("Generated/** text eol=lf", Case.Sensitive);
```

Replace with:

```csharp
        File.ReadAllText(Path.Combine(_root, ".gitattributes")).ShouldContain("Generated/** text eol=crlf", Case.Sensitive);
```

Do not touch `NeverOverwritesAnExistingGitattributes` (the neighboring test) — its hand-written `"# adopter customised this file\n*.custom text eol=lf\n"` fixture content simulates an *adopter's own* custom file, unrelated to what `InitCommand`/`UpgradeCommand` scaffold, and correctly stays as arbitrary test input regardless of this project's own convention.

Run:
```bash
dotnet build InTest.sln
dotnet test tests/InTest.Cli.Tests --filter "FullyQualifiedName~UpgradeCommandTests"
```
Expected: `ScaffoldsGitattributesWhenAbsent` passes; no other test in this file regresses.

- [ ] **Step 5: Commit**

```bash
git add src/InTest.Cli/Commands/InitCommand.cs tests/InTest.Cli.Tests/UpgradeCommandTests.cs
git commit -m "feat: init scaffolds every file, including .gitattributes, with CRLF"
```

**Expected `dotnet test tests/InTest.Cli.Tests` count at this checkpoint: 409 passing, 1 failing** — same lone `InitCommandTests.GitattributesSurvivesAnAutocrlfTrueCheckout` failure as before this task, though it may now fail on a *different* file inside its byte-comparison (e.g. `fixtures/sample.json` instead of `Generated/OrdersTests.g.cs`, since `InitCommand.Write` (Step 2) started emitting CRLF for adopter-authored-then-committed content ahead of the JSON writers catching up in the test's own hand-written fixtures) — that specific test is Task 5's job to finish, not a new regression as long as it's the only failure and it's byte-mismatch-shaped, not a crash or a different test entirely.

---

## Task 5: `InitCommandTests.cs`

**Files:** `tests/InTest.Cli.Tests/InitCommandTests.cs` (the `GitattributesSurvivesAnAutocrlfTrueCheckout` test and its helpers, roughly lines 90-220).

- [ ] **Step 1: Update the fixture content the test hand-writes**

Find:

```csharp
        Directory.CreateDirectory(Path.Combine(_root, "fixtures", "qa"));
        File.WriteAllText(Path.Combine(_root, "fixtures", "sample.json"), "{\n  \"sample\": true\n}\n");
        File.WriteAllText(Path.Combine(_root, "fixtures", "qa", "sample.json"), "{\n  \"sample\": false\n}\n");
```

Replace with:

```csharp
        Directory.CreateDirectory(Path.Combine(_root, "fixtures", "qa"));
        File.WriteAllText(Path.Combine(_root, "fixtures", "sample.json"), "{\r\n  \"sample\": true\r\n}\r\n");
        File.WriteAllText(Path.Combine(_root, "fixtures", "qa", "sample.json"), "{\r\n  \"sample\": false\r\n}\r\n");
```

And update the comment immediately above it. Find:

```csharp
        // `generate` alone never writes fixtures/ (only `fixtures repair` does), so write a base
        // fixture and a profile overlay by hand — pure LF, matching what FixtureDocument's
        // writer now produces. The overlay is the important half: fixtures/{profile}/*.json
```

Replace with:

```csharp
        // `generate` alone never writes fixtures/ (only `fixtures repair` does), so write a base
        // fixture and a profile overlay by hand — pure CRLF, matching what FixtureDocument's
        // writer now produces. The overlay is the important half: fixtures/{profile}/*.json
```

- [ ] **Step 2: Update the `.gitattributes` assertion**

Find (this is the assertion at the tail of `GitattributesSurvivesAnAutocrlfTrueCheckout` or a neighboring test that checks scaffolded content — search for the literal string first):

```bash
grep -n "Generated/\*\* text eol=lf" tests/InTest.Cli.Tests/InitCommandTests.cs
```

At that line, replace `"Generated/** text eol=lf"` with `"Generated/** text eol=crlf"`. Leave everything else on that line (the `Case.Sensitive` argument, the surrounding `ShouldContain` call) unchanged.

- [ ] **Step 3: Update the doc comments describing the checkout direction**

In the doc comment immediately above `GitattributesSurvivesAnAutocrlfTrueCheckout`, find:

```csharp
    /// <summary>
    /// Proves the scaffolded .gitattributes actually does its job, rather than merely existing.
    /// "The file on disk contains LF" would pass on Linux, or with core.autocrlf left at its
    /// non-Windows default, regardless of whether .gitattributes covers the right paths — or
    /// exists at all. This instead reproduces Step 1 of the v1-e line-endings task's manual
    /// measurement as an automated round trip: commit a real `init` + `generate` scaffold with
    /// core.autocrlf=true (the Git-for-Windows default) set on the source, then materialize a
    /// second working copy with the same setting forced on the destination — the two-step path a
    /// Windows adopter's own clone goes through — and diff the bytes. Every one of InTest's own
    /// generated artefacts (Generated/**, coverage-report.json, fixtures/**/*.json — a base
    /// fixture and a profile overlay alike) must come back byte-identical; without
    /// .gitattributes pinning them, git's own autocrlf translation would rewrite every LF to
    /// CRLF on the second checkout, exactly as the manual experiment showed.
    /// </summary>
```

Replace with:

```csharp
    /// <summary>
    /// Proves the scaffolded .gitattributes actually does its job, rather than merely existing.
    /// "The file on disk contains CRLF" would pass regardless of whether .gitattributes covers
    /// the right paths — or exists at all — on a machine whose own git config already defaults to
    /// CRLF. This instead reproduces the v1-e line-endings task's manual measurement as an
    /// automated round trip, with core.autocrlf forced explicitly rather than left at whatever
    /// this test happens to run under: commit a real `init` + `generate` scaffold with
    /// core.autocrlf=true set on the source, then materialize a second working copy with the same
    /// setting forced on the destination — the two-step path a Windows adopter's own clone goes
    /// through — and diff the bytes. Every one of InTest's own generated artefacts (Generated/**,
    /// coverage-report.json, fixtures/**/*.json — a base fixture and a profile overlay alike) must
    /// come back byte-identical; without .gitattributes pinning them to eol=crlf, an autocrlf
    /// setting that resolves to LF on some other checkout (core.autocrlf=input, or the
    /// non-Windows default) would rewrite them, the same class of gap [crlf-everywhere] exists to
    /// close, direction reversed from what the v1-e manual experiment originally showed.
    /// </summary>
```

In `AssertByteIdenticalAcrossCheckout`'s doc comment and body, find:

```csharp
    /// <summary>
    /// Fails with a message that names both things that can produce this exact symptom — bytes
    /// differing across a core.autocrlf=true checkout — rather than asserting only one. The
    /// original message ("`.gitattributes` did not pin it to LF") is true when this test's own
    /// .gitattributes has a gap; it is false, and misleading, when the writer that produced
    /// <paramref name="before"/> already emitted CRLF before the file was ever committed (a
    /// <c>JsonSerializerOptions.NewLine</c> or template <c>Normalize</c> regression) — in which
    /// case the checkout changed nothing and .gitattributes is not the bug. The two are
    /// distinguished by whether <paramref name="before"/> already contains a CRLF sequence: if it
    /// does, the checkout did not introduce it. A raw <c>byte[]</c> comparison (Shouldly's
    /// default <c>ShouldBe</c>) renders on the order of 10 KB of decimal byte codes for a file
    /// this size before reaching any custom message; hex is at least legible, and the CRLF counts
    /// alone usually say which half of the diagnosis applies without reading the dump at all.
    /// </summary>
    private static void AssertByteIdenticalAcrossCheckout(string file, byte[] before, byte[] after)
    {
        if (before.AsSpan().SequenceEqual(after))
        {
            return;
        }

        var crlfBefore = CountCrlf(before);
        var crlfAfter = CountCrlf(after);
        var likelyCause = crlfBefore > 0
            ? "the writer that produced this file already emitted CRLF before it was committed " +
              "(JsonSerializerOptions.NewLine, or a template's Normalize step, was not honored) " +
              "— .gitattributes is not at fault here"
            : ".gitattributes did not pin this file to LF, so the core.autocrlf=true checkout " +
              "rewrote its LF line endings to CRLF";
```

Replace with:

```csharp
    /// <summary>
    /// Fails with a message that names both things that can produce this exact symptom — bytes
    /// differing across a core.autocrlf=true checkout — rather than asserting only one. The naive
    /// message ("`.gitattributes` did not pin it to CRLF") is true when this test's own
    /// .gitattributes has a gap; it is false, and misleading, when the writer that produced
    /// <paramref name="before"/> already emitted LF before the file was ever committed (a
    /// <c>JsonSerializerOptions.NewLine</c> or template <c>Normalize</c> regression back toward
    /// the pre-[crlf-everywhere] direction) — in which case the checkout changed nothing and
    /// .gitattributes is not the bug. The two are distinguished by whether
    /// <paramref name="before"/> already contains a CRLF sequence: if it does not, the checkout
    /// did not remove one. A raw <c>byte[]</c> comparison (Shouldly's default <c>ShouldBe</c>)
    /// renders on the order of 10 KB of decimal byte codes for a file this size before reaching
    /// any custom message; hex is at least legible, and the CRLF counts alone usually say which
    /// half of the diagnosis applies without reading the dump at all.
    /// </summary>
    private static void AssertByteIdenticalAcrossCheckout(string file, byte[] before, byte[] after)
    {
        if (before.AsSpan().SequenceEqual(after))
        {
            return;
        }

        var crlfBefore = CountCrlf(before);
        var crlfAfter = CountCrlf(after);
        var likelyCause = crlfBefore == 0
            ? "the writer that produced this file already emitted LF before it was committed " +
              "(JsonSerializerOptions.NewLine, or a template's Normalize step, was not honored) " +
              "— .gitattributes is not at fault here"
            : ".gitattributes did not pin this file to CRLF, so the checkout stripped its CRLF " +
              "line endings down to LF";
```

Two lines further down in the same method, the `Assert.Fail` message text ("changed bytes across a core.autocrlf=true checkout") does not name a direction and needs no edit.

- [ ] **Step 4: Build and run**

```bash
dotnet build InTest.sln
dotnet test tests/InTest.Cli.Tests --filter "FullyQualifiedName~InitCommandTests"
```

Expected: all tests in this file pass, including `GitattributesSurvivesAnAutocrlfTrueCheckout` (this one shells out to a real `git` binary — confirm `git` is on PATH if it fails to start, per that test's own failure message).

- [ ] **Step 5: Run the full Cli suite**

```bash
dotnet test tests/InTest.Cli.Tests
```

Expected: 410 passing, 0 failing — same count as baseline, since no tests were added or removed in this task.

- [ ] **Step 6: Commit**

```bash
git add tests/InTest.Cli.Tests/InitCommandTests.cs
git commit -m "test: assert init's CRLF scaffold survives a checkout that would otherwise flatten it to LF"
```

---

## Task 6: Documentation

**Files:** `CONTRIBUTING.md`; `docs/getting-started.md`; `docs/superpowers/specs/2026-08-16-intest-api-test-generator-design.md`.

- [ ] **Step 1: `CONTRIBUTING.md`**

Find the sentence:

```
matched. `windows-latest`'s default `core.autocrlf=true` checks the same file out CRLF, the literal
carried CRLF, and the same bytes the assertion meant to find — `"version": 1` immediately followed
by `"isRoot": true` — were there, just not joined by a bare `\n` any more. The fix was the same
shape as the casing fix: split into two `ShouldContain` calls, one per value, so the assertion
states the claim it actually means ("these two values didn't change") rather than a stronger one it
does not ("...and stayed joined by this exact byte"). `.gitattributes` also gained a `*.cs
text eol=lf` entry so the checkout itself stops varying by platform — the mechanical scan behind
that decision (grep every `\n`/`\r\n` literal compared against source-file-derived content) found
this to be the only test in the suite with the hazard; the rest either used regular escape
```

Replace only the `.gitattributes` sentence within it:

```
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
```

- [ ] **Step 2: `docs/getting-started.md`**

Find:

```
| `.gitattributes` | yours | Pins `Generated/`, `coverage-report.json` and `fixtures/**/*.json` to LF, so a clone with `core.autocrlf=true` cannot check them out as CRLF and fail `generate --check` on every line |
```

Replace with:

```
| `.gitattributes` | yours | Pins `Generated/`, `coverage-report.json` and `fixtures/**/*.json` to CRLF, so a clone whose checkout would otherwise default to LF (Linux/macOS, or Windows with `core.autocrlf` set to `false`/`input`) cannot check them out as LF and fail `generate --check` on every line |
```

- [ ] **Step 3: The design spec's project-tree listing**

Find:

```
├── .gitattributes                # pins Generated/, coverage-report.json, fixtures/**/*.json to LF
```

Replace with:

```
├── .gitattributes                # pins Generated/, coverage-report.json, fixtures/**/*.json to CRLF
```

- [ ] **Step 4: Commit**

```bash
git add CONTRIBUTING.md docs/getting-started.md docs/superpowers/specs/2026-08-16-intest-api-test-generator-design.md
git commit -m "docs: describe the CRLF convention (was LF) for InTest-owned generated files"
```

---

## Task 7: Full verification

- [ ] **Step 1: Clean build**

```bash
dotnet build InTest.sln
```

Expected: no errors, no warnings.

- [ ] **Step 2: Full test suite**

```bash
dotnet test InTest.sln
```

Expected: **658 passing, 0 failing** — same total as the baseline (Architecture 8, Cli 410, Runtime 205, Golden 35). This plan does not add or remove any test; if the count differs, find out why before declaring done — a dropped test stays green.

- [ ] **Step 3: Confirm the golden file is actually CRLF on disk**

```bash
dotnet run --project src/InTest.Cli -- init --name Scratch.ApiTests --spec ../does-not-matter.json --project /tmp-does-not-exist
```

(This will fail — the point is not to run `init` for real here. Skip this step's exact command and instead directly inspect the committed golden file:)

```bash
grep -c $'\r' tests/InTest.Golden.Tests/Expected/OrdersTests.g.cs.txt
```

Expected: a non-zero count equal to the file's line count (every line ends in `\r\n`).

- [ ] **Step 4: Confirm `.gitattributes` round-trips correctly for a real scaffold**

Run the CI-equivalent dogfood check locally, which exercises `init` → `generate` → `fixtures repair` → `generate` → `generate --check` against the sample specs (per CLAUDE.md's documented reproduction command):

```bash
pwsh scripts/ci/dogfood.ps1 -RepoRoot . -ScaffoldRoot "$env:TEMP/intest-crlf-dogfood" -CliDll "$(pwd)/src/InTest.Cli/bin/Debug/net10.0/InTest.Cli.dll"
```

Expected: exits 0. If `src/InTest.Cli/bin/Debug/net10.0/InTest.Cli.dll` does not exist, run `dotnet build InTest.sln` first (Step 1 already does this).

- [ ] **Step 5: Report**

Summarize, in the final message to whoever requested this plan: the before/after test counts (must match), confirmation that `dotnet build` is warning-free, and a one-line pointer to this plan file for anyone who finds a stale "LF" comment later.

No commit for this task — it is verification only, over the commits already made in Tasks 1-6.

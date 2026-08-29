# HttpStatusCode in the Call Surface — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generated tests read `ExpectStatus(HttpStatusCode.NoContent, ...)` instead of `ExpectStatus(204, ...)`, and failure messages name the status instead of printing a bare number.

**Architecture:** The four `Expect*` methods gain `HttpStatusCode` overloads alongside their existing `int` ones. The generator emits the enum form for any code .NET names and a bare `int` for anything it does not, so **no cast ever appears in adopter code**. Failure messages gain the name too. A six-entry table resolves the codes with two enum names in favour of the HTTP-spec name; it is duplicated in the CLI and the runtime by necessity and enforced mechanically.

**Tech Stack:** .NET 10 · MSTest 4.3.3 · Scriban 7.2.6 · Shouldly 4.3.0

**Follows:** `docs/superpowers/plans/2026-08-29-intest-unified-call-surface.md` (Tasks 1–9, complete). This plan continues on the same branch, `unified-call-surface`, because the `Expect*` methods it changes exist **only there and are unpublished** — the signature is free today and a breaking change the moment `0.1.0-preview.2` ships.

---

## Why this shape

**`ApiResponseAssertions` keeps `int` and is not touched.** Its four public methods ship in
`0.1.0-preview.1`; changing a parameter type there is a breaking change for anyone who has already
adopted. The conversion lives in the new `Expect*` overloads, which are unpublished.

**Both overloads, not a replacement.** Keeping the `int` forms is what lets the generator emit a
bare `599` for a status .NET does not name, rather than `(HttpStatusCode)599`. The alternative —
enum-only — pushes a cast into committed adopter code for exactly the specs least likely to be
well-behaved. Overload resolution is unambiguous: an `int` literal binds to the `int` overload, an
enum member to the enum overload.

**Six codes have two enum names, and `ToString()` does not always pick the better one.** Measured
against .NET 10:

| code | members | `ToString()` returns | this plan emits |
|---|---|---|---|
| 300 | `MultipleChoices`, `Ambiguous` | `MultipleChoices` | `MultipleChoices` |
| 301 | `MovedPermanently`, `Moved` | `MovedPermanently` | `MovedPermanently` |
| 302 | `Found`, `Redirect` | `Found` | `Found` |
| 303 | `SeeOther`, `RedirectMethod` | `SeeOther` | `SeeOther` |
| **307** | `TemporaryRedirect`, `RedirectKeepVerb` | **`RedirectKeepVerb`** | **`TemporaryRedirect`** |
| 422 | `UnprocessableEntity`, `UnprocessableContent` | `UnprocessableEntity` | `UnprocessableEntity` |

Only 307 actually differs, and it matters twice over: `RedirectKeepVerb` is the legacy
`WebRequest`-era name that no OpenAPI document uses, and tie-breaking among equal-valued enum
members depends on metadata declaration order, which is not a documented contract. This tool's
output is compared byte-for-byte against a golden file, so deriving names from `ToString()` would
make that output hostage to an ordering upstream. **All six are listed explicitly** — the five that
agree with `ToString()` are listed anyway, so the table reads as a decision rather than as a patch.

**The table is duplicated on purpose.** `InTest.Cli` has no reference to `InTest.Runtime` (checked:
its `.csproj` carries `PackageReference`s only), and adding one to share six strings would couple
the generator to the runtime it generates against. So the table exists in both, and
`InTest.Architecture.Tests` enforces agreement **by reading both files as text** — the same
mechanism `PackageVersionCouplingTests` already uses for the deliberate three-way version
duplication, and it needs no new `InternalsVisibleTo` grant.

---

## File Structure

| File | Change |
|---|---|
| `src/InTest.Runtime/HttpStatusNames.cs` | **Create** — the table plus `For(int)` |
| `src/InTest.Runtime/ApiResponseAssertions.cs` | `Failure` names the status |
| `src/InTest.Runtime/ApiTestCore.cs` | Four `HttpStatusCode` overloads |
| `src/InTest.Cli/Naming/HttpStatusExpression.cs` | **Create** — the same table plus `For(int)` returning the emitted expression |
| `src/InTest.Cli/Rendering/TemplateRenderer.cs` | New `expected_status_expression` model field |
| `src/InTest.Cli/Rendering/Templates/mstest-class.scriban` | Emit it; add `using System.Net;` |
| `tests/InTest.Architecture.Tests/HttpStatusNameCouplingTests.cs` | **Create** — the two tables agree |
| `tests/InTest.Runtime.Tests/ApiResponseAssertionsTests.cs` | 3 message assertions |
| `tests/InTest.Runtime.Tests/ApiTestCoreExpectTests.cs` | Cover the enum overloads |
| `tests/InTest.Cli.Tests/TemplateRendererTests.cs`, `TemplateRendererClientTests.cs` | Status assertions |
| `tests/InTest.Golden.Tests/Expected/OrdersTests.g.cs.txt` | Regenerated |

---

### Task 10: The name table and richer failure messages

**Files:**
- Create: `src/InTest.Runtime/HttpStatusNames.cs`
- Modify: `src/InTest.Runtime/ApiResponseAssertions.cs`
- Test: `tests/InTest.Runtime.Tests/ApiResponseAssertionsTests.cs`

- [ ] **Step 1: Write the failing tests**

Three assertions in `ApiResponseAssertionsTests.cs` currently read `ex.Message.ShouldContain("expected 200, got 503")` (two of them) and `ex.Message.ShouldContain("expected 204, got 503")`. Update all three to the new shape:

```csharp
ex.Message.ShouldContain("expected 200 OK, got 503 ServiceUnavailable");
```

```csharp
ex.Message.ShouldContain("expected 204 NoContent, got 503 ServiceUnavailable");
```

Then add a test for the unnamed case:

```csharp
/// <summary>
/// A status .NET does not name must still produce a usable message — the number alone, with no
/// empty parenthetical or stray space. OpenAPI documents may declare any integer, and a vendor
/// range like 599 is exactly where a diagnostic matters most.
/// </summary>
[TestMethod]
public async Task FailureMessageOmitsTheNameForAStatusDotNetDoesNotName()
{
    using var response = new HttpResponseMessage((HttpStatusCode)599)
    {
        Content = new StringContent("boom"),
    };

    var ex = await Should.ThrowAsync<ContractAssertionException>(() =>
        ApiResponseAssertions.ShouldMatchStatusAsync(
            response, 200, "test-id", TimeSpan.FromMilliseconds(1)));

    ex.Message.ShouldContain("expected 200 OK, got 599");
    ex.Message.ShouldNotContain("599 ");
}
```

- [ ] **Step 2: Run to verify they fail**

```bash
dotnet test tests/InTest.Runtime.Tests --filter "FullyQualifiedName~ApiResponseAssertionsTests"
```

Expected: **4 failed** — the three updated assertions plus the new test.

- [ ] **Step 3: Create the name table**

`src/InTest.Runtime/HttpStatusNames.cs`:

```csharp
using System.Net;

namespace InTest.Runtime;

/// <summary>
/// Maps a numeric HTTP status to the name InTest uses for it, in failure messages here and in
/// generated code on the CLI side.
/// <para>
/// <b>This table is duplicated in <c>InTest.Cli</c>'s <c>Naming/HttpStatusExpression.cs</c> and the
/// two must agree.</b> That is deliberate, not an oversight: <c>InTest.Cli</c> takes no reference to
/// <c>InTest.Runtime</c> — it generates code *against* the runtime rather than consuming it — and
/// coupling the two packages to share six strings would be a worse trade than duplicating them.
/// <c>InTest.Architecture.Tests</c>' <c>HttpStatusNameCouplingTests</c> makes the coupling
/// mechanical by reading both files as text, the same way <c>PackageVersionCouplingTests</c> guards
/// the deliberate three-way package-version duplication.
/// </para>
/// <para>
/// <b>Why an explicit table rather than <c>((HttpStatusCode)status).ToString()</c>.</b> Six values
/// carry two enum members each, and <c>ToString()</c>'s choice between them is not a documented
/// contract — it falls out of metadata declaration order. For 307 it returns
/// <c>RedirectKeepVerb</c>, the legacy <c>WebRequest</c>-era name that no OpenAPI document uses,
/// rather than <c>TemporaryRedirect</c>. Since generated output is compared byte-for-byte against a
/// golden file, deriving names from <c>ToString()</c> would leave that output hostage to an ordering
/// nobody promises to keep. All six are listed even though five happen to agree with
/// <c>ToString()</c> today, so the table reads as a decision rather than as a patch.
/// </para>
/// </summary>
internal static class HttpStatusNames
{
    private static readonly Dictionary<int, string> Preferred = new()
    {
        [300] = "MultipleChoices",
        [301] = "MovedPermanently",
        [302] = "Found",
        [303] = "SeeOther",
        [307] = "TemporaryRedirect",
        [422] = "UnprocessableEntity",
    };

    /// <summary>
    /// The name for <paramref name="status"/>, or null when .NET names no member for it — callers
    /// then use the bare number. Null is the normal case for vendor ranges, not an error.
    /// </summary>
    internal static string? For(int status)
    {
        if (Preferred.TryGetValue(status, out var preferred))
        {
            return preferred;
        }

        return Enum.IsDefined(typeof(HttpStatusCode), status)
            ? ((HttpStatusCode)status).ToString()
            : null;
    }
}
```

- [ ] **Step 4: Use it in the failure message**

In `ApiResponseAssertions.cs`, the `Failure(int status, string? method, string? uri, …)` overload builds the first line. Replace:

```csharp
sb.Append(method ?? "?").Append(' ')
  .Append(uri ?? "<unknown uri>")
  .Append(" → expected ").Append(expectedStatus)
  .Append(", got ").Append(status)
  .Append(" (").Append(elapsed.TotalMilliseconds.ToString("N0", CultureInfo.InvariantCulture)).AppendLine("ms)");
```

with:

```csharp
sb.Append(method ?? "?").Append(' ')
  .Append(uri ?? "<unknown uri>")
  .Append(" → expected ").Append(Describe(expectedStatus))
  .Append(", got ").Append(Describe(status))
  .Append(" (").Append(elapsed.TotalMilliseconds.ToString("N0", CultureInfo.InvariantCulture)).AppendLine("ms)");
```

and add, next to it:

```csharp
/// <summary>
/// "404 NotFound" when .NET names the status, "599" when it does not. Deliberately no empty
/// parenthetical in the unnamed case — a message reading "599 ()" is worse than the bare number.
/// </summary>
private static string Describe(int status)
{
    var name = HttpStatusNames.For(status);
    return name is null
        ? status.ToString(CultureInfo.InvariantCulture)
        : string.Create(CultureInfo.InvariantCulture, $"{status} {name}");
}
```

- [ ] **Step 5: Run to verify they pass**

```bash
dotnet test tests/InTest.Runtime.Tests
```

Expected: **PASS** (265 — 264 plus the new unnamed-status test).

- [ ] **Step 6: Commit**

```bash
git add src/InTest.Runtime/HttpStatusNames.cs src/InTest.Runtime/ApiResponseAssertions.cs tests/InTest.Runtime.Tests/ApiResponseAssertionsTests.cs
git commit -m "feat: name the status in contract failure messages

'expected 200 OK, got 503 ServiceUnavailable' rather than 'expected 200, got 503'. A
status .NET does not name still prints bare, with no empty parenthetical.

HttpStatusNames resolves the six codes carrying two enum members in favour of the
HTTP-spec name. ToString() returns RedirectKeepVerb for 307 — the legacy WebRequest-era
name — and its tie-breaking is metadata declaration order rather than a documented
contract, which generated output compared byte-for-byte against a golden file must not
depend on."
```

---

### Task 11: `HttpStatusCode` overloads on the four `Expect*` methods

**Files:**
- Modify: `src/InTest.Runtime/ApiTestCore.cs`
- Test: `tests/InTest.Runtime.Tests/ApiTestCoreExpectTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `ApiTestCoreExpectTests`:

```csharp
/// <summary>
/// The enum overload must reach the same implementation as the int one — this is the form
/// generated code uses, so a divergence would be invisible in the runtime's own tests and show up
/// only in a generated suite.
/// </summary>
[TestMethod]
public async Task ExpectStatusAcceptsAnHttpStatusCodeAndBehavesIdentically()
{
    var (core, handler) = Harness(HttpStatusCode.NoContent);

    await core.ExposedExpectStatus(HttpStatusCode.NoContent, HttpMethod.Delete, "/api/orders/42");

    handler.CallCount.ShouldBe(1);
    handler.LastRequest!.Method.ShouldBe(HttpMethod.Delete);
}

[TestMethod]
public async Task ExpectStatusEnumOverloadThrowsOnAMismatchLikeTheIntOverload()
{
    var (core, _) = Harness(HttpStatusCode.InternalServerError, "boom");

    var ex = await Should.ThrowAsync<ContractAssertionException>(() =>
        core.ExposedExpectStatus(HttpStatusCode.NoContent, HttpMethod.Delete, "/api/orders/42"));

    ex.Message.ShouldContain("expected 204 NoContent, got 500 InternalServerError");
}
```

and the passthrough:

```csharp
public Task ExposedExpectStatus(HttpStatusCode expectedStatus, HttpMethod method, string url) =>
    ExpectStatus(expectedStatus, method, url);
```

- [ ] **Step 2: Run to verify they fail**

```bash
dotnet test tests/InTest.Runtime.Tests --filter "FullyQualifiedName~ApiTestCoreExpectTests"
```

Expected: **compile error** — no `ExpectStatus` overload takes `HttpStatusCode`.

- [ ] **Step 3: Add the four overloads**

In `ApiTestCore.cs`, beside each existing method. Each is a one-line delegation — the `int` forms stay the implementations:

```csharp
/// <summary>
/// [status-code-is-named]: the form generated code uses. Delegates to the <see cref="int"/>
/// overload, which stays the implementation because <see cref="ApiResponseAssertions"/>' published
/// signatures take <see cref="int"/> and cannot change.
/// <para>
/// Both overloads exist rather than one, so the generator can emit a bare number for a status .NET
/// does not name instead of pushing <c>(HttpStatusCode)599</c> into committed adopter code. An
/// <c>int</c> literal binds to the <c>int</c> overload and an enum member to this one, so the pair
/// is unambiguous at every call site.
/// </para>
/// </summary>
protected Task ExpectStatus(HttpStatusCode expectedStatus, HttpMethod method, string url) =>
    ExpectStatus((int)expectedStatus, method, url);

/// <inheritdoc cref="ExpectStatus(HttpStatusCode, HttpMethod, string)"/>
protected Task ExpectStatus(HttpStatusCode expectedStatus, HttpMethod method, string url, string body) =>
    ExpectStatus((int)expectedStatus, method, url, body);

/// <inheritdoc cref="ExpectStatus(HttpStatusCode, HttpMethod, string)"/>
protected Task ExpectContract(
    HttpStatusCode expectedStatus, string schemaKey, HttpMethod method, string url) =>
    ExpectContract((int)expectedStatus, schemaKey, method, url);

/// <inheritdoc cref="ExpectStatus(HttpStatusCode, HttpMethod, string)"/>
protected Task ExpectContract(
    HttpStatusCode expectedStatus, string schemaKey, HttpMethod method, string url, string body) =>
    ExpectContract((int)expectedStatus, schemaKey, method, url, body);

/// <inheritdoc cref="ExpectStatus(HttpStatusCode, HttpMethod, string)"/>
protected Task ExpectCapturedStatus(HttpStatusCode expectedStatus, TimeSpan elapsed) =>
    ExpectCapturedStatus((int)expectedStatus, elapsed);

/// <inheritdoc cref="ExpectStatus(HttpStatusCode, HttpMethod, string)"/>
protected Task ExpectCapturedContract(
    HttpStatusCode expectedStatus, string schemaKey, TimeSpan elapsed) =>
    ExpectCapturedContract((int)expectedStatus, schemaKey, elapsed);
```

Add `using System.Net;` to the top of `ApiTestCore.cs` — it is not in this project's implicit usings.

- [ ] **Step 4: Run to verify they pass**

```bash
dotnet test tests/InTest.Runtime.Tests
```

Expected: **PASS**, 267.

- [ ] **Step 5: Commit**

```bash
git add src/InTest.Runtime/ApiTestCore.cs tests/InTest.Runtime.Tests/ApiTestCoreExpectTests.cs
git commit -m "feat: HttpStatusCode overloads for the four Expect* methods

The int forms stay the implementations — ApiResponseAssertions' published signatures take
int and cannot change. Both overloads exist so the generator can emit a bare number for a
status .NET does not name rather than pushing (HttpStatusCode)599 into adopter code."
```

---

### Task 12: Emit the enum form from the generator

**Files:**
- Create: `src/InTest.Cli/Naming/HttpStatusExpression.cs`
- Modify: `src/InTest.Cli/Rendering/TemplateRenderer.cs`
- Modify: `src/InTest.Cli/Rendering/Templates/mstest-class.scriban`
- Test: `tests/InTest.Cli.Tests/TemplateRendererTests.cs`, `TemplateRendererClientTests.cs`

- [ ] **Step 1: Create the CLI-side table**

`src/InTest.Cli/Naming/HttpStatusExpression.cs`:

```csharp
using System.Globalization;
using System.Net;

namespace InTest.Cli.Naming;

/// <summary>
/// Renders a numeric status as the C# expression generated code should carry:
/// <c>HttpStatusCode.NotFound</c> for a status .NET names, the bare number for one it does not.
/// <para>
/// <b>The table below is duplicated in <c>InTest.Runtime</c>'s <c>HttpStatusNames.cs</c> and the two
/// must agree.</b> See that file for the full reasoning — why the duplication exists (this project
/// takes no reference to <c>InTest.Runtime</c>), and why the table is explicit rather than derived
/// from <c>ToString()</c> (307 would otherwise emit <c>RedirectKeepVerb</c>, and tie-breaking among
/// equal-valued members is not a documented contract). <c>InTest.Architecture.Tests</c>'
/// <c>HttpStatusNameCouplingTests</c> enforces agreement by reading both files as text.
/// </para>
/// </summary>
internal static class HttpStatusExpression
{
    private static readonly Dictionary<int, string> Preferred = new()
    {
        [300] = "MultipleChoices",
        [301] = "MovedPermanently",
        [302] = "Found",
        [303] = "SeeOther",
        [307] = "TemporaryRedirect",
        [422] = "UnprocessableEntity",
    };

    /// <summary>
    /// The name for <paramref name="status"/>, or null when .NET names no member for it. Kept
    /// separate from <see cref="For"/> so the coupling guard can compare names directly against the
    /// runtime's table without parsing an expression back apart.
    /// </summary>
    internal static string? Name(int status)
    {
        if (Preferred.TryGetValue(status, out var preferred))
        {
            return preferred;
        }

        return Enum.IsDefined(typeof(HttpStatusCode), status)
            ? ((HttpStatusCode)status).ToString()
            : null;
    }

    /// <summary>
    /// The expression to emit. Never a cast: an unnamed status renders as its bare number, which
    /// binds to the <c>int</c> overload of the generated call.
    /// </summary>
    internal static string For(int status)
    {
        var name = Name(status);
        return name is null
            ? status.ToString(CultureInfo.InvariantCulture)
            : "HttpStatusCode." + name;
    }
}
```

- [ ] **Step 2: Add the model field**

In `TemplateRenderer.cs`, wherever `expected_status` is set on the per-case model, add alongside it:

```csharp
expected_status_expression = HttpStatusExpression.For(c.ExpectedStatus),
```

**Leave `expected_status` in place** — other template sites and tests may still use it, and removing it is not this task's job.

The new field is **not** `CSharpLiteral.Escape` output and must **not** be suffixed `_literal`: it is a generated expression, emitted bare and unquoted. `TemplateEscapingGuardTests` classifies fields by quote parity and will hold it to that.

- [ ] **Step 3: Emit it**

In `mstest-class.scriban`, replace `{{ tc.expected_status }}` with `{{ tc.expected_status_expression }}` in **all four** call sites — the raw `ExpectContract`/`ExpectStatus` and the client `ExpectCapturedContract`/`ExpectCapturedStatus`.

Add `using System.Net;` to the generated class header, beside `using System.Diagnostics;`. Emit it unconditionally: every case carries a status, and essentially all are named, so a conditional would add template complexity for a case that barely occurs.

- [ ] **Step 4: Run and read the diff**

```bash
dotnet test tests/InTest.Cli.Tests
```

Expected: **failures** in `TemplateRendererTests` and `TemplateRendererClientTests` wherever a status literal is asserted. Update each to the enum form — `ExpectStatus(204,` becomes `ExpectStatus(HttpStatusCode.NoContent,`, `ExpectContract(200, "Order",` becomes `ExpectContract(HttpStatusCode.OK, "Order",`, and so on.

**Read each failure's Shouldly diff rather than assuming which form a test emits.** Task 6 of the previous plan was bitten by exactly this: an anchor copied from a plan was wrong because that test's fixture declared a schema key.

- [ ] **Step 5: Verify green**

```bash
dotnet test tests/InTest.Cli.Tests
dotnet build InTest.sln
```

Expected: **0 failed**, build clean.

- [ ] **Step 6: Commit**

```bash
git add src/InTest.Cli tests/InTest.Cli.Tests
git commit -m "feat: emit HttpStatusCode in generated tests

ExpectStatus(HttpStatusCode.NoContent, ...) rather than ExpectStatus(204, ...), symmetric
with the HttpMethod.Delete already beside it. A status .NET does not name emits as a bare
number, binding to the int overload — no cast ever reaches adopter code."
```

---

### Task 13: Enforce the duplication, then regenerate

**Files:**
- Create: `tests/InTest.Architecture.Tests/HttpStatusNameCouplingTests.cs`
- Modify: `tests/InTest.Golden.Tests/Expected/OrdersTests.g.cs.txt`

- [ ] **Step 1: Write the coupling guard**

It must read **both files as text**, the way `PackageVersionCouplingTests` reads its three sites — `InTest.Architecture.Tests` has no `InternalsVisibleTo` grant to either package, and this task must not add one.

```csharp
using System.Text.RegularExpressions;
using Shouldly;

namespace InTest.Architecture.Tests;

/// <summary>
/// The preferred-name table is duplicated in <c>InTest.Runtime/HttpStatusNames.cs</c> and
/// <c>InTest.Cli/Naming/HttpStatusExpression.cs</c> because <c>InTest.Cli</c> takes no reference to
/// <c>InTest.Runtime</c> — see either file for why that trade was made. This guard makes the
/// coupling mechanical, mirroring <c>PackageVersionCouplingTests</c>: it reads both sites as text
/// rather than through <c>InternalsVisibleTo</c>, so it needs no grant and cannot be defeated by one
/// side being refactored to compute its table differently.
/// </summary>
[TestClass]
public class HttpStatusNameCouplingTests
{
    private static readonly Regex Entry = new(@"\[(\d{3})\]\s*=\s*""([A-Za-z]+)""", RegexOptions.Compiled);

    [TestMethod]
    public void BothPreferredNameTablesAgree()
    {
        var runtime = ReadTable("src/InTest.Runtime/HttpStatusNames.cs");
        var cli = ReadTable("src/InTest.Cli/Naming/HttpStatusExpression.cs");

        runtime.ShouldNotBeEmpty("the runtime table was not found — has the file or its shape changed?");
        cli.Count.ShouldBe(runtime.Count,
        $"the two tables have different sizes. Runtime: {Describe(runtime)}. Cli: {Describe(cli)}.");

        foreach (var (status, name) in runtime)
        {
            cli.ShouldContainKey(status,
            $"InTest.Cli's table is missing {status}, which the runtime maps to \"{name}\".");
            cli[status].ShouldBe(name,
            $"the two tables disagree on {status}: runtime says \"{name}\", cli says \"{cli[status]}\".");
        }
    }

    /// <summary>
    /// 307 is the entry that made this table necessary — <c>ToString()</c> returns the legacy
    /// <c>RedirectKeepVerb</c> for it. Pinned by number so a well-meaning simplification back to
    /// <c>ToString()</c> fails here with the reason attached rather than silently changing generated
    /// output.
    /// </summary>
    [TestMethod]
    public void TemporaryRedirectIsPreferredOverRedirectKeepVerb()
    {
        ReadTable("src/InTest.Runtime/HttpStatusNames.cs")[307].ShouldBe("TemporaryRedirect");
        ReadTable("src/InTest.Cli/Naming/HttpStatusExpression.cs")[307].ShouldBe("TemporaryRedirect");
    }

    private static string Describe(Dictionary<int, string> table) =>
        string.Join(", ", table.OrderBy(e => e.Key).Select(e => $"{e.Key}={e.Value}"));

    private static Dictionary<int, string> ReadTable(string relativePath)
    {
        var full = Path.Combine(RepoRoot(), relativePath);
        File.Exists(full).ShouldBeTrue($"expected to find {relativePath} — has it moved?");

        return Entry.Matches(File.ReadAllText(full))
            .ToDictionary(m => int.Parse(m.Groups[1].Value), m => m.Groups[2].Value);
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "InTest.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        dir.ShouldNotBeNull("could not locate the repository root from the test assembly location.");
        return dir;
    }
}
```

If `InTest.Architecture.Tests` already has a `RepoRoot()` helper, use that instead of adding a second one, and say so in your report.

- [ ] **Step 2: Prove the guard works**

Change `[307] = "TemporaryRedirect"` to `[307] = "RedirectKeepVerb"` in **one** of the two files. Run:

```bash
dotnet test tests/InTest.Architecture.Tests
```

Expected: **both tests fail**, naming 307 and both values. Revert, confirm green. **Report the failure text** — a coupling guard that cannot fail is the defect it exists to prevent.

- [ ] **Step 3: Regenerate the golden file**

```bash
INTEST_UPDATE_GOLDEN=1 dotnet test tests/InTest.Golden.Tests --filter "FullyQualifiedName~OutputMatchesTheGoldenFile"
```

Expected: **Inconclusive** — it writes the source copy and refuses to claim success. Then verify:

```bash
dotnet test tests/InTest.Golden.Tests --filter "FullyQualifiedName~OutputMatchesTheGoldenFile"
```

Expected: **PASS**.

- [ ] **Step 4: Read the regenerated diff**

```bash
git diff tests/InTest.Golden.Tests/Expected/OrdersTests.g.cs.txt
```

Confirm: `HttpStatusCode.OK` / `NoContent` / `Forbidden` / `NotFound` / `Unauthorized` in place of bare numbers, `using System.Net;` present, and **role gating unchanged** — `FixtureParameter(...)` on Success cases, `Guid.NewGuid().ToString()` on the 401/403/404 siblings. Quote a Success case and one error sibling in your report.

- [ ] **Step 5: Commit**

```bash
git add tests/InTest.Architecture.Tests/HttpStatusNameCouplingTests.cs tests/InTest.Golden.Tests/Expected/OrdersTests.g.cs.txt
git commit -m "test: enforce the status-name duplication, regenerate the golden file

The preferred-name table lives in both InTest.Runtime and InTest.Cli because the CLI takes
no reference to the runtime. The guard reads both as text, mirroring
PackageVersionCouplingTests, so it needs no InternalsVisibleTo grant. 307 is pinned by
number: it is the entry ToString() gets wrong, so a simplification back to ToString() fails
here with the reason attached."
```

---

## Verification

The orchestrator runs the full solution — **do not run the Golden suite from a subagent.** It takes
minutes and two agents have already stalled backgrounding it. Fast suites and `dotnet build` are
enough per task; Task 13 needs Golden only for the two filtered `OutputMatchesTheGoldenFile` runs,
which are short.

Expected final state: Architecture 14, Runtime 267, Cli 627, Golden 50, all passing.

## Out of scope

- **`ApiResponseAssertions`' `int` parameters.** Published in `0.1.0-preview.1`; changing them is a
  breaking change for existing adopters. The conversion belongs in the unpublished `Expect*` layer.
- **`examples/*/Generated/**`.** Still governed by the release-checklist gate added in Task 9.
- **`TestCasePlan.ExpectedStatus`.** Stays `int`. It is the planner's own currency, is compared and
  grouped numerically, and gains nothing from an enum it would only cast back.

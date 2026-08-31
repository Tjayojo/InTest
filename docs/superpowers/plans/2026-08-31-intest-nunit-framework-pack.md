# NUnit Framework Pack Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `intest init --framework nunit` scaffolds an NUnit project whose generated tests compile and pass against a live stub, shipping `InTest.Runtime.NUnit` as a fifth package.

**Architecture:** The third framework, following the path xUnit proved. `src/InTest.Runtime` does not change — that boundary has now held under four independent builds and one real implementation. What is added is a third Scriban template, a third adapter package, one more arm on `TemplateRenderer`'s and `ConfigLoader`'s existing switches, and a per-framework scaffold branch that already exists and gains a case.

**Tech Stack:** .NET 10 · NUnit 4.6.1 · NUnit3TestAdapter 6.3.0 · MSTest 4.3.3 and xunit.v3 4.0.0 (both unchanged) · Scriban 7.2.6 · Shouldly 4.3.0

**Base:** `main` at `fc90fc1`, which carries the merged xUnit pack.

---

## This plan carries its own design, because the unknowns were measured rather than reasoned

The xUnit pack needed a four-revision design document because its critical facts were unknown. NUnit's were established by three parallel probes that **built and ran** everything below. Each finding names how it was established; nothing here is inferred from documentation.

### `[error-is-the-sink]` — the single most important finding, and it inverts xUnit's answer

`IRunDiagnostics.Warn` must *"reach the operator even when the run passes and exits 0"*. Measured against NUnit 4.6.1 on a default, passing, flagless run, capturing process output exactly as `ProcessRunner` does:

| candidate | test scope | assembly scope (`SetUpFixture`) |
|---|---|---|
| **`TestContext.Error.WriteLine`** | **appears** | **appears** |
| `TestContext.Progress.WriteLine` | flag-gated | flag-gated |
| `TestContext.WriteLine` / `TestContext.Out` | flag-gated | **silent** |
| `Console.WriteLine` | flag-gated | **silent** |

**`Console.WriteLine` is xUnit's answer and it is silent at assembly scope under NUnit, at every verbosity, throwing nothing.** An adapter written by copying `InTest.Runtime.xUnit/TestHost.cs` and swapping type names would lose every warning permanently, with no symptom. This is the one place where mirroring the xUnit adapter is actively wrong.

Use `TestContext.Error.WriteLine` for **both** `Note` and `Warn`. Reproduced on two runs and under both `NUnit3TestAdapter` 4.6.0 and 6.3.0.

### `[nunit-is-vstest]` — no harness port

NUnit goes through classic VSTest, like MSTest. Measured: `dotnet test <csproj>` works and exits 0; `--logger "trx;LogFileName=…"` produces a trx. The banner is VSTest's (`Test run for X.dll`), structurally unlike xunit.v3's Microsoft.Testing.Platform output.

So NUnit **joins MSTest's existing arm** in `GeneratedSuiteCommand.For` — no new invocation shape, no direct-exe path, no `-filterVSTest` translation. This is the largest single saving versus the xUnit pack.

*(Unchanged and still true: a solution containing an xunit.v3 project still fails `dotnet test <sln>`. Measured with all three frameworks present — MSTest and NUnit both pass inside that run; only the xUnit project errors on the VSTest target.)*

### `[nunit-is-sequential]` — nothing to pin off

NUnit's default is **sequential**. Measured three ways: default run showed no overlap (5.64s for two 1.5s classes); adding `[assembly: Parallelizable(ParallelScope.Fixtures)]` produced real overlap on distinct threads (2.68s); `[assembly: LevelOfParallelism(1)]` forced it back (4.15s).

This inverts xUnit, which parallelises by default and required an opt-out to stop a scaffolded suite hammering a *deployed* API. **The NUnit scaffold needs no parallelism attribute to be correct.** Emit `[assembly: LevelOfParallelism(1)]` anyway — it is the explicit, provable analogue of MSTest's `DoNotParallelize`, and a scaffold that states its intent survives someone later adding `[Parallelizable]` to a class.

### `[one-package]` — no library/executable split

`NUnit` 4.6.1 alone compiles a **class library** calling `Assert.Ignore`, `TestContext.CurrentContext.Test.Name` and `TestContext.Error.WriteLine`. There is no equivalent of xUnit's *"v3 test projects must be executable"* refusal, and no `extensibility.core` + `assert` split. The package is `nunit.framework.dll` + `nunit.framework.legacy.dll` with no runner dependency.

A generated adopter project needs `NUnit` + `NUnit3TestAdapter` + `Microsoft.NET.Test.Sdk`.

**Dependency policy, checked 2026-08-31:** `NUnit` 4.6.1 and `NUnit3TestAdapter` 6.3.0 are both MIT, listed, no deprecation notice, no vulnerability advisories. (`NUnit` 5.0.0-beta.1 exists and is excluded — the policy forbids prerelease.)

**`NUnit` and `NUnit3TestAdapter` do not version together** — framework on 4.x, adapter on 6.x. `PackageVersionCouplingTests` assumes the MSTest trio moves in lockstep; the NUnit entries must be checked against their own pins, not against each other.

### The three seams, all verified by running

| seam | NUnit equivalent | how established |
|---|---|---|
| cancellation | `TestContext.CurrentContext.CancellationToken` | compiled; proven live via `[CancelAfter(200)]` cancelling an awaited `Task.Delay(30s, token)` at 249ms |
| display name | `TestContext.CurrentContext.Test.Name` | `[TestCase(1)]`/`[TestCase(2)]` printed `…ForEachRow(1)` / `(2)` — varies per row, no MSTest-style collapse |
| skip | `Assert.Ignore(reason)` | trx `outcome="NotExecuted"`, reason in **both** `<StdOut>` and `<ErrorInfo><Message>` — a superset of xUnit, which uses `<StdOut>` only |

**One nuance to record, not to fix:** the token exists but is only *driven* by a mechanism such as `[CancelAfter]`. Absent one it is never cancelled — exactly like MSTest's, which `[Timeout]` drives. Generated code passes it safely; it is simply inert unless something cancels it. Do not "fix" this.

### `[lifecycle-is-a-setupfixture]`

| | MSTest | xUnit v3 | NUnit |
|---|---|---|---|
| assembly setup | `[AssemblyInitialize]` | `[assembly: AssemblyFixture(typeof(T))]` | `[SetUpFixture]` + `[OneTimeSetUp]` |
| assembly teardown | `[AssemblyCleanup]` | `IAsyncDisposable.DisposeAsync` | `[OneTimeTearDown]` |
| per-test setup | `[TestInitialize]` | `IAsyncLifetime.InitializeAsync` | `[SetUp]` |
| per-test teardown | `[TestCleanup]` | `IAsyncDisposable` | `[TearDown]` |
| class marker | `[TestClass]` | *(none)* | `[TestFixture]` |
| test marker | `[TestMethod]` | `[Fact]` | `[Test]` |
| category | `[TestCategory("x")]` | `[Trait("Category","x")]` | `[Category("x")]` |
| description | `[Description("x")]` | `[Trait("Description","x")]` | `[Description("x")]` |
| non-parallel | `[DoNotParallelize]` | `[Fact(DisableParallelization = true)]` | `[NonParallelizable]` |

`[SetUpFixture]` with **async** `Task`-returning `[OneTimeSetUp]`/`[OneTimeTearDown]` is verified working, including teardown after a *failing* test and with an ignored test present.

**`[Description]` maps directly** — unlike xUnit, where `DisplayName` *is* the display name and would have diverged the `TestId` that travels in an HTTP header. NUnit's `[Description]` is orthogonal metadata like MSTest's, so the correlation id stays aligned across all three frameworks with no decision to make.

---

## File Structure

**Created:**

| File | Responsibility |
|---|---|
| `src/InTest.Runtime.NUnit/InTest.Runtime.NUnit.csproj` | The fifth shipped package |
| `src/InTest.Runtime.NUnit/TestHost.cs` | Facade over `InTestRun`, plus `NUnitDiagnostics : IRunDiagnostics` |
| `src/InTest.Runtime.NUnit/ApiTestBase.cs` | `[SetUp]`/`[TearDown]` → `BeginTest`/`EndTest`; skip → `Assert.Ignore` |
| `src/InTest.Runtime.NUnit/README.md` | Packed — the csproj mirrors `<PackageReadmeFile>` |
| `src/InTest.Cli/Rendering/Templates/nunit-class.scriban` | The third template |
| `tests/InTest.Runtime.NUnit.Tests/` | Sixth suite — same `CS0433` reason as the xUnit one |
| `tests/InTest.Golden.Tests/Expected/OrdersTests.nunit.g.cs.txt` | Third golden file |
| `tests/InTest.Golden.Tests/Expected/MutatingOperationTests.nunit.g.cs.txt` | `[NonParallelizable]` coverage |

**Modified:** `Directory.Packages.props`, `InTest.sln`, `ConfigLoader.cs`, `TemplateRenderer.cs`, `InitCommand.cs`, `Program.cs`, `GeneratedSuiteCommand.cs`, the escaping guard, `GoldenFileTests.cs`, the three Golden matrix files, `pack-and-verify.ps1`, `local-e2e-test.ps1`, `release.yml`, `pack.yml`, `NeutralityTests.cs`, `PackageVersionCouplingTests.cs`, `README.md`, `CLAUDE.md`, `CONTRIBUTING.md`, `docs/getting-started.md`, the 2026-08-16 spec §5.

---

### Task 1: The `InTest.Runtime.NUnit` adapter package

**Files:** create the four files above; modify `Directory.Packages.props`, `InTest.sln`.

- [ ] **Step 1: Add the package versions**

```xml
<PackageVersion Include="NUnit" Version="4.6.1" />
<PackageVersion Include="NUnit3TestAdapter" Version="6.3.0" />
```

Both MIT, listed, no deprecation, no advisories (checked 2026-08-31). Note they version independently — see `[one-package]`.

- [ ] **Step 2: Create the project**

Mirror `src/InTest.Runtime.xUnit/InTest.Runtime.xUnit.csproj` exactly — packaging metadata, MinVer, `ProjectReference` to `InTest.Runtime`, icon and README pack items — changing only:

```xml
<PackageReference Include="NUnit" />
```

**One package, not two.** Unlike xUnit, `NUnit` alone compiles a class library.

```xml
<InternalsVisibleTo Include="InTest.Runtime.NUnit.Tests" />
```

Create `src/InTest.Runtime.NUnit/README.md` mirroring the xUnit one with NUnit wording — **`dotnet pack` fails without it.** Add the project to `InTest.sln`.

- [ ] **Step 3: Write `TestHost.cs`**

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace InTest.Runtime;

/// <summary>
/// The NUnit counterpart of <c>InTest.Runtime.MSTest</c>'s and <c>InTest.Runtime.xUnit</c>'s
/// <c>TestHost</c>: a facade over <see cref="InTestRun"/>, the assembly-scope composition root.
/// <para>
/// Same name, same namespace, same passthroughs as the other two adapters — an adopter migrating
/// between frameworks changes a <c>PackageReference</c> id and their <c>ConfigureServices</c>
/// registration keeps compiling untouched.
/// </para>
/// <para>
/// Like the xUnit adapter and unlike the MSTest one, <c>InitializeAsync</c> takes no context
/// parameter: NUnit's <c>[SetUpFixture]</c> is itself the lifecycle hook and
/// <c>TestContext.CurrentContext</c> is ambient. The profile argument is a literal
/// <see langword="null"/> — NUnit has no run-settings equivalent, so <c>INTEST_PROFILE</c> and the
/// config default are what remain of <c>InTestRun.ResolveProfile</c>'s precedence chain.
/// </para>
/// </summary>
public static class TestHost
{
    public static IConfiguration Configuration => InTestRun.Configuration;

    public static IServiceProvider Root => InTestRun.Root;

    public static SchemaBundle Schemas => InTestRun.Schemas;

    public static string RunIdValue => InTestRun.RunIdValue;

    public static string Profile => InTestRun.Profile;

    public static FixtureStore Fixtures => InTestRun.Fixtures;

    public static FixtureValidation.Report FixtureValidationReport => InTestRun.FixtureValidationReport;

    public static TokenResolver FixtureTokens => InTestRun.FixtureTokens;

    public static Action<IServiceCollection, IConfiguration>? ConfigureServices
    {
        get => InTestRun.ConfigureServices;
        set => InTestRun.ConfigureServices = value;
    }

    public static Task InitializeAsync(CancellationToken cancellationToken = default) =>
        InTestRun.InitializeAsync(profileFromRunSettings: null, new NUnitDiagnostics(), cancellationToken);

    public static Task CleanupAsync() => InTestRun.CleanupAsync(new NUnitDiagnostics());

    /// <summary>
    /// [error-is-the-sink]: <see cref="IRunDiagnostics.Warn"/> must reach the operator even when the
    /// run passes and exits 0, and under NUnit exactly one sink does that.
    /// <para>
    /// <b>This is the one place where copying the xUnit adapter is actively wrong.</b> Measured
    /// against NUnit 4.6.1 on a default, passing, flagless run: <c>Console.WriteLine</c> — which is
    /// the xUnit adapter's answer — is <b>silent at assembly scope at every verbosity, and throws
    /// nothing</b>. So is <c>TestContext.WriteLine</c> and <c>TestContext.Out</c>.
    /// <c>TestContext.Progress</c> appears only at raised verbosity, the same flag-gated failure
    /// xUnit's <c>SendDiagnosticMessage</c> has. Only <c>TestContext.Error</c> reaches captured
    /// process output unconditionally, at both test scope and <c>[SetUpFixture]</c> assembly scope.
    /// </para>
    /// <para>
    /// Both <c>Note</c> and <c>Warn</c> therefore use it. A future editor "tidying" <c>Note</c> to
    /// <c>TestContext.Out</c> would silently lose it at the scope <c>InTestRun.InitializeAsync</c>
    /// uses — which is why this comment is this long.
    /// </para>
    /// </summary>
    internal sealed class NUnitDiagnostics : IRunDiagnostics
    {
        public void Note(string message) => TestContext.Error.WriteLine(message);

        public void Warn(string message) => TestContext.Error.WriteLine(message);
    }
}
```

- [ ] **Step 4: Write `ApiTestBase.cs`**

```csharp
using NUnit.Framework;

namespace InTest.Runtime;

/// <summary>
/// NUnit adapter over <see cref="ApiTestCore"/>, mirroring the MSTest and xUnit adapters. Generated
/// classes derive from a project base class deriving from this; everything they call —
/// <c>UseIdentity</c>, <c>RequireFixture</c>, <c>FixtureBody</c>, <c>Client</c>, <c>TestId</c>,
/// <c>Schemas</c> — lives on the neutral base and needs no adapting.
/// <para>
/// Lifecycle is <c>[SetUp]</c>/<c>[TearDown]</c>, NUnit's per-test hooks. The display name comes
/// from <c>TestContext.CurrentContext.Test.Name</c>, which — unlike MSTest's <c>TestName</c> —
/// already distinguishes data-row variations (verified: two <c>[TestCase]</c> rows reported
/// <c>…ForEachRow(1)</c> and <c>(2)</c>), so the correlation id stays distinct per row.
/// </para>
/// </summary>
[TestFixture]
public abstract class ApiTestBase : ApiTestCore
{
    [SetUp]
    public void ApiTestSetUp() =>
        BeginTest(TestContext.CurrentContext.Test.Name, new TestHost.NUnitDiagnostics());

    [TearDown]
    public void ApiTestTearDown() => EndTest();

    /// <summary>
    /// [skip-is-a-reason]: the neutral layer returns a reason string, null meaning "run". MSTest's
    /// adapter turns that into <c>Assert.Inconclusive</c>, xUnit's into <c>Assert.Skip</c>, and
    /// NUnit's into <c>Assert.Ignore</c> — verified to produce trx <c>outcome="NotExecuted"</c>,
    /// the same outcome as the other two, with the reason in both <c>&lt;StdOut&gt;</c> and
    /// <c>&lt;ErrorInfo&gt;&lt;Message&gt;</c>.
    /// </summary>
    protected internal static void RequireMultipleIdentities()
    {
        if (MultipleIdentitiesSkipReason() is { } reason)
        {
            Assert.Ignore(reason);
        }
    }

    /// <inheritdoc cref="RequireMultipleIdentities"/>
    protected internal static void RequireSecondaryIdentityLacks(params string[] requiredScopes)
    {
        if (SecondaryIdentityScopeSkipReason(requiredScopes) is { } reason)
        {
            Assert.Ignore(reason);
        }
    }
}
```

- [ ] **Step 5: Build and confirm the boundary held**

```bash
dotnet build src/InTest.Runtime.NUnit
dotnet build InTest.sln
git status --short src/InTest.Runtime/
```

Expected: 0 warnings, 0 errors, and `src/InTest.Runtime/` **empty**. If the neutral package had to change, **stop and report** — that boundary has held under five builds now.

- [ ] **Step 6: Commit**

```bash
git add Directory.Packages.props InTest.sln src/InTest.Runtime.NUnit
git commit -m "feat: add the InTest.Runtime.NUnit adapter package

Mirrors the MSTest and xUnit adapters: same namespace, same type names, depending on
InTest.Runtime at the same version. One package, not two — unlike xunit.v3, NUnit compiles
from a class library with no extensibility split.

Note and Warn both use TestContext.Error.WriteLine. Console.WriteLine is the xUnit adapter's
answer and is silent at NUnit's assembly scope at every verbosity, throwing nothing — copying
that adapter here would have lost every warning with no symptom."
```

---

### Task 2: The sixth test suite

Same reason as the xUnit one: all three adapters declare the same types in the same namespace, so no two can share a compilation (`CS0433`).

**Files:** create `tests/InTest.Runtime.NUnit.Tests/`; modify `InTest.sln`, `.github/workflows/build-and-test.yml`, `CLAUDE.md` (Commands section only).

- [ ] **Step 1: Create the project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/InTest.Runtime.NUnit/InTest.Runtime.NUnit.csproj" />
    <PackageReference Include="NUnit" />
    <PackageReference Include="NUnit3TestAdapter" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
  </ItemGroup>
</Project>
```

**No `<OutputType>Exe</OutputType>`** — that was an xunit.v3 requirement. NUnit runs under classic VSTest, so this is an ordinary test project.

- [ ] **Step 2: Write the failing test**

```csharp
using InTest.Runtime;
using NUnit.Framework;

namespace InTest.Runtime.NUnit.Tests;

/// <summary>
/// [error-is-the-sink]: Warn must reach the operator on a passing run. Every sink that fails under
/// NUnit fails *silently* — nothing throws — so a test that merely called Warn and asserted nothing
/// about output would pass against every wrong implementation.
/// </summary>
[TestFixture]
public class NUnitDiagnosticsTests
{
    private const string Marker = "WARN_MARKER";

    // The leaf. Only calls Warn, always passes. It exists solely as a filterable subprocess
    // target whose passing-run console output is what the real test asserts on.
    [Test]
    public void EmitsWarnMarker()
    {
        new TestHost.NUnitDiagnostics().Warn(Marker);
        Assert.Pass();
    }

    [Test]
    public async Task WarnWritesToTheErrorSinkWhichSurvivesAPassingRun()
    {
        var (exitCode, output) = await RunFilteredSubprocessAsync(nameof(EmitsWarnMarker));

        Assert.That(exitCode, Is.EqualTo(0), $"subprocess run should have passed; output was:
{output}");
        Assert.That(output, Does.Contain(Marker));
    }
}
```

**Do not write the in-process `Console.SetError` capture rev 1 prescribed here — it does not work, measured.**
`TestContext.Error` is NUnit's own per-test capture buffer, **not** a wrapper over `Console.Error`, so
`Console.SetError` around the call sees an empty string while the marker still reaches the real console.
An implementer who trusts rev 1 will conclude the *sink* is wrong and "fix" `TestHost` — breaking the one
thing `[error-is-the-sink]` established. The subprocess shape above is the only one that works, and it is
how the design probe established the finding in the first place.

Two details that are not optional:

- The **leaf test must be separate and always-passing**, and the subprocess `--filter` must select it by
  name (`FullyQualifiedName~EmitsWarnMarker`). The outer test's name does not contain the leaf's, so the
  subprocess cannot re-enter itself. One combined test would recurse.
- `RunFilteredSubprocessAsync` **must set `startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1"`** before
  `Process.Start`, and must start both `ReadToEndAsync` calls *before* awaiting `WaitForExitAsync`. Redirecting
  both pipes around `dotnet test` without that env var is the exact shape that cost this repo a measured 40m51s
  test run — orphaned MSBuild worker nodes inherit the redirected handles and EOF never arrives.
  `tests/InTest.Golden.Tests/ProcessRunner.cs` carries the canonical explanation; point at it rather than
  restating it. Do not call `ProcessRunner` itself — different assembly, and its own doc comment argues
  against sharing it for a single call site.

- [ ] **Step 3: Run it, then prove it discriminates**

```bash
dotnet build tests/InTest.Runtime.NUnit.Tests
dotnet test tests/InTest.Runtime.NUnit.Tests
```

Then change `Warn` to `TestContext.Out.WriteLine` — the plausible wrong answer — and confirm the test **fails**. Restore. Report both directions.

- [ ] **Step 4: Add to CI's `fast` job**

`.github/workflows/build-and-test.yml`. **This one is a plain `dotnet test <csproj>` step**, unlike the xUnit suite's `dotnet <dll>` — NUnit is VSTest-based. Update `CLAUDE.md`'s Commands section to name six suites.

- [ ] **Step 5: Commit**

```bash
git add tests/InTest.Runtime.NUnit.Tests InTest.sln .github/workflows/build-and-test.yml CLAUDE.md
git commit -m "test: add the NUnit adapter's own suite

Third adapter, same CS0433 namespace collision, so its internals need their own project.
Runs as a plain dotnet test — NUnit is VSTest-based, unlike xunit.v3."
```

---

### Task 3: Config, CLI flag, and scaffold

Every switch this touches already has two arms. This adds a third to each.

**Files:** `ConfigLoader.cs`, `Program.cs`, `InitCommand.cs`, `GenerateCommand.cs`, `InitCommandTests.cs`, `ConfigLoaderTests.cs`, `GenerateCommandTests.cs`.

- [ ] **Step 1: Write the failing tests**

```csharp
[TestMethod]
public void AcceptsNunitAsAFrameworkValue()
{
    WriteConfig("\"framework\": \"nunit\"");

    ConfigLoader.Load(_root).Framework.ShouldBe("nunit");
}
```

```csharp
[TestMethod]
public void ScaffoldsAnNunitProjectWhenAskedFor()
{
    InitCommand.Run(_root, "Orders.ApiTests", "orders.json", framework: "nunit").ShouldBe(0);

    var csproj = File.ReadAllText(Path.Combine(_root, "Orders.ApiTests.csproj"));
    csproj.ShouldContain("NUnit");
    csproj.ShouldContain("InTest.Runtime.NUnit");
    csproj.ShouldNotContain("MSTest.TestFramework");
    csproj.ShouldNotContain("xunit.v3");
    csproj.ShouldNotContain("<OutputType>Exe</OutputType>");

    File.ReadAllText(Path.Combine(_root, "intest.json")).ShouldContain("\"framework\": \"nunit\"");
}

/// <summary>
/// [nunit-is-sequential]: NUnit's default is already sequential — measured, unlike xUnit v3 which
/// parallelises by default. The attribute is emitted anyway so the scaffold states its intent and
/// survives someone later adding [Parallelizable] to a class.
/// </summary>
[TestMethod]
public void ScaffoldsTheNunitParallelismOptOut()
{
    InitCommand.Run(_root, "Orders.ApiTests", "orders.json", framework: "nunit");

    File.ReadAllText(Path.Combine(_root, "AssemblyInfo.cs"))
        .ShouldContain("[assembly: NUnit.Framework.LevelOfParallelism(1)]");
}
```

**A test asserting the exemplar still refuses an unsupported value must keep working.** `ConfigLoaderTests` uses `"nunit"` as its unsupported-framework exemplar today — Task 4 of the xUnit plan repointed it there from `"xunit"`. **Accepting `"nunit"` breaks it again.** Repoint it to a value that stays unsupported (`"junit"` or similar) and update its doc comment. This is the second time this test has moved; consider whether it should assert on a deliberately-nonsense value instead.

- [ ] **Step 2: Run to verify they fail, then implement**

`ConfigLoader.SupportedFrameworks` gains `"nunit"`. `FrameworkRule`'s text names all three and **keeps the word "yet"** if any framework remains unsupported — if none does, rewrite the sentence rather than leaving a dangling "yet".

`Program.cs`'s `--framework` option description and `InitCommand.Run`'s guard both gain `nunit`.

- [ ] **Step 3: Branch the scaffold**

`InitCommand` already branches on `isXunit`. **Replace the boolean with a framework switch** rather than adding `isNunit` beside it — two booleans for three frameworks is the shape that produces a fourth bug. Five files differ:

| file | NUnit form |
|---|---|
| `.csproj` | `NUnit` + `NUnit3TestAdapter` + `InTest.Runtime.NUnit`; keep `Microsoft.NET.Test.Sdk`/`Shouldly`; **no `OutputType`** |
| `AssemblyInfo.cs` | `[assembly: NUnit.Framework.LevelOfParallelism(1)]` |
| assembly setup | a `[SetUpFixture]` class with async `[OneTimeSetUp]`/`[OneTimeTearDown]` calling `TestHost.InitializeAsync()`/`CleanupAsync()`, **carrying `TestHost.ConfigureServices = Register;` and the `Register` method** exactly as the MSTest and xUnit scaffolds do |
| `intest.json` | `"framework": "nunit"` |
| `*.runsettings` | **not written** — no NUnit equivalent, same as xUnit |

- [ ] **Step 4: Extend the frozen-axis detection**

`GenerateCommand.DetectFrameworkMismatch` maps adapter `PackageReference` ids to framework names. Add `InTest.Runtime.NUnit` → `nunit`. Confirm its exit code still matches §5 — the xUnit pack settled on **2** after reading §5's table, and this must not diverge.

- [ ] **Step 5: Verify and commit**

```bash
dotnet test tests/InTest.Cli.Tests
dotnet build InTest.sln
```

```bash
git add src/InTest.Cli tests/InTest.Cli.Tests
git commit -m "feat: accept nunit, add it to init --framework, and scaffold it

Every switch this touches already had two arms; this adds a third. InitCommand's isXunit
boolean becomes a framework switch — two booleans for three frameworks is the shape that
produces a fourth bug.

NUnit's parallelism default is already sequential, unlike xUnit's, so the emitted
LevelOfParallelism(1) states intent rather than fixing a hazard."
```

---

### Task 4: The template and its selection

**Files:** create `nunit-class.scriban`; modify `TemplateRenderer.cs`, `TemplateRendererTests.cs`, `TemplateEscapingGuardTests.cs`.

- [ ] **Step 1: Write the failing test**

```csharp
[TestMethod]
public void RendersTheNunitShapeWhenTheFrameworkIsNunit()
{
    var rendered = new TemplateRenderer(framework: "nunit")
        .RenderClass(Plan(), "Orders.ApiTests", "Orders.ApiTests.OrdersTestBase");

    rendered.ShouldContain("using NUnit.Framework;");
    rendered.ShouldContain("[TestFixture]");
    rendered.ShouldContain("[Test]");
    rendered.ShouldContain("[Category(\"Contract\")]");
    rendered.ShouldContain("TestContext.CurrentContext.CancellationToken");

    rendered.ShouldNotContain("[TestClass]");
    rendered.ShouldNotContain("[Fact]");
    rendered.ShouldNotContain("Microsoft.VisualStudio.TestTools.UnitTesting");
}
```

- [ ] **Step 2: Create the template**

Copy `mstest-class.scriban` and apply exactly these substitutions. Everything else — `path_argument_list`, `query_expression`, the `has_body` block, the client branch's pinned `try`/filters/stopwatch, all `*_literal` quoting — stays byte-identical:

| mstest-class.scriban | nunit-class.scriban |
|---|---|
| `using Microsoft.VisualStudio.TestTools.UnitTesting;` | `using NUnit.Framework;` |
| `[TestClass]` | `[TestFixture]` |
| `[TestMethod, TestCategory("{{ tc.category }}")]` | `[Test]`<br>`[Category("{{ tc.category }}")]` |
| `[Description("{{ tc.display_name_literal }}")]` | **unchanged** — NUnit has `[Description]` and it is orthogonal metadata, exactly like MSTest's |
| `{{~ if tc.mutates ~}}[DoNotParallelize]{{~ end ~}}` | `{{~ if tc.mutates ~}}[NonParallelizable]{{~ end ~}}` |
| `TestContext.CancellationToken` (5 sites) | `TestContext.CurrentContext.CancellationToken` |

**`[Description]` needing no change is the one place NUnit is simpler than xUnit.** xUnit's `DisplayName` *is* the display name and flows into `InTestId` and out over an HTTP header, which forced a decision; NUnit's does not.

- [ ] **Step 3: Add the third arm to selection**

`TemplateRenderer`'s constructor switch gains `"nunit"`. **So does `_cancellationTokenExpression`** — the field added when a hardcoded `"TestContext.CancellationToken"` in `BuildClientCallExpression` was found to break every generated xUnit client-routed case with `CS0120`. NUnit's value is `TestContext.CurrentContext.CancellationToken`. **Missing this is the same bug a third time.**

- [ ] **Step 4: Extend the escaping guard**

`TemplateEscapingGuardTests` runs over template names via `[DataRow]`. Add `nunit-class.scriban`. Use `[TestMethod]` + `[DataRow]` — `[DataTestMethod]` is analyzer-obsolete (MSTEST0044) and this repo builds with `TreatWarningsAsErrors`.

**Prove it reads the new file:** temporarily quote a bare field in `nunit-class.scriban`, confirm the guard fails naming that field and that row, revert. A guard that does not read the template enforces nothing, and nothing announces it.

- [ ] **Step 5: Verify and commit**

```bash
dotnet test tests/InTest.Cli.Tests
dotnet build InTest.sln
```

```bash
git add src/InTest.Cli tests/InTest.Cli.Tests
git commit -m "feat: add the NUnit template and select by project.framework

Third template, third arm on the constructor switch — including
_cancellationTokenExpression, whose absence for xUnit broke every generated client-routed
case with CS0120 and was caught only by compiling one."
```

---

### Task 5: Golden coverage

**Files:** `GoldenFileTests.cs`, two new `Expected/*.nunit.g.cs.txt` files.

- [ ] **Step 1: Add the NUnit rows**

`GoldenFileTests.OutputMatchesTheGoldenFile` is `[DataRow]`-driven over spec × framework. Add `orders.json`/nunit and `mutating-operation.json`/nunit.

The mutating spec already exists — Task 7 of the xUnit plan created it precisely because no Orders case sets `mutates`, so `[DoNotParallelize]`/`[Fact(DisableParallelization = true)]` would otherwise never appear in checked-in output. **`[NonParallelizable]` needs the same coverage.**

- [ ] **Step 2: Regenerate**

```bash
INTEST_UPDATE_GOLDEN=1 dotnet test tests/InTest.Golden.Tests --filter "FullyQualifiedName~OutputMatchesTheGoldenFile"
```

Expected: **Inconclusive** — it writes the source copies and refuses to claim success. Re-run without the variable: **PASS**.

- [ ] **Step 3: Read the diff**

Confirm in the new files: `[TestFixture]`/`[Test]`/`[Category]`/`[Description]`, no `[TestClass]`, no `[Fact]`, `TestContext.CurrentContext.CancellationToken` at every site, `[NonParallelizable]` in the mutating file — and **role gating identical to the other two golden files**: `FixtureParameter(...)` on Success cases, `Guid.NewGuid().ToString()` on the 401/403/404 siblings for the same path.

Role gating is a planner decision the template only interpolates. **If the three files disagree there, the NUnit template re-derived something it should have passed through — stop and report.**

- [ ] **Step 4: Commit**

```bash
git add tests/InTest.Golden.Tests
git commit -m "test: golden coverage for the NUnit template

Both specs under all three frameworks. Role gating verified identical across all three —
FixtureParameter on Success cases, unmatchable values on their error siblings."
```

---

### Task 6: The Golden matrix

**Files:** `GeneratedSuiteCommand.cs`, `CompileVerificationTests.cs`, `GeneratedSuiteExecutionTests.cs`, `ScaffoldCompileVerificationTests.cs`.

- [ ] **Step 1: Add NUnit to the runner selector**

`GeneratedSuiteCommand.For` switches on framework. **NUnit joins the `"mstest"` arm** — measured: it runs under classic VSTest, `dotnet test <csproj>` exits 0, and `--logger "trx;LogFileName=…"` produces a trx. No direct-exe path, no `-filterVSTest`.

The cleanest expression is `"mstest" or "nunit" =>` on the existing arm rather than a duplicate branch. Add a test asserting both produce the same shape.

- [ ] **Step 2: Parameterise `CreateProject` for NUnit**

`CompileVerificationTests.CreateProject(specFileName, framework)` already varies three things — the `.csproj`, the `AssemblyInfo.cs` string, and `intest.json`'s framework value. Add the NUnit forms.

- [ ] **Step 3: Add the matrix cases**

Per `[matrix-stays-representative]`, only shapes whose rendering or runtime behaviour differs. The framework-independent tail stays as it is.

| case | why |
|---|---|
| a raw contract case | base shape: attributes, lifecycle, `TestContext.CurrentContext` |
| **a client-routed case** | the distinct body carrying 2 of the 5 token sites — this is what caught the `CS0120` bug for xUnit |
| an auth case using the skip path | `Assert.Ignore` versus the other two, and the trx outcome |
| **the `Warn` contract test** | the only proof the sink reaches the operator on a passing run — and NUnit's sink is the one that differs most |
| the scaffold compile | the only test that compiles raw scaffold output |

- [ ] **Step 4: Verify**

```bash
dotnet test tests/InTest.Golden.Tests
```

This is the slow suite — read the timing figure from `CLAUDE.md` (currently ~4m13s) and pass a generous explicit timeout. **Run it in the foreground.** Report the new wall-clock time; this task grows it and that figure has gone stale five times.

- [ ] **Step 5: Commit**

```bash
git add tests/InTest.Golden.Tests
git commit -m "test: NUnit cases in the Golden matrix

NUnit joins the mstest arm of GeneratedSuiteCommand — it runs under classic VSTest, so no
new invocation shape. Includes the client-routed case that caught CS0120 for xUnit and the
Warn-contract test, since NUnit's diagnostics sink is the one that differs most."
```

---

### Task 7: Ship the fifth package, and the docs

**Files:** `pack-and-verify.ps1`, `local-e2e-test.ps1`, `release.yml`, `pack.yml`, `NeutralityTests.cs`, `PackageVersionCouplingTests.cs`, `README.md`, `CLAUDE.md`, `CONTRIBUTING.md`, `docs/getting-started.md`, the 2026-08-16 spec §5.

- [ ] **Step 1: Packaging**

`pack-and-verify.ps1` gains `InTest.Runtime.NUnit` in the pack list, the version-equality check, artifact-contents assertions, and a positive control — `NUnit` is its adapter dependency, the analogue of `MSTest.TestFramework` and `xunit.v3.extensibility.core`.

`release.yml`'s asset count is **derived** from a package-id list (`$packageIds.Count * 2`). **Add the id; do not touch the arithmetic.** It becomes 10. That check runs *after* `dotnet nuget push`, so a wrong count leaves packages permanently published and then fails.

`pack.yml` and `local-e2e-test.ps1` gain the fifth package; `local-e2e-test.ps1`'s `-Framework` `ValidateSet` gains `nunit`.

- [ ] **Step 2: The guards**

`NeutralityTests.AdapterPackageDeclaresItsTestFramework` is `[DataRow]`-driven over adapters — add `("InTest.Runtime.NUnit", "NUnit")`.

`PackageVersionCouplingTests`' `RuntimeSelfVersionedPackages` set gains `InTest.Runtime.NUnit`. **And note `NUnit` and `NUnit3TestAdapter` version independently** — 4.x and 6.x — unlike the MSTest trio this guard was written around. Each must be checked against its own pin.

- [ ] **Step 3: Prove the guards fail**

From a **fresh, empty `-OutputDir`** — `pack-and-verify.ps1` never cleans it, and a stale `.nupkg` makes the mutation pass spuriously. Remove `InTest.Runtime.NUnit` from the pack list, confirm the failure names it, restore. Then remove its `NUnit` reference and confirm `NeutralityTests` fails on that row. Report both.

- [ ] **Step 4: Docs**

Five packages, three frameworks. `README.md`'s Test framework row; `CLAUDE.md`'s package count, framework constraint and Architecture section (three templates now); `CONTRIBUTING.md`'s **five** hardcoded package counts (four packages/eight files becomes five/ten); `docs/getting-started.md`'s framework-specific sites; §5's command table.

- [ ] **Step 5: Full verification and commit**

```bash
dotnet build InTest.sln
dotnet test tests/InTest.Architecture.Tests
dotnet test tests/InTest.Runtime.Tests
dotnet test tests/InTest.Cli.Tests
dotnet test tests/InTest.Runtime.NUnit.Tests
dotnet build tests/InTest.Runtime.XUnit.Tests && dotnet tests/InTest.Runtime.XUnit.Tests/bin/Debug/net10.0/InTest.Runtime.XUnit.Tests.dll
dotnet test tests/InTest.Golden.Tests
```

**Six separate commands** — `dotnet test InTest.sln` fails while any xunit.v3 project is in the solution.

---

## Out of scope

- **Regenerating `examples/`.** Still blocked until the adapters publish: the three version markers must move together, and `InTest.Runtime.MSTest` has never shipped at `0.1.0-preview.1`. Release-time work.
- **Exercising `release.yml`.** It cannot be rehearsed without publishing.
- **NUnit's `[CancelAfter]`.** The token is inert unless something cancels it, exactly like MSTest's without `[Timeout]`. Generated code passes it safely; do not add a timeout mechanism.

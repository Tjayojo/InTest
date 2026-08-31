# xUnit Framework Pack Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `intest init --framework xunit` scaffolds a project whose generated tests are xUnit v3, built on a new `InTest.Runtime.xUnit` adapter package.

**Architecture:** The neutral `InTest.Runtime` package does not change — this has now been proven three times by three independent reviewers, each building a working adapter against it with zero edits. What is added is a second Scriban template, a second adapter package mirroring `InTest.Runtime.MSTest`, framework selection in `TemplateRenderer` and `ConfigLoader`, and a `--framework` flag on `init`. The bulk of the *unexpected* work is not the adapter — it is the Golden test harness, which cannot execute an xUnit v3 project as currently written.

**Tech Stack:** .NET 10 · xunit.v3 4.0.0 (`xunit.v3.extensibility.core` + `xunit.v3.assert` for the library; `xunit.v3` for generated projects) · MSTest 4.3.3 (unchanged) · Scriban 7.2.6 · Shouldly 4.3.0

**Revision 3.** Wave 1 (Tasks 2, 4, 10) implemented in parallel and merged clean; all four suites
green. Four further defects surfaced *during* implementation and are fixed below: Task 2's build
checkpoint expected a failure that does not happen, Task 4's Step 1 referenced three test helpers
that do not exist, and two pieces of work were **owned by no task at all** —
`scripts/local-e2e-test.ps1` (now Task 9) and `CLAUDE.md`'s Architecture section (now Task 6).

**Revision 2.** Nine agents built or probed every remaining task against the real repository, and a
second adversarial pass tried to refute what they found. **26 defects were confirmed; 3 were
refuted and are not acted on.** The plan as first written did not survive contact: Task 2's adapter
did not compile, Task 4 broke an existing test, Task 6 produced generated code that would not build,
and Task 9 left a release failing *after* an irreversible `nuget push`. Every fix below is what was
measured, not what was reasoned.

**Source spec:** `docs/superpowers/specs/2026-08-30-intest-xunit-framework-pack.md` (revision 4). Named decisions in `[slug]` form below are defined there. **Read §2's decisions before starting** — several prescribe a specific API whose obvious alternative does not compile.

**Branch:** `xunit-framework-pack`, worktree `D:/TestGen-xunit`, cut from `main` at `038c06b`. Nothing from PR #8.

---

## Before you start

**Read `CLAUDE.md`.** Three things in it govern this work: comments explain *why* at length with evidence; the three text-safety rules are non-negotiable; and the Golden suite's timing figure is a budget you must read from there rather than from any other document, including this one.

**Line numbers in this plan were taken at `e4a0a6e`.** Locate everything by content as well — every reference below names what to search for.

**Two rules that come from this design's review history, and both have already cost a rewrite:**

1. **When the design names a specific API, use exactly that one.** `[scaffold-per-framework]` prescribes `[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]` and explicitly forbids `CollectionBehavior(DisableTestParallelization = true)`, which is obsolete-**as-error** and will not compile. The namespaces are not where you would guess.
2. **If a step's code does not compile or does not match current source, STOP and report it.** Do not improvise around it. Four blocking defects were found in this design by people who stopped; the two most expensive were prescriptions that looked right and did not run.

---

## A gap this plan closes that the design does not state

The design's `[harness-port-comes-first]` says *"Every `dotnet test` shell-out becomes a direct-exe invocation."* **That is wrong as a blanket instruction, and following it literally breaks the MSTest suites.**

Measured: `src/InTest.Cli/Commands/InitCommand.cs` sets no `OutputType` and no `EnableMSTestRunner`, so an MSTest scaffold builds a **dll**, not an exe. There is no executable to invoke. The direct-exe path is correct for xUnit and impossible for MSTest, so **the harness must branch on framework** — which is what Task 1 builds.

---

## File Structure

**Created:**

| File | Responsibility |
|---|---|
| `src/InTest.Runtime.xUnit/InTest.Runtime.xUnit.csproj` | The fourth shipped package |
| `src/InTest.Runtime.xUnit/ApiTestBase.cs` | `IAsyncLifetime` lifecycle → `BeginTest`/`EndTest`; skip → `Assert.Skip` |
| `src/InTest.Runtime.xUnit/TestHost.cs` | Facade over `InTestRun`, plus `XunitDiagnostics : IRunDiagnostics` |
| `src/InTest.Runtime.xUnit/README.md` | Packed into the nupkg — the csproj mirrors `<PackageReadmeFile>`, so packing fails without it |
| `src/InTest.Cli/Rendering/Templates/xunit-class.scriban` | The second template |
| `tests/InTest.Runtime.XUnit.Tests/` | Fifth suite — the xUnit adapter's own tests, xUnit-based by necessity |
| `tests/InTest.Golden.Tests/Expected/OrdersTests.xunit.g.cs.txt` | Second golden expectation file |

**Modified:**

| File | Change |
|---|---|
| `tests/InTest.Golden.Tests/GeneratedSuiteExecutionTests.cs` | Harness branches by framework (Task 1) |
| `scripts/ci/assert-trx-results.ps1` | Accepts trx from either runner |
| `src/InTest.Cli/Configuration/ConfigLoader.cs` | Accepts `"xunit"` |
| `src/InTest.Cli/Commands/GenerateCommand.cs` | Frozen-axis detection |
| `src/InTest.Cli/Commands/InitCommand.cs` | `framework` parameter; per-framework scaffold |
| `src/InTest.Cli/Program.cs` | `--framework` option |
| `src/InTest.Cli/Rendering/TemplateRenderer.cs` | Template selection |
| `tests/InTest.Cli.Tests/TemplateEscapingGuardTests.cs` | Runs over both templates |
| `scripts/ci/pack-and-verify.ps1`, `.github/workflows/{release,pack,build-and-test}.yml` | Fourth package; CI matrix |
| `tests/InTest.Architecture.Tests/{NeutralityTests,PackageVersionCouplingTests}.cs` | Cover the fourth package |
| `Directory.Packages.props`, `InTest.sln` | xUnit versions; new projects |
| `docs/getting-started.md`, the 2026-08-16 spec §5 | Adoption path and command table |

---

### Task 1: Make the Golden harness able to run either framework

This is first because nothing downstream can be verified without it, and because it is where the design was most wrong.

**Files:**
- Modify: `tests/InTest.Golden.Tests/GeneratedSuiteExecutionTests.cs`
- Modify: `scripts/ci/assert-trx-results.ps1`

- [ ] **Step 1: Write the failing test**

Add to `GeneratedSuiteExecutionTests.cs`:

```csharp
/// <summary>
/// [harness-port-comes-first]: the harness must run either framework, and the two need different
/// invocations — not because of the trx logger (a plain `dotnet test` with no logger fails
/// identically) but because `dotnet test` uses the VSTest target, which the .NET 10 SDK refuses for
/// a Microsoft.Testing.Platform project: "Testing with VSTest target is no longer supported by
/// Microsoft.Testing.Platform on .NET 10 SDK and later."
/// <para>
/// The reverse is equally true and is why this cannot be a wholesale port: the MSTest scaffold sets
/// no <c>OutputType</c> and no <c>EnableMSTestRunner</c>, so it builds a <b>dll</b> and there is no
/// executable to invoke. Each framework has exactly one invocation that works.
/// </para>
/// </summary>
[TestMethod]
public void RunGeneratedSuiteArgumentsDifferByFramework()
{
    var mstest = GeneratedSuiteRunner.For("mstest", "/tmp/proj", "Orders.ApiTests", trxPath: "r.trx");
    mstest.FileName.ShouldBe("dotnet");
    mstest.Arguments.ShouldContain("test");
    mstest.Arguments.ShouldContain("--logger");

    var xunit = GeneratedSuiteRunner.For("xunit", "/tmp/proj", "Orders.ApiTests", trxPath: "r.trx");
    xunit.FileName.ShouldEndWith("Orders.ApiTests.exe");
    xunit.Arguments.ShouldContain("-result-trx");
    xunit.Arguments.ShouldNotContain("dotnet test");
}

/// <summary>
/// `--filter` is a `dotnet test` option. The direct runner rejects it outright
/// (<c>error: unknown option: --filter</c>); its equivalent is <c>-filterVSTest</c>, which takes the
/// same query string. Twelve call sites in this file pass a filter, so getting this wrong is not a
/// single-site mistake.
/// </summary>
[TestMethod]
public void RunGeneratedSuiteTranslatesTheFilterArgumentPerFramework()
{
    GeneratedSuiteRunner.For("mstest", "/tmp/p", "P", filter: "FullyQualifiedName~Foo")
        .Arguments.ShouldContain("--filter \"FullyQualifiedName~Foo\"");

    GeneratedSuiteRunner.For("xunit", "/tmp/p", "P", filter: "FullyQualifiedName~Foo")
        .Arguments.ShouldContain("-filterVSTest \"FullyQualifiedName~Foo\"");
}
```

- [ ] **Step 2: Run to verify it fails**

```bash
dotnet test tests/InTest.Golden.Tests --filter "FullyQualifiedName~RunGeneratedSuite"
```

Expected: **compile error** — `GeneratedSuiteRunner` does not exist.

- [ ] **Step 3: Create the runner selector**

Create `tests/InTest.Golden.Tests/GeneratedSuiteRunner.cs`:

```csharp
namespace InTest.Golden.Tests;

/// <summary>
/// [harness-port-comes-first]: chooses how to execute a generated suite, because the two frameworks
/// have exactly one working invocation each and they are not the same shape.
/// <para>
/// <b>MSTest — `dotnet test`.</b> The scaffold sets no <c>OutputType</c>, so it builds a dll. There
/// is no executable to run.
/// </para>
/// <para>
/// <b>xUnit v3 — the built executable, directly.</b> `dotnet test` uses the VSTest target, which the
/// .NET 10 SDK refuses for a Microsoft.Testing.Platform project. This was measured on SDK 10.0.400,
/// 10.0.303 and 10.0.111, with and without a logger argument, so it is not a flag problem. The
/// Microsoft.Testing.Platform opt-in path (`dotnet test -- --report-trx`, with a `global.json`
/// runner entry) was also tried and produced <c>Zero tests ran</c>, exit 5 — and an MSTest control
/// under the same `global.json` failed identically, so what is broken there is `dotnet test`'s MTP
/// handshake rather than anything xUnit-specific. The direct executable needs no opt-in at all and
/// works unconditionally, which is why it is the one used here. <b>Do not spend time trying to make
/// `dotnet test` work for xUnit.</b>
/// </para>
/// </summary>
internal sealed record GeneratedSuiteRunner(string FileName, string Arguments)
{
    internal static GeneratedSuiteRunner For(
        string framework,
        string projectRoot,
        string projectName,
        string? trxPath = null,
        string? filter = null)
    {
        ArgumentNullException.ThrowIfNull(framework);
        ArgumentNullException.ThrowIfNull(projectRoot);
        ArgumentNullException.ThrowIfNull(projectName);

        return framework switch
        {
            "mstest" => new GeneratedSuiteRunner("dotnet", MsTestArguments(projectRoot, trxPath, filter)),
            "xunit" => new GeneratedSuiteRunner(
                Path.Combine(projectRoot, "bin", "Debug", "net10.0", projectName + ".exe"),
                XunitArguments(trxPath, filter)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(framework), framework, "expected \"mstest\" or \"xunit\"."),
        };
    }

    private static string MsTestArguments(string projectRoot, string? trxPath, string? filter)
    {
        var sb = new StringBuilder($"test \"{projectRoot}\" --no-build --nologo");
        if (trxPath is not null)
        {
            sb.Append($" --logger \"trx;LogFileName={trxPath}\"");
        }

        if (filter is not null)
        {
            sb.Append($" --filter \"{filter}\"");
        }

        return sb.ToString();
    }

    private static string XunitArguments(string? trxPath, string? filter)
    {
        var sb = new StringBuilder();
        if (trxPath is not null)
        {
            sb.Append($"-result-trx \"{trxPath}\"");
        }

        // -filterVSTest, not --filter: the direct runner rejects --filter with
        // "error: unknown option: --filter" and takes the identical query string under this name.
        if (filter is not null)
        {
            sb.Append(sb.Length > 0 ? " " : "").Append($"-filterVSTest \"{filter}\"");
        }

        return sb.ToString();
    }
}
```

Add `using System.Text;` at the top.

- [ ] **Step 4: Run to verify it passes**

```bash
dotnet test tests/InTest.Golden.Tests --filter "FullyQualifiedName~RunGeneratedSuite"
```

Expected: **PASS**, 2 tests.

- [ ] **Step 5: Route every existing shell-out through it**

Replace each `ProcessRunner.RunAsync("dotnet", $"test …")` call in `GeneratedSuiteExecutionTests.cs` with a `GeneratedSuiteRunner.For("mstest", …)` call and run its `FileName`/`Arguments`. **Every existing case stays on `"mstest"`** — this task changes no behaviour, only the shape of the call.

There are two bare invocations (`:448`, `:1511`) plus the logger-bearing and filter-bearing ones. Find them all:

```bash
grep -n 'RunAsync("dotnet", \$\?"test' tests/InTest.Golden.Tests/GeneratedSuiteExecutionTests.cs
```

- [ ] **Step 6: Prove nothing changed**

```bash
dotnet test tests/InTest.Golden.Tests
```

Expected: **PASS**, 52 tests (50 + the 2 new). This is the slow suite — read the timing figure from `CLAUDE.md` and pass a timeout well past it. **Run it in the foreground.** Do not background it and wait for a notification; that has killed two agents on this project.

- [ ] **Step 7: Teach `assert-trx-results.ps1` the second shape**

`scripts/ci/assert-trx-results.ps1` parses trx to confirm each suite reported executed tests. Both runners emit trx, so the file format is shared — but confirm the element paths it depends on (`:14`, `:22`) are present in a trx produced by the direct xUnit runner, and widen them if not. **If the two trx shapes differ in a way that needs branching, report it before writing the branch** — the design assumes they do not.

- [ ] **Step 8: Commit**

```bash
git add tests/InTest.Golden.Tests scripts/ci/assert-trx-results.ps1
git commit -m "test: Golden harness runs either framework

MSTest builds a dll and must go through dotnet test; xUnit v3 builds an exe and cannot,
because dotnet test uses the VSTest target that the .NET 10 SDK refuses for a
Microsoft.Testing.Platform project. Each framework has exactly one invocation that works.

The design said every shell-out becomes a direct-exe call. That would have broken MSTest,
which has no executable to invoke.

--filter is a dotnet test option; the direct runner takes the same query as -filterVSTest.
Twelve call sites pass one. Every existing case stays on mstest — no behaviour change."
```

---

### Task 2: The `InTest.Runtime.xUnit` adapter package

**Files:**
- Create: `src/InTest.Runtime.xUnit/InTest.Runtime.xUnit.csproj`, `TestHost.cs`, `ApiTestBase.cs`, `README.md`
- Modify: `Directory.Packages.props`, `InTest.sln`

- [ ] **Step 1: Add the package versions**

In `Directory.Packages.props`, beside the MSTest entries:

```xml
<PackageVersion Include="xunit.v3" Version="4.0.0" />
<PackageVersion Include="xunit.v3.extensibility.core" Version="4.0.0" />
<PackageVersion Include="xunit.v3.assert" Version="4.0.0" />
```

All three are Apache-2.0, listed, no deprecation notice, no vulnerability advisories, published 2026-08-15, owner `xunit` — checked 2026-08-30 per the dependency policy.

- [ ] **Step 2: Create the project**

`src/InTest.Runtime.xUnit/InTest.Runtime.xUnit.csproj` — mirror `src/InTest.Runtime.MSTest/InTest.Runtime.MSTest.csproj` exactly, including its packaging metadata and its `InternalsVisibleTo`, changing only the package references:

```xml
<ItemGroup>
  <ProjectReference Include="../InTest.Runtime/InTest.Runtime.csproj" />
  <!--
    Two packages, not xunit.v3. Referencing xunit.v3 from a library fails outright:
      "xUnit.net v3 test projects must be executable (set project property <OutputType>Exe</OutputType>).
       If this is not a test project, reference xunit.v3.extensibility.core instead."
    and extensibility.core alone does not carry Assert, so Assert.Skip is CS0103 without
    xunit.v3.assert. Generated adopter projects reference xunit.v3 itself, because they are executable.
  -->
  <PackageReference Include="xunit.v3.extensibility.core" />
  <PackageReference Include="xunit.v3.assert" />
</ItemGroup>

<ItemGroup>
  <InternalsVisibleTo Include="InTest.Runtime.XUnit.Tests" />
</ItemGroup>
```

**Also create `src/InTest.Runtime.xUnit/README.md`**, mirroring `src/InTest.Runtime.MSTest/README.md`
with xUnit wording. The csproj carries `<PackageReadmeFile>README.md</PackageReadmeFile>` from the
mirror; without the file, `dotnet pack` fails outright — and Task 9 is where that would otherwise
surface, four tasks later.

Add the project to `InTest.sln`.

- [ ] **Step 3: Write the failing test**

The fifth test project does not exist yet (Task 3). For now, prove the package compiles and the neutral boundary holds:

```bash
dotnet build src/InTest.Runtime.xUnit
```

Expected: **succeeds** — a C# class library with no source files is a valid empty assembly. Rev 1
said this should fail; it does not, and an implementer who trusts that will waste time doubting their
setup. This step is a smoke test that the csproj and its package references resolve, nothing more.

- [ ] **Step 4: Write `TestHost.cs`**

**`using Xunit;` is required and its absence is not obvious.** `MSTest.TestFramework` ships
`buildTransitive/net9.0/MSTest.TestFramework.targets` containing
`<Using Include="Microsoft.VisualStudio.TestTools.UnitTesting" />`, which — combined with
`ImplicitUsings` being on repo-wide — is why `InTest.Runtime.MSTest`'s files compile with no MSTest
using directive at all. **The xunit.v3 library packages ship no props or targets whatsoever**, so
nothing is injected. Measured: without it, `error CS0246: The type or namespace name
'IAsyncLifetime' could not be found`, then three `CS0103: The name 'TestContext' does not exist`.
Copying the MSTest adapter's file shape without this is the single reason rev 1 of this plan did not
compile.

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InTest.Runtime;

/// <summary>
/// [adapter-mirrors-mstest]: the xUnit counterpart of <c>InTest.Runtime.MSTest</c>'s <c>TestHost</c>: a facade over
/// <see cref="InTestRun"/>, the assembly-scope composition root.
/// <para>
/// <b>Same name, same namespace, same passthroughs — deliberately.</b> An adopter migrating between
/// frameworks changes a <c>PackageReference</c> id; their <c>TestHost.ConfigureServices</c>
/// registration keeps compiling untouched.
/// </para>
/// <para>
/// <b>What does not mirror: the <c>TestContext</c> parameter.</b> MSTest's
/// <c>InitializeAsync(TestContext)</c> exists because MSTest hands the assembly hook a context and
/// because the run-settings profile is read from it. xUnit has neither — the assembly fixture object
/// is itself the lifecycle hook, and <c>TestContext.Current</c> is ambient. So the parameter is
/// dropped rather than faked, and the profile argument is a literal <see langword="null"/>: see
/// <c>[profile-loses-its-first-source]</c> for what that costs an xUnit adopter and what replaces it
/// (<c>INTEST_PROFILE</c>).
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

    /// <summary>
    /// Called from the adopter's assembly fixture. The profile is <see langword="null"/> because
    /// xUnit has no run-settings equivalent — <c>INTEST_PROFILE</c> and the config default are what
    /// remain in <c>InTestRun.ResolveProfile</c>'s precedence chain.
    /// </summary>
    public static Task InitializeAsync(CancellationToken cancellationToken = default) =>
        // profileFromRunSettings, not profile — that is the neutral method's actual parameter name
        // (InTestRun.cs:114). The named form is kept because it documents
        // [profile-loses-its-first-source]; the wrong name is CS1739.
        InTestRun.InitializeAsync(profileFromRunSettings: null, new XunitDiagnostics(), cancellationToken);

    public static Task CleanupAsync() => InTestRun.CleanupAsync(new XunitDiagnostics());

    /// <summary>
    /// [warn-needs-a-real-sink]: <see cref="IRunDiagnostics.Warn"/> must reach the operator even
    /// when the run passes and exits 0, and under xUnit v3 only one sink does that unconditionally.
    /// <para>
    /// Measured against xunit.v3 4.0.0. <c>TestContext.SendDiagnosticMessage</c> prints nothing
    /// without <c>-diagnostics</c> on the command line. <c>TestContext.Current.TestOutputHelper</c>
    /// is <see langword="null"/> outside a running test — which is exactly the assembly scope
    /// <c>InTestRun.InitializeAsync</c> and the fixture report use it from.
    /// <c>AddWarning</c> at assembly scope is refused, and refused <em>silently</em>: it returns
    /// without throwing and only logs "Attempted to log a test warning message while not running a
    /// test" under <c>-diagnostics</c>. <c>Console.WriteLine</c> reaches process output on a passing
    /// default run at both assembly-init and assembly-dispose scope, which is what
    /// <c>GeneratedSuiteExecutionTests.ValidationReportWithAProblemSurfacesOnAPassingRun</c> asserts
    /// on.
    /// </para>
    /// <para>
    /// So <see cref="Warn"/> always writes to the console, and additionally calls
    /// <c>AddWarning</c> when a test is running so the message also surfaces in the runner's own
    /// reporting. The console write is the one that satisfies the contract; the second is a bonus
    /// and must never be the only sink.
    /// </para>
    /// </summary>
    internal sealed class XunitDiagnostics : IRunDiagnostics
    {
        public void Note(string message)
        {
            var helper = TestContext.Current.TestOutputHelper;
            if (helper is null)
            {
                Console.WriteLine(message);
                return;
            }

            helper.WriteLine(message);
        }

        public void Warn(string message)
        {
            Console.WriteLine(message);

            if (TestContext.Current.Test is not null)
            {
                TestContext.Current.AddWarning(message);
            }
        }
    }
}
```

- [ ] **Step 5: Write `ApiTestBase.cs`**

```csharp
using Xunit;

namespace InTest.Runtime;

/// <summary>
/// xUnit adapter over <see cref="ApiTestCore"/>, mirroring <c>InTest.Runtime.MSTest</c>'s
/// <c>ApiTestBase</c>. Generated classes derive from a project base class deriving from this, and
/// call <c>RequireMultipleIdentities()</c>, <c>RequireSecondaryIdentityLacks(...)</c>,
/// <c>UseIdentity(...)</c>, <c>RequireFixture(...)</c>, <c>FixtureBody(...)</c>, <c>Client</c>,
/// <c>TestId</c> and <c>Schemas</c> — all of which live on the neutral base and need no adapting.
/// <para>
/// This class's whole job is the two seams <see cref="ApiTestCore"/> cannot own without naming a
/// test framework: lifecycle, and turning a skip <em>reason</em> (a plain <c>string?</c>, null
/// meaning "run") into an actual skip call.
/// </para>
/// <para>
/// <b>[lifecycle-is-the-real-difference]: lifecycle is where the frameworks genuinely differ.</b> MSTest uses
/// <c>[TestInitialize]</c>/<c>[TestCleanup]</c>. xUnit v3 uses <see cref="IAsyncLifetime"/>, which
/// declares <b>only</b> <c>InitializeAsync</c> and inherits <see cref="IAsyncDisposable"/> — the
/// v2 shape with both on the interface does not exist here. Verified: inside
/// <c>InitializeAsync</c>, <c>TestContext.Current.Test</c> is non-null and its
/// <c>TestDisplayName</c> is populated, which is what makes this the right place to call
/// <see cref="ApiTestCore.BeginTest"/>; and <c>DisposeAsync</c> runs on pass, fail <em>and</em>
/// skip, so <see cref="ApiTestCore.EndTest"/> is not missed on any path.
/// </para>
/// <para>
/// [snapshot-at-call-time]: <c>TestContext.Current</c> is read at each use and never cached — xUnit documents it as a
/// point-in-time snapshot. Its static type is <c>ITestContext</c>, not <c>TestContext</c>.
/// </para>
/// </summary>
public abstract class ApiTestBase : ApiTestCore, IAsyncLifetime
{
    public ValueTask InitializeAsync()
    {
        BeginTest(TestContext.Current.Test?.TestDisplayName, new TestHost.XunitDiagnostics());
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        EndTest();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// [skip-is-a-reason]: the neutral layer returns a reason string, null meaning "run". MSTest's
    /// adapter turns that into <c>Assert.Inconclusive</c>; xUnit's into <c>Assert.Skip</c>. Verified
    /// to produce trx <c>outcome="NotExecuted"</c> — the same outcome MSTest reports — with the
    /// reason in <c>&lt;Output&gt;&lt;StdOut&gt;</c> rather than <c>&lt;Message&gt;</c>, which
    /// matters to any acceptance check asserting on skip reasons.
    /// </summary>
    protected internal static void RequireMultipleIdentities()
    {
        if (MultipleIdentitiesSkipReason() is { } reason)
        {
            Assert.Skip(reason);
        }
    }

    /// <inheritdoc cref="RequireMultipleIdentities"/>
    protected internal static void RequireSecondaryIdentityLacks(params string[] requiredScopes)
    {
        if (SecondaryIdentityScopeSkipReason(requiredScopes) is { } reason)
        {
            Assert.Skip(reason);
        }
    }
}
```

- [ ] **Step 6: Build and confirm the neutral package was not touched**

```bash
dotnet build InTest.sln
git status --short src/InTest.Runtime/
```

Expected: **Build succeeded, 0 Warning(s), 0 Error(s)**, and `git status` on the neutral package **empty**. If the neutral package had to change to make this compile, **stop and report it** — §6 of the design says that means the type is in the wrong layer, and three reviewers each built this with zero such edits.

- [ ] **Step 7: Commit**

```bash
git add Directory.Packages.props InTest.sln src/InTest.Runtime.xUnit
git commit -m "feat: add the InTest.Runtime.xUnit adapter package

Mirrors InTest.Runtime.MSTest: same namespace, same type names, depending on InTest.Runtime
at the same version. An adopter switching frameworks changes a PackageReference id, never a
using or a type name.

Two xUnit packages, not one — referencing xunit.v3 from a library fails outright ('must be
executable'), and extensibility.core alone does not carry Assert.

TestHost drops MSTest's TestContext parameter rather than faking it: xUnit's assembly fixture
is itself the lifecycle hook and TestContext.Current is ambient. The profile argument is a
literal null, which is what [profile-loses-its-first-source] costs.

Warn writes to the console because that is the only sink reaching the operator on a passing
run — SendDiagnosticMessage needs -diagnostics, TestOutputHelper is null at assembly scope,
and AddWarning is refused there silently."
```

---

### Task 3: The fifth test project

The two adapters declare the same types in the same namespace, so they cannot coexist in one compilation — adding both to `tests/InTest.Runtime.Tests` produces `CS0433` for `ApiTestBase` and `TestHost` (40 lines, 20 unique sites). The xUnit adapter's internals therefore need their own suite, and it must itself be xUnit-based.

**Files:**
- Create: `tests/InTest.Runtime.XUnit.Tests/InTest.Runtime.XUnit.Tests.csproj`, `XunitDiagnosticsTests.cs`
- Modify: `InTest.sln`, `.github/workflows/build-and-test.yml`

- [ ] **Step 1: Create the project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- xunit.v3 requires this; without it the build fails with the "must be executable" error. -->
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/InTest.Runtime.xUnit/InTest.Runtime.xUnit.csproj" />
    <PackageReference Include="xunit.v3" />
  </ItemGroup>
</Project>
```

Add to `InTest.sln`.

- [ ] **Step 2: Write the failing test**

`tests/InTest.Runtime.XUnit.Tests/XunitDiagnosticsTests.cs`:

```csharp
using InTest.Runtime;
using Xunit;

namespace InTest.Runtime.XUnit.Tests;

/// <summary>
/// [warn-needs-a-real-sink]: Warn must reach the operator on a passing run. The sinks that do not
/// are all silent rather than throwing, so a test that merely calls Warn and does not assert on
/// output would pass against every wrong implementation.
/// </summary>
public class XunitDiagnosticsTests
{
    [Fact]
    public void WarnWritesToTheConsoleEvenWhenNoTestIsRunning()
    {
        var original = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            IRunDiagnostics diagnostics = new TestHost.XunitDiagnostics();
            diagnostics.Warn("WARN_MARKER");
        }
        finally
        {
            Console.SetOut(original);
        }

        captured.ToString().ShouldContain("WARN_MARKER");
    }
}
```

Use whatever assertion library the project references — if Shouldly is not referenced here, use `Assert.Contains("WARN_MARKER", captured.ToString())` rather than adding a dependency for one call.

- [ ] **Step 3: Run to verify it fails, then passes**

```bash
dotnet build tests/InTest.Runtime.XUnit.Tests
dotnet tests/InTest.Runtime.XUnit.Tests/bin/Debug/net10.0/InTest.Runtime.XUnit.Tests.dll
```

Expected after Task 2's code is in place: **passing**. Note this is not `dotnet test` — that will not
work here, for the same reason it does not work for generated xUnit projects.

**`dotnet <dll>`, not the apphost.** Step 5 puts this in CI's `fast` job, which is matrixed
`ubuntu-latest` / `windows-latest`, where no `.exe` is produced. This is the identical defect already
fixed in Task 1's `GeneratedSuiteCommand` (commit `a2b96cd`) — use the same form here rather than
rediscovering it on a red Linux leg.

- [ ] **Step 4: Prove it discriminates**

Change `Warn` in `TestHost.cs` to drop the `Console.WriteLine` line. Re-run: the test **must fail**. Restore.

- [ ] **Step 5: Add it to CI's `fast` job**

`.github/workflows/build-and-test.yml:105-119` runs the fast suites. **A solution-level `dotnet test InTest.sln` now fails** — measured: with both an MSTest project and an xunit.v3 project in one solution, the xUnit project errors on the VSTest target while the MSTest project runs and prints `Passed!`, and the command exits 1. So this suite is a **separate direct-exe step**, not another `dotnet test` line.

Update `CLAUDE.md`'s Commands section in the same commit: `dotnet test InTest.sln # all four suites` is no longer true.

- [ ] **Step 6: Commit**

```bash
git add tests/InTest.Runtime.XUnit.Tests InTest.sln .github/workflows/build-and-test.yml CLAUDE.md
git commit -m "test: add the xUnit adapter's own suite

Both adapters declare the same types in the same namespace, so they cannot coexist in one
compilation (CS0433 for ApiTestBase and TestHost). That is the price of the same-namespace
decision, and it buys an adopter changing only a PackageReference id.

Runs as a direct exe, not dotnet test — and a solution-level dotnet test now fails outright
with both frameworks present, so CLAUDE.md's all-four-suites command is corrected here."
```

---

### Task 4: `project.framework` accepts `"xunit"`, and a changed value is refused

**Files:**
- Modify: `src/InTest.Cli/Configuration/ConfigLoader.cs`
- Modify: `src/InTest.Cli/Commands/GenerateCommand.cs`
- Test: `tests/InTest.Cli.Tests/ConfigLoaderTests.cs`, `tests/InTest.Cli.Tests/GenerateCommandTests.cs`

- [ ] **Step 0: Repoint the existing test that uses `"xunit"` as its counter-example**

`ConfigLoaderTests.ExplainsAnUnsupportedFrameworkAsNotYetSupportedRatherThanInvalid` currently uses
`"xunit"` as its *unsupported-framework exemplar*. Accepting `"xunit"` makes that test fail, and
rev 1 of this plan never mentioned it — so Step 5's "Expected: PASS" was wrong before a line was
written.

Repoint it to `"nunit"`, the remaining roadmapped-but-unshipped framework, and update its doc comment
to say why the exemplar moved. Verified: with `"nunit"` the test passes unchanged in substance.

- [ ] **Step 1: Write the failing tests**

**The code below is illustrative, not literal.** `LoadConfigWith`, `ScaffoldMsTestProject` and
`SetConfiguredFramework` **do not exist** — rev 1 invented them. Use the files' real idioms:
`ConfigLoaderTests` has `WriteConfig(json)` + `ConfigLoader.Load(_root)`, and `ReasonFor(json)` for
the throwing case; `GenerateCommandTests` has its own scaffolding helpers. Write the two scaffolding
helpers you need if they are genuinely absent, following the surrounding conventions.

```csharp
[TestMethod]
public void AcceptsXunitAsAFrameworkValue()
{
    var config = LoadConfigWith("\"framework\": \"xunit\"");

    config.Framework.ShouldBe("xunit");
}

/// <summary>
/// Ordinal-exact lowercase, the same discipline the mstest value has always had: this is
/// adopter-facing JSON, not a C# identifier with case-insensitive lookup.
/// </summary>
[TestMethod]
public void RefusesAFrameworkValueThatOnlyDiffersInCase()
{
    Should.Throw<ConfigLoadException>(() => LoadConfigWith("\"framework\": \"xUnit\""))
        .Message.ShouldContain("xUnit");
}
```

And for the frozen axis, in `GenerateCommandTests.cs`:

```csharp
/// <summary>
/// [frozen-axis-becomes-reachable]: §5 makes the test framework a frozen axis and promises that
/// changing one "fails with a real error". Nothing enforced that, because with one supported value
/// it was unreachable. Accepting "xunit" makes it reachable: an adopter can edit one string and get
/// a wholesale-rewritten Generated/ targeting a framework their .csproj does not match.
/// <para>
/// Detected the way UpgradeCommand.DetectRuntimeReferenceMismatch already detects version drift —
/// by comparing intest.json against the adapter PackageReference in the .csproj. No new state is
/// recorded.
/// </para>
/// </summary>
[TestMethod]
public async Task RefusesWhenTheConfiguredFrameworkDisagreesWithTheAdapterReference()
{
    ScaffoldMsTestProject(_root);
    SetConfiguredFramework(_root, "xunit");

    // GenerateCommand.RunAsync takes a required CancellationToken — the file's own helper at
    // GenerateCommandTests.cs:62 is the idiom.
    var exit = await GenerateCommand.RunAsync(_root, CancellationToken.None);

    exit.ShouldBe(2);
    // Directory.Exists, not GetFiles: a refusal means Generated/ is never created at all, so
    // GetFiles throws DirectoryNotFoundException on the very path it is asserting about. This is
    // the idiom every other exit-2 refusal in this file already uses (GenerateCommandTests.cs:100).
    Directory.Exists(Path.Combine(_root, "Generated")).ShouldBeFalse();
}
```

**Confirm the exit code against §5's table before implementing.** `2` is "an argument was refused / tool error"; `generate --check` already uses `4` for a version mismatch, and these two must not disagree by accident. If §5 makes `4` the better fit, use `4` and say so in the commit message.

- [ ] **Step 2: Run to verify they fail**

```bash
dotnet test tests/InTest.Cli.Tests --filter "FullyQualifiedName~Framework"
```

Expected: **FAIL** — `"xunit"` is refused, and no mismatch detection exists.

- [ ] **Step 3: Open the config value**

In `ConfigLoader.RequireSupportedFramework`, replace the single-value check with a set. Keep the refusal message naming what *is* supported, and keep the ordinal-exact comparison:

```csharp
private static readonly string[] SupportedFrameworks = ["mstest", "xunit"];

// …
if (!SupportedFrameworks.Contains(framework, StringComparer.Ordinal))
{
    throw new ConfigLoadException(
    $"project.framework in {FileName} is \"{framework}\", which intest does not support. {FrameworkRule}");
}
```

**Update `FrameworkRule`'s text to name both values — and keep the word "yet".** A surviving test
asserts on it, so dropping it turns a passing test red for a reason unrelated to this change. For
example:

> It must be the test framework generated tests target. Supported today: `"mstest"` and `"xunit"`.
> InTest is designed to support three frameworks (§3); NUnit is not supported **yet**.

- [ ] **Step 4: Add the frozen-axis detection**

In `GenerateCommand`, before any generation work, compare the configured framework against the adapter `PackageReference` in the project's `.csproj` — `InTest.Runtime.MSTest` implies `mstest`, `InTest.Runtime.xUnit` implies `xunit`. Read `UpgradeCommand.DetectRuntimeReferenceMismatch` first and follow its shape and its error voice.

The message must name both values and tell the adopter what to do — the framework is frozen per project, so the answer is a new project, not an edit.

- [ ] **Step 5: Run to verify they pass**

```bash
dotnet test tests/InTest.Cli.Tests
```

Expected: **PASS**.

- [ ] **Step 6: Commit**

```bash
git add src/InTest.Cli tests/InTest.Cli.Tests
git commit -m "feat: accept xunit as a framework, and refuse a changed one

[config-opens-by-one-value] and [frozen-axis-becomes-reachable]. §5 promises that changing a
frozen axis fails with a real error; nothing enforced that, because with one supported value
the case was unreachable. Accepting a second value makes it reachable, so generate now
detects a config/csproj disagreement using the comparison
UpgradeCommand.DetectRuntimeReferenceMismatch already makes."
```

---

### Task 5: `init --framework`, and the per-framework scaffold

**Files:**
- Modify: `src/InTest.Cli/Program.cs`, `src/InTest.Cli/Commands/InitCommand.cs`
- Test: `tests/InTest.Cli.Tests/InitCommandTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[TestMethod]
public void ScaffoldsAnXunitProjectWhenAskedFor()
{
    InitCommand.Run(_root, "Orders.ApiTests", "orders.json", framework: "xunit").ShouldBe(0);

    var csproj = File.ReadAllText(Path.Combine(_root, "Orders.ApiTests.csproj"));
    csproj.ShouldContain("<OutputType>Exe</OutputType>");
    csproj.ShouldContain("xunit.v3");
    csproj.ShouldContain("InTest.Runtime.xUnit");
    csproj.ShouldNotContain("MSTest.TestFramework");

    File.ReadAllText(Path.Combine(_root, "intest.json")).ShouldContain("\"framework\": \"xunit\"");
}

/// <summary>
/// [scaffold-per-framework]: xUnit v3 parallelises by default (measured: "parallel mode =
/// collections [22 threads]"), and the MSTest scaffold deliberately pins DoNotParallelize. Without
/// its xUnit counterpart a scaffolded suite runs concurrently against a *deployed* API.
/// <para>
/// This assertion exists because no build-only test can catch it — a missing attribute compiles
/// perfectly. It is the counterpart of this file's existing
/// ShouldContain("[assembly: DoNotParallelize]") assertion.
/// </para>
/// <para>
/// Note the exact attribute: CollectionBehavior(DisableTestParallelization = true) is
/// obsolete-as-error in xunit.v3 4.0.0 and does not compile. ParallelizationAttribute lives in
/// Xunit.v3 and ParallelMode in Xunit.Sdk.
/// </para>
/// </summary>
[TestMethod]
public void ScaffoldsTheXunitParallelismOptOut()
{
    InitCommand.Run(_root, "Orders.ApiTests", "orders.json", framework: "xunit");

    File.ReadAllText(Path.Combine(_root, "AssemblyInfo.cs"))
        .ShouldContain("[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]");
}

[TestMethod]
public void RefusesAnUnknownFrameworkWithExitTwo()
{
    InitCommand.Run(_root, "Orders.ApiTests", "orders.json", framework: "junit").ShouldBe(2);
}

[TestMethod]
public void DefaultsToMsTestWhenNoFrameworkIsGiven()
{
    InitCommand.Run(_root, "Orders.ApiTests", "orders.json").ShouldBe(0);

    File.ReadAllText(Path.Combine(_root, "intest.json")).ShouldContain("\"framework\": \"mstest\"");
}
```

- [ ] **Step 2: Run to verify they fail**

```bash
dotnet test tests/InTest.Cli.Tests --filter "FullyQualifiedName~InitCommandTests"
```

Expected: **compile error** — `InitCommand.Run` has no `framework` parameter.

- [ ] **Step 3: Add the parameter and the option**

`InitCommand.Run` gains `string framework = "mstest"` as its last parameter — after `clientLockfilePath`, so no existing call site breaks.

In `Program.cs`, beside the other options:

```csharp
var frameworkOption = new Option<string>("--framework")
{
    Description = "Test framework for the scaffolded project: mstest (default) or xunit. Frozen per project — a suite cannot be migrated in place.",
    DefaultValueFactory = _ => "mstest",
};
```

Add it to `init.Options` and thread it through `init.SetAction`.

- [ ] **Step 4: Branch the scaffold**

`init` writes 11 files (`InitCommand.cs:410-637`). Five differ by framework:

| file | xUnit form |
|---|---|
| `.csproj` | `<OutputType>Exe</OutputType>`; `xunit.v3` + `InTest.Runtime.xUnit` in place of the three MSTest packages and `InTest.Runtime.MSTest`; keep `Microsoft.NET.Test.Sdk` and `Shouldly` |
| `AssemblyInfo.cs` | `[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]` in place of `[assembly: DoNotParallelize]` |
| assembly-setup class | an `IAsyncLifetime` fixture plus `[assembly: AssemblyFixture(typeof(...))]`, in place of the `[AssemblyInitialize]` static method |
| `intest.json` | `"framework": "xunit"` |
| `*.runsettings` | **not written** — it has no meaning under xUnit; see `[profile-loses-its-first-source]` |

The xUnit assembly-setup file:

```csharp
using InTest.Runtime;
using Xunit;

[assembly: AssemblyFixture(typeof({{rootNamespace}}.InTestAssemblyFixture))]

namespace {{rootNamespace}};

/// <summary>
/// Assembly-scope setup. xUnit v3 has no [AssemblyInitialize] equivalent — an AssemblyFixture is
/// constructed before any test runs and disposed after all of them finish.
/// </summary>
public sealed class InTestAssemblyFixture : IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await TestHost.InitializeAsync();

    public async ValueTask DisposeAsync() => await TestHost.CleanupAsync();
}
```

**The fixture must also carry the DI registration hook.** The MSTest scaffold's `TestStartup.cs`
sets `TestHost.ConfigureServices = Register;` and defines a `Register` method — that is the
scaffolded place an adopter registers `ITestTokenProvider` and their own services. Rev 1's xUnit
fixture dropped both, leaving an xUnit-scaffolded project with nowhere to do it. Give the fixture
the same two usings and the same `Register` method, comments included, and set
`TestHost.ConfigureServices = Register;` as the **first statement** of `InitializeAsync`, before
awaiting `TestHost.InitializeAsync()`.

Keep the `InTestGuardParallelizeProperties` MSBuild target for MSTest only — it names MSTest properties.

- [ ] **Step 5: Run to verify they pass, and that MSTest is unchanged**

```bash
dotnet test tests/InTest.Cli.Tests
```

Expected: **PASS**, including every existing MSTest scaffold test.

- [ ] **Step 6: Commit**

```bash
git add src/InTest.Cli tests/InTest.Cli.Tests
git commit -m "feat: init --framework, and a per-framework scaffold

[framework-is-an-init-flag]. Defaults to mstest, refuses anything else with exit 2, and
writes an explicit value into intest.json either way — which is what ConfigLoader's
no-defaulting rule exists to guarantee.

Five of the eleven scaffolded files differ. The parallelism one is a correctness issue, not a
detail: xUnit v3 parallelises by default and the MSTest scaffold deliberately pins
DoNotParallelize, so without its counterpart a scaffolded suite runs concurrently against a
deployed API. It is guarded by a text assertion because no build-only test can see a missing
attribute."
```

---

### Task 6: The xUnit template, and framework-based selection

**Files:**
- Create: `src/InTest.Cli/Rendering/Templates/xunit-class.scriban`
- Modify: `src/InTest.Cli/Rendering/TemplateRenderer.cs`
- Test: `tests/InTest.Cli.Tests/TemplateRendererTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[TestMethod]
public void RendersTheXunitShapeWhenTheFrameworkIsXunit()
{
    // RenderClass takes namespace and base class too — the neighbouring Render(TestClassPlan)
    // helper at TemplateRendererTests.cs:131 shows the shape.
    var rendered = new TemplateRenderer(framework: "xunit")
        .RenderClass(Plan(), "Orders.ApiTests", "Orders.ApiTests.OrdersTestBase");

    rendered.ShouldContain("using Xunit;");
    rendered.ShouldContain("[Fact]");
    rendered.ShouldContain("[Trait(\"Category\", \"Contract\")]");
    rendered.ShouldContain("TestContext.Current.CancellationToken");

    rendered.ShouldNotContain("[TestClass]");
    rendered.ShouldNotContain("[TestMethod");
    rendered.ShouldNotContain("Microsoft.VisualStudio.TestTools.UnitTesting");
}
```

- [ ] **Step 2: Run to verify it fails**

Expected: **compile error** — `TemplateRenderer` has no constructor taking a framework.

- [ ] **Step 3: Create the template**

Copy `src/InTest.Cli/Rendering/Templates/mstest-class.scriban` to `xunit-class.scriban` and apply exactly these substitutions. Everything else — including `path_argument_list`, `query_expression`, the `has_body` block, the client branch's pinned `try`/filters/stopwatch, and all `*_literal` quoting — stays byte-identical:

| mstest-class.scriban | xunit-class.scriban |
|---|---|
| `using Microsoft.VisualStudio.TestTools.UnitTesting;` | `using Xunit;` |
| `[TestClass]` on the class | *(delete the line)* |
| `[TestMethod, TestCategory("{{ tc.category }}")]` | `[Fact]`<br>`[Trait("Category", "{{ tc.category }}")]` |
| `[Description("{{ tc.display_name_literal }}")]` | **decide per `[display-name-is-not-metadata]`** — see below |
| `TestContext.CancellationToken` (5 sites: lines 93, 96, 107, 113, 116) | `TestContext.Current.CancellationToken` |
| `{{~ if tc.mutates ~}}` / `[DoNotParallelize]` / `{{~ end ~}}` (lines 19-21) | **the `[Fact]` line becomes `[Fact(DisableParallelization = true)]` when `tc.mutates`** — see below |

**The `[DoNotParallelize]` row is the one rev 1 missed, and it produces code that does not compile.**
The template emits it per-method, conditionally on `tc.mutates`. It is absent from
`Expected/OrdersTests.g.cs.txt` because no Orders case sets `mutates` — so the golden file cannot
catch it either. xUnit v3 has no per-method non-parallel attribute; the equivalent is a property on
`[Fact]` itself:

```
{{~ if tc.mutates ~}}
    [Fact(DisableParallelization = true)]
{{~ else ~}}
    [Fact]
{{~ end ~}}
```

Task 7 must add a `mutates` case to the xUnit golden coverage, or this stays unverified.

**On the `[Description]` row — this is the open decision the design flags, and it must be made here, not deferred.** In MSTest `[Description]` is orthogonal metadata; in xUnit `[Fact(DisplayName = "…")]` *is* the display name, and `ApiTestCore.BeginTest` feeds that string to `InTestId.ForTest`, which slugs it into a `TestId` that travels in an HTTP header. Using `DisplayName` therefore makes the same operation's correlation id differ between frameworks.

**Recommended: emit `[Trait("Description", "{{ tc.display_name_literal }}")]` and leave `[Fact]` bare**, keeping `TestId` aligned across frameworks. If you choose `DisplayName` instead, say so in the commit message and add a line to `docs/getting-started.md` — the divergence must not be silent either way.

`tc.expected_status` and `tc.http_method_pascal` stay bare; every `*_literal` stays quoted. `TemplateEscapingGuardTests` enforces this by quote parity and Task 7 makes it read this file.

- [ ] **Step 4: Make selection framework-based**

`TemplateRenderer.cs:10` is a field initialiser with the filename baked in:

```csharp
private readonly Template _classTemplate = Template.Parse(LoadEmbedded("mstest-class.scriban"));
```

Replace with constructor-time selection:

```csharp
private readonly Template _classTemplate;

/// <summary>
/// [framework-selects-template]: one template per framework, chosen once at construction.
/// Two files rather than one file branching internally — the templates are ~121 lines and mostly
/// identical, and a third framework would otherwise add a third set of conditionals to every block.
/// </summary>
public TemplateRenderer(string framework)
{
    ArgumentNullException.ThrowIfNull(framework);

    _classTemplate = framework switch
    {
        "mstest" => Template.Parse(LoadEmbedded("mstest-class.scriban")),
        "xunit" => Template.Parse(LoadEmbedded("xunit-class.scriban")),
        _ => throw new ArgumentOutOfRangeException(
            nameof(framework), framework, "expected \"mstest\" or \"xunit\"."),
    };
}
```

Thread the framework from the loaded config at every `TemplateRenderer` construction site. Add the new `.scriban` as an embedded resource alongside the existing one.

- [ ] **Step 5: Run to verify it passes**

```bash
dotnet test tests/InTest.Cli.Tests
```

Expected: **PASS**.

- [ ] **Step 5b: Update `CLAUDE.md`'s Architecture section — orphaned in rev 1, owned here**

Two statements there become false the moment this task lands, and **no task claimed them**:

- the `Rendering/` bullet's "one Scriban template, `Templates/mstest-class.scriban`. This is the only
  place MSTest code shape is decided" — now two templates, and the sentence's point (one place per
  framework) needs restating rather than deleting;
- the paragraph stating `TemplateRenderer` "still hardcodes `mstest-class.scriban` regardless of the
  value" and that "nothing yet *branches* on it" — this task is what makes that untrue.

**Touch only the Architecture section.** Task 3 owns `CLAUDE.md`'s Commands section (the
"all four suites" line) and Task 10 already changed the package count and the "MSTest only"
constraint. Staying in your own section is what keeps these merges clean.

- [ ] **Step 6: Commit**

```bash
git add src/InTest.Cli tests/InTest.Cli.Tests CLAUDE.md
git commit -m "feat: add the xUnit template and select by project.framework

[framework-selects-template]. Two templates rather than one branching internally.

All five TestContext.CancellationToken sites become TestContext.Current.CancellationToken —
Xunit.TestContext has no static CancellationToken, which is the fact that forces v3 over v2."
```

---

### Task 7: Text-safety and golden coverage for the second template

**Files:**
- Modify: `tests/InTest.Cli.Tests/TemplateEscapingGuardTests.cs`
- Create: `tests/InTest.Golden.Tests/Expected/OrdersTests.xunit.g.cs.txt`
- Modify: `tests/InTest.Golden.Tests/GoldenFileTests.cs`

- [ ] **Step 1: Make the escaping guard read both templates**

`TemplateEscapingGuardTests` parses the template and classifies each `tc.<name>` by quote parity, mechanically enforcing one of the three text-safety rules `CLAUDE.md` calls non-negotiable. Its `LoadEmbeddedTemplate("mstest-class.scriban")` call is at `:97`.

**A second template the guard does not read has no text-safety enforcement at all, and nothing fails to tell you.** Convert the test to run over both template names — a `[DataRow("mstest-class.scriban")]` / `[DataRow("xunit-class.scriban")]` pair is the smallest change that keeps the existing logic.

**Use `[TestMethod]` + `[DataRow]`, not `[DataTestMethod]`.** The latter is analyzer-obsolete
(MSTEST0044) and this repo builds with `TreatWarningsAsErrors`, so it will not compile. Two
independent agents hit this — in this task and in Task 9. `NeutralityTests.cs:245-246` is the
established convention to copy.

- [ ] **Step 2: Prove the guard now covers the new template**

Temporarily add `"{{ tc.expected_status }}"` — quoted, which is wrong for a bare field — to `xunit-class.scriban`. Run:

```bash
dotnet test tests/InTest.Cli.Tests --filter "FullyQualifiedName~TemplateEscapingGuardTests"
```

Expected: **FAIL**, naming `expected_status`. Revert and confirm green. **If it passes with the mutation in place, the guard is not reading the new file** — stop and fix that before continuing.

- [ ] **Step 3: Add the second golden expectation file — and a `mutates` case with it**

**The Orders spec has no `mutates` case, so the golden file cannot catch the `[DoNotParallelize]`
substitution Task 6 adds.** That attribute is emitted per-method and conditionally; with no case
setting `mutates`, `Expected/OrdersTests.g.cs.txt` contains zero occurrences and neither will its
xUnit counterpart. The substitution would be unverified by exactly the artifact meant to verify it.

Add a `mutates` case to the xUnit golden coverage — either by extending the fixture spec used here or
by adding a dedicated `CompileVerificationTests` shape in Task 8 — so that
`[Fact(DisableParallelization = true)]` appears in checked-in output at least once.



`GoldenFileTests` (3 tests: `OutputMatchesTheGoldenFile`, `GenerationIsDeterministic`, `EveryCaseIsCategorizedContract`) pins the template's byte-for-byte output. Extend it to cover the xUnit template against a new `Expected/OrdersTests.xunit.g.cs.txt`, generated through the same `INTEST_UPDATE_GOLDEN=1` path.

```bash
INTEST_UPDATE_GOLDEN=1 dotnet test tests/InTest.Golden.Tests --filter "FullyQualifiedName~OutputMatchesTheGoldenFile"
```

Expected: **Inconclusive** — it writes the source copy and refuses to claim success. Then re-run without the variable and expect **PASS**.

- [ ] **Step 4: Read the new golden file**

```bash
git diff --stat tests/InTest.Golden.Tests/Expected/
cat tests/InTest.Golden.Tests/Expected/OrdersTests.xunit.g.cs.txt
```

Confirm by eye: `[Fact]` and `[Trait(...)]` present, no `[TestClass]`/`[TestMethod]`, `TestContext.Current.CancellationToken` at every site, and — most importantly — **`FixtureParameter(...)` only on Success cases with `Guid.NewGuid().ToString()` on their 401/403/404 siblings for the same path.** Role gating is a planner decision the template only interpolates; if it differs between the two golden files, the xUnit template re-derived something it should have passed through, and that is a stop-and-report.

- [ ] **Step 5: Commit**

```bash
git add tests/InTest.Cli.Tests tests/InTest.Golden.Tests
git commit -m "test: text-safety and golden coverage for the xUnit template

TemplateEscapingGuardTests now reads both templates — a second template it does not read has
no text-safety enforcement at all, and nothing fails to announce that.

Adds the second golden expectation file. Role gating verified identical across both:
FixtureParameter on Success cases, unmatchable values on their error siblings."
```

---

### Task 8: xUnit cases in the Golden matrix

Per `[matrix-stays-representative]`, run under xUnit only the shapes whose rendering or runtime behaviour differs. The framework-independent long tail — integer path parameters, the NSwag convention call, query composition — stays MSTest-only, because those exercise `TestPlanBuilder`/`TemplateRenderer` logic that is framework-independent by construction.

**Files:**
- Modify: `tests/InTest.Golden.Tests/CompileVerificationTests.cs`, `GeneratedSuiteExecutionTests.cs`, `ScaffoldCompileVerificationTests.cs`

**Task 7 has already created `tests/InTest.Golden.Tests/Specs/mutating-operation.json`** and golden
files for it under both frameworks, so the `mutates` shape is covered from checked-in output. Do not
duplicate it here — use it if you need a mutating case, and spend this task's budget on the shapes
that are still uncovered.

- [ ] **Step 0: Parameterise `CompileVerificationTests.CreateProject` on three things, not one**

Rev 1 implied only the `.csproj` varies. Measured, an xUnit variant needs three:

- the `.csproj` string (packages, `<OutputType>Exe</OutputType>`),
- **the `AssemblyInfo.cs` string** — `[assembly: DoNotParallelize]` versus
  `[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]`,
- **`intest.json`'s `"framework"` value**, which requires Task 4's `ConfigLoader` change to already
  be in place.

So the signature becomes `CreateProject(string specFileName, string framework = "mstest")`, and the
`AssemblyInfo.cs` one is the parameterisation rev 1 missed entirely — without it the xUnit project
carries an MSTest assembly attribute and does not compile.

- [ ] **Step 1: Add the xUnit cases**

Five, each with a stated reason:

| case | why it must be xUnit |
|---|---|
| A raw contract case | the base shape: attributes, lifecycle, `TestContext.Current` |
| **A client-routed case** | a distinct body (`ApiClient<T>()`, two `catch … when` filters, `ShouldMatchCapturedContractAsync`) carrying 2 of the 5 token sites — the sites that are the entire justification for `[v3-only]` |
| An auth case using the skip path | `Assert.Skip` versus `Assert.Inconclusive`, and the trx outcome |
| **`ValidationReportWithAProblemSurfacesOnAPassingRun`** | the only test proving the `Warn` contract, which is exactly what `[warn-needs-a-real-sink]` shows the obvious mapping breaks |
| **Hostile spec text** | **the stated reason was wrong** — see below |

**Two corrections to how an xUnit run is asserted on.**

**(1) There is exactly ONE `test.Output.ShouldContain("Passed!")` assertion in the repository** (11
`test.Output.Should*` assertions in total). Any larger number you may have been told is wrong. The
xUnit runner prints no `Passed!` string at all — its summary is `=== TEST EXECUTION SUMMARY ===`
followed by `Total: N, Errors: 0, Failed: 0, …`. **The per-framework equivalent is not a per-test
string:** assert on `Failed: 0` plus the process exit code, which is 0 on success and 1 on failure
for both runners.

**(2) On the hostile-spec-text case.** Rev 1 justified it by saying the text reaches the trx `testName`
attribute and the runner's console line. **That is false under this plan's own recommended Task 6
mapping** — `[Trait("Description", …)]` keeps the description out of the display name entirely, so
hostile text never reaches either. Keep the case, but on its real merit: it proves
`CSharpLiteral.Escape`'s output is valid C# inside the *second* template's literal sites. That is a
compile-only concern, so it belongs in `CompileVerificationTests` rather than in the
run-against-a-stub set. **If Task 6 instead chooses `[Fact(DisplayName = …)]`, rev 1's justification
becomes true and the case moves back** — the two decisions are coupled.

- [ ] **Step 2: Add the xUnit scaffold-compile case**

`ScaffoldCompileVerificationTests` calls `InitCommand.Run` and builds the raw scaffold. It is **the only test in the repository that compiles scaffold output at all**, and the scaffold is what changes most for xUnit. Add an xUnit counterpart.

It catches `<OutputType>Exe</OutputType>`, the assembly-fixture file and the package set. **It cannot catch the parallelism attribute** — a missing attribute compiles fine; Task 5's text assertion is what covers that.

- [ ] **Step 3: Run the suite**

```bash
dotnet test tests/InTest.Golden.Tests
```

Expected: **PASS**. Read the timing figure from `CLAUDE.md`, pass a generous timeout, run it in the **foreground**. Report the new wall-clock time — this task is the one that grows it, and the next reader needs a real number rather than a prediction.

- [ ] **Step 4: Commit**

```bash
git add tests/InTest.Golden.Tests
git commit -m "test: xUnit cases in the Golden matrix

[matrix-stays-representative]: the shapes whose rendering or runtime behaviour differs, not
every shape. Includes the client-routed case (which carries the token sites that justify
v3-only), the Warn-contract test, hostile spec text (which reaches the trx testName the
harness parses), and the scaffold compile."
```

---

### Task 9: Ship the fourth package

**Files:**
- Modify: `scripts/ci/pack-and-verify.ps1`, `scripts/local-e2e-test.ps1`,
  `.github/workflows/release.yml`, `.github/workflows/pack.yml`
- Modify: `tests/InTest.Architecture.Tests/NeutralityTests.cs`, `PackageVersionCouplingTests.cs`

- [ ] **Step 1: Extend the packaging scripts — including the asset-count check that fails AFTER publishing**

**`release.yml` hardcodes an exact release-asset count of 6, and that check runs *after*
`dotnet nuget push`.** With a fourth package it becomes 8. Left unchanged, the push succeeds — putting
artifacts on nuget.org permanently, since a version can never be re-pushed — and *then* the job fails
on the count. This is the only irreversible failure mode in the whole plan, and rev 1 did not mention
it.

Change `-ne 6` to `-ne 8` (around line 378) and update the error string to name
`InTest.Runtime.xUnit`. **Prefer deriving the count from a package-id list** so a fifth package cannot
repeat this. `CONTRIBUTING.md` carries the same number in prose (Task 10) — the two must agree.



`pack-and-verify.ps1` packs three projects by explicit path (`:136-138`) and hardcodes an `MSTest.TestFramework` positive control (`:386-391`). `release.yml:206` and `pack.yml` name the three packages explicitly.

Add the fourth to each. The positive control for the xUnit adapter is that its packed nuspec declares `xunit.v3.extensibility.core` — the same shape as the MSTest one.

- [ ] **Step 1b: `scripts/local-e2e-test.ps1` — orphaned in rev 1, owned here**

The design says this script "still needs changes for the fourth package and `--framework`", and **no
task claimed it**. It packs the CLI and runtime locally and drives a scaffold end to end, so it must
learn the fourth package and be able to scaffold an xUnit project.

**It does not carry the VSTest assumption** — its own header states three times that `dotnet test` is
*"deliberately out of scope"* (`:61`, `:76`, `:81`) and it only runs `dotnet build` on the scaffold
(`:431`). So this is a packaging and `--framework` change, not a runner port. Do not "fix" a `dotnet
test` problem it does not have.

- [ ] **Step 2: Extend `NeutralityTests`**

`AdapterPackageDeclaresItsTestFramework` (`:229-260`) hardcodes the `InTest.Runtime.MSTest` csproj path. **It will pass vacuously for the new package** — it simply never looks at it. Parameterise it over both adapters.

- [ ] **Step 3: Prove both guards fail when they should — from a clean output directory**

**`pack-and-verify.ps1` never cleans `$OutputDir`, so a stale `.nupkg` from an earlier run satisfies
the check even with the pack step deleted.** The mutation would pass spuriously and certify a guard
that does not guard. Use a **fresh, empty `-OutputDir`** for the mutation run, or add a
`Remove-Item -Recurse $OutputDir/*.nupkg` at the top of the script. The failure text to report is
`No .nupkg in <dir> has a nuspec <id> of 'InTest.Runtime.xUnit'`.



Temporarily remove `InTest.Runtime.xUnit` from `pack-and-verify.ps1`'s project list and confirm its verification step fails. Temporarily remove the `xunit.v3.extensibility.core` reference from the adapter csproj and confirm `NeutralityTests` fails. Restore both, and report the failure text for each.

- [ ] **Step 4: Extend `PackageVersionCouplingTests`**

**This guard goes red at Task 5 and stays red until here — and Task 5's own verification would not
catch it**, because Task 5 runs only `tests/InTest.Cli.Tests`. Either move the
`RuntimeSelfVersionedPackage` change forward into Task 5, or add
`dotnet test tests/InTest.Architecture.Tests` to Task 5 Step 5 with an explicit note that it is
expected red until this task closes it. Say plainly which you chose.

Note also that `RuntimeSelfVersionedPackage` is a **scalar const, not a set** — it must become a
collection to name both adapters, which is a change of shape rather than a change of value.



Third-party versions are duplicated by design across `Directory.Packages.props`, the scaffolded `.csproj` string in `InitCommand.cs`, and the hand-written project in `CompileVerificationTests.cs`. xUnit's packages join that three-way rule. The `InTest.Runtime.xUnit` reference itself is checked the *other* way, like the MSTest adapter — no `Directory.Packages.props` entry, so the guard confirms the scaffold interpolates `CliVersion.Current` rather than a literal.

- [ ] **Step 5: Run and commit**

```bash
dotnet test tests/InTest.Architecture.Tests
pwsh scripts/ci/pack-and-verify.ps1
```

```bash
git add scripts .github tests/InTest.Architecture.Tests
git commit -m "build: ship InTest.Runtime.xUnit as the fourth package

pack-and-verify.ps1, release.yml and pack.yml each named three packages explicitly, so a
fourth would have shipped unverified. NeutralityTests hardcoded the MSTest adapter path and
would have passed vacuously for the new one."
```

---

### Task 10: Documentation and the release gate

**Files:**
- Modify: `README.md`, `CLAUDE.md`, `docs/getting-started.md`,
  `docs/superpowers/specs/2026-08-16-intest-api-test-generator-design.md` (§5), `CONTRIBUTING.md`

**`README.md` and `CLAUDE.md` were owned by no task in rev 1.** `README.md:81`'s Test framework row is
the most visible statement that InTest is MSTest-only, and after this change it tells every visitor
the opposite of the truth. `CLAUDE.md` carries both "Three shipped packages" and the "MSTest only"
non-negotiable. Neither is under `docs/`, so rev 1's `git add docs CONTRIBUTING.md` could not even
stage them.

- [ ] **Step 1: Document the adoption path — nine sites, not two**

Rev 1 named only the profile mechanism and the `TestTimeout` gap. `docs/getting-started.md` assumes
MSTest in at least nine places, three of which become **actively wrong** rather than merely
incomplete. Enumerate them by line so none is missed: `:49` prerequisites row, `:153-158` the Phase 2
file table (three rows differ), `:200-206` profile precedence, `:443` the `TestHost` package name,
`:651` Phase 6's run command (a direct `dotnet <dll>`, not `dotnet test`).



`docs/getting-started.md` gains the xUnit path: `init --framework xunit`, what differs in the scaffold, and — explicitly — that **`INTEST_PROFILE` is the profile mechanism for xUnit projects**, because there is no runsettings equivalent. Be accurate about the size of that gap: the MSTest scaffold's `<Parameter name="profile">` line is **commented out by design**, so `INTEST_PROFILE` is already the primary mechanism there too. xUnit loses an opt-in, not a default.

Also note that `<MSTest><TestTimeout>60000</TestTimeout></MSTest>` has no xUnit equivalent — `[Fact(Timeout = …)]` is per-test, not a global default.

- [ ] **Step 2: Update §5 — the table *and* the prose around it**

The command-surface table gains `--framework` on `init`, and its **Writes** column must lose
`*.runsettings` for xUnit projects.

**But the frozen-axes prose 40 lines above it, inside the same §5, becomes factually false the moment
`xunit` is accepted** (lines ~450 and ~464-467). It says the framework message is unreachable because
one framework ships. `[frozen-axis-becomes-reachable]` changes that: say the message is now reachable
and name `generate`'s refusal as its enforcement. §2's "Deferred to v2" line for a second framework
also needs revisiting.



The 2026-08-16 spec's §5 command-surface table is the semver contract for the CLI. Add `--framework` to `init`'s flags, and correct its **Writes** column — it lists `*.runsettings`, which an xUnit project does not get.

- [ ] **Step 3: Update every hardcoded package count in `CONTRIBUTING.md` — there are five**

There is no single "package list" step to edit. Rev 1's wording would have left four sites still
saying "three packages". Name all five and give the new numbers: step 9's "three packages, six files"
becomes four packages / eight files; line ~513's "three-package, six-artifact shape" becomes
four/eight; step 11 gains `InTest.Runtime.xUnit`; and the remaining two counts follow.

**The eight-file number must match `release.yml`'s asset-count check from Task 9 Step 1.** They are
the same fact in two places, and this is where they are reconciled.



`CONTRIBUTING.md`'s publishing checklist gains `InTest.Runtime.xUnit` to the package list, and a note that the `InTest.` prefix is still unreserved on nuget.org, so the new id can be squatted before first push.

- [ ] **Step 4: Full verification**

```bash
dotnet build InTest.sln
dotnet test tests/InTest.Cli.Tests
dotnet test tests/InTest.Runtime.Tests
dotnet test tests/InTest.Architecture.Tests
dotnet test tests/InTest.Golden.Tests
./tests/InTest.Runtime.XUnit.Tests/bin/Debug/net10.0/InTest.Runtime.XUnit.Tests.exe
```

**Five commands, not `dotnet test InTest.sln`** — a solution-level run now fails with both frameworks present.

- [ ] **Step 5: Commit**

```bash
git add README.md CLAUDE.md docs CONTRIBUTING.md
git commit -m "docs: xUnit adoption path, command surface, release gate

§5's command table is the CLI's semver contract, so --framework lands there in the same
change — including its Writes column, which lists a runsettings file xUnit projects do not get."
```

---

## Verification

The load-bearing proofs, in the order they become available:

1. **Task 1** — the harness runs both frameworks, with every existing MSTest case still green.
2. **Task 3** — the adapter's own suite, running as a direct exe.
3. **Task 7** — text-safety enforced on both templates, proven by mutation; role gating identical across both golden files.
4. **Task 8** — generated xUnit code both compiles and runs against a stub.
5. **A live acceptance run** against the Orders sample, matching the MSTest run's discipline: **assert per-assembly counts and skip reasons, not the total** — noting xUnit puts a skip reason in `<StdOut>` rather than `<Message>`.

**Do not restate a Golden timing figure in this plan.** Read it from `CLAUDE.md` when you run it. Four copies of that number have gone stale across this project's documents already.

## Out of scope

- **NUnit.** Its equivalents are known, but its cancellation and parallelism defaults are unresearched and must not be assumed to mirror xUnit's.
- **xUnit v2.** No `TestContext.CancellationToken` equivalent; supporting it means inventing a mechanism.
- **Making `dotnet test` work for xUnit.** Measured as broken across three SDK versions, with an MSTest control failing identically. The direct-exe path needs no opt-in.
- **General frozen-axis enforcement.** Identifier naming is also frozen and also unenforced. Task 4 covers the framework axis because this change makes it reachable; the rest is pre-existing debt.

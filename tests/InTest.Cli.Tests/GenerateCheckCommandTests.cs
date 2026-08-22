using System.Text;
using InTest.Cli;
using InTest.Cli.Commands;
using Shouldly;

namespace InTest.Cli.Tests;

/// <summary>
/// Task 3 of the v1-e plan: `generate --check`. Exercises the same command as
/// <see cref="GenerateCommandTests"/>, with <c>check: true</c>, against the exit-code table §5
/// and the plan's Task 3 Step 2 both specify:
/// <list type="bullet">
/// <item>0 — everything matches a fresh render</item>
/// <item>1 — any file differs, a file exists that a fresh render does not produce, or fixture
/// drift (§5's own text for exit 1 already lists both "fixture drift" and "`--check`
/// differences" as members of one code — see the comment above <c>GenerateCommand.RunAsync</c>'s
/// drift branch for why one number carries two distinct meanings here on purpose)</item>
/// <item>2 — malformed config, unreadable spec, crash (unchanged from plain `generate`)</item>
/// <item>4 — <c>intestVersion</c> present and not equal to the running tool, checked before any
/// output comparison ([exact-match])</item>
/// </list>
/// Every test in this class that reports a difference or a mismatch also asserts the project was
/// not written to — [no-write]'s actual guarantee, not merely its exit code.
/// </summary>
[TestClass]
public class GenerateCheckCommandTests
{
    // Same shape as GenerateCommandTests.Spec: a single GET with a path parameter and no request
    // body, so FixtureComposer.NeedsFixture is false and no committed fixture is required — the
    // fixture-drift path is exercised separately, by its own spec, below.
    private const string Spec = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": { "/orders/{id}": { "get": { "operationId": "getOrderById", "tags": ["Orders"],
        "responses": { "200": { "description": "ok", "content": { "application/json": {
          "schema": { "$ref": "#/components/schemas/Order" } } } } } } } },
      "components": { "schemas": { "Order": { "type": "object" } } }
    }
    """;

    // Two tags, so removing one tag's only operation orphans its whole class file while leaving
    // the other byte-identical — the shape Task 6 Step 3 documents as the only way to reach the
    // stale-file case without also tripping the "any file differs" case first.
    private const string TwoTagSpec = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": {
        "/orders/{id}": { "get": { "operationId": "getOrderById", "tags": ["Orders"],
          "responses": { "200": { "description": "ok" } } } },
        "/customers/{id}": { "get": { "operationId": "getCustomerById", "tags": ["Customers"],
          "responses": { "200": { "description": "ok" } } } }
      }
    }
    """;

    // Every operation with a customers tag removed — leaves OrdersTests.g.cs byte-identical to
    // TwoTagSpec's render and drops CustomersTests.g.cs from the fresh render entirely.
    private const string OneTagSpec = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": {
        "/orders/{id}": { "get": { "operationId": "getOrderById", "tags": ["Orders"],
          "responses": { "200": { "description": "ok" } } } }
      }
    }
    """;

    // A request body, so FixtureComposer.NeedsFixture is true and no fixture exists — the same
    // scenario GenerateDriftTests pins for plain `generate`, reused here to pin the decision that
    // --check shares the same drift check rather than skipping it.
    private const string SpecNeedingAFixture = """
    {
      "openapi":"3.0.3","info":{"title":"T","version":"1"},
      "paths":{"/api/products":{"post":{
        "operationId":"createProduct",
        "requestBody":{"content":{"application/json":{"schema":{"type":"object",
          "required":["sku"],"properties":{"sku":{"type":"string"}}}}}},
        "responses":{"201":{"description":"ok"}}}}}
    }
    """;

    private string _root = null!;

    [TestInitialize]
    public void CreateProject()
    {
        _root = Path.Combine(Path.GetTempPath(), "intest-check-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        WriteSpec(Spec);
        WriteConfig(withIntestVersion: null);
    }

    [TestCleanup]
    public void RemoveProject()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void WriteSpec(string spec) => File.WriteAllText(Path.Combine(_root, "orders.json"), spec);

    private void WriteConfig(string? withIntestVersion)
    {
        var intestVersionLine = withIntestVersion is null
            ? string.Empty
            : $"\"intestVersion\": \"{withIntestVersion}\", ";

        File.WriteAllText(Path.Combine(_root, "intest.json"), $$"""
        { "schemaVersion": 1, {{intestVersionLine}}"spec": { "source": "orders.json" },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);
    }

    private static Task<int> GenerateAsync(string root, bool check = false) =>
        GenerateCommand.RunAsync(root, CancellationToken.None, check: check);

    private static async Task<(int ExitCode, string Report)> CheckAsync(string root)
    {
        var report = new StringWriter();
        var exitCode = await GenerateCommand.RunAsync(root, CancellationToken.None, report, check: true);
        return (exitCode, report.ToString());
    }

    /// <summary>
    /// Every file under the whole project root, with its current bytes — used to prove --check
    /// left all of them exactly as they were, not merely that the same file names still exist.
    /// Walks the whole tree, not just <c>Generated/</c> and <c>coverage-report.json</c>: a stray
    /// file written anywhere else in the project — <c>&lt;projectRoot&gt;/intest-check-scratch.tmp</c>
    /// is [no-write]'s own example of "the most likely accidental form" — is just as much a
    /// [no-write] violation as one written under <c>Generated/</c>, and a narrower snapshot
    /// cannot see it. The config, spec, and scaffolded files under the root are all stable across
    /// a --check run, so no exclusions are needed.
    /// </summary>
    private Dictionary<string, string> SnapshotOwnedFiles()
    {
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            snapshot[file] = File.ReadAllText(file);
        }

        return snapshot;
    }

    /// <summary>
    /// The plan's "wrote nothing" assertion, factored out because every test that reports a
    /// difference under --check needs it. What it proves: the file **set** under the whole
    /// project root is unchanged (no file created, deleted, or renamed anywhere in the project,
    /// per <see cref="SnapshotOwnedFiles"/>) and every previously-existing file's bytes are
    /// unchanged. What it does NOT prove, and cannot: it cannot distinguish "never wrote" from
    /// "wrote and restored to the exact original name and bytes" — a before/after snapshot has no
    /// way to observe a write that happens in between and leaves no trace, which is what "restore
    /// exactly" means. Only a mid-run observer (a FileSystemWatcher, a read-only ACL) could catch
    /// that shape; see the [no-write] seam comment above <c>GenerateCommand.BuildOutputs</c> for
    /// why that shape is instead ruled out by construction rather than by this assertion.
    /// </summary>
    private void AssertOwnedFilesUntouched(Dictionary<string, string> before)
    {
        var after = SnapshotOwnedFiles();
        after.Keys.ShouldBe(before.Keys, ignoreOrder: true,
            customMessage: "--check must not create, delete, or rename any file generate owns");
        foreach (var (path, content) in before)
        {
            after[path].ShouldBe(content, customMessage: $"--check must not modify {path}");
        }
    }

    // ---- 0: everything matches -----------------------------------------------------------

    [TestMethod]
    public async Task ReturnsOkWhenCommittedOutputMatchesAFreshRender()
    {
        (await GenerateAsync(_root)).ShouldBe(ExitCode.Ok);

        var before = SnapshotOwnedFiles();
        var (exitCode, _) = await CheckAsync(_root);

        exitCode.ShouldBe(ExitCode.Ok);
        AssertOwnedFilesUntouched(before);
    }

    [TestMethod]
    public async Task SkipsTheVersionCheckWhenIntestVersionIsAbsent()
    {
        // WriteConfig(null) in CreateProject already omits intestVersion. [read-what-init-wrote]:
        // absent means no claim made, so --check must still run the output comparison rather than
        // failing or silently passing without checking anything.
        (await GenerateAsync(_root)).ShouldBe(ExitCode.Ok);

        var (exitCode, _) = await CheckAsync(_root);

        exitCode.ShouldBe(ExitCode.Ok);
    }

    [TestMethod]
    public async Task TreatsAMatchingIntestVersionAsNoMismatch()
    {
        WriteConfig(withIntestVersion: CliVersion.Current);
        (await GenerateAsync(_root)).ShouldBe(ExitCode.Ok);

        var (exitCode, _) = await CheckAsync(_root);

        exitCode.ShouldBe(ExitCode.Ok);
    }

    // ---- 1: any file differs ---------------------------------------------------------------

    /// <summary>
    /// Finding 1: the comparison must be bytes, not text. File.ReadAllTextAsync performs
    /// BOM-based encoding detection, so prepending a UTF-8 BOM to a committed file decodes back
    /// to a string equal to a fresh render's — a text-based compare reports "match" (exit 0) for
    /// a file `generate` would rewrite without the BOM. Before this fix that was exactly what
    /// happened; File.ReadAllBytesAsync sees the extra three bytes and reports differs (exit 1).
    /// </summary>
    [TestMethod]
    public async Task ReturnsWorkOutstandingWhenAGeneratedFileGainsAUtf8Bom()
    {
        (await GenerateAsync(_root)).ShouldBe(ExitCode.Ok);

        var path = Path.Combine(_root, "Generated", "OrdersTests.g.cs");
        var originalBytes = File.ReadAllBytes(path);
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        File.WriteAllBytes(path, bom.Concat(originalBytes).ToArray());

        var (exitCode, report) = await CheckAsync(_root);

        exitCode.ShouldBe(ExitCode.WorkOutstanding);
        report.ShouldContain("Generated/OrdersTests.g.cs", Case.Sensitive);
        report.ShouldContain("differs from a fresh render");
    }

    /// <summary>
    /// Finding 1's second shape: re-encoding a committed file as UTF-16LE produces a file that is
    /// byte-for-byte unrecognisable (roughly double the size) yet still decodes, via
    /// File.ReadAllTextAsync's BOM sniffing, to a string equal to a fresh render's — the same
    /// silently-permissive gap as the BOM case, reached by a different route. Bytes catch it;
    /// text does not.
    /// </summary>
    [TestMethod]
    public async Task ReturnsWorkOutstandingWhenAGeneratedFileIsReencodedAsUtf16()
    {
        (await GenerateAsync(_root)).ShouldBe(ExitCode.Ok);

        var path = Path.Combine(_root, "Generated", "OrdersTests.g.cs");
        var originalText = File.ReadAllText(path);
        File.WriteAllText(path, originalText, Encoding.Unicode);

        var (exitCode, report) = await CheckAsync(_root);

        exitCode.ShouldBe(ExitCode.WorkOutstanding);
        report.ShouldContain("Generated/OrdersTests.g.cs", Case.Sensitive);
        report.ShouldContain("differs from a fresh render");
    }

    [TestMethod]
    public async Task ReturnsWorkOutstandingWhenCoverageReportDiffers()
    {
        (await GenerateAsync(_root)).ShouldBe(ExitCode.Ok);

        // info.title flows only into coverage-report.json's "title" field (CoverageReport.cs) —
        // every Generated/*.g.cs file is unaffected, so this isolates "coverage-report.json is
        // compared" from "Generated/ is compared" instead of changing both at once.
        WriteSpec(Spec.Replace("\"title\": \"Orders\"", "\"title\": \"Orders v2\""));

        // Snapshotted after the test's own WriteSpec, not before: SnapshotOwnedFiles now walks
        // the whole project root (finding 2), which includes orders.json itself — snapshotting
        // beforehand would capture the pre-edit spec and then flag this test's own deliberate
        // mutation as if --check had made it.
        var before = SnapshotOwnedFiles();
        var (exitCode, report) = await CheckAsync(_root);

        exitCode.ShouldBe(ExitCode.WorkOutstanding);
        report.ShouldContain("coverage-report.json", Case.Sensitive);
        report.ShouldContain("differs from a fresh render");
        report.ShouldNotContain("OrdersTests.g.cs",
            customMessage: "a title-only spec change must not also claim the untouched class file differs");
        AssertOwnedFilesUntouched(before);
    }

    [TestMethod]
    public async Task ReturnsWorkOutstandingWhenAGeneratedClassDiffers()
    {
        (await GenerateAsync(_root)).ShouldBe(ExitCode.Ok);

        // A different operationId changes the rendered method name in OrdersTests.g.cs.
        WriteSpec(Spec.Replace("getOrderById", "fetchOrderById"));

        // See the comment in ReturnsWorkOutstandingWhenCoverageReportDiffers: snapshotted after
        // this test's own WriteSpec, now that SnapshotOwnedFiles walks the whole project root.
        var before = SnapshotOwnedFiles();
        var (exitCode, report) = await CheckAsync(_root);

        exitCode.ShouldBe(ExitCode.WorkOutstanding);
        report.ShouldContain("Generated/OrdersTests.g.cs", Case.Sensitive);
        report.ShouldContain("differs from a fresh render");
        AssertOwnedFilesUntouched(before);
    }

    [TestMethod]
    public async Task ReturnsWorkOutstandingWhenAFileIsMissing()
    {
        (await GenerateAsync(_root)).ShouldBe(ExitCode.Ok);
        File.Delete(Path.Combine(_root, "Generated", "spec-schemas.json"));
        var before = SnapshotOwnedFiles();

        var (exitCode, report) = await CheckAsync(_root);

        exitCode.ShouldBe(ExitCode.WorkOutstanding);
        report.ShouldContain("Generated/spec-schemas.json", Case.Sensitive);
        report.ShouldContain("is missing");
        AssertOwnedFilesUntouched(before);
    }

    // ---- 1: a file exists that a fresh render does not produce (the silently-permissive case) -

    [TestMethod]
    public async Task ReturnsWorkOutstandingForAFileAFreshRenderNoLongerProduces()
    {
        WriteSpec(TwoTagSpec);
        (await GenerateAsync(_root)).ShouldBe(ExitCode.Ok);
        File.Exists(Path.Combine(_root, "Generated", "CustomersTests.g.cs")).ShouldBeTrue();

        // Removes every /customers operation, so the whole CustomersTests class disappears from
        // a fresh render — not merely one operation inside a class whose file still exists, which
        // is Task 6 Step 3's distinction: deleting one path can leave the same filename present
        // with different content (the "any file differs" case), never exercising this branch.
        WriteSpec(OneTagSpec);
        var before = SnapshotOwnedFiles();

        var (exitCode, report) = await CheckAsync(_root);

        exitCode.ShouldBe(ExitCode.WorkOutstanding);
        report.ShouldContain("Generated/CustomersTests.g.cs", Case.Sensitive);
        report.ShouldContain("a fresh render does not produce it");
        report.ShouldNotContain("Generated/OrdersTests.g.cs differs",
            customMessage: "OrdersTests.g.cs is byte-identical between TwoTagSpec and OneTagSpec " +
                           "and must not be reported as differing just because a sibling was orphaned");
        AssertOwnedFilesUntouched(before);
    }

    /// <summary>
    /// [paired]: the orphan sweep above exists only because plain `generate` clears an orphan by
    /// deleting Generated/ wholesale rather than reconciling file-by-file — a documented gate
    /// whose only prescribed fix is `intest generate`. Nothing before this test proved that fix
    /// actually works: removing GenerateCommand's <c>Directory.Delete(generated, recursive:
    /// true)</c> entirely still passes all four suites, because no test ran `generate` on a
    /// project --check had just flagged as orphaned. This one does both halves: reproduce the
    /// orphan, then run `generate` and assert it is gone and --check reports clean afterward. If
    /// the wholesale delete were removed or narrowed to skip files a fresh render doesn't
    /// reproduce, CustomersTests.g.cs would still exist after `generate` runs and this test would
    /// fail — the shape [paired] exists to prevent: a documented gate with an unreachable fix.
    /// </summary>
    [TestMethod]
    public async Task GenerateClearsTheOrphanThatCheckReported()
    {
        WriteSpec(TwoTagSpec);
        (await GenerateAsync(_root)).ShouldBe(ExitCode.Ok);
        WriteSpec(OneTagSpec);
        (await CheckAsync(_root)).ExitCode.ShouldBe(ExitCode.WorkOutstanding);

        (await GenerateAsync(_root)).ShouldBe(ExitCode.Ok);

        File.Exists(Path.Combine(_root, "Generated", "CustomersTests.g.cs")).ShouldBeFalse(
            "generate's wholesale delete of Generated/ is what clears an orphan --check reported");
        (await CheckAsync(_root)).ExitCode.ShouldBe(ExitCode.Ok);
    }

    /// <summary>
    /// Pins <c>SearchOption.AllDirectories</c> in the orphan sweep (<c>GenerateCommand.
    /// RunCheckAsync</c>): a stray file nested under a subdirectory of Generated/ is exactly as
    /// much an orphan as one sitting directly in Generated/, and every artefact BuildOutputs
    /// renders today happens to be flat, so nothing else in this suite would notice
    /// <c>TopDirectoryOnly</c> silently skipping a nested file. Mutating the sweep to
    /// TopDirectoryOnly makes this test fail (exit 0 instead of 1, no mention of the nested
    /// file) while every other test in this class still passes.
    /// </summary>
    [TestMethod]
    public async Task ReturnsWorkOutstandingForAStrayFileInASubdirectoryOfGenerated()
    {
        (await GenerateAsync(_root)).ShouldBe(ExitCode.Ok);

        var nestedDir = Path.Combine(_root, "Generated", "nested");
        Directory.CreateDirectory(nestedDir);
        File.WriteAllText(Path.Combine(nestedDir, "stray.g.cs"), "// stray");

        var (exitCode, report) = await CheckAsync(_root);

        exitCode.ShouldBe(ExitCode.WorkOutstanding);
        report.ShouldContain("Generated/nested/stray.g.cs", Case.Sensitive);
        report.ShouldContain("a fresh render does not produce it");
    }

    /// <summary>
    /// The half-message case: <see cref="File.Exists"/> matches case-insensitively on Windows, so
    /// renaming OrdersTests.g.cs to only change its casing still passes the content-compare loop
    /// (it opens the same file) and is caught by the orphan sweep instead — but the sweep only
    /// ever had the on-disk (wrong) casing to report until this fix, so the message named a file
    /// that "does not exist" without ever saying what name was expected. Pins that the message
    /// now names both.
    /// </summary>
    [TestMethod]
    public async Task NamesTheExpectedCasingWhenOnlyCasingDiffersFromAFreshRender()
    {
        (await GenerateAsync(_root)).ShouldBe(ExitCode.Ok);
        File.Move(
            Path.Combine(_root, "Generated", "OrdersTests.g.cs"),
            Path.Combine(_root, "Generated", "orderstests.g.cs"));

        var (exitCode, report) = await CheckAsync(_root);

        exitCode.ShouldBe(ExitCode.WorkOutstanding);
        // Shouldly's ShouldContain(string) is case-insensitive by default, which would make this
        // assertion pass whether or not the expected casing is actually named — Case.Sensitive is
        // required here, or a mutation that deletes the fix silently keeps this test green.
        report.ShouldContain("orderstests.g.cs", Case.Sensitive);
        report.ShouldContain("OrdersTests.g.cs", Case.Sensitive,
            customMessage: "the message must name the expected casing, not just flag the wrong one");
    }

    /// <summary>
    /// Finding 2's mutation proof: before SnapshotOwnedFiles walked the whole project root, a
    /// stray file written directly under the project root — [no-write]'s own example of "the
    /// most likely accidental form" — was invisible to AssertOwnedFilesUntouched, because the
    /// snapshot only ever covered Generated/ and coverage-report.json. This test simulates that
    /// shape directly (rather than requiring a buggy RunCheckAsync to exist) by writing the stray
    /// file itself and asserting the guard now notices. Reverting SnapshotOwnedFiles to its old
    /// Generated/-only scope makes this test fail to throw.
    /// </summary>
    [TestMethod]
    public async Task AssertOwnedFilesUntouchedCatchesAStrayFileAnywhereInTheProject()
    {
        (await GenerateAsync(_root)).ShouldBe(ExitCode.Ok);
        var before = SnapshotOwnedFiles();

        File.WriteAllText(Path.Combine(_root, "intest-check-scratch.tmp"), "stray");

        Should.Throw<ShouldAssertException>(() => AssertOwnedFilesUntouched(before));
    }

    // ---- 1: fixture drift, decided to share --check's exit 1 with a different message ------

    [TestMethod]
    public async Task FixtureDriftUnderCheckReturnsWorkOutstandingAndNamesTheOperation()
    {
        WriteSpec(SpecNeedingAFixture);

        var report = new StringWriter();
        var exitCode = await GenerateCommand.RunAsync(_root, CancellationToken.None, report, check: true);

        exitCode.ShouldBe(ExitCode.WorkOutstanding);
        report.ToString().ShouldContain("createProduct", Case.Sensitive);
        report.ToString().ShouldContain("Run 'intest fixtures repair'", Case.Sensitive,
            customMessage: "drift's remedy under --check is still fixtures repair, not generate — " +
                           "the two exit-1 causes share a code but must not share a message");
        Directory.Exists(Path.Combine(_root, "Generated")).ShouldBeFalse(
            "drift is detected before BuildOutputs runs, in both modes — --check must not render, " +
            "let alone write, once drift is already reported");
    }

    // ---- 2: malformed config, unreadable spec — unchanged from plain generate --------------

    [TestMethod]
    public async Task ReturnsToolErrorWhenTheSpecIsMissing()
    {
        File.Delete(Path.Combine(_root, "orders.json"));

        (await GenerateAsync(_root, check: true)).ShouldBe(ExitCode.ToolError);
    }

    [TestMethod]
    public async Task ReturnsToolErrorForMalformedConfig()
    {
        File.WriteAllText(Path.Combine(_root, "intest.json"), "{ not json");

        (await GenerateAsync(_root, check: true)).ShouldBe(ExitCode.ToolError);
    }

    // ---- 4: version mismatch, checked before any output comparison -------------------------

    [TestMethod]
    public async Task ReturnsVersionMismatchWhenIntestVersionDiffersFromTheRunningTool()
    {
        WriteConfig(withIntestVersion: "1.0.0");
        (await GenerateAsync(_root)).ShouldBe(ExitCode.Ok);

        var before = SnapshotOwnedFiles();
        var (exitCode, report) = await CheckAsync(_root);

        exitCode.ShouldBe(ExitCode.VersionMismatch);
        report.ShouldContain("intest.json was generated by intest 1.0.0", Case.Sensitive);
        report.ShouldContain($"running tool is {CliVersion.Current}");
        report.ShouldContain("intest upgrade", Case.Sensitive);
        AssertOwnedFilesUntouched(before);
    }

    /// <summary>
    /// §8: the version check must fail "before comparing any output" — a version mismatch *and*
    /// a real diff must report 4, not 1. Constructed by declaring a mismatched version on a
    /// project whose spec has also drifted from what is committed, so if the implementation ever
    /// reordered these two checks, this is the test that would catch it: an exit code flip (4 to
    /// 1) or a message that also names a differing file would both mean the reorder happened.
    /// </summary>
    [TestMethod]
    public async Task VersionMismatchPreemptsAnOutputDifferenceRatherThanRacingIt()
    {
        WriteConfig(withIntestVersion: "1.0.0");
        (await GenerateAsync(_root)).ShouldBe(ExitCode.Ok);
        WriteSpec(Spec.Replace("getOrderById", "fetchOrderById"));

        var (exitCode, report) = await CheckAsync(_root);

        exitCode.ShouldBe(ExitCode.VersionMismatch);
        report.ShouldNotContain("differs from a fresh render",
            customMessage: "a version mismatch must pre-empt the output comparison entirely, not " +
                           "just win the exit code while still running and reporting it");
    }

    /// <summary>
    /// generate (no --check) has no exit-4 row in §5's command table at all — intestVersion
    /// existing does not mean every regeneration re-validates it, only that --check (and upgrade)
    /// can. A mismatched version must not block plain `generate`.
    /// </summary>
    [TestMethod]
    public async Task PlainGenerateIgnoresAVersionMismatch()
    {
        WriteConfig(withIntestVersion: "1.0.0");

        (await GenerateAsync(_root, check: false)).ShouldBe(ExitCode.Ok);
    }

    // ---- ReportVersionMismatch's two message shapes, pinned directly -----------------------

    [TestMethod]
    public void ReportVersionMismatchMatchesTheSpecsWorkedExample()
    {
        // §8's own worked example, verbatim, is 1.0.0 vs 1.1.0 — reused here rather than a fresh
        // pair of numbers, so this test also pins that the message is not merely "similar in
        // shape" but the exact text §8 specifies as a deliverable, not a suggestion.
        var report = new StringWriter();

        GenerateCommand.ReportVersionMismatch(report, declaredVersion: "1.0.0", runningVersion: "1.1.0");

        report.ToString().ShouldBe(
            "intest.json was generated by intest 1.0.0; running tool is 1.1.0." + Environment.NewLine +
            "Regenerate with the pinned version, or run `intest upgrade` to adopt 1.1.0 deliberately." +
            Environment.NewLine);
    }

    /// <summary>
    /// The build-problem branch of <c>GenerateCommand.ReportVersionMismatch</c> — see
    /// <c>CliVersion.FallbackVersion</c>'s own doc comment for why that value needs one. Exercised
    /// by calling the internal seam directly with runningVersion: CliVersion.FallbackVersion,
    /// since a normal test build always carries a real informational version (Directory.Build.props
    /// pins one) and cannot produce "0.0.0" from CliVersion.Current itself.
    /// </summary>
    [TestMethod]
    public void ReportVersionMismatchNamesABuildProblemWhenTheRunningToolHasNoVersion()
    {
        var report = new StringWriter();

        GenerateCommand.ReportVersionMismatch(
            report, declaredVersion: "1.0.0", runningVersion: CliVersion.FallbackVersion);

        var text = report.ToString();
        text.ShouldContain("built without version");
        text.ShouldContain("do not run `intest upgrade`", Case.Sensitive,
            customMessage: "the ordinary remedy is actively wrong here and must not be suggested");
    }
}

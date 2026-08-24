using System.Net;
using System.Text;
using InTest.Cli;
using InTest.Cli.Commands;
using InTest.Cli.Spec;
using Shouldly;

namespace InTest.Cli.Tests;

/// <summary>
/// `generate` against a URL <c>spec.source</c> — §9's snapshot, end to end through the command,
/// with the network stubbed. Kept apart from <see cref="GenerateCommandTests"/> and
/// <see cref="GenerateCheckCommandTests"/> rather than folded into either, because every test here
/// needs a transport and neither of those classes has one; splicing an optional handler through
/// their shared helpers would complicate every existing test to serve these.
/// <para>
/// The load-bearing tests in this file are
/// <see cref="WritesTheSnapshotEvenWhenFixtureDriftEndsTheRun"/> (<c>[snapshot-is-input]</c>) and
/// <see cref="CheckNeverFetches"/> (<c>[no-refetch]</c>). Both pin decisions that are invisible in
/// the output of a passing run and would be silently undone by a plausible-looking refactor.
/// </para>
/// </summary>
[TestClass]
public class GenerateUrlSpecTests
{
    private const string Url = "https://orders-staging.example.com/swagger/v1/swagger.json";

    // Minified deliberately: this is the shape a real Swagger endpoint serves, and it is what
    // makes the reprint assertions below mean something.
    private const string Spec =
        """{"openapi":"3.0.3","info":{"title":"Orders","version":"1.0"},"paths":{"/orders/{id}":{"get":{"operationId":"getOrderById","tags":["Orders"],"responses":{"200":{"description":"ok","content":{"application/json":{"schema":{"$ref":"#/components/schemas/Order"}}}}}}}},"components":{"schemas":{"Order":{"type":"object"}}}}""";

    // A POST with a required body: FixtureComposer.NeedsFixture is true, so `generate` reports
    // drift and exits 1 until `fixtures repair` has run. The state [snapshot-is-input] is about.
    private const string SpecNeedingAFixture =
        """{"openapi":"3.0.3","info":{"title":"Orders","version":"1.0"},"paths":{"/orders":{"post":{"operationId":"createOrder","tags":["Orders"],"requestBody":{"required":true,"content":{"application/json":{"schema":{"type":"object","required":["sku"],"properties":{"sku":{"type":"string"}}}}}},"responses":{"201":{"description":"created"}}}}}}""";

    private string _root = null!;

    [TestInitialize]
    public void CreateProject()
    {
        _root = Path.Combine(Path.GetTempPath(), "intest-url-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "intest.json"), $$"""
        { "schemaVersion": 1, "spec": { "source": "{{Url}}" },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);
    }

    [TestCleanup]
    public void RemoveProject()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>Serves one canned body, and counts how many times it was asked for anything.</summary>
    private sealed class StubTransport(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>
    /// The transport <c>[no-refetch]</c> is proven with. A command that must not open a socket is
    /// handed a transport that fails the test if it does — which is a mechanical proof, unlike
    /// reading the call graph and concluding no fetch is reachable.
    /// </summary>
    private sealed class ForbiddenTransport : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new ShouldAssertException(
                $"a request was issued to {request.RequestUri} by a command that must never fetch");
    }

    private string SnapshotPath => Path.Combine(_root, SpecSnapshot.FileName);

    private async Task<(int ExitCode, string Report)> RunAsync(
        HttpMessageHandler transport, bool check = false)
    {
        var report = new StringWriter();
        var exitCode = await GenerateCommand.RunAsync(
            _root, CancellationToken.None, report, check, transport);
        return (exitCode, report.ToString());
    }

    /// <summary>Captures stderr too — SpecLoadException is printed there, not to the report.</summary>
    private async Task<(int ExitCode, string Error)> RunCapturingErrorAsync(HttpMessageHandler transport)
    {
        var originalError = Console.Error;
        var captured = new StringWriter();
        Console.SetError(captured);
        try
        {
            var exitCode = await GenerateCommand.RunAsync(
                _root, CancellationToken.None, new StringWriter(), check: false, transport);
            return (exitCode, captured.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    // ---- the happy path ------------------------------------------------------------------------

    [TestMethod]
    public async Task FetchesTheSpecAndWritesTheSnapshot()
    {
        using var transport = new StubTransport(Spec);

        var (exitCode, _) = await RunAsync(transport);

        exitCode.ShouldBe(ExitCode.Ok);
        transport.Calls.ShouldBe(1, "write-mode generate refreshes the snapshot, exactly once");
        File.Exists(SnapshotPath).ShouldBeTrue();
        File.Exists(Path.Combine(_root, "Generated", "OrdersTests.g.cs")).ShouldBeTrue();
    }

    /// <summary>
    /// The snapshot is reprinted rather than written verbatim ([reprinted]). The served body is
    /// minified, so a byte-equal snapshot would prove the reprint step had been skipped.
    /// </summary>
    [TestMethod]
    public async Task WritesTheSnapshotAsReviewableCrlfJson()
    {
        using var transport = new StubTransport(Spec);

        await RunAsync(transport);

        var snapshot = File.ReadAllText(SnapshotPath);
        snapshot.ShouldNotBe(Spec, "a verbatim copy of a minified body has no reviewable diff");
        snapshot.ShouldBe(SpecSnapshot.Reprint(Spec), "the snapshot is exactly what Reprint produces");
        snapshot.Replace("\r\n", string.Empty).ShouldNotContain("\n");
        snapshot.ShouldEndWith("}\r\n");
    }

    /// <summary>
    /// The document the plan is built from is the document that lands on disk. `generate` parses
    /// the <i>reprinted</i> text rather than the raw response precisely so this holds, which is
    /// what lets a later `--check` — reading the snapshot — reach an identical plan.
    /// </summary>
    [TestMethod]
    public async Task ChecksCleanImmediatelyAfterGenerating()
    {
        using var transport = new StubTransport(Spec);
        (await RunAsync(transport)).ExitCode.ShouldBe(ExitCode.Ok);

        using var forbidden = new ForbiddenTransport();
        var (exitCode, report) = await RunAsync(forbidden, check: true);

        exitCode.ShouldBe(ExitCode.Ok, report);
    }

    // ---- [snapshot-is-input] -------------------------------------------------------------------

    /// <summary>
    /// <b><c>[snapshot-is-input]</c>'s regression test.</b> `generate` writes the snapshot as soon
    /// as the fetched document parses — before the fixture-drift gate — even on a run that then
    /// exits 1 and writes no generated output at all.
    /// <para>
    /// Move that write down beside the other writes, which looks tidier and is the shape a future
    /// reader will reach for, and the tool deadlocks: the spec changes upstream and adds a
    /// required property; `generate` fetches, sees drift, exits 1 <i>without</i> writing the
    /// snapshot; `fixtures repair` reads the old snapshot and repairs against the old spec;
    /// `generate` fetches, sees the same drift, exits 1. Forever, with every command behaving
    /// exactly as documented. This test is the only thing standing in front of that.
    /// </para>
    /// <para>
    /// CLAUDE.md's "detects fixture drift before writing anything" invariant is narrowed, not
    /// broken: what it protects is generated <i>output</i>, and this run demonstrably writes none
    /// — the Generated/ assertion below is half the point of the test.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task WritesTheSnapshotEvenWhenFixtureDriftEndsTheRun()
    {
        using var transport = new StubTransport(SpecNeedingAFixture);

        var (exitCode, report) = await RunAsync(transport);

        exitCode.ShouldBe(ExitCode.WorkOutstanding, report);
        report.ShouldContain("no fixture found");

        File.Exists(SnapshotPath).ShouldBeTrue(
            "without this, `fixtures repair` would read a stale snapshot and repair against the " +
            "wrong spec — the drift would never clear, however many times either command ran");
        Directory.Exists(Path.Combine(_root, "Generated")).ShouldBeFalse(
            "the invariant that still holds: no generated output before the drift gate");
    }

    /// <summary>
    /// The other half of `[snapshot-is-input]`: having written the snapshot, `fixtures repair`
    /// can act on the new spec, and the next `generate` clears. The loop terminates — which is
    /// the actual claim, and cannot be shown by either command alone.
    /// </summary>
    [TestMethod]
    public async Task DriftClearsAfterRepairBecauseTheSnapshotWasAlreadyWritten()
    {
        using var transport = new StubTransport(SpecNeedingAFixture);
        (await RunAsync(transport)).ExitCode.ShouldBe(ExitCode.WorkOutstanding);

        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(ExitCode.Ok);

        var (exitCode, report) = await RunAsync(transport);
        exitCode.ShouldBe(ExitCode.Ok, report);
    }

    // ---- [fail-closed] -------------------------------------------------------------------------

    /// <summary>
    /// A failed fetch is exit 2 and the existing snapshot is untouched — never a silent fall back
    /// to it. Falling back would make "I regenerated against the current spec" and "I regenerated
    /// against whatever I had lying around" produce identical output and an identical exit code.
    /// </summary>
    [TestMethod]
    public async Task FailsWithoutTouchingAnExistingSnapshotWhenTheFetchFails()
    {
        using var ok = new StubTransport(Spec);
        await RunAsync(ok);
        var before = File.ReadAllBytes(SnapshotPath);

        using var failing = new StubTransport("nope", HttpStatusCode.ServiceUnavailable);
        var (exitCode, error) = await RunCapturingErrorAsync(failing);

        exitCode.ShouldBe(ExitCode.ToolError);
        error.ShouldContain("503");
        File.ReadAllBytes(SnapshotPath).ShouldBe(before,
            "a stale snapshot silently standing in for a refresh is the quiet-green failure " +
            "\"Fail loudly\" exists to reject");
    }

    /// <summary>
    /// The fetch succeeded and the document is still garbage. The last known-good snapshot is
    /// worth more than a fresh copy of nonsense, which is why parsing happens before writing.
    /// </summary>
    [TestMethod]
    public async Task LeavesAnExistingSnapshotIntactWhenTheResponseDoesNotParse()
    {
        using var ok = new StubTransport(Spec);
        await RunAsync(ok);
        var before = File.ReadAllBytes(SnapshotPath);

        using var garbage = new StubTransport("{ this is not json");
        var (exitCode, _) = await RunCapturingErrorAsync(garbage);

        exitCode.ShouldBe(ExitCode.ToolError);
        File.ReadAllBytes(SnapshotPath).ShouldBe(before);
    }

    [TestMethod]
    public async Task WritesNoSnapshotAtAllWhenTheFirstFetchFails()
    {
        using var failing = new StubTransport("nope", HttpStatusCode.NotFound);

        var (exitCode, _) = await RunCapturingErrorAsync(failing);

        exitCode.ShouldBe(ExitCode.ToolError);
        File.Exists(SnapshotPath).ShouldBeFalse();
    }

    /// <summary>
    /// A snapshot that cannot be written is refused with a sentence, not a stack trace. Without
    /// the translation this escapes both of <c>RunAsync</c>'s catches and lands on
    /// <c>Program</c>'s crash floor as "intest: unexpected failure:
    /// UnauthorizedAccessException", naming neither the file nor anything the adopter can act on.
    /// A read-only checkout is an ordinary condition, not a defect in the tool.
    /// <para>
    /// Skipped where the process can write regardless of the read-only bit — root on Linux, which
    /// is how CI containers usually run. Asserting the guard from a context that cannot provoke it
    /// would make this test pass for the wrong reason.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task ExplainsASnapshotThatCannotBeWritten()
    {
        using var transport = new StubTransport(Spec);
        await RunAsync(transport);

        var snapshot = new FileInfo(SnapshotPath);
        snapshot.IsReadOnly = true;
        try
        {
            using var probe = File.OpenWrite(SnapshotPath);
            Assert.Inconclusive(
                "this process can write a read-only file (running as root?), so the guard " +
                "cannot be provoked here");
        }
        catch (UnauthorizedAccessException)
        {
            // The read-only bit is enforced for this process — the precondition holds.
        }

        try
        {
            var (exitCode, error) = await RunCapturingErrorAsync(transport);

            exitCode.ShouldBe(ExitCode.ToolError);
            error.ShouldNotContain("unexpected failure");
            error.ShouldContain(SpecSnapshot.FileName);
        }
        finally
        {
            snapshot.IsReadOnly = false;
        }
    }

    /// <summary>
    /// The write leaves no <c>.tmp</c> sibling behind. A stray <c>spec.json.tmp</c> in a committed
    /// project is its own small confusion, and the temp file is the price of writing atomically —
    /// see <c>GenerateCommand.WriteSnapshotAsync</c> for why that price is worth paying for this
    /// one artefact.
    /// </summary>
    [TestMethod]
    public async Task LeavesNoTemporaryFileBesideTheSnapshot()
    {
        using var transport = new StubTransport(Spec);
        await RunAsync(transport);

        Directory.GetFiles(_root, "*.tmp").ShouldBeEmpty();
    }

    // ---- [no-refetch] --------------------------------------------------------------------------

    /// <summary>
    /// <b><c>[no-refetch]</c>, proven mechanically.</b> §9: "`--check` does not re-fetch. It
    /// compares against the committed snapshot, so CI stays hermetic and does not depend on the
    /// service being reachable." A transport that throws when invoked is what turns that sentence
    /// into something a test can fail on.
    /// </summary>
    [TestMethod]
    public async Task CheckNeverFetches()
    {
        using var transport = new StubTransport(Spec);
        await RunAsync(transport);

        using var forbidden = new ForbiddenTransport();
        var (exitCode, report) = await RunAsync(forbidden, check: true);

        exitCode.ShouldBe(ExitCode.Ok, report);
    }

    /// <summary>
    /// `--check` before any `generate` has run. Reported as outstanding work rather than a tool
    /// error: nothing is broken, a human simply has not run `generate` yet, and CI needs to be
    /// able to tell those apart (§5's 1/2 split).
    /// </summary>
    [TestMethod]
    public async Task CheckReportsAMissingSnapshotAsWorkOutstanding()
    {
        using var forbidden = new ForbiddenTransport();

        var (exitCode, report) = await RunAsync(forbidden, check: true);

        exitCode.ShouldBe(ExitCode.WorkOutstanding);
        report.ShouldContain("spec.json is missing.");
        report.ShouldContain("intest generate");
    }

    /// <summary>[no-write] extended to the URL path: `--check` writes nothing, snapshot included.</summary>
    [TestMethod]
    public async Task CheckWritesNothing()
    {
        using var transport = new StubTransport(Spec);
        await RunAsync(transport);

        var before = Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, File.ReadAllBytes);

        using var forbidden = new ForbiddenTransport();
        await RunAsync(forbidden, check: true);

        var after = Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, File.ReadAllBytes);

        after.Keys.OrderBy(k => k, StringComparer.Ordinal)
            .ShouldBe(before.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (path, bytes) in before)
        {
            after[path].ShouldBe(bytes, $"{path} was modified by a --check run");
        }
    }

    /// <summary>
    /// The snapshot is not a <c>BuildOutputs</c> entry, so `--check` must not report it as an
    /// unaccounted-for file. It sits at the project root rather than under Generated/, which is
    /// what keeps the orphan sweep away from it — but "by construction" is worth one assertion.
    /// </summary>
    [TestMethod]
    public async Task CheckDoesNotReportTheSnapshotAsAStrayFile()
    {
        using var transport = new StubTransport(Spec);
        await RunAsync(transport);

        using var forbidden = new ForbiddenTransport();
        var (_, report) = await RunAsync(forbidden, check: true);

        report.ShouldNotContain("spec.json exists on disk");
    }

    // ---- a path source is unaffected -----------------------------------------------------------

    /// <summary>
    /// The false-positive guard, at the command level: a path <c>spec.source</c> must never be
    /// routed down the fetch path. <see cref="SpecLoader.IsUrl"/>'s narrowness is what prevents
    /// it, and this is what would fail if that predicate were ever widened to a general
    /// absolute-URI check — under which every rooted Windows path becomes a <c>file:</c> URI.
    /// </summary>
    [TestMethod]
    public async Task APathSourceNeverFetches()
    {
        File.WriteAllText(Path.Combine(_root, "orders.json"), Spec);
        File.WriteAllText(Path.Combine(_root, "intest.json"), """
        { "schemaVersion": 1, "spec": { "source": "orders.json" },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        using var forbidden = new ForbiddenTransport();
        var (exitCode, report) = await RunAsync(forbidden);

        exitCode.ShouldBe(ExitCode.Ok, report);
        File.Exists(SnapshotPath).ShouldBeFalse("a path source has no snapshot to take");
    }
}

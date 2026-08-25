using System.Reflection;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace InTest.Runtime.Tests;

/// <summary>
/// Covers <see cref="TestHost.CleanupAsync"/> (Task 5) — the caller that makes
/// <see cref="FixtureRunner.DrainAsync"/> reachable at all from a generated project's
/// [AssemblyCleanup]. The behaviour that matters most here is the one a test merely asserting
/// "the method exists and calls DrainAsync" would miss entirely: a throwing drain must not
/// propagate out of [AssemblyCleanup], or a teardown complaint becomes the whole run's headline
/// and buries whatever test actually failed.
/// <para>
/// The narrowness of <see cref="TestHost.CleanupAsync"/>'s catch clause is deliberately not
/// pinned here: <see cref="FixtureRunner.DrainAsync"/>'s own contract (hardened in
/// <c>FixtureRunnerTests.DrainWrapsACauseEvenWhenItsOwnMessageGetterThrows</c>, Task 5) promises
/// to only ever throw <see cref="FixtureLifecycleException"/>, so once that promise genuinely
/// holds, no cleanup action can make anything else reach this class's catch clause — a test for
/// that path would assert nothing. The decision stays argued in
/// <see cref="TestHost.CleanupAsync"/>'s own doc comment instead.
/// </para>
/// </summary>
[TestClass]
public class TestHostTests
{
    /// <summary>
    /// A minimal <see cref="TestContext"/> double. Every abstract member must be overridden to
    /// instantiate at all; only <see cref="WriteLine(string?)"/> is exercised by
    /// <see cref="TestHost.CleanupAsync"/> today, but the rest still need bodies to compile.
    /// </summary>
    private sealed class FakeTestContext : TestContext
    {
        public List<string> Lines { get; } = [];

        /// <summary>Calls to <see cref="DisplayMessage"/>, in order — the sink
        /// <c>TestHost.TestContextDiagnostics</c> and the fixture-validation report now actually
        /// use, since <see cref="WriteLine(string?)"/> was confirmed to be invisible on a passing
        /// [AssemblyInitialize] under VSTest (see <c>TestHost.TestContextDiagnostics</c>'s doc).</summary>
        public List<(MessageLevel Level, string Message)> DisplayedMessages { get; } = [];

        public override IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>();

        public override void WriteLine(string? message) => Lines.Add(message ?? "");

        public override void WriteLine(string format, params object?[] args) =>
            Lines.Add(string.Format(format, args));

        public override void Write(string? message) => Lines.Add(message ?? "");

        public override void Write(string format, params object?[] args) =>
            Lines.Add(string.Format(format, args));

        public override void AddResultFile(string fileName)
        {
        }

        public override void DisplayMessage(MessageLevel messageLevel, string message) =>
            DisplayedMessages.Add((messageLevel, message));
    }

    // InTestRun.RetainedFixtureContext is process-wide static state. Reset both before and after
    // each test — before, so a test is never at the mercy of whatever its predecessor left
    // behind if that predecessor's own cleanup were ever skipped; after, so this test does not
    // leak into whichever one runs next (DoNotParallelize makes that deterministic rather than
    // merely unlikely, but still wrong).
    [TestInitialize]
    public void ResetRetainedFixtureContextBeforeTest() => InTestRun.RetainedFixtureContext = null;

    [TestCleanup]
    public void ResetRetainedFixtureContextAfterTest() => InTestRun.RetainedFixtureContext = null;

    /// <summary>
    /// Task 2 question (c): audience is <c>Api:Audience</c> when configured, falling back to the
    /// base URL's authority — never the spec's security-scheme audience, since OpenAPI OAuth2
    /// flows carry <c>tokenUrl</c> and <c>scopes</c>, not reliably an audience. Pulled out of
    /// <see cref="InTestRun.InitializeAsync"/> as an internal, dependency-free seam (the
    /// <see cref="InTestRun.RegisterInTestClients"/> precedent) specifically so this resolution
    /// gets its own test independent of the full <c>InitializeAsync</c> weight — before this,
    /// nothing anywhere asserted that a configured <c>Api:Audience</c> or the authority fallback
    /// actually reached <see cref="AuthHandler"/>.
    /// </summary>
    [TestMethod]
    public void ResolveAudienceUsesConfiguredApiAudienceWhenPresent()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Api:Audience"] = "api://configured" })
            .Build();

        InTestRun.ResolveAudience(configuration, new Uri("https://h.invalid/api/")).ShouldBe("api://configured");
    }

    [TestMethod]
    public void ResolveAudienceFallsBackToTheBaseUrlsAuthorityWhenNotConfigured()
    {
        var configuration = new ConfigurationBuilder().Build();

        InTestRun.ResolveAudience(configuration, new Uri("https://h.invalid/api/")).ShouldBe("h.invalid");
    }

    /// <summary>
    /// The profile seam (see <see cref="InTestRun.ResolveProfile"/>'s own doc for why this stays
    /// a plain <c>string?</c> rather than an <c>IRunSettings</c> abstraction). The run-settings
    /// value wins outright, with nothing else evaluated — no <c>INTEST_PROFILE</c> lookup, no
    /// <c>BuildConfiguration</c> call, so this test needs neither an environment variable
    /// nor an appsettings.json this test project does not ship.
    /// </summary>
    [TestMethod]
    public void ResolveProfileReturnsTheRunSettingsValueWhenOneIsSupplied()
    {
        InTestRun.ResolveProfile("from-runsettings").ShouldBe("from-runsettings");
    }

    /// <summary>
    /// <c>null</c> — "no run-settings value was supplied" — falls through to the
    /// <c>INTEST_PROFILE</c> environment variable, the chain's second link. The <c>??</c> chain's
    /// short-circuiting is what lets this test avoid <c>BuildConfiguration</c>'s own
    /// appsettings.json read (the third link): once the environment variable answers non-null,
    /// nothing evaluates further.
    /// </summary>
    [TestMethod]
    public void ResolveProfileFallsBackToTheEnvironmentVariableWhenNoRunSettingsValueWasSupplied()
    {
        var original = Environment.GetEnvironmentVariable("INTEST_PROFILE");
        Environment.SetEnvironmentVariable("INTEST_PROFILE", "from-env");
        try
        {
            InTestRun.ResolveProfile(null).ShouldBe("from-env");
        }
        finally
        {
            // Restored even if the assertion above fails, so a red test here does not leak an
            // INTEST_PROFILE override into whichever test runs next — [assembly: DoNotParallelize]
            // makes "next" deterministic, but a leak would still be wrong.
            Environment.SetEnvironmentVariable("INTEST_PROFILE", original);
        }
    }

    /// <summary>
    /// <c>TestHost.ProfileFromRunSettings</c> directly — the mapping from MSTest's run-settings
    /// "profile" property to the plain <c>string?</c> <see cref="InTestRun.ResolveProfile"/>
    /// expects. Internal, not private, specifically so this test (and
    /// <see cref="AnEmptyRunSettingsProfileFallsThroughToTheEnvironmentVariableRatherThanPinningAnEmptyProfile"/>
    /// below) can exercise it without the full <see cref="TestHost.InitializeAsync"/> weight.
    /// </summary>
    [TestMethod]
    public void ProfileFromRunSettingsMapsAnAbsentPropertyToNull()
    {
        var context = new FakeTestContext();

        TestHost.ProfileFromRunSettings(context).ShouldBeNull();
    }

    /// <summary>
    /// The trap Task 4 exists to guard, isolated to the adapter mapping alone: MSTest's
    /// runsettings XML represents "no value configured" as an empty-string property, not an
    /// absent one, so an unmapped empty string reaching <see cref="InTestRun.ResolveProfile"/>
    /// would be indistinguishable from a deliberately-chosen empty profile and would pin every
    /// run to it — silently skipping <c>INTEST_PROFILE</c>, the config default, and <c>"local"</c>
    /// the way an absent property already falls through to all three. This must come out
    /// <c>null</c>, exactly like the absent-property case above, not <c>""</c>.
    /// </summary>
    [TestMethod]
    public void ProfileFromRunSettingsMapsAnEmptyPropertyToNull()
    {
        var context = new FakeTestContext();
        context.Properties["profile"] = "";

        TestHost.ProfileFromRunSettings(context).ShouldBeNull();
    }

    [TestMethod]
    public void ProfileFromRunSettingsPassesThroughANonEmptyValue()
    {
        var context = new FakeTestContext();
        context.Properties["profile"] = "staging";

        TestHost.ProfileFromRunSettings(context).ShouldBe("staging");
    }

    /// <summary>
    /// The trap end to end, composing <see cref="TestHost.ProfileFromRunSettings"/> (the
    /// adapter's mapping) with <see cref="InTestRun.ResolveProfile"/> (the neutral chain) exactly
    /// the way <see cref="TestHost.InitializeAsync"/> itself does, without needing that method's
    /// full weight. An empty run-settings "profile" property must fall through to
    /// <c>INTEST_PROFILE</c> here — if the adapter's empty-to-null mapping in
    /// <see cref="ProfileFromRunSettingsMapsAnEmptyPropertyToNull"/> above were ever skipped or
    /// undone before reaching <see cref="InTestRun.ResolveProfile"/>, this test would see the
    /// pinned empty string instead of "from-env" and fail.
    /// </summary>
    [TestMethod]
    public void AnEmptyRunSettingsProfileFallsThroughToTheEnvironmentVariableRatherThanPinningAnEmptyProfile()
    {
        var context = new FakeTestContext();
        context.Properties["profile"] = "";

        var original = Environment.GetEnvironmentVariable("INTEST_PROFILE");
        Environment.SetEnvironmentVariable("INTEST_PROFILE", "from-env");
        try
        {
            InTestRun.ResolveProfile(TestHost.ProfileFromRunSettings(context)).ShouldBe("from-env");
        }
        finally
        {
            Environment.SetEnvironmentVariable("INTEST_PROFILE", original);
        }
    }

    [TestMethod]
    public async Task CleanupAsyncDoesNotRethrowWhenDrainFails()
    {
        var context = new FixtureContext();
        context.OnCleanup(() => throw new InvalidOperationException("drain boom"));
        InTestRun.RetainedFixtureContext = context;

        // DrainAsync throws FixtureLifecycleException by design (Task 3) whenever a cleanup
        // action fails. If CleanupAsync let that propagate, an unhandled exception out of
        // [AssemblyCleanup] would become the whole run's headline, burying whatever test
        // actually failed. Should.NotThrowAsync puts that expectation in the assertion itself
        // rather than leaving it implicit in "the test would fail if this threw".
        await Should.NotThrowAsync(() => TestHost.CleanupAsync(new FakeTestContext()));
    }

    [TestMethod]
    public async Task CleanupAsyncWritesTheDrainFailureToTheTestContext()
    {
        var context = new FixtureContext();
        context.OnCleanup(() => throw new InvalidOperationException("drain boom"));
        InTestRun.RetainedFixtureContext = context;
        var testContext = new FakeTestContext();

        await TestHost.CleanupAsync(testContext);

        // Swallowed silently, a drain failure would be invisible in the .trx even though a
        // fixture's teardown genuinely failed and something it created may have leaked. The
        // exception's own message must survive into what gets written.
        testContext.Lines.ShouldContain(line => line.Contains("drain boom"),
        "a drain failure must still be visible in the TestContext log even though it does not fail the run");
    }

    [TestMethod]
    public async Task CleanupAsyncAlsoWritesTheDrainFailureToConsoleError()
    {
        var context = new FixtureContext();
        context.OnCleanup(() => throw new InvalidOperationException("drain boom"));
        InTestRun.RetainedFixtureContext = context;

        var originalError = Console.Error;
        var capturedError = new StringWriter();
        Console.SetError(capturedError);
        try
        {
            await TestHost.CleanupAsync(new FakeTestContext());
        }
        finally
        {
            // Restored even if the assertion below fails, so a red test here does not also
            // corrupt every other test's Console.Error for the rest of this run.
            Console.SetError(originalError);
        }

        // TestContext.WriteLine lands in the .trx but is invisible at `dotnet test`'s default
        // console verbosity (confirmed against a real MSTest 4.3.3 run) — the common CI shape
        // of console log plus exit code, with no .trx published, would otherwise make a drain
        // failure completely invisible even though it does not fail the run.
        capturedError.ToString().ShouldContain("drain boom");
    }

    [TestMethod]
    public async Task CleanupAsyncNamesTheRunIdInTheDrainFailureMessage()
    {
        // RunIdValue is null! (unset) here: nothing in this test file calls
        // InTestRun.InitializeAsync, which is the only place that assigns it. That is also the
        // real scenario this guards — InitializeAsync throwing before it gets that far, while
        // RetainedFixtureContext is still non-null because an earlier fixture already ran, is
        // exactly the readiness-failure path CleanupAsyncIsANoOpWhenNoFixtureContextWasRetained
        // covers the opposite side of.
        var context = new FixtureContext();
        context.OnCleanup(() => throw new InvalidOperationException("drain boom"));
        InTestRun.RetainedFixtureContext = context;
        var testContext = new FakeTestContext();

        await TestHost.CleanupAsync(testContext);

        // The run id is the one handle an operator has for finding what a leaked row belongs
        // to — RunIdHandler stamps every request with it. An unset run id must say so
        // explicitly rather than silently vanishing from the message.
        testContext.Lines.ShouldContain(line => line.Contains("AssemblyInitialize did not complete"),
        "an unavailable run id must be named explicitly, not silently omitted");
    }

    [TestMethod]
    public async Task CleanupAsyncNamesTheRiskToALaterRunNotThisOnesResults()
    {
        var context = new FixtureContext();
        context.OnCleanup(() => throw new InvalidOperationException("drain boom"));
        InTestRun.RetainedFixtureContext = context;
        var testContext = new FakeTestContext();

        await TestHost.CleanupAsync(testContext);

        // "This run's results are unaffected" is true but wrong-footed: F7 exists because state
        // a run fails to tear down can break a *later* run, which is the risk worth naming.
        testContext.Lines.ShouldContain(line => line.Contains("later run"),
        "the message must name the risk to a later run, not just reassure about this one");
    }

    [TestMethod]
    public async Task CleanupAsyncActuallyDrainsTheRetainedContextOnSuccess()
    {
        var ran = false;
        var context = new FixtureContext();
        context.OnCleanup(() =>
        {
            ran = true;
            return Task.CompletedTask;
        });
        InTestRun.RetainedFixtureContext = context;

        await TestHost.CleanupAsync(new FakeTestContext());

        // Guards against an implementation that merely catches-and-swallows without ever
        // draining at all, which the no-rethrow test above would not by itself catch.
        ran.ShouldBeTrue("CleanupAsync must actually drain the retained context, not merely avoid throwing");
    }

    [TestMethod]
    public async Task CleanupAsyncWritesHowManyActionsItDrainedOnSuccess()
    {
        var context = new FixtureContext();
        context.OnCleanup(() => Task.CompletedTask);
        context.OnCleanup(() => Task.CompletedTask);
        InTestRun.RetainedFixtureContext = context;
        var testContext = new FakeTestContext();

        await TestHost.CleanupAsync(testContext);

        // Before this, a drain that ran two actions and a context nobody ever registered
        // anything against both wrote nothing at all — a reader of the .trx could not tell
        // "cleanup ran and succeeded" from "cleanup was never wired up" from the log alone.
        testContext.Lines.ShouldContain(line => line.Contains("drained 2 action(s)"),
        "a successful drain must say how many actions it drained");
    }

    [TestMethod]
    public async Task CleanupAsyncWritesNothingWhenThereWasNothingToDrain()
    {
        var context = new FixtureContext();
        InTestRun.RetainedFixtureContext = context;
        var testContext = new FakeTestContext();

        await TestHost.CleanupAsync(testContext);

        // RetainedFixtureContext is non-null here (InitializeAsync always creates one now), but
        // no fixture registered any teardown against it — the overwhelmingly common case for a
        // suite with no fixtures at all. Announcing "drained 0 action(s)" every run would be
        // noise; staying silent keeps the log free of the null-vs-zero distinction that already
        // requires no announcement.
        testContext.Lines.ShouldBeEmpty();
    }

    [TestMethod]
    public async Task CleanupAsyncIsANoOpWhenNoFixtureContextWasRetained()
    {
        InTestRun.RetainedFixtureContext = null;

        // AssemblyInitialize can throw before ever creating the retained context — a readiness
        // failure, say (Task 6). AssemblyCleanup still runs in that case, and null here must not
        // throw a NullReferenceException out of [AssemblyCleanup] on top of whatever already
        // failed during AssemblyInitialize.
        await TestHost.CleanupAsync(new FakeTestContext());
    }

    [TestMethod]
    public async Task CleanupAsyncRejectsANullTestContext()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => TestHost.CleanupAsync(null!));
    }

    /// <summary>
    /// Covers <c>TestHost.TestContextDiagnostics</c> — the <see cref="IRunDiagnostics"/>
    /// <see cref="TestHost.InitializeAsync"/> hands to <c>FixtureRunner.RunAsync</c> and uses
    /// itself for the fixture-validation report. This is as close to that wiring as a cheap,
    /// in-process test can get: it proves the adapter class itself forwards correctly. It cannot
    /// prove that <see cref="TestHost.InitializeAsync"/> actually constructs and passes
    /// <em>this</em> adapter — that is an implementation detail of a private call site inside a
    /// method this repo deliberately does not build an in-process harness for (it needs
    /// <c>AppContext.BaseDirectory</c>, a real <see cref="TestContext"/>, and live HTTP). See
    /// <c>TestHost.TestContextDiagnostics</c>'s own doc for why <see cref="IRunDiagnostics.Note"/>
    /// forwards to <see cref="TestContext.WriteLine(string)"/> while
    /// <see cref="IRunDiagnostics.Warn"/> forwards to <see cref="TestContext.DisplayMessage"/> at
    /// <see cref="MessageLevel.Warning"/> instead — the two are not interchangeable under this
    /// project's actual runner.
    /// </summary>
    [TestMethod]
    public void NoteForwardsToTestContextWriteLine()
    {
        var testContext = new FakeTestContext();
        var diagnostics = new TestHost.TestContextDiagnostics(testContext);

        diagnostics.Note("InTest run id: abc123 (profile 'local')");

        testContext.Lines.ShouldContain("InTest run id: abc123 (profile 'local')");
    }

    [TestMethod]
    public void WarnForwardsToDisplayMessageAtWarningLevel()
    {
        var testContext = new FakeTestContext();
        var diagnostics = new TestHost.TestContextDiagnostics(testContext);

        diagnostics.Warn("Skipping fixture 'Some.Fixture': its AppliesTo does not include profile 'local'.");

        testContext.DisplayedMessages.ShouldContain(
        (MessageLevel.Warning, "Skipping fixture 'Some.Fixture': its AppliesTo does not include profile 'local'."));
    }

    /// <summary>
    /// Pins the half of the mapping that is easy to get backwards by accident: a future edit to
    /// <c>TestContextDiagnostics.Warn</c> that reached for <see cref="MessageLevel.Error"/>
    /// instead of <see cref="MessageLevel.Warning"/> would fail the whole run over what is, by
    /// design, a mere skip (see the class's own doc for why <see cref="MessageLevel.Error"/> was
    /// rejected). <see cref="WarnForwardsToDisplayMessageAtWarningLevel"/> above already proves
    /// the positive case; this proves the specific negative that matters.
    /// </summary>
    [TestMethod]
    public void WarnNeverUsesMessageLevelError()
    {
        var testContext = new FakeTestContext();
        var diagnostics = new TestHost.TestContextDiagnostics(testContext);

        diagnostics.Warn("Skipping fixture 'Some.Fixture': its AppliesTo does not include profile 'local'.");

        testContext.DisplayedMessages.ShouldNotContain(m => m.Level == MessageLevel.Error);
    }

    /// <summary>
    /// A name-parity tripwire for the facade split (Task 4): every public static property
    /// <see cref="TestHost"/> declares must have a same-named public static member on
    /// <see cref="InTestRun"/>, the neutral composition root it forwards to. This catches a
    /// <em>missing</em> forward — a property added to one side of the split and never mirrored on
    /// the other — nothing more. It cannot prove a forward reads the <em>right</em> neutral member:
    /// a facade property that compiled but forwarded to the wrong same-shaped property (e.g.
    /// <c>TestHost.RunIdValue</c> accidentally returning <see cref="InTestRun.Profile"/>, since
    /// both are <c>string</c>) would pass this reflection check just as cleanly as a correct one.
    /// <c>InTest.Golden.Tests.GeneratedSuiteExecutionTests</c> is what actually proves that: it
    /// builds and runs a real generated suite end to end through <see cref="TestHost.InitializeAsync"/>,
    /// every facade property a generated test touches, real HTTP, and
    /// <see cref="TestHost.CleanupAsync"/>, so a swapped forward would surface as a wrong value
    /// somewhere real rather than merely a missing member here.
    /// </summary>
    [TestMethod]
    public void EveryPublicStaticPropertyOnTestHostHasASameNamedPublicStaticMemberOnInTestRun()
    {
        var testHostPropertyNames = typeof(TestHost)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Select(p => p.Name)
            .ToList();

        // Guards the tripwire itself: if a future refactor emptied TestHost of every public
        // static property, the loop below would pass vacuously and this test would stop proving
        // anything at all without ever going red.
        testHostPropertyNames.ShouldNotBeEmpty(
        "the tripwire would pass vacuously if TestHost declared no public static properties");

        var inTestRunMemberNames = typeof(InTestRun)
            .GetMembers(BindingFlags.Public | BindingFlags.Static)
            .Select(m => m.Name)
            .ToHashSet();

        var missing = testHostPropertyNames.Where(name => !inTestRunMemberNames.Contains(name)).ToList();

        missing.ShouldBeEmpty(
        $"TestHost declares public static propert{(missing.Count == 1 ? "y" : "ies")} with no " +
        $"same-named public static member on InTestRun: {string.Join(", ", missing)}. Every public " +
        "forward on the facade is supposed to have a same-named counterpart on the neutral " +
        "composition root it forwards to.");
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InTest.Runtime;

/// <summary>
/// Neutral, framework-agnostic composition root for one InTest run: the process-wide state
/// assembled once by <see cref="InitializeAsync"/> and read by every generated test case's
/// fixture, HTTP client and identity resolution. Named to match the existing neutral
/// static/ambient family (<see cref="InTestAmbient"/>, <see cref="InTestId"/>,
/// <see cref="InTestClients"/>, <see cref="InTestUrl"/>, <see cref="InTestIdentities"/>) rather
/// than after "TestHost" — that MSTest-shaped name now belongs to <see cref="TestHost"/>, the thin
/// adapter facade that forwards here so a generated project's scaffolded TestStartup.cs keeps
/// compiling unchanged (see <see cref="TestHost"/>'s own doc for why <c>[TypeForwardedTo]</c>
/// cannot bridge the two instead).
/// </summary>
public static class InTestRun
{
    public static IConfiguration Configuration { get; private set; } = null!;

    public static IServiceProvider Root { get; private set; } = null!;

    public static SchemaBundle Schemas { get; private set; } = null!;

    public static string RunIdValue { get; private set; } = null!;

    public static string Profile { get; private set; } = null!;

    public static FixtureStore Fixtures { get; private set; } = null!;

    /// <summary>
    /// One aggregated fixture-validation report, built once at <see cref="InitializeAsync"/> and
    /// consulted by every <c>ApiTestCore.RequireFixture</c> call — never rebuilt per test, and
    /// never bypassed by going straight to <see cref="Fixtures"/> (decision 2 / Task 7).
    /// </summary>
    public static FixtureValidation.Report FixtureValidationReport { get; private set; } = null!;

    /// <summary>
    /// The token resolver built once here and reused by every generated request via
    /// <c>ApiTestCore</c>'s fixture helpers — the same instance <see cref="FixtureValidationReport"/>
    /// was built from, so <c>{{config:}}</c>/<c>{{secret:}}</c> are read once per run (Task 6's
    /// resolution-timing table) while <c>{{utcNow}}</c> still varies per call, because
    /// <c>TokenResolver</c> invokes the clock itself on every <c>Resolve</c> rather than caching it.
    /// </summary>
    public static TokenResolver FixtureTokens { get; private set; } = null!;

    /// <summary>Registration hook. The generated project's TestStartup assigns this before
    /// InitializeAsync runs, so team registrations compose with InTest's.</summary>
    public static Action<IServiceCollection, IConfiguration>? ConfigureServices { get; set; }

    /// <summary>
    /// The <see cref="ITestTokenProvider"/> the generated project's <c>ConfigureServices</c>
    /// registered, resolved once from <see cref="Root"/> right after it is built and exposed here
    /// so <c>ApiTestCore.MultipleIdentitiesSkipReason</c> and <c>ApiTestCore.UseIdentity</c> (v1-c
    /// Task 5) have something to consult without needing a live scope of their own — unlike
    /// <see cref="AuthHandler"/>, neither runs inside one. Null for every spec that declares no
    /// <c>security</c>, exactly as <c>ApiTestCore.ResolveDefaultIdentity(null)</c> already treats
    /// as ordinary rather than an error.
    /// <para>
    /// This is the *same instance* <see cref="AuthHandler"/> uses, but not because
    /// <see cref="AuthHandler"/> resolves anything: it takes the provider through its primary
    /// constructor, supplied once by the factory lambda this class registers
    /// (<c>services.AddTransient(sp =&gt; new AuthHandler(sp.GetService&lt;ITestTokenProvider&gt;(),
    /// audience))</c>), and <c>IHttpClientFactory</c> builds a named client's handler chain once
    /// and caches it for the handler lifetime (two minutes by default) — from a scope the factory
    /// creates for itself, not the caller's scope, and not per request. "Same instance as this
    /// field" therefore holds only because the provider itself is registered
    /// <c>AddSingleton</c> (the scaffold and getting-started.md's Auth section both show it that
    /// way); a scoped or transient registration would still let <see cref="AuthHandler"/>
    /// construct correctly, just from a different instance than this field holds.
    /// </para>
    /// <para>
    /// Internal, settable, hand-rolled the same way <see cref="RetainedFixtureContext"/> is: only
    /// <see cref="InitializeAsync"/> writes it in production, and
    /// <c>InTest.Runtime.Tests</c> sets it directly to drive <c>ApiTestCore</c>'s guard and
    /// override without needing a real <see cref="InitializeAsync"/> run — the same reason that
    /// method gets no in-process harness (see <see cref="TestHost.TestContextDiagnostics"/>'s doc).
    /// </para>
    /// </summary>
    internal static ITestTokenProvider? TokenProvider { get; set; }

    /// <summary>
    /// The one <see cref="FixtureContext"/> instance <see cref="InitializeAsync"/> creates and
    /// passes to every fixture, retained here so <see cref="CleanupAsync"/> can drain the exact
    /// instance the fixtures wrote to rather than a fresh, empty one (v1-b decision 4). Reset to null
    /// at the top of every <see cref="InitializeAsync"/> call and set again just before
    /// <see cref="FixtureRunner.RunAsync"/> runs, not after: <see cref="FixtureRunner.RunAsync"/>
    /// deliberately does not drain a cancellation that lands between fixtures (its own doc calls
    /// this out — v1-b decision 4 does not guarantee cleanup across a cancellation, crash, or agent
    /// timeout), so whatever already-succeeded fixtures registered before that point only reaches
    /// <see cref="CleanupAsync"/>'s later, unconditional drain if this field was already pointing
    /// at the live context when the cancellation happened. An ordinary fixture failure does not
    /// depend on this ordering — <see cref="FixtureRunner.RunAsync"/> drains the context it was
    /// given directly, before the exception it throws ever reaches this method. Null whenever
    /// <see cref="InitializeAsync"/> threw before reaching the assignment (e.g. a readiness
    /// failure) or has not run yet this process. <see cref="CleanupAsync"/> treats null as
    /// "nothing to drain," not an error. Internal because only <see cref="CleanupAsync"/> reads
    /// it and only <see cref="InitializeAsync"/> should write it; a generated project has no
    /// business touching it directly.
    /// </summary>
    internal static FixtureContext? RetainedFixtureContext { get; set; }

    /// <summary>
    /// Builds process-wide state for one InTest run: configuration, DI container, schema bundle,
    /// run id, profile, fixtures and their validation report. <see cref="TestHost.InitializeAsync"/>
    /// is the MSTest entry point a generated project's scaffolded TestStartup.cs actually calls;
    /// <paramref name="profileFromRunSettings"/> and <paramref name="diagnostics"/> are that
    /// adapter's two narrow contributions to this otherwise framework-neutral method — a plain
    /// string for the run-settings "profile" property (see <see cref="ResolveProfile"/> for why
    /// this stays a string rather than an interface) and an <see cref="IRunDiagnostics"/> sink for
    /// progress and warnings. Everything below is unchanged from when this method lived directly
    /// on <see cref="TestHost"/>.
    /// </summary>
    public static async Task InitializeAsync(
        string? profileFromRunSettings,
        IRunDiagnostics diagnostics,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        // Reset rather than relying on the field's default: MSTest runs [AssemblyInitialize]
        // once per process in practice, but the doc on RetainedFixtureContext promises null
        // "whenever InitializeAsync threw before reaching that point" — a promise a bare field
        // default cannot keep on a second call within one process, and TestHostTests.cs already
        // resets this field by hand between tests for the same reason.
        RetainedFixtureContext = null;

        // Same reasoning, same precedent (Task 10 item 2): a bare field default cannot promise
        // null on a second InitializeAsync call within one process any more than
        // RetainedFixtureContext's could — a run that threw before reaching the real assignment
        // below must not leave whatever the previous call registered for the next one to see.
        TokenProvider = null;

        Profile = ResolveProfile(profileFromRunSettings);
        Configuration = BuildConfiguration(Profile);
        RunIdValue = RunId.Create(Configuration["InTest:RunId:Prefix"]);
        diagnostics.Note($"InTest run id: {RunIdValue} (profile '{Profile}')");

        Fixtures = FixtureStore.Load(AppContext.BaseDirectory, Profile);

        var services = new ServiceCollection();
        services.AddSingleton(Configuration);
        services.AddTransient(_ => new RunIdHandler(() => RunIdValue));

        // Hoisted here so RegisterInTestClients can share one normalized value between
        // InTestClients.Api and .Readiness below. This also changes which exception a
        // misconfigured project sees when Api:BaseUrl is missing: previously this exact
        // InvalidOperationException lived inside the Api client's AddHttpClient configure lambda
        // and never actually fired there in practice, because EnsureNoPrefixDuplication further
        // down called InTestUrl.NormalizeBase(Configuration["Api:BaseUrl"]!) first — via a
        // null-forgiving operator on that same missing key — and NormalizeBase's own
        // ArgumentException("Base URL must not be null or whitespace.") won that race every time.
        // Computing baseUrl once, here, means the profile-named message below is what a
        // misconfigured project now sees instead. Nothing in Task 1 asked for that change; it is
        // a side effect of sharing baseUrl between both clients — deliberate, but unpinned by any
        // test.
        var baseUrl = InTestUrl.NormalizeBase(
        Configuration["Api:BaseUrl"]
        ?? throw new InvalidOperationException(
        $"Api:BaseUrl is not configured for profile '{Profile}'."));

        // Task 2 question (c): ResolveAudience below. sp.GetService (not GetRequiredService) is
        // question (b): Catalog and Inventory declare no `security` and register no provider, so
        // AuthHandler must be constructible, and must no-op, when none is there.
        var audience = ResolveAudience(Configuration, baseUrl);
        services.AddTransient(sp => new AuthHandler(sp.GetService<ITestTokenProvider>(), audience));

        // ResponseCaptureHandler takes baseUrl by constructor injection, the same shape as
        // AuthHandler's audience just above — see that handler's own doc for why it must not read
        // IConfiguration itself. Registered unconditionally, exactly like AuthHandler: whether it
        // actually ends up attached to InTestClients.Api is a decision RegisterInTestClients makes
        // below, off the clientCaptureEnabled flag read from spec-paths.json, not a decision this
        // registration makes.
        services.AddTransient(_ => new ResponseCaptureHandler(baseUrl));

        // Read once, here — before RegisterInTestClients needs ClientCaptureEnabled to decide
        // whether ResponseCaptureHandler gets attached to InTestClients.Api, and reused again below
        // for EnsureNoPrefixDuplication's OperationPathPrefix — rather than at the previous read
        // site further down, which ran after RegisterInTestClients and so could never have supplied
        // this value in time. Nothing between here and that original site depends on Root, Schemas,
        // or any other state RegisterInTestClients itself does not need, so moving the read earlier
        // changes nothing about what it can see.
        var specPaths = ReadSpecPaths();

        RegisterInTestClients(services, baseUrl, specPaths.ClientCaptureEnabled);

        ConfigureServices?.Invoke(services, Configuration);
        Root = services.BuildServiceProvider();

        // Resolved once here, from the same container the
        // services.AddTransient(sp => new AuthHandler(sp.GetService<ITestTokenProvider>(), audience))
        // registration above resolves from when IHttpClientFactory builds the Api client's
        // handler chain — not a second, independent registration, and not AuthHandler resolving
        // anything itself (it takes the provider through its primary constructor; see
        // TokenProvider's doc above).
        // This field and the instance AuthHandler was built with are the same object only
        // because the provider is registered AddSingleton; a scoped or transient registration
        // would still let AuthHandler construct correctly, just from a different instance than
        // this field holds. GetService, not GetRequiredService: most specs declare no security
        // and register no provider at all (question (b) from Task 2), which
        // RequireMultipleIdentities' own zero-identity handling already treats as ordinary.
        TokenProvider = Root.GetService<ITestTokenProvider>();

        Schemas = SchemaBundle.FromFile(Path.Combine(AppContext.BaseDirectory, "spec-schemas.json"));

        // Fail on a base URL that repeats a prefix the spec's paths already carry, before a
        // single request is sent. The alternative is every test returning 404 with no clue why.
        // Reuses the baseUrl computed above rather than re-normalizing Configuration["Api:BaseUrl"]
        // a second time, and the specPaths tuple read earlier rather than opening spec-paths.json
        // a second time.
        InTestUrl.EnsureNoPrefixDuplication(baseUrl, specPaths.OperationPathPrefix);

        var readiness = new ReadinessOptions();
        Configuration.GetSection("InTest:Readiness").Bind(readiness);

        using var scope = Root.CreateScope();

        // InTestClients.Readiness, not .Api (F10): probing on the API client meant an adopter's
        // auth handler ran on the anonymous /health/ready request too, so an unreachable identity
        // provider surfaced as a 120-second "dead API" instead of the auth failure it actually was.
        var client = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(InTestClients.Readiness);
        await Readiness.WaitAsync(client, readiness, cancellationToken).ConfigureAwait(false);

        // The reorder that is this task's whole substance (v1-b decision 1): seeding needs a service
        // provider and a reachable API, so it runs here — after both — and before TokenResolver,
        // which needs seeding's published keys to resolve {{fixture:...}} at all. One visible
        // consequence: a dead API now fails on readiness before the fixture report is ever built
        // (previously validation ran first); that trade is intentional, not an oversight.
        var fixtureContext = new FixtureContext();

        // See RetainedFixtureContext's own doc for exactly which failure mode this ordering
        // protects — an ordinary fixture failure does not depend on it.
        RetainedFixtureContext = fixtureContext;

        // FixtureGraph.Order is deliberately not called here — RunAsync orders fixtures itself
        // (see its own doc for why splitting that responsibility across two callers is a risk
        // nothing in the existing suite would catch), so the guarantee stays unbypassable.
        //
        // Resolved from the scope readiness already opened, so a fixture can take a constructor
        // dependency on anything ConfigureServices registered, including IHttpClientFactory
        // (v1-b Task 6's own golden proof, SeedIdFixture, does exactly this). Only AddSingleton is a
        // documented, supported registration for IAssemblyFixture (see TestStartup.cs's scaffold
        // comment): an AddScoped or AddTransient fixture that also implements IDisposable would
        // be disposed when this scope ends below, at the end of this method, while any OnCleanup
        // closure it registered survives on fixtureContext until AssemblyCleanup — a
        // disposed-object trap for anyone who strays from the scaffolded shape.
        //
        // GetServices<IAssemblyFixture>() resolves — and so constructs — every registered
        // fixture eagerly, right here, not lazily as FixtureRunner enumerates them. A fixture
        // whose constructor takes a dependency nobody registered therefore throws before
        // FixtureRunner ever runs, which would otherwise bypass every guarantee it exists to
        // give (§13: a failure names the fixture and says setup broke, not a bare framework
        // exception with no such framing). Wrapped here so construction failures get the same
        // treatment as an InitializeAsync failure, rather than a raw InvalidOperationException
        // escaping [AssemblyInitialize] unexplained.
        List<IAssemblyFixture> fixtures;
        try
        {
            fixtures = scope.ServiceProvider.GetServices<IAssemblyFixture>().ToList();
        }
        catch (Exception ex)
        {
            throw new FixtureLifecycleException(
            $"Failed to construct one or more registered IAssemblyFixture instances: {ex.Message} " +
            "Check that every constructor dependency an IAssemblyFixture takes is itself " +
            "registered in TestStartup's Register method.",
            ex);
        }

        await FixtureRunner.RunAsync(fixtures, fixtureContext, Profile, diagnostics, cancellationToken)
            .ConfigureAwait(false);

        // Built only now, with the published keys FixtureRunner just seeded — TokenResolver's own
        // doc explains why an empty snapshot here would fail every {{fixture:...}} token.
        FixtureTokens = new TokenResolver(Configuration, RunIdValue, publishedFixtureValues: fixtureContext.PublishedValues);
        FixtureValidationReport = FixtureValidation.Build(Fixtures, FixtureTokens);

        // Warn, not Note: see TestContextDiagnostics's doc for the full, confirmed story, but the
        // short version is that Note alone is invisible in exactly the case decision 2 exists for
        // — a passing run with a non-blocking fixture problem. Warn reaches real stdout and the
        // trx without failing a run nothing here is blocking; Note still lands in the trx but
        // skips stdout, so a clean run stays quiet. This is exactly today's Warning/Informational
        // mapping, carried over unchanged onto the two-level seam.
        if (FixtureValidationReport.HasProblems)
        {
            diagnostics.Warn(FixtureValidationReport.Message);
        }
        else
        {
            diagnostics.Note(FixtureValidationReport.Message);
        }
    }

    /// <summary>
    /// Task 2 question (c): the audience passed to <see cref="ITestTokenProvider.GetTokenAsync"/>
    /// is <c>Api:Audience</c> when configured, falling back to <paramref name="baseUrl"/>'s
    /// authority — never the spec's security-scheme audience, since OpenAPI OAuth2 flows carry
    /// <c>tokenUrl</c> and <c>scopes</c>, not reliably an audience. Pulled out of
    /// <see cref="InitializeAsync"/> as an internal, dependency-free seam — the same reason
    /// <see cref="RegisterInTestClients"/> is one — so this resolution has its own test
    /// independent of <see cref="InitializeAsync"/>'s full weight (no <c>AppContext.BaseDirectory</c>,
    /// no real <c>TestContext</c>, no live HTTP).
    /// </summary>
    internal static string ResolveAudience(IConfiguration configuration, Uri baseUrl) =>
        configuration["Api:Audience"] ?? baseUrl.Authority;

    /// <summary>
    /// Registers InTest's two named HTTP clients — <see cref="InTestClients.Api"/> and
    /// <see cref="InTestClients.Readiness"/> — against the same <paramref name="baseUrl"/>.
    /// Both carry <see cref="RunIdHandler"/>; only <see cref="InTestClients.Api"/> also carries
    /// <see cref="AuthHandler"/> (Task 2, F8). Extracted from <see cref="InitializeAsync"/> as
    /// an internal seam (the csproj's own <c>InternalsVisibleTo</c> comment explains why an
    /// internal seam is sanctioned here) so <c>InTest.Runtime.Tests</c> can prove that the
    /// registration this method performs — not a hand-duplicated copy of it — keeps a handler an
    /// adopter's <see cref="ConfigureServices"/> attaches to <see cref="InTestClients.Api"/> off
    /// <see cref="InTestClients.Readiness"/> (F10, decision 1). <see cref="InitializeAsync"/> as a
    /// whole is still not given an in-process harness — see <c>TestHostTests</c>'s note on
    /// <c>TestContextDiagnostics</c> for why — but this narrower seam needs none of what makes
    /// that true: no <c>AppContext.BaseDirectory</c>, no real <c>TestContext</c>, no live HTTP.
    /// Requires <see cref="RunIdHandler"/>, <see cref="AuthHandler"/> and (when
    /// <paramref name="captureEnabled"/> is true) <see cref="ResponseCaptureHandler"/> already
    /// registered in <paramref name="services"/>; this method only wires the two named clients to
    /// them.
    /// </summary>
    /// <param name="captureEnabled">
    /// [capture-is-opt-in]: true only when the generated project's <c>spec-paths.json</c> carries
    /// <c>clientCaptureEnabled: true</c> — written by <c>generate</c> when at least one case
    /// resolved a client-routed call. When true, <see cref="ResponseCaptureHandler"/> is appended to
    /// <see cref="InTestClients.Api"/>'s handler chain, after <see cref="AuthHandler"/> so it sits
    /// closest to the wire. Never appended to <see cref="InTestClients.Readiness"/>, mirroring
    /// <see cref="AuthHandler"/>'s own F10 exclusion just below: the readiness probe hits an
    /// anonymous endpoint with no typed client anywhere near it, so there is nothing for
    /// <see cref="ResponseCaptureHandler"/> to usefully capture there, only an unconditional
    /// <see cref="HttpResponseMessage.Content"/> replacement to needlessly perform on every probe.
    /// </param>
    internal static void RegisterInTestClients(IServiceCollection services, Uri baseUrl, bool captureEnabled)
    {
        var apiClient = services.AddHttpClient(InTestClients.Api, client => client.BaseAddress = baseUrl)
            .AddHttpMessageHandler<RunIdHandler>()
            .AddHttpMessageHandler<AuthHandler>();

        if (captureEnabled)
        {
            apiClient.AddHttpMessageHandler<ResponseCaptureHandler>();
        }

        // Separate from Api so that the one attachment point the scaffold documents and adopters
        // actually use — InTestClients.Api, via ConfigureServices — cannot carry an auth handler
        // into the anonymous /health/ready probe (F10). Registration order buys nothing toward
        // that: named-HttpClient configuration is additive IConfigureNamedOptions applied at
        // CreateClient time, independent of which AddHttpClient call ran first, so nothing stops
        // a ConfigureServices that deliberately names InTestClients.Readiness from attaching to
        // it too. AuthHandler is attached to InTestClients.Api by name, above, precisely so this
        // ordering is never load-bearing. RunIdHandler stays regardless: probe traffic should
        // still carry X-Test-Run-Id and remain traceable, and it never throws regardless of
        // identity-provider health, unlike AuthHandler would if it were attached here too.
        services.AddHttpClient(InTestClients.Readiness, client => client.BaseAddress = baseUrl)
            .AddHttpMessageHandler<RunIdHandler>();
    }

    /// <summary>
    /// Drains <see cref="RetainedFixtureContext"/> during the generated project's
    /// [AssemblyCleanup] — the caller that makes <see cref="FixtureRunner.DrainAsync"/> (v1-b Task 3)
    /// reachable at all, since <see cref="TestHost"/> is a plain static class and cannot carry
    /// the attribute itself.
    /// <para>
    /// Called unconditionally by the scaffolded <c>TestStartup.cs</c> (via <see cref="TestHost.CleanupAsync"/>),
    /// regardless of whether <see cref="InitializeAsync"/> succeeded. That is exactly the composition
    /// <see cref="FixtureRunner.DrainAsync"/>'s idempotency exists for: a fixture failure during
    /// <see cref="InitializeAsync"/> already triggers one drain inside
    /// <see cref="FixtureRunner.RunAsync"/>, so this second, unconditional drain finds nothing
    /// left and is a safe no-op rather than a repeat failure.
    /// </para>
    /// <para>
    /// <see cref="FixtureRunner.DrainAsync"/> throws <see cref="FixtureLifecycleException"/> by
    /// design (v1-b Task 3) to report a teardown action that failed. That exception is caught here
    /// rather than rethrown: an exception escaping [AssemblyCleanup] becomes the whole run's
    /// headline, burying whatever test actually failed underneath a teardown complaint — the
    /// drain report is diagnostic, not a verdict. Only <see cref="FixtureLifecycleException"/>
    /// is caught, because that is the only type <see cref="FixtureRunner.DrainAsync"/>'s own
    /// contract promises to throw — a promise <see cref="FixtureRunner.DrainAsync"/> itself
    /// defends even against a misbehaving cause (v1-b Task 5's hardening in
    /// <c>FixtureRunnerTests.DrainWrapsACauseEvenWhenItsOwnMessageGetterThrows</c>) — so anything
    /// else escaping from here would be a genuine bug in <see cref="FixtureRunner"/> and must
    /// propagate rather than be swallowed alongside a legitimate teardown failure.
    /// </para>
    /// <para>
    /// Written to both <paramref name="diagnostics"/> and <see cref="Console.Error"/>, because
    /// neither sink alone reaches every CI shape: under the MSTest adapter,
    /// <c>TestContext.WriteLine(string)</c> (what <see cref="TestHost.TestContextDiagnostics"/>'s
    /// <see cref="IRunDiagnostics.Note"/> forwards to) lands in the .trx but is invisible at
    /// <c>dotnet test</c>'s default console verbosity, and a CI setup that captures console output
    /// plus exit code without publishing the .trx would otherwise never see this failure at all —
    /// even though, by design, it does not fail the run or its exit code.
    /// </para>
    /// <para>
    /// The message names the run id (<see cref="RunIdValue"/>) — the handle an operator has for
    /// finding what a leaked row belongs to, since every request <c>RunIdHandler</c> sends
    /// carries it — falling back to an explicit "unavailable" note when <see cref="RunIdValue"/>
    /// is still its default <c>null!</c> because <see cref="InitializeAsync"/> never reached the
    /// line that assigns it. It names the risk to a <em>later</em> run, not this one: this run's
    /// own results are genuinely unaffected, but that is not the risk worth an operator's
    /// attention — state this run failed to tear down outliving it and breaking the next one is
    /// (§14/F7).
    /// </para>
    /// <para>
    /// <see cref="RetainedFixtureContext"/> is null whenever <see cref="InitializeAsync"/> threw
    /// before creating it — a readiness failure, say — in which case there is nothing to drain
    /// and this method returns without touching <paramref name="diagnostics"/>, rather than throwing
    /// a <see cref="NullReferenceException"/> out of [AssemblyCleanup] that would itself become a
    /// second, unrelated failure stacked on top of whatever <see cref="InitializeAsync"/> already
    /// reported.
    /// </para>
    /// <para>
    /// A successful drain also writes one line to <paramref name="diagnostics"/> naming how many
    /// actions ran, but only when there was at least one: today, a drain that ran zero actions
    /// and a context nobody ever registered a fixture against (<see cref="RetainedFixtureContext"/>
    /// is null for every scaffolded suite until a team writes its first fixture) both write
    /// nothing, and the reader cannot tell which happened from the log alone. The count is read
    /// from <see cref="FixtureContext.CleanupActions"/> before draining, since
    /// <see cref="FixtureRunner.DrainAsync"/> takes and clears the list as it runs — reading after
    /// would always see zero. Written only on success, since a failed drain already gets its own,
    /// more specific message below naming how many of how many threw.
    /// </para>
    /// <para>
    /// Deliberately does not dispose <see cref="Root"/>. This is the obvious place to try, but a
    /// fixture's <see cref="FixtureContext.OnCleanup"/> closure can capture an <c>HttpClient</c>
    /// pulled from <see cref="Root"/>'s <see cref="IHttpClientFactory"/> and that closure runs
    /// during the drain this method performs — disposing <see cref="Root"/> first would tear
    /// down the very client the drain is about to use. <see cref="Root"/> is process-lifetime by
    /// design; nothing in this type ever disposes it.
    /// </para>
    /// </summary>
    public static async Task CleanupAsync(IRunDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (RetainedFixtureContext is null)
        {
            return;
        }

        var pendingActions = RetainedFixtureContext.CleanupActions.Count;

        try
        {
            await FixtureRunner.DrainAsync(RetainedFixtureContext).ConfigureAwait(false);

            if (pendingActions > 0)
            {
                diagnostics.Note($"InTest fixture cleanup: drained {pendingActions} action(s).");
            }
        }
        catch (FixtureLifecycleException ex)
        {
            // RunIdValue defaults to null! rather than throwing when read unset, but an unset
            // run id must be named explicitly here rather than silently disappearing from the
            // one message that gives an operator something to search logs and a database for.
            var runId = RunIdValue ?? "unavailable (AssemblyInitialize did not complete)";

            // DrainAsync's own message already carries its remediation clause (v1-b Task 3). "This
            // run's results are unaffected" is deliberately not said: it is true, but the risk
            // worth naming is that state this run failed to tear down can break a later one.
            var message =
                $"InTest fixture cleanup failed during AssemblyCleanup for run '{runId}': {ex.Message} " +
                "State this run created may still be present and can break a later run.";

            diagnostics.Note(message);
            Console.Error.WriteLine(message);
        }
    }

    /// <summary>
    /// Reads the generator-owned <c>spec-paths.json</c> once and returns both values it carries —
    /// rather than opening and re-parsing the file a second time for the second value — because
    /// <see cref="InitializeAsync"/> now needs both: <see cref="OperationPathPrefix"/> for
    /// <see cref="InTestUrl.EnsureNoPrefixDuplication"/> (unchanged from before this task) and
    /// <see cref="ClientCaptureEnabled"/> for <see cref="RegisterInTestClients"/>'s
    /// <c>captureEnabled</c> parameter ([capture-is-opt-in], new).
    /// <para>
    /// <see cref="ClientCaptureEnabled"/> defaults to false when the property is <em>absent</em> —
    /// every suite generated before this task existed, and every suite whose <c>client</c> config
    /// resolved no case to a client-routed call — matching this task's brief verbatim
    /// ("absent → false") rather than treating a missing key as an error. A generated project's
    /// spec-paths.json is rewritten wholesale by every <c>generate</c> run, so there is no drift for
    /// an absent-vs-present value to guard against the way fixtures' own drift check exists for; a
    /// stale absence simply cannot occur.
    /// </para>
    /// <para>
    /// A <em>present but non-boolean</em> <c>clientCaptureEnabled</c> — a string <c>"true"</c>, a
    /// number, an object — is deliberately not folded into that same "default to false" leniency,
    /// even though an earlier version of this method did exactly that (read: ValueKind != True,
    /// therefore false, no distinction from "absent"). <c>spec-paths.json</c> is generator-owned
    /// (CLAUDE.md's ownership table: <c>Generated/</c> is "written by generate ... never touched by
    /// humans"), so a present-and-malformed value can only mean the file was hand-edited or
    /// corrupted — never a legitimate generate output — and reading it as false anyway produces a
    /// misleading failure downstream: capture silently never attaches, and the eventual error an
    /// adopter sees is <c>ApiTestCore.LastCapturedResponse</c>'s own [client-rides-the-api-pipeline]
    /// message telling them to construct their client over
    /// <c>IHttpClientFactory.CreateClient(InTestClients.Api)</c> — a remedy that does not fix this
    /// cause, since that registration was already correct, and sends the adopter to re-check
    /// something that was never wrong. So this shape throws instead, naming the file path, the
    /// offending value, and the fact that the file is generator-owned and must not be hand-edited —
    /// consistent with CLAUDE.md's "fail loudly" directive ("Missing fixture data becomes an
    /// obvious TODO: sentinel and a red test. Never substitute plausible defaults that let a suite
    /// pass while asserting nothing" — the same shape of mistake this silent-false default would
    /// otherwise be).
    /// </para>
    /// </summary>
    internal static (string? OperationPathPrefix, bool ClientCaptureEnabled) ReadSpecPaths()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "spec-paths.json");
        if (!File.Exists(path))
        {
            return (null, false);
        }

        return ParseSpecPaths(path, File.ReadAllText(path));
    }

    /// <summary>
    /// The parse itself, split out from <see cref="ReadSpecPaths"/> — the same "internal,
    /// dependency-free seam" shape <see cref="ResolveAudience"/> and <see cref="RegisterInTestClients"/>
    /// already use elsewhere in this class — so <c>InTest.Runtime.Tests</c> can exercise every
    /// <c>clientCaptureEnabled</c> shape (absent, <c>true</c>, <c>false</c>, malformed) against
    /// literal JSON text directly, rather than needing a real file under
    /// <see cref="AppContext.BaseDirectory"/> to drive each case. <paramref name="path"/> is passed
    /// through only for the malformed-value exception message below; it is never read from disk
    /// here.
    /// </summary>
    internal static (string? OperationPathPrefix, bool ClientCaptureEnabled) ParseSpecPaths(string path, string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var operationPathPrefix = document.RootElement.TryGetProperty("operationPathPrefix", out var prefixValue)
            ? prefixValue.GetString()
            : null;

        // Absent property -> false (see ReadSpecPaths's own doc, "absent → false"). Present and
        // JsonValueKind.True/False -> that literal boolean. Present and anything else (string,
        // number, object, array, null) -> a hard error naming the file, the offending raw value, and
        // the generator-ownership reason a human should never have been able to produce this shape
        // in the first place (see ReadSpecPaths's own doc, second paragraph, for why silently
        // defaulting to false here — as an earlier version of this method did — produces a
        // misleading downstream failure instead of this one, correct-cause error).
        var clientCaptureEnabled = document.RootElement.TryGetProperty("clientCaptureEnabled", out var captureValue)
            ? captureValue.ValueKind switch
            {
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                _ => throw new InvalidOperationException(
                    $"InTest: '{path}' has a malformed 'clientCaptureEnabled' value: " +
                    $"{captureValue.GetRawText()}. Expected a JSON boolean (true or false). " +
                    "spec-paths.json is written by 'generate' and is generator-owned — it must not " +
                    "be hand-edited. Re-run 'intest generate' to regenerate it."),
            }
            : false;

        return (operationPathPrefix, clientCaptureEnabled);
    }

    /// <summary>
    /// The profile precedence chain, run-settings value first: <paramref name="fromRunSettings"/>
    /// → the <c>INTEST_PROFILE</c> environment variable → <c>InTest:DefaultProfile</c> read from
    /// <see cref="BuildConfiguration"/> with no profile applied yet (the profile is exactly what
    /// has not been resolved at this point, so its own appsettings.&lt;profile&gt;.json cannot be
    /// layered in) → the literal fallback <c>"local"</c>.
    /// <para>
    /// Deliberately a plain <c>string?</c> parameter rather than an <c>IRunSettings</c>
    /// abstraction over "read the profile property from wherever the runner keeps run-settings".
    /// An interface here would have exactly one string-returning member, exactly one
    /// implementation (<see cref="TestHost"/>'s <c>ProfileFromRunSettings</c>), and exactly one
    /// call site — the single-implementation abstraction this codebase's conventions rule out.
    /// The behaviour that actually matters is the four-level precedence chain below, and it stays
    /// neutral and stays tested regardless of how many runners this project ever grows; the
    /// MSTest adapter's whole job is contributing one string.
    /// </para>
    /// <para>
    /// The adapter must map an empty run-settings string to <c>null</c> before calling this
    /// method — see <see cref="TestHost"/>'s <c>ProfileFromRunSettings</c> for why — so that
    /// <paramref name="fromRunSettings"/> being <c>null</c> here always means "no run-settings
    /// value was supplied," never "an empty one was," and the chain below falls through
    /// identically for both an absent property and one MSTest's runsettings XML declared with no
    /// text content.
    /// </para>
    /// </summary>
    internal static string ResolveProfile(string? fromRunSettings) =>
        fromRunSettings
        ?? Environment.GetEnvironmentVariable("INTEST_PROFILE")
        ?? BuildConfiguration(profile: null)["InTest:DefaultProfile"]
        ?? "local";

    private static IConfiguration BuildConfiguration(string? profile)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false);

        if (profile is not null)
        {
            builder.AddJsonFile($"appsettings.{profile}.json", optional: true);
        }

        return builder.AddJsonFile("appsettings.local.json", optional: true)
            .AddEnvironmentVariables("INTEST_")
            .Build();
    }
}

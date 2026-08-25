using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InTest.Runtime;

/// <summary>
/// MSTest adapter facade over <see cref="InTestRun"/>, the neutral composition root that now
/// holds the actual implementation. This class exists purely to keep a generated project's
/// scaffolded <c>TestStartup.cs</c> — which calls <c>TestHost.InitializeAsync(context)</c> /
/// <c>TestHost.CleanupAsync(context)</c> and assigns <c>TestHost.ConfigureServices</c> — compiling
/// byte-for-byte unchanged while the split into a neutral <c>InTest.Runtime</c> package and an
/// MSTest-specific adapter proceeds. Task 6 moves this file into its own
/// <c>InTest.Runtime.MSTest</c> project; nothing here anticipates that beyond what this task asks
/// for.
/// <para>
/// Cannot use <c>[TypeForwardedTo]</c> to bridge the two instead: that attribute forwards the
/// *same* fully-qualified type name into a different assembly, and
/// <c>InTest.Runtime.TestHost</c> forwarding to a differently-named <c>InTest.Runtime.InTestRun</c>
/// is not what it does. Nor could this class itself simply move to the neutral assembly — it
/// names <see cref="TestContext"/> directly, which is exactly the thing that keeps it out of the
/// neutral layer (see <c>NeutralityTests</c>).
/// </para>
/// <para>
/// Forwards <see cref="InTestRun"/>'s <em>public</em> members only.
/// <see cref="InTestRun"/>'s internal members — <c>TokenProvider</c>, <c>RetainedFixtureContext</c>,
/// <c>ResolveAudience</c>, <c>RegisterInTestClients</c> — are deliberately not forwarded here: once
/// Task 6 lands, this class lives in a different assembly than <see cref="InTestRun"/>, and an
/// internal forwarder would need a new <c>InternalsVisibleTo</c> between the two shipped packages
/// that the whole split exists to avoid. <c>InTest.Runtime.Tests</c> reaches those members directly
/// on <see cref="InTestRun"/> through the <c>InternalsVisibleTo</c> already declared in
/// <c>InTest.Runtime.csproj</c>. <c>ApiTestCore.cs</c> — in this same assembly permanently, unlike
/// <c>MSTest/ApiTestBase.cs</c>, which Task 6 moves out — reads <see cref="InTestRun"/>'s internal
/// <c>TokenProvider</c> directly for the same reason.
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

    /// <summary>See <see cref="InTestRun.FixtureValidationReport"/> for the canonical doc — this
    /// is a pure forward.</summary>
    public static FixtureValidation.Report FixtureValidationReport => InTestRun.FixtureValidationReport;

    /// <summary>See <see cref="InTestRun.FixtureTokens"/> for the canonical doc — this is a pure
    /// forward.</summary>
    public static TokenResolver FixtureTokens => InTestRun.FixtureTokens;

    /// <summary>Registration hook. The generated project's TestStartup assigns this before
    /// InitializeAsync runs, so team registrations compose with InTest's.</summary>
    public static Action<IServiceCollection, IConfiguration>? ConfigureServices
    {
        get => InTestRun.ConfigureServices;
        set => InTestRun.ConfigureServices = value;
    }

    /// <summary>
    /// Adapts <see cref="TestContext"/> to <see cref="InTestRun.InitializeAsync"/> — see that
    /// method's own doc for what actually happens; this method's whole job is the seam: mapping
    /// the run-settings "profile" property to a plain string (<see cref="ProfileFromRunSettings"/>)
    /// and wrapping <paramref name="context"/> in an <see cref="IRunDiagnostics"/> sink
    /// (<see cref="TestContextDiagnostics"/>).
    /// </summary>
    public static Task InitializeAsync(TestContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return InTestRun.InitializeAsync(
        ProfileFromRunSettings(context), new TestContextDiagnostics(context), cancellationToken);
    }

    /// <summary>
    /// Adapts <see cref="TestContext"/> to <see cref="InTestRun.CleanupAsync"/> — see that
    /// method's own doc for the full drain/report semantics; this is only the seam.
    /// </summary>
    public static Task CleanupAsync(TestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return InTestRun.CleanupAsync(new TestContextDiagnostics(context));
    }

    /// <summary>
    /// Maps MSTest's run-settings <c>"profile"</c> property to the plain <c>string?</c>
    /// <see cref="InTestRun.ResolveProfile"/> expects. An empty string is mapped to <c>null</c>
    /// here rather than passed through: <see cref="InTestRun.ResolveProfile"/>'s precedence chain
    /// treats any non-null value as "the run-settings value wins" with no further check, so an
    /// unmapped empty string would silently become the pinned profile instead of falling through
    /// to <c>INTEST_PROFILE</c> / the config default / <c>"local"</c> the way an altogether absent
    /// property already does. This is the one behaviour-preservation trap in the whole seam: the
    /// prior, unsplit <c>ResolveProfile(TestContext)</c> checked <c>s.Length &gt; 0</c> inline,
    /// and that check has to live somewhere after the split — here, not in the neutral chain,
    /// because it is a fact about how MSTest's runsettings XML represents "no value" (an empty
    /// string), not a fact the neutral precedence chain has any business knowing.
    /// <para>
    /// Internal, not private, so <c>InTest.Runtime.Tests</c> can exercise this mapping directly —
    /// the same seam pattern <see cref="InTestRun.ResolveAudience"/> already uses — rather than
    /// only indirectly through the full <see cref="InitializeAsync"/> weight, which this project
    /// deliberately gives no in-process harness (see this class's own <see cref="TestContextDiagnostics"/>
    /// doc for why).
    /// </para>
    /// </summary>
    internal static string? ProfileFromRunSettings(TestContext context) =>
        context.Properties.TryGetValue("profile", out var v) && v is string s && s.Length > 0 ? s : null;

    /// <summary>
    /// Forwards <see cref="IRunDiagnostics"/> to <see cref="TestContext"/> the way the confirmed
    /// behaviour of this project's actual runner requires: <see cref="Note"/> to
    /// <see cref="TestContext.WriteLine(string)"/>, <see cref="Warn"/> to
    /// <see cref="TestContext.DisplayMessage(MessageLevel, string)"/> at
    /// <see cref="MessageLevel.Warning"/> — confirmed to be the one call that survives a
    /// <em>passing</em> [AssemblyInitialize] under this project's actual runner: VSTest via
    /// MSTest.TestAdapter 4.3.3, <em>not</em> Microsoft.Testing.Platform (no
    /// <c>EnableMSTestRunner</c> anywhere here; forcing MTP fails outright on the .NET 10 SDK, and
    /// its native host prints nothing on a pass either). VSTest buffers an
    /// [AssemblyInitialize]'s <see cref="TestContext.WriteLine(string)"/>, <see cref="Console.Out"/>
    /// and <see cref="Console.Error"/> into the <c>UnitTestResult</c> it would attach them to, and
    /// only flushes that buffer when a failure synthesises a result to carry it — so all three
    /// reach nowhere on a passing run, confirmed by direct probe, not assumed.
    /// <see cref="MessageLevel.Warning"/> escapes anyway: real process stdout plus the trx's
    /// run-level output, and the run still exits 0 — unlike <see cref="MessageLevel.Error"/>,
    /// which would fail it, wrong for a mere skip. This is the canonical explanation of why the
    /// MSTest mapping is what it is; <see cref="IRunDiagnostics.Warn"/>'s own doc states the
    /// intent and points here.
    /// <para>
    /// Internal so <c>InTest.Runtime.Tests</c> can exercise this class's own forwarding directly.
    /// Replaces the prior <c>ContextTextWriter</c>, which was handed out typed as
    /// <see cref="TextWriter"/> even though only <see cref="TextWriter.WriteLine(string?)"/> was
    /// overridden — every other member silently no-opped via <see cref="TextWriter"/>'s own
    /// empty-bodied <c>Write(char)</c>, a real trap for a future caller. Implementing
    /// <see cref="IRunDiagnostics"/> directly instead of a <see cref="TextWriter"/> subclass
    /// removes that trap entirely: there is no member left to silently no-op, because the
    /// interface only ever had the two methods this class actually implements.
    /// </para>
    /// </summary>
    internal sealed class TestContextDiagnostics(TestContext context) : IRunDiagnostics
    {
        public void Note(string message) => context.WriteLine(message);

        public void Warn(string message) => context.DisplayMessage(MessageLevel.Warning, message);
    }
}

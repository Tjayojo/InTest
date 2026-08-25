namespace InTest.Runtime;

/// <summary>
/// The framework-neutral seam a runner (MSTest today; xUnit/NUnit potentially later) implements
/// so that neutral-layer code — <see cref="FixtureRunner"/> today — can report what happened
/// during a run without knowing anything about how any particular test framework surfaces
/// messages. <c>[intent-not-mechanism]</c>: the two members below name what the caller means,
/// not how the runner shows it, and that distinction is the entire reason this interface has the
/// shape it has rather than one of the shapes rejected below.
/// <para>
/// <see cref="Note"/> is routine progress — informational, safe to lose. A runner is permitted to
/// swallow it on a passing run; nothing about correctness depends on a <see cref="Note"/> being
/// seen.
/// </para>
/// <para>
/// <see cref="Warn"/> is not. It must reach the operator even when the run passes and exits 0 —
/// a fixture silently not running, say, is otherwise indistinguishable from one that ran and did
/// nothing — and it must never fail the run merely for having been called. A caller that wants
/// the run itself to fail throws; it does not call <see cref="Warn"/> and hope.
/// </para>
/// <para>
/// Three shapes were considered and rejected in favour of this one:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <c>Action&lt;SomeLevelEnum, string&gt;</c> — this is MSTest's own
/// <c>TestContext.DisplayMessage(MessageLevel, string)</c> with the parameters renamed, not a
/// framework-neutral abstraction over it. It leaks MSTest's own level taxonomy (which has more
/// than two levels, most of which nothing here needs) into the neutral layer instead of hiding
/// it behind the two levels the neutral layer actually cares about. Mechanism, not intent.
/// </description>
/// </item>
/// <item>
/// <description>
/// Two separate <c>Action&lt;string&gt;</c> delegates (e.g. <c>onNote</c> and <c>onWarn</c>
/// parameters) — carries the same two pieces of information as this interface, but a delegate
/// parameter has no name at the call site once assigned to a local or passed along, so a caller
/// wiring up <c>RunAsync(..., warn, note, ...)</c> instead of <c>RunAsync(..., note, warn, ...)</c>
/// transposes them and the compiler cannot catch it — both are <c>Action&lt;string&gt;</c>. An
/// interface with two named methods cannot be transposed the same way: <c>diagnostics.Warn(x)</c>
/// says what it does regardless of argument order elsewhere.
/// </description>
/// </item>
/// <item>
/// <description>
/// <c>TextWriter</c> — structurally cannot express two levels; every <c>TextWriter</c> is one
/// undifferentiated stream. Grafting a level onto it (a <c>WriteLine</c> convention, a marker
/// prefix) would be inventing a private protocol instead of stating the two levels directly.
/// This is also the shape the seam is replacing: see <see cref="FixtureRunner.RunAsync"/>'s prior
/// <c>TextWriter log</c> parameter and <c>TestHost.TestContextDiagnostics</c> (née
/// <c>ContextTextWriter</c>) for the MSTest mapping this interface now lets that class state
/// directly instead of faking through <see cref="TextWriter.WriteLine(string?)"/> overrides.
/// </description>
/// </item>
/// </list>
/// <para>
/// Deliberately no <c>RunDiagnostics.Null</c> or other shipped convenience implementation here.
/// Nothing outside a test currently needs one, and adding it now would be exactly the speculative
/// addition this codebase's conventions rule out — a test-local double is enough until a real
/// caller needs otherwise.
/// </para>
/// </summary>
public interface IRunDiagnostics
{
    /// <summary>Routine progress. A runner may discard this on a passing run.</summary>
    void Note(string message);

    /// <summary>
    /// Must reach the operator even when the run passes and exits 0. Must never itself fail the
    /// run — a caller that wants the run to fail throws instead of calling this.
    /// </summary>
    void Warn(string message);
}

namespace InTest.Cli;

/// <summary>
/// The single source for the process exit codes defined by the design spec's §5 exit-code
/// convention. Before this, <c>InitCommand</c>, <c>GenerateCommand</c> and
/// <c>FixturesRepairCommand</c> each declared their own subset — eight declarations of four
/// numbers — and <c>Program</c> needed the same numbers again, using a literal with a §5 citation
/// rather than becoming a fourth copy. Nothing kept the copies in step but discipline, which is
/// the arrangement <c>CONTRIBUTING.md</c>'s "One canonical explanation" rule exists to replace:
/// one copy authoritative, the rest pointing at it.
/// <para>
/// Same shape and same reason as <see cref="CliVersion"/>, which collapsed the same three
/// commands' hardcoded version literals.
/// </para>
/// <para>
/// §5 is the contract; this type only transcribes it. Changing a value here does not change the
/// convention, it breaks it. §5's code <c>4</c> — tool/config version mismatch — now has a
/// declaration and a returner: <c>generate --check</c> (v1-e) is the one command that reads
/// <c>intestVersion</c> and can tell a stale committed run from a fresh comparison, so it is the
/// only path this code can come from. Before v1-e it was deliberately absent here for the
/// opposite reason it is present now — declaring a constant no code path could return would have
/// been the same defect this file exists to prevent, just aimed at a number instead of a name.
/// </para>
/// </summary>
public static class ExitCode
{
    /// <summary>
    /// The requested state was reached, <b>including when no work was needed</b> — a PR script
    /// running <c>fixtures repair</c> unconditionally must not fail on a clean tree.
    /// </summary>
    public const int Ok = 0;

    /// <summary>
    /// Real work is outstanding that a human must do: fixture drift, validation failures,
    /// <c>--check</c> differences. Kept separate from <see cref="ToolError"/> deliberately —
    /// folding a crash or an unreadable spec into this code would leave CI unable to tell "the
    /// fixtures drifted, fix them" from "the tool blew up", two failures with entirely different
    /// responses and only one of them the developer's to act on.
    /// </summary>
    public const int WorkOutstanding = 1;

    /// <summary>
    /// Tool error — the tool did not do the work it was asked to do, and nothing was written: the
    /// command line could not be parsed, the spec is unparseable, <c>spec.source</c> is missing,
    /// <c>intest.json</c> is malformed, or an exception went unhandled. Returned by <b>any</b>
    /// command; §5 lists it per-command only where it is likely.
    /// </summary>
    public const int ToolError = 2;

    /// <summary>
    /// The command declined because proceeding would destroy or duplicate existing state.
    /// </summary>
    public const int AlreadyInitialised = 3;

    /// <summary>
    /// <c>intest.json</c>'s <c>intestVersion</c> does not match the running tool's own version —
    /// <c>generate --check</c> only, and only when the config declares a version at all (absent
    /// means no claim made, not a mismatch — the <c>[read-what-init-wrote]</c> decision in
    /// <c>docs/superpowers/plans/2026-08-21-intest-v1e-check-and-upgrade.md</c>; that slug is a
    /// plan section, not a spec one, and §5 records no absent-version rule of its own). Kept
    /// apart from <see cref="WorkOutstanding"/> deliberately, per §5's exit-code table (the
    /// <c>4</c> row): "so CI can distinguish it from a genuine diff" — a version drift and a real
    /// spec change call for different remedies
    /// (<c>intest upgrade</c> vs. reviewing what the spec changed), and folding both into <c>1</c>
    /// would make a CI failure unable to say which one happened. Checked, and returned, before
    /// any output comparison runs — §8 requires the version mismatch to pre-empt a diff, not
    /// race it, so a stale-tool run never reports "the spec changed" when the real story is "the
    /// generator changed".
    /// </summary>
    public const int VersionMismatch = 4;
}

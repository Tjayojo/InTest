using System.Text.Encodings.Web;
using System.Text.Json;

namespace InTest.Cli.Json;

/// <summary>
/// The one home for the <see cref="JsonSerializerOptions"/> behind every JSON file `generate` or
/// `fixtures repair` writes to disk. <see cref="Value"/> serves four of them:
/// <c>CoverageReport</c>, <c>FixtureDocument</c>, <c>SchemaBundleBuilder</c>, and
/// <c>GenerateCommand</c>'s spec-paths.json manifest. <see cref="SpecSnapshot"/> serves the
/// fifth, <c>spec.json</c>, and differs in exactly one property — see its own doc comment for
/// why that one difference cannot be folded into <see cref="Value"/>. All five are committed
/// artefacts — three are `--check`-compared, one (fixtures/) is hand-edited and diffed by
/// adopters, one is a reviewable input snapshot — where a stray LF is not cosmetic; see
/// [crlf-everywhere]
/// (`docs/superpowers/plans/2026-08-23-crlf-everywhere.md`), which reverses the v1-e line-endings
/// task's LF choice for the same reason it was made: one fixed convention, chosen deliberately,
/// beats one that tracks whatever the writing platform's default happens to be.
/// <para>
/// <see cref="JsonSerializerOptions.NewLine"/> pins the <em>interior</em> line endings a writer
/// emits between properties to CRLF; without it, System.Text.Json defaults to
/// <see cref="Environment.NewLine"/>, which is LF on Linux/macOS — so a writer that skipped this
/// would still vary by platform, only now it would happen to match on Windows and diverge
/// everywhere else. Each call site still appends its own trailing <c>"\r\n"</c> by hand —
/// <c>WriteIndented</c> never emits a line ending after the final closing brace, and one call site
/// (<c>SchemaBundleBuilder</c>) also needs its own <c>.Replace(...)</c> pass afterwards — so the
/// trailing newline is not folded into this shared instance; there would be nothing left to share
/// if it were.
/// </para>
/// <para>
/// Shared instances, not one inline copy per writer: <see cref="JsonSerializerOptions"/> keeps a
/// per-options reflection/metadata cache (documented on the type itself), so structurally-equal
/// instances pay for that cache once each for no reason. Sharing also means the "why NewLine"
/// reasoning above lives once, not once per call site with most of them restating "same fix, same
/// reasoning" instead of the reasoning itself — and JsonWritingOptionsGuardTests
/// (InTest.Cli.Tests) enforces mechanically that no writer declares its own options elsewhere and
/// silently forgets NewLine. A writer that genuinely needs different settings adds a named
/// instance <em>here</em>, as <see cref="SpecSnapshot"/> does, rather than an inline copy at its
/// call site; that is what keeps the guard's rule literally true instead of exemption-ridden.
/// </para>
/// </summary>
internal static class CommittedJsonOptions
{
    public static readonly JsonSerializerOptions Value = new() { WriteIndented = true, NewLine = "\r\n" };

    /// <summary>
    /// <see cref="Value"/>'s variant for the <c>spec.json</c> snapshot a URL <c>spec.source</c>
    /// produces (§9, <c>[reprinted]</c> in
    /// <c>docs/superpowers/plans/2026-08-24-intest-url-spec-source.md</c>). Identical but for the
    /// encoder, and that one difference is the whole reason it is a second instance rather than a
    /// fifth caller of <see cref="Value"/>.
    /// <para>
    /// <b>Why a different encoder.</b> The snapshot exists so that a spec change arrives as a
    /// reviewable diff — that is §9's entire justification for writing it at all. The default
    /// encoder escapes non-ASCII and HTML-sensitive characters aggressively, so a description
    /// reading <c>café &amp; &lt;tag&gt;</c> in the source document comes out of it as
    /// <c>café & <tag></c> — measured, not assumed. That is valid JSON
    /// encoding the identical string, and it is unreadable in a pull request, which defeats the
    /// point of taking the snapshot. <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/>
    /// writes those characters literally. This is not a new argument in this repository:
    /// <c>InitCommand.JsonSpecSource</c> reached the same conclusion for the same reason (a value
    /// adopters read by hand) and its doc comment carries the longer version.
    /// </para>
    /// <para>
    /// <b>Why not just change <see cref="Value"/>.</b> Four committed artefacts are written
    /// through it — <c>coverage-report.json</c>, <c>spec-schemas.json</c>, <c>spec-paths.json</c>
    /// and every file under <c>fixtures/</c> — three of which are byte-compared by
    /// <c>generate --check</c> and one of which has golden files. Swapping their encoder would
    /// re-encode all four for a reason that has nothing to do with any of them.
    /// </para>
    /// <para>
    /// <b>Why it lives here.</b> <c>JsonWritingOptionsGuardTests</c> allows the
    /// <c>WriteIndented</c> marker in this file and nowhere else under <c>src/InTest.Cli</c>.
    /// Declaring this beside its caller would trip that guard — correctly, since the guard's
    /// subject is exactly "a writer that constructs its own options and silently forgets
    /// <see cref="JsonSerializerOptions.NewLine"/>". Keeping both instances in the one file means
    /// the guard needs no exemption and the rule it enforces stays literally true.
    /// </para>
    /// </summary>
    public static readonly JsonSerializerOptions SpecSnapshot = new()
    {
        WriteIndented = true,
        NewLine = "\r\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

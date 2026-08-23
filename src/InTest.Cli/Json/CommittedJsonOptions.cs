using System.Text.Json;

namespace InTest.Cli.Json;

/// <summary>
/// The one <see cref="JsonSerializerOptions"/> instance behind every JSON file `generate` or
/// `fixtures repair` writes to disk: <c>CoverageReport</c>, <c>FixtureDocument</c>,
/// <c>SchemaBundleBuilder</c>, and <c>GenerateCommand</c>'s spec-paths.json manifest. All four
/// are committed artefacts — three are `--check`-compared, the fourth (fixtures/) is hand-edited
/// and diffed by adopters — where a stray LF is not cosmetic; see [crlf-everywhere]
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
/// One instance, not four inline copies: <see cref="JsonSerializerOptions"/> keeps a per-options
/// reflection/metadata cache (documented on the type itself), so four structurally-equal
/// instances paid for that cache four times over for no reason. A single instance also means the
/// "why NewLine" reasoning above lives once, not once per call site with three of the four
/// restating "same fix, same reasoning" instead of the reasoning itself — and
/// JsonWritingOptionsGuardTests (InTest.Cli.Tests) enforces mechanically that no fifth writer
/// reintroduces an inline copy and silently forgets NewLine.
/// </para>
/// </summary>
internal static class CommittedJsonOptions
{
    public static readonly JsonSerializerOptions Value = new() { WriteIndented = true, NewLine = "\r\n" };
}

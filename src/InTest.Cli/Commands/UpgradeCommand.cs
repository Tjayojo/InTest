using System.Text.Json;
using System.Text.RegularExpressions;
using InTest.Cli.Configuration;

namespace InTest.Cli.Commands;

/// <summary>
/// `intest upgrade` — the first command in this project to write inside a file the adopting team
/// owns. CLAUDE.md's ownership table gives "everything else" to the adopting team and says InTest
/// never touches it; this command exists precisely because §5's <c>generate --check</c> refuses on
/// a version mismatch by design (v1-e plan, <c>[exact-match]</c>) and that refusal names a remedy
/// — <c>run `intest upgrade` to adopt &lt;version&gt; deliberately</c> — that has to actually run,
/// or exit 4 points at a command that does not exist (v1-e plan, <c>[paired]</c>). §5: "the one
/// deliberate way to adopt a new tool version… It bumps the manifest and the config together and
/// regenerates, so the version change and its output change land in one reviewable commit rather
/// than arriving disguised as spec drift."
/// <para>
/// <b>Decision 1 — targeted text edit, never parse-and-rewrite (Task 4 Step 1a).</b>
/// <c>intest.json</c> and <c>.config/dotnet-tools.json</c> are adopter-owned; a round trip
/// through <see cref="JsonDocument"/> or <see cref="System.Text.Json.Nodes.JsonNode"/> would
/// reserialize the *whole* document, replacing an adopter's chosen indentation, blank lines and
/// key order with whatever the serializer's own options produce — none of which this command has
/// any business touching, since its only actual job is one string value in each file. So both
/// edits below are regex-bounded substring replacements: everything outside the matched span is
/// copied through byte-for-byte.
/// </para>
/// <para>
/// The plan's own framing for why this matters — "documented as jsonc with inline comments" —
/// turned out to overstate what a real <c>intest.json</c> can actually carry today.
/// <b>Confirmed by direct experiment</b> (a throwaway console probe against
/// <see cref="JsonDocument.Parse(string)"/> with the same default <see cref="JsonDocumentOptions"/>
/// <see cref="ConfigLoader.Load"/> uses — <c>CommentHandling.Disallow</c>,
/// <c>AllowTrailingCommas: false</c>): a real <c>//</c> comment throws
/// <c>'/' is an invalid start of a property name</c>, and a trailing comma throws <c>The JSON
/// object contains a trailing comma at the end which is not supported in this mode</c>. Both are
/// "not valid JSON" refusals from <see cref="ConfigLoader.Load"/> — and that call happens inside
/// <see cref="GenerateCommand.RunAsync"/>, which this command calls into to regenerate *before*
/// either text edit below ever runs (decision 3). So a config actually carrying a comment or a
/// trailing comma never reaches this command's editing code at all; it fails one call earlier,
/// with the same message plain `generate` would give it. The targeted-edit code below is not
/// "comment-safe" — it is simply never asked to be. What it does protect, because nothing else
/// in the pipeline does: an adopter's indentation, unrelated key order, and keys this command
/// never reads (<c>UpgradeCommandTests</c>, InTest.Cli.Tests, pins this directly, by round-
/// tripping a config with unusual formatting and asserting every byte outside the intestVersion
/// value is unchanged — not just asserting the new value is present, which a parse-and-rewrite
/// would also satisfy).
/// </para>
/// <para>
/// <b>Decision 2 — <c>.gitattributes</c>: write if absent, never overwrite (Task 4 Step 1b).</b>
/// The narrow, decided exception to "team-owned files are never touched": <c>init</c> refuses to
/// run against an existing project (exit 3), so a project scaffolded before
/// <c>[lf-everywhere]</c> shipped can never get a fresh <c>.gitattributes</c> from <c>init</c>
/// again — <c>upgrade</c> is the only remaining path that already touches the project, making
/// this the one deliberately widened case rather than a general license to write team-owned
/// files. <see cref="InitCommand.GitattributesContent"/> is the single copy of what gets written,
/// referenced here rather than re-typed, so the two commands cannot silently diverge on what
/// "the" scaffolded <c>.gitattributes</c> actually contains.
/// </para>
/// <para>
/// <b>Decision 3 — <c>schemaVersion</c> migration does not exist here, and building one would be
/// dead code (Task 4 Step 1c).</b> §3 says a major bump "may change… the intest.json schema" and
/// that <c>schemaVersion</c> "moves only on a major" — so in principle, upgrading across a major
/// means writing a new <c>schemaVersion</c> alongside the new <c>intestVersion</c>. In practice,
/// on this branch, <see cref="ConfigLoader.SupportedSchemaVersion"/> is <c>1</c> and has never
/// been anything else — no second schema shape exists anywhere in this repository for a config to
/// migrate *to*. Worse: the CLI's only way to read <c>intest.json</c> at all is
/// <see cref="ConfigLoader.Load"/>, and <see cref="ConfigLoader"/>'s own doc comment states the
/// rule this project holds to elsewhere — "one loader means one answer" — which is exactly why
/// <see cref="ConfigLoader.RequireSupportedSchemaVersion"/> refuses <b>any</b> config whose
/// declared <c>schemaVersion</c> is not already <see cref="ConfigLoader.SupportedSchemaVersion"/>,
/// before this command (which calls straight into <see cref="GenerateCommand.RunAsync"/>, and
/// that calls <see cref="ConfigLoader.Load"/> the same way `generate` always has) ever sees the
/// document. A config on an old schema — precisely the input a migration would need to accept —
/// cannot reach `upgrade` at all under the current loader, and giving `upgrade` a second, more
/// lenient way to read <c>intest.json</c> would be the "two loaders, two answers" defect
/// <see cref="ConfigLoader"/> exists to prevent, not a fix for this one.
/// <para>
/// So this command writes no <c>schemaVersion</c> migration logic. Not because the case is rare,
/// but because there is no reachable input to exercise it against: every config this command can
/// ever act on already declares <c>schemaVersion</c> equal to
/// <see cref="ConfigLoader.SupportedSchemaVersion"/> by construction (the loader refused it
/// otherwise), so writing "if the declared schema is old, migrate it" here would be a branch with
/// no path to it — the same shape <c>ExitCode.VersionMismatch</c>'s own doc comment calls out for
/// a declared-but-unreturnable constant, aimed at a migration instead of a number. Building it
/// anyway would look like a real migration mechanism while being unable to ever run one; the
/// honest statement is that this is future work gated on a second schema version actually
/// existing, not a gap in this command.
/// </para>
/// </para>
/// </summary>
public static class UpgradeCommand
{
    private static readonly Regex IntestVersionProperty = new(
        @"(?<prefix>""intestVersion""\s*:\s*)""(?<value>[^""]*)""",
        RegexOptions.Compiled);

    private static readonly Regex SchemaVersionProperty = new(
        @"(?<indent>[ \t]*)""schemaVersion""\s*:\s*-?\d+(?<comma>\s*,)?",
        RegexOptions.Compiled);

    private static readonly Regex IntestCliToolBlock = new(
        @"""intest\.cli""\s*:\s*\{(?<body>[^{}]*)\}",
        RegexOptions.Compiled);

    private static readonly Regex ToolVersionProperty = new(
        @"(?<prefix>""version""\s*:\s*)""(?<value>[^""]*)""",
        RegexOptions.Compiled);

    public static async Task<int> RunAsync(
        string projectRoot, CancellationToken cancellationToken, TextWriter? report = null)
    {
        report ??= Console.Out;

        // See CliVersion.FallbackVersion's own doc comment, and GenerateCommand.
        // ReportVersionMismatch's build-problem branch, for the defect this refuses: a binary
        // built without version metadata reads its own version as "0.0.0", and writing that into
        // intestVersion would read back as a deliberate adoption of a real version 0.0.0 rather
        // than what it actually is — evidence the running intest itself was built wrong. Checked
        // before regeneration even starts, so a bad build cannot launder its own defect into a
        // config that then looks perfectly fine to every later command.
        if (CliVersion.Current == CliVersion.FallbackVersion)
        {
            Console.Error.WriteLine(NoVersionMetadataMessage(CliVersion.FallbackVersion));
            return ExitCode.ToolError;
        }

        // Regenerate first, through generate's own logic rather than a second copy of it — no
        // re-deriving fixture-drift detection, no re-deriving what "write Generated/ and
        // coverage-report.json wholesale" means. This ordering is also what makes the plan's
        // named half-applied state unreachable *by construction*: nothing below this line runs
        // unless regeneration itself already succeeded, so a failed regeneration can never be
        // followed by an intestVersion bump against output that was never refreshed — the state
        // that "makes --check lie afterwards" (Task 4 Step 2). check: false, deliberately:
        // upgrade's whole purpose is adopting a version regardless of what the old one claimed;
        // running [exact-match]'s gate here would refuse the exact drift upgrade exists to fix.
        var generateExitCode = await GenerateCommand
            .RunAsync(projectRoot, cancellationToken, report, check: false)
            .ConfigureAwait(false);
        if (generateExitCode != ExitCode.Ok)
        {
            return generateExitCode;
        }

        var newVersion = CliVersion.Current;
        var intestJsonPath = Path.Combine(projectRoot, ConfigLoader.FileName);
        var dotnetToolsPath = Path.Combine(projectRoot, ".config", "dotnet-tools.json");

        // Both edits are computed in memory before either is written — the same "compute
        // everything, then write" discipline GenerateCommand.BuildOutputs uses for [no-write],
        // applied here so intest.json and dotnet-tools.json can never land out of step with each
        // other: if the tools pin turns out to be unbumpable (no intest.cli entry to find), the
        // config file above must not already have been rewritten moments earlier only for this
        // command to fail on the very next line.
        string intestJsonText;
        try
        {
            intestJsonText = await File.ReadAllTextAsync(intestJsonPath, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"{ConfigLoader.FileName} at '{intestJsonPath}' could not be read: {ex.Message}");
            return ExitCode.ToolError;
        }

        var newIntestJsonText = SetIntestVersion(intestJsonText, newVersion);

        if (!File.Exists(dotnetToolsPath))
        {
            Console.Error.WriteLine(
                $"No .config/dotnet-tools.json found at '{dotnetToolsPath}'. `intest upgrade` " +
                "pins the tool version there and cannot proceed without it — `intest init` " +
                "scaffolds one for a brand-new project; for an existing one, create it by hand: " +
                "{ \"version\": 1, \"isRoot\": true, \"tools\": { \"intest.cli\": " +
                $"{{ \"version\": \"{newVersion}\", \"commands\": [\"intest\"] }} }} }}.");
            return ExitCode.ToolError;
        }

        var dotnetToolsText = await File.ReadAllTextAsync(dotnetToolsPath, cancellationToken).ConfigureAwait(false);
        if (!TrySetDotnetToolsVersion(dotnetToolsText, newVersion, out var newDotnetToolsText, out var reason))
        {
            Console.Error.WriteLine($".config/dotnet-tools.json at '{dotnetToolsPath}' {reason}");
            return ExitCode.ToolError;
        }

        // Only now, with both edits computed successfully, does anything hit disk.
        await File.WriteAllTextAsync(intestJsonPath, newIntestJsonText, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(dotnetToolsPath, newDotnetToolsText, cancellationToken).ConfigureAwait(false);

        // Decision 2: the one narrow, decided exception to "team-owned files are never touched"
        // — write .gitattributes only if it is not already there, never overwrite an existing
        // one (an adopter may have customised it since `init` wrote it).
        var gitattributesPath = Path.Combine(projectRoot, ".gitattributes");
        if (!File.Exists(gitattributesPath))
        {
            InitCommand.Write(projectRoot, ".gitattributes", InitCommand.GitattributesContent);
        }

        report.WriteLine($"Upgraded intest.json and .config/dotnet-tools.json to intest {newVersion}.");
        return ExitCode.Ok;
    }

    /// <summary>
    /// <paramref name="runningVersionAsFallback"/> is passed in rather than read from
    /// <see cref="CliVersion.Current"/> directly, mirroring
    /// <see cref="GenerateCommand.ReportVersionMismatch"/>'s own reason for the same shape: a
    /// normal test build always carries a real informational version (<c>Directory.Build.props</c>
    /// pins one), so this branch cannot be produced from <see cref="CliVersion.Current"/> in a
    /// test without a binary genuinely built without version metadata. The call site above still
    /// always passes <see cref="CliVersion.FallbackVersion"/>; only the seam moved.
    /// </summary>
    internal static string NoVersionMetadataMessage(string runningVersionAsFallback) =>
        "The running intest carries no version metadata (its assembly has no " +
        "AssemblyInformationalVersionAttribute), so its own version reads as " +
        $"\"{runningVersionAsFallback}\" — a build problem, not a version to adopt. Rebuild " +
        "intest so its assembly carries a real informational version, then re-run `intest upgrade`.";

    /// <summary>
    /// Replaces <c>intestVersion</c>'s value in place when the key already exists, or inserts the
    /// key immediately after <c>schemaVersion</c> — matching the field order <c>InitCommand</c>
    /// itself writes — when it does not (a config predating Task 1, or hand-edited without it;
    /// <c>ConfigLoader</c> already treats that as "no claim made" and loads it happily, but
    /// <c>upgrade</c>'s entire purpose is writing a version claim deliberately, so it adds the
    /// field rather than only ever updating one already there). Everything outside the matched
    /// span is copied through unchanged; see the type doc comment for why this is a targeted edit
    /// rather than a parse-and-rewrite.
    /// </summary>
    internal static string SetIntestVersion(string configText, string newVersion)
    {
        var literal = JsonSerializer.Serialize(newVersion); // quotes included, matching InitCommand's own JsonSpecSource approach

        var existing = IntestVersionProperty.Match(configText);
        if (existing.Success)
        {
            var prefix = existing.Groups["prefix"];
            return string.Concat(
                configText.AsSpan(0, prefix.Index + prefix.Length),
                literal,
                configText.AsSpan(existing.Index + existing.Length));
        }

        var schemaVersion = SchemaVersionProperty.Match(configText);
        if (!schemaVersion.Success)
        {
            // Unreachable in practice: by the time this runs, GenerateCommand.RunAsync has
            // already loaded this exact file through ConfigLoader.Load, which refuses any config
            // with no schemaVersion at all (RequireSupportedSchemaVersion). Thrown rather than
            // silently skipped, so a future change that somehow does make this reachable fails
            // loudly instead of an upgrade quietly not upgrading anything.
            throw new InvalidOperationException(
                $"{ConfigLoader.FileName} has no schemaVersion; ConfigLoader.Load should already " +
                "have refused this file before upgrade reached it.");
        }

        var indent = schemaVersion.Groups["indent"].Value;
        var hasComma = schemaVersion.Groups["comma"].Success;
        var insertAt = schemaVersion.Index + schemaVersion.Length;
        var insertion = hasComma
            ? $"\n{indent}\"intestVersion\": {literal},"
            : $",\n{indent}\"intestVersion\": {literal}";

        return configText.Insert(insertAt, insertion);
    }

    /// <summary>
    /// Bumps only the <c>"intest.cli"</c> tool's own <c>"version"</c> field — never the
    /// manifest's top-level <c>"version": 1</c> (the dotnet-tools.json format version, an
    /// unrelated integer that happens to share a key name one level up) and never another tool's
    /// pin, since Task 4 Step 1a notes <c>.config/dotnet-tools.json</c> "may pin other tools".
    /// Returns <see langword="false"/> with <paramref name="reason"/> set rather than throwing,
    /// because an adopter who removed (or never had) an <c>intest.cli</c> pin is not a crash —
    /// it is a config <c>upgrade</c> cannot act on, the same "refused, not a crash" shape
    /// <see cref="CommandArguments"/> documents for argument refusals.
    /// </summary>
    internal static bool TrySetDotnetToolsVersion(
        string toolsText, string newVersion, out string updatedText, out string? reason)
    {
        var toolBlock = IntestCliToolBlock.Match(toolsText);
        if (!toolBlock.Success)
        {
            updatedText = toolsText;
            reason = "does not pin \"intest.cli\" under \"tools\" — upgrade cannot bump a pin " +
                      "that is not there. Add it by hand: \"intest.cli\": { \"version\": \"" +
                      newVersion + "\", \"commands\": [\"intest\"] }.";
            return false;
        }

        var body = toolBlock.Groups["body"];
        var versionInBody = ToolVersionProperty.Match(body.Value);
        if (!versionInBody.Success)
        {
            updatedText = toolsText;
            reason = "pins \"intest.cli\" but that entry has no \"version\" field for upgrade to bump.";
            return false;
        }

        var literal = JsonSerializer.Serialize(newVersion);
        var prefix = versionInBody.Groups["prefix"];
        var absolutePrefixEnd = body.Index + prefix.Index + prefix.Length;
        var absoluteValueEnd = body.Index + versionInBody.Index + versionInBody.Length;

        updatedText = string.Concat(
            toolsText.AsSpan(0, absolutePrefixEnd),
            literal,
            toolsText.AsSpan(absoluteValueEnd));
        reason = null;
        return true;
    }
}

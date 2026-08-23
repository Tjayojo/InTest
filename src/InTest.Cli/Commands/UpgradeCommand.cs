using System.Text;
using System.Text.Json;
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
/// edits below stay targeted, surgical byte replacements: everything outside the matched span is
/// copied through byte-for-byte, including a leading UTF-8 BOM (see
/// <see cref="TryFindDirectChildProperty"/>'s doc comment for why this is bytes, not text, and
/// what a first review round found wrong with the text version).
/// </para>
/// <para>
/// <b>The first review round found the "targeted edit" itself was not targeted enough.</b> The
/// original implementation located <c>"intestVersion"</c> and <c>"intest.cli"</c> with two plain
/// regexes (<c>"intestVersion"\s*:\s*"..."</c> and <c>"intest\.cli"\s*:\s*\{...\}</c>) that matched
/// the <b>earliest textual occurrence anywhere in the file</b> — not "the top-level key", which is
/// what every doc comment in this file already claimed. A config carrying an unrelated nested key
/// of the same name — <c>{ "vendorNotes": { "intestVersion": "pinned-by-us" }, "intestVersion":
/// "0.0.1" }</c>, exactly the shape <c>[read-what-init-wrote]</c> and
/// <c>ConfigLoaderTests.IgnoresSettingsItDoesNotRead</c> both promise must still load — corrupted
/// the adopter's own nested key and left the real, top-level <c>intestVersion</c> unbumped, exit
/// 0. <c>IntestCliToolBlock</c> had the identical shape one level down: it never checked its match
/// was actually inside <c>"tools"</c>, even though its own refusal message asserted it was. Fixed
/// by replacing both regexes with <see cref="TryFindDirectChildProperty"/>, built on
/// <see cref="Utf8JsonReader"/> used purely as a read-only scanner (never to re-serialize
/// anything): it walks brace/bracket structure the way a regex cannot, so "direct child of this
/// exact object" is well-defined regardless of what same-named keys exist elsewhere in the
/// document, at any depth, including inside string values. The original test for this
/// (<c>PreservesUnusualFormattingKeyOrderAndUnknownKeysInIntestJson</c>) could not discriminate
/// the defect because its unknown key sits *after* <c>intestVersion</c> and contains no matching
/// text — <c>UpgradeCommandTests</c> now also has a case with the colliding key sitting *before*
/// and *nested*, which the old regex-based version fails.
/// </para>
/// <para>
/// The plan's own framing for why a targeted edit matters — "documented as jsonc with inline
/// comments" — turned out to overstate what a real <c>intest.json</c> can actually carry today.
/// <b>Confirmed by direct experiment</b> (a throwaway console probe against
/// <see cref="JsonDocument.Parse(string)"/> with the same default <see cref="JsonDocumentOptions"/>
/// <see cref="ConfigLoader.Load"/> uses — <c>CommentHandling.Disallow</c>,
/// <c>AllowTrailingCommas: false</c>): a real <c>//</c> comment throws
/// <c>'/' is an invalid start of a property name</c>, and a trailing comma throws <c>The JSON
/// object contains a trailing comma at the end which is not supported in this mode</c>. Both are
/// "not valid JSON" refusals from <see cref="ConfigLoader.Load"/> — and that call happens inside
/// <see cref="GenerateCommand.RunAsync"/>, which this command calls into to regenerate before
/// <see cref="SetIntestVersion"/> ever runs (decision 3 below explains why that ordering is now
/// load-bearing for a second reason too). So a config actually carrying a comment or a trailing
/// comma never reaches this command's editing code at all; it fails one call earlier, with the
/// same message plain `generate` would give it. The targeted-edit code below is not "comment-safe"
/// — it is simply never asked to be. What it does protect, because nothing else in the pipeline
/// does: an adopter's indentation, unrelated key order, and keys this command never reads
/// (<c>UpgradeCommandTests</c>, InTest.Cli.Tests, pins this directly, by round-tripping a config
/// with unusual formatting and asserting every byte outside the intestVersion value is unchanged —
/// not just asserting the new value is present, which a parse-and-rewrite would also satisfy).
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
/// "the" scaffolded <c>.gitattributes</c> actually contains. Because this is the one command that
/// can create a team-owned file from nothing, the success report below says so explicitly when it
/// happens — <c>generate</c> and <c>fixtures repair</c> only ever touch InTest-owned paths, so
/// neither of their reports has ever needed this kind of line before.
/// </para>
/// <para>
/// <b>Decision 3 — <c>schemaVersion</c> migration does not exist here, and the gap is structural,
/// not missing code (Task 4 Step 1c).</b> §3 says a major bump "may change… the intest.json
/// schema" and that <c>schemaVersion</c> "moves only on a major" — so in principle, upgrading
/// across a major means writing a new <c>schemaVersion</c> alongside the new <c>intestVersion</c>.
/// A reviewer built the case directly: set <see cref="ConfigLoader.SupportedSchemaVersion"/> to
/// <c>2</c>, republished the CLI, and ran that build's <c>upgrade</c> against an ordinary
/// <c>schemaVersion: 1</c> project. Result: exit 2, config untouched — precisely the input a major
/// migration exists to accept, refused outright. The reason is not a missing branch in this file.
/// <see cref="ConfigLoader.Load"/> is the CLI's <b>only</b> way to read <c>intest.json</c>, and its
/// own doc comment states the rule this project holds to elsewhere — "one loader means one
/// answer" — which is exactly why <see cref="ConfigLoader.RequireSupportedSchemaVersion"/> refuses
/// <b>any</b> config whose declared <c>schemaVersion</c> is not already
/// <see cref="ConfigLoader.SupportedSchemaVersion"/>, before this command (which calls straight
/// into <see cref="GenerateCommand.RunAsync"/>, and that calls <see cref="ConfigLoader.Load"/> the
/// same way `generate` always has) ever sees the document. A config on an old schema — precisely
/// the input a migration would need to accept — cannot reach `upgrade` at all under the current
/// loader, and giving `upgrade` a second, more lenient way to read <c>intest.json</c> would be the
/// "two loaders, two answers" defect <see cref="ConfigLoader"/> exists to prevent, not a fix for
/// this one.
/// <para>
/// So this command writes no <c>schemaVersion</c> migration logic, and shipping one is not a
/// matter of adding a branch to <c>UpgradeCommand</c>. It requires changing
/// <see cref="ConfigLoader"/> first — teaching it to accept (or itself upgrade) an old
/// <c>schemaVersion</c> rather than refuse it on sight — because as long as "one loader, one
/// answer" refuses old schemas before this command's own regenerate-first call ever runs, there is
/// no reachable input for a migration branch here to act on. The regenerate-first ordering
/// (decision 4 below) compounds this: even a hypothetical looser loader would still hand a
/// pre-migration document to <c>GenerateCommand.RunAsync</c> before this command's own edits run,
/// so the render itself would need to already understand the old schema. Building a migration
/// branch in this file today would look like a real mechanism while being structurally unable to
/// ever run one — the honest statement is that this is future work gated on <c>ConfigLoader</c>
/// changing first, not a gap in this command that a local fix could close.
/// </para>
/// </para>
/// <para>
/// <b>Decision 4 — regenerate first, and every check that can still fail runs before that (Task 4
/// Step 2).</b> Regeneration goes through <c>generate</c>'s own logic rather than a second copy of
/// it — no re-deriving fixture-drift detection, no re-deriving what "write Generated/ and
/// coverage-report.json wholesale" means. A first review round found this ordering applied to only
/// half the command: regeneration ran, and only <i>then</i> did the <c>.config/dotnet-tools.json</c>
/// checks run — missing manifest, missing <c>intest.cli</c> pin, pin with no <c>version</c> field,
/// even an unreadable <c>intest.json</c> on the re-read — each one able to return
/// <see cref="ExitCode.ToolError"/> (§5: "the tool did not do the work it was asked to do, and
/// nothing was written") after <c>Generated/</c> had already been rewritten wholesale. Measured:
/// delete <c>.config/dotnet-tools.json</c> and run `upgrade` — exit 2, with
/// <c>coverage-report.json</c> and every <c>Generated/*.g.cs</c> file freshly written on disk. That
/// is also the exact inverse of the half-applied state this same step's own comment already
/// guarded against below: fresh output committed against an <b>un-bumped</b> version marker, from a
/// command that reported it did nothing — which is precisely the "legitimate tool upgrade arriving
/// disguised as spec drift" §5 gives as this command's whole reason to exist. Every one of those
/// four checks is a pure read (or a computation over bytes already read) with no dependency on
/// regeneration having happened, so all of it now runs first; regeneration is the first line in
/// this method capable of writing anything, and only runs once every one of them has already
/// succeeded. <see cref="CliVersion.Current"/>'s fallback guard was already placed correctly ahead
/// of everything, before this fix — it was the model this ordering now matches everywhere else.
/// </para>
/// </summary>
public static class UpgradeCommand
{
    public static async Task<int> RunAsync(
        string projectRoot, CancellationToken cancellationToken, TextWriter? report = null)
    {
        report ??= Console.Out;

        // See CliVersion.FallbackVersion's own doc comment, and GenerateCommand.
        // ReportVersionMismatch's build-problem branch, for the defect this refuses: a binary
        // built without version metadata reads its own version as "0.0.0", and writing that into
        // intestVersion would read back as a deliberate adoption of a real version 0.0.0 rather
        // than what it actually is — evidence the running intest itself was built wrong. Checked
        // before anything else even starts, so a bad build cannot launder its own defect into a
        // config that then looks perfectly fine to every later command.
        if (CliVersion.Current == CliVersion.FallbackVersion)
        {
            Console.Error.WriteLine(NoVersionMetadataMessage(CliVersion.FallbackVersion));
            return ExitCode.ToolError;
        }

        var newVersion = CliVersion.Current;
        var intestJsonPath = Path.Combine(projectRoot, ConfigLoader.FileName);
        var dotnetToolsPath = Path.Combine(projectRoot, ".config", "dotnet-tools.json");

        // Decision 4: every check below is a pure read, or a computation over bytes already read
        // — nothing here depends on regeneration, so none of it waits for regeneration either.
        // Reading as bytes rather than text (File.ReadAllBytesAsync, not ReadAllTextAsync) is also
        // what keeps a leading UTF-8 BOM alive: ReadAllTextAsync silently consumes one and
        // WriteAllTextAsync never re-emits it, which a first review round measured directly (first
        // byte 0xEF before this file's edit, 0x7B — a bare '{' — after). A BOM sits outside every
        // matched span this command ever touches, so it must survive the same way any other byte
        // outside the span does.
        byte[] intestJsonBytes;
        try
        {
            intestJsonBytes = await File.ReadAllBytesAsync(intestJsonPath, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"{ConfigLoader.FileName} at '{intestJsonPath}' could not be read: {ex.Message}");
            return ExitCode.ToolError;
        }

        if (!File.Exists(dotnetToolsPath))
        {
            Console.Error.WriteLine(
                $"No .config/dotnet-tools.json found at '{dotnetToolsPath}'. `intest upgrade` " +
                "pins the tool version there and cannot proceed without it — `intest init` " +
                "scaffolds one for a brand-new project; for an existing one, create it by hand:" +
                Environment.NewLine + InitCommand.DotnetToolsJsonContent(newVersion));
            return ExitCode.ToolError;
        }

        byte[] dotnetToolsBytes;
        try
        {
            dotnetToolsBytes = await File.ReadAllBytesAsync(dotnetToolsPath, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($".config/dotnet-tools.json at '{dotnetToolsPath}' could not be read: {ex.Message}");
            return ExitCode.ToolError;
        }

        if (!TrySetDotnetToolsVersion(dotnetToolsBytes, newVersion, out var newDotnetToolsBytes, out var reason))
        {
            Console.Error.WriteLine($".config/dotnet-tools.json at '{dotnetToolsPath}' {reason}");
            return ExitCode.ToolError;
        }

        // Only now — every pure-read check above already succeeded — does anything capable of
        // writing run at all. check: false, deliberately: upgrade's whole purpose is adopting a
        // version regardless of what the old one claimed; running [exact-match]'s gate here would
        // refuse the exact drift upgrade exists to fix.
        var generateExitCode = await GenerateCommand
            .RunAsync(projectRoot, cancellationToken, report, check: false)
            .ConfigureAwait(false);
        if (generateExitCode != ExitCode.Ok)
        {
            return generateExitCode;
        }

        // intest.json's own edit is computed only now, after regeneration, even though the bytes
        // were read before it (decision 4 above) — reusing them rather than re-reading is safe
        // because nothing between that read and here writes to intest.json. The ordering itself is
        // still load-bearing: SetIntestVersion's "no schemaVersion" branch is unreachable only
        // because GenerateCommand.RunAsync, just above, has already loaded this exact byte content
        // through ConfigLoader.Load, which refuses any config missing schemaVersion before this
        // method would ever see it (decision 3 explains why that guarantee cannot be moved earlier
        // without a second, looser loader).
        var newIntestJsonBytes = SetIntestVersion(intestJsonBytes, newVersion);

        await File.WriteAllBytesAsync(intestJsonPath, newIntestJsonBytes, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(dotnetToolsPath, newDotnetToolsBytes, cancellationToken).ConfigureAwait(false);
        // A Ctrl+C between these two writes can half-apply the upgrade: intest.json bumped and the
        // tools pin not, or vice versa. Not guarded further, by choice rather than oversight — both
        // SetIntestVersion and TrySetDotnetToolsVersion are idempotent against their own
        // already-bumped state (an intestVersion that already reads newVersion is matched and
        // replaced with the identical literal; likewise the tools pin), so re-running `intest
        // upgrade` after an interrupted run finishes the job instead of corrupting it further. True
        // atomicity across two independently-owned files would need a transaction log or a
        // lockstep temp-file/rename dance that neither file's ownership model asks for; re-run-to-
        // recover was judged the better trade for a failure mode this narrow (a signal landing in
        // the ~microseconds between two sequential in-process writes).

        var gitattributesPath = Path.Combine(projectRoot, ".gitattributes");
        var scaffoldedGitattributes = false;
        if (!File.Exists(gitattributesPath))
        {
            // Decision 2: the one narrow, decided exception to "team-owned files are never
            // touched" — write .gitattributes only if it is not already there, never overwrite an
            // existing one (an adopter may have customised it since `init` wrote it).
            InitCommand.Write(projectRoot, ".gitattributes", InitCommand.GitattributesContent);
            scaffoldedGitattributes = true;
        }

        report.WriteLine(
            $"Upgraded intest.json and .config/dotnet-tools.json to intest {newVersion}." +
            (scaffoldedGitattributes
                ? " Also scaffolded .gitattributes, which this project did not have yet — see " +
                  "InitCommand.GitattributesContent for what it pins and why."
                : string.Empty));
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
    /// Replaces <c>intestVersion</c>'s value in place when the key already exists as a direct
    /// child of the document's root object, or inserts the key immediately after the root's own
    /// <c>schemaVersion</c> — matching the field order <c>InitCommand</c> itself writes — when it
    /// does not (a config predating Task 1, or hand-edited without it; <c>ConfigLoader</c> already
    /// treats that as "no claim made" and loads it happily, but <c>upgrade</c>'s entire purpose is
    /// writing a version claim deliberately, so it adds the field rather than only ever updating
    /// one already there). Everything outside the matched span is copied through unchanged,
    /// including a leading UTF-8 BOM — see the type doc comment for why this operates on bytes
    /// rather than text, and for the nested-key defect a first review round found in the version
    /// that used to search for <c>intestVersion</c> anywhere in the file rather than only at the
    /// root.
    /// </summary>
    internal static byte[] SetIntestVersion(byte[] configBytes, string newVersion)
    {
        var bomLength = GetUtf8BomLength(configBytes);
        var body = configBytes.AsSpan(bomLength);
        var literal = JsonSerializer.Serialize(newVersion); // quotes included, matching InitCommand's own JsonSpecSource approach
        var literalBytes = Encoding.UTF8.GetBytes(literal);

        if (TryFindDirectChildProperty(body, "intestVersion", out var existing))
        {
            return Splice(configBytes, bomLength + existing.ValueStart, existing.ValueLength, literalBytes);
        }

        if (!TryFindDirectChildProperty(body, "schemaVersion", out var schemaVersion))
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

        var nameStart = bomLength + schemaVersion.NameStart;
        var afterValue = bomLength + schemaVersion.ValueEnd;
        var indent = IndentBefore(configBytes, nameStart, bomLength);
        var (hasComma, insertAt) = ScanOptionalTrailingComma(configBytes, afterValue);
        // Derived from the file being edited, not hard-coded: a config predating Task 1 — the
        // exact case this insert path exists to migrate — is CRLF whenever it was checked out with
        // core.autocrlf=true, because the scaffolded .gitattributes explicitly pins Generated/**,
        // coverage-report.json and fixtures/**/*.json but never intest.json itself — intest.json's
        // line endings follow whatever the checking-out machine's own core.autocrlf produces
        // rather than a fixed convention. A first review round measured a hard-coded "\n" planting
        // a lone LF line inside an otherwise-CRLF file; this reads the file's own convention
        // instead.
        var newline = DetectFileNewline(configBytes);

        var insertionText = hasComma
            ? $"{newline}{indent}\"intestVersion\": {literal},"
            : $",{newline}{indent}\"intestVersion\": {literal}";

        return Splice(configBytes, insertAt, 0, Encoding.UTF8.GetBytes(insertionText));
    }

    /// <summary>
    /// Bumps only the <c>"intest.cli"</c> tool's own <c>"version"</c> field, and only when
    /// <c>"intest.cli"</c> is itself a direct child of a top-level <c>"tools"</c> object — never
    /// the manifest's top-level <c>"version": 1</c> (the dotnet-tools.json format version, an
    /// unrelated integer that happens to share a key name one level up), never another tool's pin
    /// (Task 4 Step 1a notes <c>.config/dotnet-tools.json</c> "may pin other tools"), and never a
    /// same-named key nested somewhere else in the document that has nothing to do with the tools
    /// manifest at all — the defect a first review round found in this method's previous,
    /// unanchored-regex form (see the type doc comment). Returns <see langword="false"/> with
    /// <paramref name="reason"/> set rather than throwing, because an adopter who removed (or
    /// never had) an <c>intest.cli</c> pin is not a crash — it is a config <c>upgrade</c> cannot
    /// act on, the same "refused, not a crash" shape <see cref="CommandArguments"/> documents for
    /// argument refusals.
    /// </summary>
    internal static bool TrySetDotnetToolsVersion(
        byte[] toolsBytes, string newVersion, out byte[] updatedBytes, out string? reason)
    {
        var bomLength = GetUtf8BomLength(toolsBytes);
        var body = toolsBytes.AsSpan(bomLength);

        var noIntestCliPin = "does not pin \"intest.cli\" under \"tools\" — upgrade cannot bump a pin " +
                              "that is not there. Add it by hand: \"intest.cli\": { \"version\": \"" +
                              newVersion + "\", \"commands\": [\"intest\"] }.";

        if (!TryFindDirectChildProperty(body, "tools", out var tools))
        {
            updatedBytes = toolsBytes;
            reason = noIntestCliPin;
            return false;
        }

        var toolsBody = body.Slice(tools.ValueStart, tools.ValueLength);
        if (!TryFindDirectChildProperty(toolsBody, "intest.cli", out var intestCli))
        {
            updatedBytes = toolsBytes;
            reason = noIntestCliPin;
            return false;
        }

        var intestCliBody = toolsBody.Slice(intestCli.ValueStart, intestCli.ValueLength);
        if (!TryFindDirectChildProperty(intestCliBody, "version", out var version))
        {
            updatedBytes = toolsBytes;
            reason = "pins \"intest.cli\" but that entry has no \"version\" field for upgrade to " +
                      "bump — add one by hand: \"version\": \"" + newVersion + "\".";
            return false;
        }

        var literalBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(newVersion));
        var absoluteValueStart = bomLength + tools.ValueStart + intestCli.ValueStart + version.ValueStart;

        updatedBytes = Splice(toolsBytes, absoluteValueStart, version.ValueLength, literalBytes);
        reason = null;
        return true;
    }

    /// <summary>The byte span of one JSON value, relative to whatever span it was located in.</summary>
    private readonly record struct JsonProperty(int NameStart, int ValueStart, int ValueLength)
    {
        public int ValueEnd => ValueStart + ValueLength;
    }

    /// <summary>
    /// Locates a property that is a <b>direct child</b> — not a descendant at any depth — of the
    /// JSON object occupying <paramref name="objectJson"/>, and returns the byte span of its
    /// <i>value</i> token (for a string, the span includes the surrounding quotes; for an object
    /// or array, the whole nested value). Built on <see cref="Utf8JsonReader"/> used purely as a
    /// read-only scanner over already-valid JSON: it never writes or re-serializes anything, so
    /// this call by itself never touches adopter formatting — see <see cref="SetIntestVersion"/>
    /// and <see cref="TrySetDotnetToolsVersion"/> for what the caller then does with the returned
    /// span.
    /// <para>
    /// This replaces two plain regexes (<c>"intestVersion"\s*:\s*"..."</c> and
    /// <c>"intest\.cli"\s*:\s*\{...\}</c>) that a first review round found matched the earliest
    /// textual occurrence anywhere in the file, not "the top-level key" every doc comment in this
    /// file already claimed — proven with <c>{ "vendorNotes": { "intestVersion": "pinned-by-us" },
    /// "intestVersion": "0.0.1" }</c>, which the regex silently rewrote inside
    /// <c>vendorNotes</c> while leaving the real key at "0.0.1" and exiting 0. Constructing a
    /// fresh <see cref="Utf8JsonReader"/> over exactly <paramref name="objectJson"/> — rather than
    /// reading the whole document once and tracking <see cref="Utf8JsonReader.CurrentDepth"/> — is
    /// what makes "direct child of this object" well-defined for a nested object like
    /// <c>"tools"</c> too: a sub-slice that is itself a complete, valid object value resets the
    /// reader's own depth counting to the same state as reading a top-level document, so this one
    /// method handles both the document root and any object nested inside it identically —
    /// <see cref="TrySetDotnetToolsVersion"/> calls it twice, once for <c>"tools"</c> at the root
    /// and again for <c>"intest.cli"</c> inside whatever span the first call returned.
    /// </para>
    /// </summary>
    private static bool TryFindDirectChildProperty(
        ReadOnlySpan<byte> objectJson, string propertyName, out JsonProperty property)
    {
        property = default;
        try
        {
            var reader = new Utf8JsonReader(objectJson);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                var isMatch = reader.ValueTextEquals(propertyName);
                var nameStart = checked((int)reader.TokenStartIndex);
                reader.Read(); // advance to the value token
                var valueStart = checked((int)reader.TokenStartIndex);
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                {
                    reader.Skip(); // consume the whole nested value so the loop sees its sibling next
                }
                var valueEnd = checked((int)reader.BytesConsumed);

                if (isMatch)
                {
                    property = new JsonProperty(nameStart, valueStart, valueEnd - valueStart);
                    return true;
                }
            }

            return false;
        }
        catch (JsonException)
        {
            // Not reachable in the actual call chain — every byte array reaching this method has
            // already been proven valid JSON by ConfigLoader.Load inside GenerateCommand.RunAsync
            // (decision 4) — but "not found" is the correct answer for malformed input regardless
            // of why it could not be scanned, and it is a cheaper, more honest failure than an
            // unhandled exception for a scanner that is not the JSON document's authority anyway.
            return false;
        }
    }

    private static byte[] Splice(byte[] original, int spanStart, int spanLength, ReadOnlySpan<byte> replacement)
    {
        var result = new byte[original.Length - spanLength + replacement.Length];
        original.AsSpan(0, spanStart).CopyTo(result);
        replacement.CopyTo(result.AsSpan(spanStart));
        original.AsSpan(spanStart + spanLength).CopyTo(result.AsSpan(spanStart + replacement.Length));
        return result;
    }

    private static int GetUtf8BomLength(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;

    /// <summary>The run of spaces/tabs immediately before <paramref name="nameStart"/>, matching
    /// the indentation of whatever line the property name sits on.</summary>
    private static string IndentBefore(byte[] bytes, int nameStart, int bomLength)
    {
        var i = nameStart;
        while (i > bomLength && (bytes[i - 1] == (byte)' ' || bytes[i - 1] == (byte)'\t'))
        {
            i--;
        }

        return Encoding.UTF8.GetString(bytes, i, nameStart - i);
    }

    private static bool IsJsonWhitespace(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r';

    /// <summary>Skips whitespace from <paramref name="start"/> and reports whether a comma follows
    /// — and, if so, the index immediately after it.</summary>
    private static (bool HasComma, int EndIndex) ScanOptionalTrailingComma(byte[] bytes, int start)
    {
        var i = start;
        while (i < bytes.Length && IsJsonWhitespace(bytes[i]))
        {
            i++;
        }

        return i < bytes.Length && bytes[i] == (byte)',' ? (true, i + 1) : (false, start);
    }

    /// <summary>The file's own newline convention, taken from its first line break — LF if none
    /// is found, since there is nothing else to derive it from.</summary>
    private static string DetectFileNewline(byte[] bytes)
    {
        var lf = Array.IndexOf(bytes, (byte)'\n');
        return lf > 0 && bytes[lf - 1] == (byte)'\r' ? "\r\n" : "\n";
    }
}

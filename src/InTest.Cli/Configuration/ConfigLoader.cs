using System.Text.Json;
using InTest.Cli.Naming;
using InTest.Cli.Spec;

namespace InTest.Cli.Configuration;

/// <summary>
/// Reads and validates <c>intest.json</c> — the whole file, in one place, before any command
/// acts on it. Mirrors <see cref="Spec.SpecLoader"/> deliberately: a static loader, a
/// <see cref="ConfigLoadException"/> carrying a message written for the adopter, and a caller
/// that catches it beside <c>SpecLoadException</c> and returns exit 2 (§5: "Tool error —
/// unparseable spec, <c>spec.source</c> missing, malformed <c>intest.json</c>, unhandled
/// exception. Nothing was written").
/// <para>
/// This exists as one type rather than as checks at each read site because the read sites are
/// what produced the defect it fixes. <c>intest.json</c> is hand-edited by adopters, so every
/// key is an untrusted input; validating each setting where it happened to be consumed left the
/// settings next to it unguarded, and left <c>fixtures repair</c> — which reads a subset — with a
/// different idea of what a valid config is than <c>generate</c> had. One loader means one answer.
/// </para>
/// <para>
/// This is the adopter-config rule, and it is the third of four deliberately distinct rules in
/// this repository — see <see cref="CSharpIdentifier.TryValidateDottedName"/>, which it calls.
/// Spec text naming a file is refused (<see cref="Fixtures.FixtureDocument.TryValidateOperationKey"/>);
/// spec text reaching a C# string literal is escaped (<see cref="CSharpLiteral"/>); adopter config
/// reaching declaration syntax is validated here, at load, before anything is written; adopter
/// input reaching JSON and XML syntax — the <c>--spec</c> value <c>InitCommand</c> writes into
/// <c>intest.json</c> and the generated <c>.csproj</c> — is escaped (<see cref="MSBuildPropertyValue"/>),
/// with a narrow refusal for the residue XML 1.0 cannot represent in any form, because both
/// grammars can carry a path losslessly. Merging them is a known hazard: escaping cannot rescue
/// an invalid identifier, and validation is the wrong tool for text that is legitimately
/// arbitrary.
/// </para>
/// </summary>
public static class ConfigLoader
{
    public const string FileName = "intest.json";

    /// <summary>
    /// The one <c>schemaVersion</c> this build understands. §5: it "moves only on a major".
    /// <c>InitCommand</c> writes this same value into every scaffolded config.
    /// </summary>
    public const int SupportedSchemaVersion = 1;

    private const string SpecSourceRule =
        "It must be the path to the OpenAPI document, relative to the project directory — for " +
        "example \"../Orders/bin/Debug/net10.0/orders.json\" — or the URL it is served from, for " +
        "example \"https://orders-staging.example.com/swagger/v1/swagger.json\".";

    private const string SpecSectionRule =
        "It must declare spec.source, the path to the OpenAPI document relative to the project " +
        "directory (or the URL it is served from) — for example " +
        "\"spec\": { \"source\": \"../Orders/bin/Debug/net10.0/orders.json\" }.";

    private const string ProjectSectionRule =
        "It must declare project.rootNamespace, project.testBaseClass and project.framework — " +
        "for example \"project\": { \"rootNamespace\": \"Orders.ApiTests\", " +
        "\"testBaseClass\": \"Orders.ApiTests.OrdersTestBase\", \"framework\": \"mstest\" }.";

    private const string RootNamespaceRule =
        "It must be the C# namespace generated tests are declared in — for example \"Orders.ApiTests\".";

    private const string TestBaseClassRule =
        "It must be the C# name of the class generated tests derive from — for example " +
        "\"Orders.ApiTests.OrdersTestBase\".";

    /// <summary>
    /// <c>mstest</c>, <c>xunit</c> and <c>nunit</c> are the values accepted — all three
    /// frameworks §3 designs InTest for now ship. Until this pack, this list "grew the day a
    /// framework shipped, not before," and the accompanying adopter-facing sentence named
    /// whichever framework was still missing as "not supported yet." That framing no longer
    /// applies: with nunit added there is no fourth framework on §3's roadmap left to name as
    /// forthcoming, so the sentence below states the closed set plainly rather than leaving a
    /// dangling "yet" that would promise a framework that does not exist.
    /// </summary>
    private const string FrameworkRule =
        "It must be the test framework generated tests target: \"mstest\", \"xunit\" or " +
        "\"nunit\" — the three frameworks InTest is designed to support (§3).";

    // The optional "client" section (docs/superpowers/plans/2026-08-25-intest-typed-client-invocation.md,
    // `[convention-plus-override]`). Unlike every rule above, this section is allowed to be
    // entirely absent — see ReadOptionalClientConfig's own doc comment for why that is optional
    // while the two fields inside it, once the section itself is present, are not.
    private const string ClientSectionRule =
        "When present, it must declare client.kind (\"kiota\", \"nswag\" or \"refit\") and " +
        "client.typeName (the generated client's fully-qualified C# type name) together — for " +
        "example \"client\": { \"kind\": \"kiota\", \"typeName\": \"Orders.ApiClient.OrdersApiClient\" }.";

    private const string ClientKindRule =
        "It must be the generator that produced the client: \"kiota\", \"nswag\" or \"refit\".";

    private const string ClientTypeNameRule =
        "It must be the generated client's fully-qualified C# type name — for example " +
        "\"Orders.ApiClient.OrdersApiClient\".";

    public static LoadedConfig Load(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        var path = Path.Combine(projectRoot, FileName);
        if (!File.Exists(path))
        {
            // Preserved verbatim from the two commands this replaced: adopters and the getting
            // started guide both already know this sentence.
            throw new ConfigLoadException($"No {FileName} found in '{projectRoot}'. Run `intest init` first.");
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            throw new ConfigLoadException($"{FileName} at '{path}' could not be read: {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ConfigLoadException($"{FileName} at '{path}' could not be read: {ex.Message}", ex);
        }

        using var document = Parse(text, path);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ConfigLoadException(
            $"{FileName} must be a JSON object with 'spec' and 'project' sections, but its top " +
            $"level is {Describe(root.ValueKind)}. Compare it against the {FileName} that " +
            "`intest init` writes.");
        }

        RequireSupportedSchemaVersion(root);
        var intestVersion = ReadOptionalIntestVersion(root);

        var spec = RequireSection(root, "spec", SpecSectionRule);
        var specSource = RequireString(spec, "spec.source", "source", SpecSourceRule);

        // Not folded into RequireString: an empty spec.source is the one value on this surface
        // that never failed loudly. Path.Combine(projectRoot, "") is projectRoot, so SpecLoader
        // reported "Spec file not found:" against the project directory — a true sentence about
        // the wrong thing, which sends the adopter hunting for a file that was never named.
        if (string.IsNullOrWhiteSpace(specSource))
        {
            throw new ConfigLoadException($"spec.source in {FileName} is empty. {SpecSourceRule}");
        }

        // A URL spec.source is now a supported kind of source (§9), so what is left to check is
        // that it is a well-formed one. The question this guard asks changed; the reason it sits
        // here did not. Until URL support landed, this branch refused every URL outright, because
        // Path.Combine(projectRoot, "https://example.com/x.json") appends the URL as a relative
        // segment and SpecLoader then reported "Spec file not found:" against a path spliced out
        // of a Windows separator and a URL — a path the adopter never wrote, phrased as a file
        // that is merely missing. Measured at the time, not inferred:
        // "Spec file not found: <projectRoot>\https://example.com/openapi.json" at exit 2.
        //
        // A value that clears SpecLoader.IsUrl but not TryValidateUrl ("https://" alone) would
        // reach exactly that defect today, which is why the guard survives its own original
        // purpose rather than being deleted with it.
        //
        // Judged here rather than only at `init` because `init` is not how most URLs get here.
        // getting started's Phase 1 instructs adopters to point spec.source at the URL — a hand
        // edit, which no argument guard sees. One loader covers generate and fixtures repair
        // both, which is this type's whole reason for being.
        var specSourceIsUrl = SpecLoader.IsUrl(specSource);
        if (specSourceIsUrl && !SpecFetcher.TryValidateUrl(specSource, "spec.source", out var urlReason))
        {
            throw new ConfigLoadException($"{urlReason} {SpecSourceRule}");
        }

        var project = RequireSection(root, "project", ProjectSectionRule);
        var rootNamespace = RequireString(project, "project.rootNamespace", "rootNamespace", RootNamespaceRule);
        var testBaseClass = RequireString(project, "project.testBaseClass", "testBaseClass", TestBaseClassRule);

        // rootNamespace and testBaseClass reach mstest-class.scriban as declaration syntax
        // ("namespace {{ namespace }};" and ": {{ base_class }}"), not as a string literal — no
        // escaping makes an invalid identifier resolve there, so refusing a bad value before
        // anything is written is the only fix. The type checks above are what let this run at
        // all: GetProperty/GetString threw on a missing key or a non-string first, so this guard
        // only ever saw a string or null and the two throwing cases went to the catch-all.
        if (!CSharpIdentifier.TryValidateDottedName(rootNamespace, "project.rootNamespace", out var namespaceReason))
        {
            throw new ConfigLoadException(
            $"{namespaceReason} Change project.rootNamespace in {FileName} — for example \"Orders.ApiTests\".");
        }
        if (!CSharpIdentifier.TryValidateDottedName(testBaseClass, "project.testBaseClass", out var baseClassReason))
        {
            throw new ConfigLoadException(
            $"{baseClassReason} Change project.testBaseClass in {FileName} — for example \"Orders.ApiTests.OrdersTestBase\".");
        }

        var framework = RequireSupportedFramework(project);
        var client = ReadOptionalClientConfig(root);

        return new LoadedConfig(specSource, rootNamespace, testBaseClass, framework, intestVersion, specSourceIsUrl, client);
    }

    /// <summary>
    /// The optional "client" section that opts a project into routing generated Success cases
    /// through an adopter-owned, pre-generated API client instead of raw HTTP. Absent entirely →
    /// <see langword="null"/>, and every downstream behaviour is unchanged — §5's "config grows by
    /// addition" already covers an unrecognised key
    /// (<c>ConfigLoaderTests.IgnoresSettingsItDoesNotRead</c>), and an absent, recognised-but-optional
    /// section is the same shape one field earlier: <see cref="ReadOptionalIntestVersion"/> already
    /// established that pattern for <c>intestVersion</c>.
    /// <para>
    /// Mirrors <see cref="ReadOptionalIntestVersion"/> in structure — <c>TryGetProperty</c>
    /// early-null, a <c>ValueKind</c> check, a named rule string, a <see cref="ConfigLoadException"/>
    /// naming the dotted setting — but diverges once the section itself is present:
    /// <c>intestVersion</c> has nothing else inside it that could be half-written, while a
    /// <c>client</c> section can name a <c>kind</c> without a <c>typeName</c> (or vice versa), and
    /// <c>ClientCallPlanner</c> needs both to resolve anything at all. Calling
    /// <see cref="RequireString"/> for each field, rather than hand-rolling a bespoke
    /// "which one is missing" branch, gets that naming for free — whichever field
    /// <c>RequireString</c> cannot find throws first, naming exactly that dotted setting
    /// ("intest.json has no client.kind." / "…has no client.typeName."), and cannot drift from the
    /// message every other required-string setting on this surface already uses.
    /// </para>
    /// </summary>
    private static LoadedClientConfig? ReadOptionalClientConfig(JsonElement root)
    {
        if (!root.TryGetProperty("client", out var declared))
        {
            return null;
        }

        if (declared.ValueKind != JsonValueKind.Object)
        {
            throw new ConfigLoadException(
            $"client in {FileName} is {Describe(declared.ValueKind)}, not an object: " +
            $"{Quote(declared)}. {ClientSectionRule}");
        }

        var kind = RequireString(declared, "client.kind", "kind", ClientKindRule);
        if (kind is not ("kiota" or "nswag" or "refit"))
        {
            // Ordinal-exact lowercase, the same discipline RequireSupportedFramework applies to
            // project.framework and for the same reason: adopter-facing JSON, not a C# identifier
            // with case-insensitive lookup — no other spelling reaches Planning.ClientKind.
            throw new ConfigLoadException(
            $"client.kind in {FileName} is \"{kind}\", which intest does not support. {ClientKindRule}");
        }

        var typeName = RequireString(declared, "client.typeName", "typeName", ClientTypeNameRule);

        // typeName reaches mstest-class.scriban in reference position
        // (ApiClient<Orders.ApiClient.OrdersApiClient>()), not inside a string literal — the same
        // reasoning that governs project.rootNamespace and project.testBaseClass above. No
        // escaping construct makes an invalid identifier resolve there, so it is refused here
        // rather than emitted and left to fail the adopter's build with a message that never
        // mentions intest.json at all.
        if (!CSharpIdentifier.TryValidateDottedName(typeName, "client.typeName", out var typeNameReason))
        {
            throw new ConfigLoadException(
            $"{typeNameReason} Change client.typeName in {FileName} — for example " +
            "\"Orders.ApiClient.OrdersApiClient\".");
        }

        return new LoadedClientConfig(kind, typeName);
    }

    /// <summary>
    /// The values <c>project.framework</c> accepts. §3 designs InTest for exactly three
    /// frameworks and all three now ship — mstest, xunit and nunit — so, unlike when this array
    /// last grew, there is no fourth roadmapped framework waiting to be added on the same "grows
    /// the day it ships" premise. See <see cref="FrameworkRule"/> for the adopter-facing text
    /// this same list backs; the two are kept in step by both reading from one array rather than
    /// by two independent lists agreeing by discipline.
    /// </summary>
    private static readonly string[] SupportedFrameworks = ["mstest", "xunit", "nunit"];

    /// <summary>
    /// Unlike <see cref="ReadOptionalIntestVersion"/>, <c>project.framework</c> is required — read
    /// with the same <see cref="RequireString"/> helper <c>rootNamespace</c> and
    /// <c>testBaseClass</c> already use, not the optional path. That asymmetry is the one
    /// genuinely debatable call in this method, so it is argued here rather than merely asserted:
    /// <list type="bullet">
    /// <item><c>framework</c> is a section-mate of <c>rootNamespace</c> and <c>testBaseClass</c>,
    /// both required. <c>intestVersion</c> is the one optional setting in this file, and it is
    /// optional for a stated reason, not by default: §5 says a config "grows by addition", so a
    /// config predating a field — or hand-edited without it — must still load. That reason is
    /// about <c>intestVersion</c> specifically; it does not generalize to every setting that
    /// happens to be new.</item>
    /// <item>§5 makes the test framework a <b>frozen</b> axis — a generated suite cannot be
    /// migrated to a different framework in place. A config that declares no framework has no
    /// answer to "which framework is this suite?", the same unanswerable question
    /// <see cref="RequireSection"/> already refuses to leave open for the <c>project</c> section
    /// itself.</item>
    /// <item>Defaulting to <c>"mstest"</c> was considered and rejected: it is exactly the
    /// plausible-default <c>CLAUDE.md</c>'s "Fail loudly" rule forbids. A config that says nothing
    /// would silently behave as MSTest forever — correct only until a second framework ships,
    /// at which point every adopter who never wrote the key is depending on a default they never
    /// chose. <c>intestVersion</c> does not set a counter-precedent here: under
    /// <c>[exact-match]</c>, <c>generate --check</c> compares it by string equality, so an absent
    /// claim and a wrong claim merely render differently — <c>framework</c> instead selects
    /// behaviour outright (which template renders), so absence and a wrong value are the same
    /// failure, not two.</item>
    /// <item>Safety net, verified rather than assumed: <c>InitCommand</c> has always written
    /// <c>"framework": "mstest"</c> into every project it scaffolds, and both
    /// <c>examples/Catalog.ApiTests/intest.json</c> and <c>examples/Orders.ApiTests/intest.json</c>
    /// already declare it. Making the key required breaks no config this repository ships.</item>
    /// </list>
    /// <para>
    /// The value itself accepts exactly <c>"mstest"</c>, <c>"xunit"</c> or <c>"nunit"</c> —
    /// lowercase, matching what <c>InitCommand</c> writes — and nothing else, including
    /// differently-cased spellings like <c>"MSTest"</c> or <c>"xUnit"</c>. §5's config is
    /// adopter-facing JSON, not a C#
    /// identifier with case-insensitive lookup rules; treating <c>"MSTest"</c> as equivalent would
    /// mean this loader accepts spellings <c>init</c> never writes and no other setting on this
    /// surface tolerates (<c>rootNamespace</c> and <c>testBaseClass</c> are both compared exactly
    /// as written). A fixed set of accepted spellings is also simpler to document and to grep for
    /// than a case-insensitive comparison would be, with no adopter-facing upside: nothing
    /// hand-writes this key in a different case today.
    /// </para>
    /// </summary>
    private static string RequireSupportedFramework(JsonElement project)
    {
        var framework = RequireString(project, "project.framework", "framework", FrameworkRule);

        if (!SupportedFrameworks.Contains(framework, StringComparer.Ordinal))
        {
            throw new ConfigLoadException(
            $"project.framework in {FileName} is \"{framework}\", which intest does not support yet. {FrameworkRule}");
        }

        return framework;
    }

    /// <summary>
    /// <c>intestVersion</c> joins <see cref="ConfigLoader"/> because that is where the whole
    /// document is available (<c>CONTRIBUTING.md</c>'s "Where validation lives" rule), but unlike
    /// <see cref="RequireSupportedSchemaVersion"/> it stays optional: §5's config grows by
    /// addition, and a config written by a newer patch release — or predating this field, or
    /// hand-edited without it — still has to load. Absence is surfaced as null, not defaulted to
    /// some version string, so a caller can tell "no claim made" from "claimed and matched".
    /// <para>
    /// No shape is enforced beyond "non-empty string". <see cref="CliVersion.Current"/> does NOT
    /// always take the form of three dot-separated whole numbers: it strips only the SourceLink
    /// <c>+&lt;sha&gt;</c> suffix, and a SemVer 2 informational version puts a prerelease label
    /// <i>before</i> that build metadata — <c>1.0.0-rc.1+&lt;sha&gt;</c> — so the "-rc.1" survives.
    /// A three-number shape check therefore rejects a config the tool's own binary just wrote
    /// whenever it was built from a prerelease, which is worse than the field going unread
    /// entirely. It also buys nothing: under <c>[exact-match]</c>, <c>generate --check</c>
    /// compares this value against the running CLI by string equality, so any non-empty string
    /// that isn't a match already produces §8's message naming both sides and pointing at
    /// <c>upgrade</c> — including something malformed like "banana". Only emptiness is refused
    /// here, since <c>""</c> is a mistake rather than a version claim, not a shape to validate.
    /// (<c>generate --check</c> itself does not exist yet — this argument is what it will need
    /// once it does, not a description of behaviour already implemented.)
    /// </para>
    /// </summary>
    private static string? ReadOptionalIntestVersion(JsonElement root)
    {
        if (!root.TryGetProperty("intestVersion", out var declared))
        {
            return null;
        }

        var rule = "It must be the intest version that generated this config, as a non-empty " +
                   "string — for example \"0.1.0\".";

        if (declared.ValueKind != JsonValueKind.String)
        {
            var written = declared.ValueKind == JsonValueKind.Null ? "null" : Quote(declared);
            throw new ConfigLoadException(
            $"intestVersion in {FileName} is {Describe(declared.ValueKind)}, not a string: " +
            $"{written}. {rule}");
        }

        var text = declared.GetString()!;
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ConfigLoadException($"intestVersion in {FileName} is empty. {rule}");
        }

        return text;
    }

    /// <summary>
    /// Checked before every setting it governs. §5: <c>schemaVersion</c> "moves only on a major —
    /// it is how the CLI detects a config it must not silently reinterpret." Until this existed
    /// that sentence described a capability the tool did not have: nothing read the value, so a
    /// config written for a later schema was reinterpreted under this one's meanings, producing
    /// wrong output and no error. That is the only failure on this surface that was silent.
    /// <para>
    /// <b>The message deliberately does not mention <c>intest upgrade</c>, and the reason is not
    /// that the command is unimplemented — it now is (v1-e).</b> It is still not the remedy: a
    /// reviewer built the case directly (v1-e plan, Task 5 Step 4) — set
    /// <see cref="SupportedSchemaVersion"/> to a higher number, republished the CLI, and ran that
    /// build's <c>upgrade</c> against an ordinary, older-schema project. Result: exit 2, config
    /// untouched. <c>UpgradeCommand</c> calls straight into <c>GenerateCommand.RunAsync</c>, which
    /// calls <see cref="Load"/> the same way `generate` always has — so this exact check refuses
    /// the config before <c>upgrade</c>'s own edits ever run, on the very input a schema migration
    /// exists to accept. Naming <c>intest upgrade</c> here would point at a command that, for this
    /// refusal, cannot act — the documented-but-unreachable remedy shape this project has closed
    /// six times before (v1-e plan, `[paired]`). The remedy this message states instead is the one
    /// actually available: the declared version and the implemented version must match, by moving
    /// either. A real migration path requires this loader itself to learn to accept (or upgrade)
    /// an old <c>schemaVersion</c> rather than refuse it on sight — future work, not a gap
    /// <c>UpgradeCommand</c> could close on its own (see its own doc comment, decision 3).
    /// </para>
    /// </summary>
    private static void RequireSupportedSchemaVersion(JsonElement root)
    {
        var rule = $"It must be the schema version this intest implements, as a whole number — " +
                   $"\"schemaVersion\": {SupportedSchemaVersion}.";

        if (!root.TryGetProperty("schemaVersion", out var declared))
        {
            throw new ConfigLoadException(
            $"{FileName} has no schemaVersion. It must declare \"schemaVersion\": {SupportedSchemaVersion} — " +
            "the schema this intest implements, and how intest tells a config written for a " +
            "different version from one it can read.");
        }

        if (declared.ValueKind != JsonValueKind.Number)
        {
            throw new ConfigLoadException(
            $"schemaVersion in {FileName} is {Describe(declared.ValueKind)}, not a whole number: " +
            $"{Quote(declared)}. {rule}");
        }

        // A fractional schemaVersion is still JsonValueKind.Number, so the kind check above does
        // not catch it and GetInt32 would throw.
        if (!declared.TryGetInt32(out var version))
        {
            throw new ConfigLoadException(
            $"schemaVersion in {FileName} is {Quote(declared)}, which is not a whole number. {rule}");
        }

        if (version != SupportedSchemaVersion)
        {
            throw new ConfigLoadException(
            $"{FileName} declares schemaVersion {version}, but this intest implements " +
            $"schemaVersion {SupportedSchemaVersion}. They must match: run an intest that " +
            $"implements schemaVersion {version}, or set schemaVersion to " +
            $"{SupportedSchemaVersion} and reconcile {FileName} against what this version " +
            "expects. Continuing would reinterpret settings under meanings they may not have.");
        }
    }

    private static JsonDocument Parse(string text, string path)
    {
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            // ex.Message already carries the line and byte position, which is the only part of
            // this an adopter can act on directly.
            throw new ConfigLoadException($"{FileName} is not valid JSON: {ex.Message} Fix it at '{path}'.", ex);
        }
    }

    private static JsonElement RequireSection(JsonElement root, string name, string rule)
    {
        if (!root.TryGetProperty(name, out var section))
        {
            throw new ConfigLoadException($"{FileName} has no '{name}' section. {rule}");
        }

        if (section.ValueKind != JsonValueKind.Object)
        {
            throw new ConfigLoadException(
            $"'{name}' in {FileName} is {Describe(section.ValueKind)}, not an object: " +
            $"{Quote(section)}. {rule}");
        }

        return section;
    }

    /// <summary>
    /// <paramref name="setting"/> is the dotted path the adopter sees in the file
    /// (<c>project.rootNamespace</c>); <paramref name="property"/> is the bare key to look up.
    /// Both are passed rather than derived, so a message can never name a path the loader did
    /// not actually read.
    /// </summary>
    private static string RequireString(JsonElement section, string setting, string property, string rule)
    {
        if (!section.TryGetProperty(property, out var value))
        {
            throw new ConfigLoadException($"{FileName} has no {setting}. {rule}");
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            // JSON null is called out by name rather than described as "not a string": it is the
            // value a half-finished hand edit leaves behind, and it used to survive .GetString()!
            // only to fail later inside Path.Combine, as an ArgumentNullException naming that
            // method's own 'path2' parameter.
            var written = value.ValueKind == JsonValueKind.Null ? "null" : Quote(value);
            throw new ConfigLoadException(
            $"{setting} in {FileName} is {Describe(value.ValueKind)}, not a string: {written}. {rule}");
        }

        return value.GetString()!;
    }

    private static string Describe(JsonValueKind kind) => kind switch
    {
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "an array",
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        JsonValueKind.Null => "null",
        _ => "missing",
    };

    /// <summary>
    /// What the adopter actually wrote, bounded. An object or array here is a structural mistake
    /// whose full text can be arbitrarily long, and a message that scrolls is a message nobody
    /// reads — the first line is enough to recognise which edit went wrong.
    /// </summary>
    private static string Quote(JsonElement value)
    {
        const int maxLength = 60;
        var raw = value.GetRawText().ReplaceLineEndings(" ").Trim();
        return raw.Length <= maxLength ? raw : raw[..maxLength] + "…";
    }
}

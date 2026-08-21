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
        "It must be the path to the OpenAPI document, relative to the project directory — " +
        "for example \"../Orders/bin/Debug/net10.0/orders.json\".";

    private const string SpecSectionRule =
        "It must declare spec.source, the path to the OpenAPI document relative to the project " +
        "directory — for example \"spec\": { \"source\": \"../Orders/bin/Debug/net10.0/orders.json\" }.";

    private const string ProjectSectionRule =
        "It must declare project.rootNamespace and project.testBaseClass — for example " +
        "\"project\": { \"rootNamespace\": \"Orders.ApiTests\", " +
        "\"testBaseClass\": \"Orders.ApiTests.OrdersTestBase\" }.";

    private const string RootNamespaceRule =
        "It must be the C# namespace generated tests are declared in — for example \"Orders.ApiTests\".";

    private const string TestBaseClassRule =
        "It must be the C# name of the class generated tests derive from — for example " +
        "\"Orders.ApiTests.OrdersTestBase\".";

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

        // The empty source's twin, and it needed the same treatment for the same reason: it does
        // not fail on its own terms either. Path.Combine(projectRoot, "https://example.com/x.json")
        // appends the URL as a relative segment, so SpecLoader reported "Spec file not found:"
        // against a path spliced out of a Windows separator and a URL — a path the adopter never
        // wrote, phrased as a file that is merely missing rather than as a kind of source InTest
        // cannot read. Measured, not inferred: the message was
        // "Spec file not found: <projectRoot>\https://example.com/openapi.json" at exit 2.
        //
        // Refused here rather than only at `init` because `init` is not how most URLs get here.
        // The help text said "Path or URL" and getting started's Phase 1 instructed adopters to
        // "Point spec.source at the URL" — a hand edit, which no argument guard sees. One loader
        // covers generate and fixtures repair both, which is this type's whole reason for being.
        if (SpecLoader.IsUrl(specSource))
        {
            throw new ConfigLoadException(SpecLoader.UrlReason("spec.source", specSource, SpecSourceRule));
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

        return new LoadedConfig(specSource, rootNamespace, testBaseClass, intestVersion);
    }

    /// <summary>
    /// <c>intestVersion</c> joins <see cref="ConfigLoader"/> because that is where the whole
    /// document is available (<c>CONTRIBUTING.md</c>'s "Where validation lives" rule), but unlike
    /// <see cref="RequireSupportedSchemaVersion"/> it stays optional: §5's config grows by
    /// addition, and a config written by a newer patch release — or predating this field, or
    /// hand-edited without it — still has to load. Absence is surfaced as null, not defaulted to
    /// some version string, so a caller can tell "no claim made" from "claimed and matched".
    /// <para>
    /// Only the shape is checked here — that it is a string of the same form
    /// <see cref="CliVersion.Current"/> takes, three dot-separated whole numbers. What the version
    /// <i>means</i> — whether it matches the running CLI — is <c>generate --check</c>'s job, not
    /// this loader's.
    /// </para>
    /// </summary>
    private static string? ReadOptionalIntestVersion(JsonElement root)
    {
        if (!root.TryGetProperty("intestVersion", out var declared))
        {
            return null;
        }

        var rule = "It must be the intest version that generated this config, as three " +
                   "dot-separated whole numbers — for example \"0.1.0\".";

        if (declared.ValueKind != JsonValueKind.String)
        {
            var written = declared.ValueKind == JsonValueKind.Null ? "null" : Quote(declared);
            throw new ConfigLoadException(
                $"intestVersion in {FileName} is {Describe(declared.ValueKind)}, not a string: " +
                $"{written}. {rule}");
        }

        var text = declared.GetString()!;
        if (!IsWellFormedVersion(text))
        {
            throw new ConfigLoadException(
                $"intestVersion in {FileName} is {Quote(declared)}, which is not a version. {rule}");
        }

        return text;
    }

    /// <summary>
    /// Exactly three dot-separated whole numbers — the shape <see cref="CliVersion.Current"/>
    /// always takes, since it already strips SourceLink's <c>+&lt;sha&gt;</c> suffix.
    /// <see cref="Version.TryParse(string?, out Version?)"/> alone is too lenient: it also accepts
    /// two components ("1.0") and four ("1.0.0.0"), neither of which this CLI ever writes.
    /// </summary>
    private static bool IsWellFormedVersion(string text) =>
        text.Split('.') is [_, _, _] parts &&
        Array.TrueForAll(parts, part => part.Length > 0 && Array.TrueForAll(part.ToCharArray(), char.IsAsciiDigit));

    /// <summary>
    /// Checked before every setting it governs. §5: <c>schemaVersion</c> "moves only on a major —
    /// it is how the CLI detects a config it must not silently reinterpret." Until this existed
    /// that sentence described a capability the tool did not have: nothing read the value, so a
    /// config written for a later schema was reinterpreted under this one's meanings, producing
    /// wrong output and no error. That is the only failure on this surface that was silent.
    /// <para>
    /// The message deliberately does not mention <c>intest upgrade</c>, which does not exist yet.
    /// Naming a command that is not implemented would reproduce, one level down, the same defect
    /// this check closes. The remedy it states instead is the one available today: the declared
    /// version and the implemented version must match, by moving either.
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

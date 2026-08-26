using System.Text.Json;

namespace InTest.Cli.Spec;

/// <summary>
/// Recovers a <c>spec.source</c> — and, where the lockfile records enough to say, a
/// <c>client.kind</c>/<c>client.typeName</c> pair — from a client generator's own lockfile, for
/// <c>init --client-lockfile</c> (Task 5, <c>[lockfile-recovery]</c>,
/// docs/superpowers/plans/2026-08-25-intest-typed-client-invocation.md). A team that owns a
/// generated client but cannot readily get at the OpenAPI document it was generated from does not
/// have to go hunting for one: the generator already recorded where it generated <b>from</b>, in a
/// file that ships beside the client and is committed by convention (kiota's own docs recommend
/// committing <c>kiota-lock.json</c> so a regeneration is reproducible).
/// <para>
/// <b>This is a DISTINCT FOURTH concern, not a fourth <c>LoadFrom*</c> shape bolted onto
/// <see cref="SpecLoader"/>, <see cref="SpecFetcher"/> or <see cref="SpecSnapshot"/>.</b> CLAUDE.md
/// warns explicitly against folding a new concern into that split — <c>SpecLoader</c> turns spec
/// *text* into an <c>OpenApiDocument</c>, <c>SpecFetcher</c> owns HTTP policy for a URL source, and
/// <c>SpecSnapshot</c> owns the committed <c>spec.json</c> bytes; none of the three has ever parsed
/// anything but an OpenAPI document or the transport that fetches one. A client generator's
/// lockfile is a categorically different file: it is produced by a third-party tool (kiota), not by
/// InTest or an OpenAPI producer, its schema has nothing to do with OpenAPI, and what this type reads
/// out of it — a spec location plus a generator's own naming choices — is not "the spec" in any
/// sense those three types already model. It sits beside them in <c>Spec/</c> because what it
/// ultimately produces (a spec.source string) feeds the same downstream path, not because it shares
/// their parsing logic.
/// </para>
/// <para>
/// <b>Kiota only, by measured choice, mirroring <c>[compiler-is-oracle]</c>'s NSwag call.</b>
/// <c>kiota-lock.json</c> is a lockfile in the conventional sense — written by the generator
/// *after* generation, as a record of the choices it made, independent of what the adopter
/// re-types by hand next time. Measured directly against real output (kiota 1.34.1, see
/// <c>ClientLockfileTests</c>'s fixture, itself copied from a real
/// <c>kiota generate --openapi samples/Orders.Api/Orders.Api.json --class-name OrdersApiClient
/// --namespace-name Orders.ApiClient --language CSharp</c> run): <c>descriptionLocation</c> names
/// the spec exactly as it was passed to <c>generate</c> (an absolute local path with forward
/// slashes on this measurement, and per kiota's own documentation equally an <c>http(s)</c> URL
/// when the source was one — both are handled identically here, as a bare string), and
/// <c>clientClassName</c>/<c>clientNamespaceName</c> compose, dot-joined, into exactly the
/// <c>client.typeName</c> shape <c>intest.json</c> wants — confirmed against this measurement:
/// <c>"OrdersApiClient"</c> + <c>"Orders.ApiClient"</c> gives
/// <c>Orders.ApiClient.OrdersApiClient</c>, the same value the getting-started guide's own worked
/// example already uses. <c>kiotaVersion</c> is also recorded (<c>"1.34.1"</c> in this measurement)
/// — a stable field, not the unverified risk the plan's original risk section described before this
/// task measured it; see <c>[lockfile-recovery]</c>'s note there for the correction.
/// </para>
/// <para>
/// <b>NSwag was measured and scoped out, not merely skipped.</b> <c>nswag new</c> (NSwag 14.7.1)
/// was run to get real ground truth rather than guessing, and what it produces is materially
/// different from a lockfile: <c>nswag.json</c> is the *input config* an adopter hand-writes and
/// maintains *before* generation — not a record the generator writes *after* — so recovering a spec
/// location from it delivers little a team without the OpenAPI document does not already have (they
/// already own and edit this file). More fatally for <c>client.typeName</c> recovery specifically:
/// under NSwag's own default <c>operationGenerationMode</c>
/// (<c>MultipleClientsFromOperationId</c>), <c>codeGenerators.openApiToCSharpClient.className</c>
/// is <c>"{controller}Client"</c> — a *naming template* with a placeholder, not a concrete type
/// name, and that generation mode itself produces one class *per controller*, not the single class
/// a <c>client.typeName</c> setting names. Resolving the template against the actual spec's
/// controllers (which controller, which of potentially several classes) is exactly the kind of
/// per-generator guessing <c>[compiler-is-oracle]</c>'s own NSwag call already rejected for
/// convention derivation; reading it from a config file changes nothing about that. Consistent with
/// the plan's own instruction to scope a generator out on measured difficulty rather than ship a
/// guess, NSwag lockfile recovery is not built. A NSwag-shaped file handed to
/// <see cref="Recover"/> still fails loudly, on the same "no descriptionLocation" message any
/// unrecognised JSON object gets — not a wrong answer, an honest "cannot recover this".
/// </para>
/// <para>
/// <b>Fails loudly by design, never returns a null spec source.</b> A missing, renamed, blank or
/// wrong-typed <c>descriptionLocation</c> throws <see cref="ClientLockfileException"/> naming the
/// exact field, rather than handing <c>InitCommand</c> a null or empty spec.source that would
/// resurface, far from here, as <c>ConfigLoader</c>'s "spec.source is empty" refusal — exactly the
/// confusing-at-a-distance failure <c>InitCommand</c>'s own blank-<c>--spec</c> guard exists to
/// prevent (CLAUDE.md's "fail loudly" rule). The client-identity pair is looser only because it is
/// documented as optional in the feature this recovers for
/// (<c>LoadedClientConfig</c>: "present only when the adopter opted into" a client) — but a
/// lockfile that names *one* of <c>clientClassName</c>/<c>clientNamespaceName</c> without the other
/// still throws, because kiota itself always writes both together (confirmed in the measured
/// fixture); a lockfile with exactly one is far more likely hand-edited or truncated than a
/// legitimate partial state this type should quietly tolerate.
/// </para>
/// </summary>
public static class ClientLockfile
{
    /// <summary>
    /// What <see cref="Recover"/> hands back — <see cref="SpecSource"/> is always non-blank text
    /// ready for <c>InitCommand</c>'s existing <c>normalizedSpecSource</c> path
    /// (<see cref="SpecLoader.IsUrl"/> / <see cref="SpecFetcher.TryValidateUrl"/> /
    /// <see cref="Naming.MSBuildPropertyValue.TryEscape"/> — unchanged, never a parallel one).
    /// <see cref="ClientKind"/>/<see cref="ClientTypeName"/> are either both present (the lockfile
    /// named a client) or both <see langword="null"/> (it did not) — deliberately not a
    /// <c>Configuration.LoadedClientConfig</c>: that record's own doc comment states
    /// <c>ConfigLoader.Load</c> is its only producer, so a second constructor site here would
    /// break the invariant its callers rely on ("by the time a LoadedClientConfig exists both
    /// fields are already known-good" — known-good by <em>that</em> loader's rules, not this
    /// one's). <c>InitCommand</c> validates and scaffolds these two raw strings itself, the same
    /// way it already validates and scaffolds <c>projectName</c>.
    /// </summary>
    public sealed record Recovered(string SpecSource, string? ClientKind, string? ClientTypeName);

    private const string DescriptionLocationRule =
        "kiota writes this field itself, from the --openapi value passed to `kiota generate` — " +
        "if it is missing, the file is not a kiota-lock.json, or it was hand-edited after the " +
        "fact. Point --client-lockfile at the kiota-lock.json kiota generated your client from, " +
        "or pass --spec directly if you have the OpenAPI document to hand.";

    /// <summary>
    /// Reads and validates the lockfile at <paramref name="path"/>. Every failure throws
    /// <see cref="ClientLockfileException"/>, which <c>InitCommand</c> catches the same way
    /// <c>GenerateCommand</c> already catches <see cref="SpecLoadException"/> and
    /// <see cref="Configuration.ConfigLoadException"/> — print the message bare, exit 2.
    /// </summary>
    public static Recovered Recover(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new ClientLockfileException(
            $"--client-lockfile '{path}' does not exist. Pass the path to the kiota-lock.json " +
            "your client generator wrote — for example \"../Orders.ApiClient/kiota-lock.json\".");
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            throw new ClientLockfileException($"--client-lockfile '{path}' could not be read: {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ClientLockfileException($"--client-lockfile '{path}' could not be read: {ex.Message}", ex);
        }

        return Parse(text, path);
    }

    /// <summary>
    /// The parsing half, separated from <see cref="Recover"/>'s file I/O the same way
    /// <c>Clients.ClientCallMap.Parse</c> is separated from its own <c>Load</c> — so a test can
    /// drive the field-level refusals directly against a JSON string, without a temp file for
    /// every case.
    /// </summary>
    internal static Recovered Parse(string json, string path)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new ClientLockfileException($"--client-lockfile '{path}' is not valid JSON: {ex.Message}", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new ClientLockfileException(
                $"--client-lockfile '{path}' must be a JSON object — a kiota-lock.json, not " +
                $"{Describe(root.ValueKind)}.");
            }

            var specSource = RequireNonBlankString(root, "descriptionLocation", path, DescriptionLocationRule);

            var hasClassName = root.TryGetProperty("clientClassName", out _);
            var hasNamespaceName = root.TryGetProperty("clientNamespaceName", out _);

            if (!hasClassName && !hasNamespaceName)
            {
                // No client identity to recover — still a fully usable result. `init` scaffolds a
                // project with a spec.source and no `client` section, exactly as it would from a
                // plain --spec, and the adopter can add `client` by hand later.
                return new Recovered(specSource, ClientKind: null, ClientTypeName: null);
            }

            if (!(hasClassName && hasNamespaceName))
            {
                // kiota always writes both together (measured — see this type's own doc comment).
                // Exactly one present is a stronger signal of a hand-edited or truncated file than
                // of a legitimate partial state, so this fails loudly rather than silently
                // recovering the spec alone and dropping the client section without a word.
                var missing = hasClassName ? "clientNamespaceName" : "clientClassName";
                throw new ClientLockfileException(
                $"--client-lockfile '{path}' declares " +
                $"{(hasClassName ? "clientClassName" : "clientNamespaceName")} but no {missing}. " +
                "kiota writes both together; a lockfile naming only one has likely been hand-" +
                "edited. Fix the lockfile, or omit both and add a \"client\" section to " +
                "intest.json by hand.");
            }

            var className = RequireNonBlankString(
            root, "clientClassName", path,
            "kiota writes this field itself, alongside clientNamespaceName, from the " +
            "--class-name passed to `kiota generate`.");
            var namespaceName = RequireNonBlankString(
            root, "clientNamespaceName", path,
            "kiota writes this field itself, alongside clientClassName, from the " +
            "--namespace-name passed to `kiota generate`.");

            // Dot-joined, matching kiota's own generated output: a class named clientClassName
            // declared in namespace clientNamespaceName. Confirmed against the measured fixture —
            // "OrdersApiClient" + "Orders.ApiClient" gives exactly
            // "Orders.ApiClient.OrdersApiClient", the getting-started guide's own worked example.
            return new Recovered(specSource, ClientKind: "kiota", ClientTypeName: $"{namespaceName}.{className}");
        }
    }

    private static string RequireNonBlankString(JsonElement root, string property, string path, string rule)
    {
        if (!root.TryGetProperty(property, out var value))
        {
            throw new ClientLockfileException($"--client-lockfile '{path}' has no {property}. {rule}");
        }

        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            var written = value.ValueKind == JsonValueKind.Null ? "null" : Quote(value);
            throw new ClientLockfileException(
            $"--client-lockfile '{path}' has {property} {written}, which is not usable. {rule}");
        }

        return value.GetString()!;
    }

    private static string Describe(JsonValueKind kind) => kind switch
    {
        JsonValueKind.Array => "an array",
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        JsonValueKind.Null => "null",
        _ => "missing",
    };

    private static string Quote(JsonElement value)
    {
        const int maxLength = 60;
        var raw = value.GetRawText().ReplaceLineEndings(" ").Trim();
        return raw.Length <= maxLength ? raw : raw[..maxLength] + "…";
    }
}

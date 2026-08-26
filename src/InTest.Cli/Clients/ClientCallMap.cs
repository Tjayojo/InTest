using System.Text.Json;
using System.Text.Json.Nodes;

namespace InTest.Cli.Clients;

/// <summary>
/// Parses the adopter-owned <c>client-map.json</c> at the project root:
/// <c>{ "overrides": { "&lt;operationKey&gt;": "&lt;C# call expression&gt;" } }</c>. Overrides
/// exist because <see cref="Planning.ClientCallPlanner"/>'s convention either does not apply to a
/// given operation (a query-parameter or request-body Success case, for either Kiota or NSwag; an
/// NSwag operation with no <c>operationId</c> or an underscored one —
/// <c>[nswag-needs-operationid]</c>), does not exist at all for the adopter's generator (Refit —
/// <c>[refit-override-only]</c>, permanent and unconditional, unlike NSwag's gated case), or the
/// adopter simply prefers to spell the call themselves.
/// <para>
/// Parses the same way <see cref="Fixtures.FixtureDocument"/> parses a fixture, deliberately: both
/// are committed, hand-edited files, so a malformed field — an unquoted number, a nested object
/// where a string belongs — is a realistic typo, not adversarial input. Every failure surfaces as
/// a <see cref="ClientCallMapFormatException"/> naming the offending field with its inner exception
/// preserved, rather than a raw framework exception (<c>InvalidOperationException</c>,
/// <c>JsonException</c>) that never mentions <c>client-map.json</c> at all.
/// </para>
/// <para>
/// <b>The trust model, stated once because it governs every validation choice below</b>
/// (<c>[convention-plus-override]</c>,
/// docs/superpowers/plans/2026-08-25-intest-typed-client-invocation.md): an override value is a
/// complete, trusted C# expression, spliced bare into the generated test method by stage 3's
/// renderer. It gets none of <see cref="Naming.CSharpLiteral"/>'s escaping — that rule's authority
/// is the C# grammar for a string <i>literal</i>, and this value is never quoted, it <i>is</i> code
/// — and none of <see cref="Fixtures.FixtureDocument.TryValidateOperationKey"/>'s filename check —
/// its authority is the filesystem, and an operation key here is only ever a JSON object key, never
/// a path. The adopter who writes <c>client-map.json</c> already owns — and could already break —
/// the generated <c>.csproj</c> in the very same repository, reviewed by the very same people;
/// validating a call expression's shape beyond "is it non-blank" would buy nothing a malicious edit
/// here could not already do through the project file. What actually catches a wrong expression is
/// <c>[compiler-is-oracle]</c>: it fails the adopter's own build at a generated line, loudly — the
/// same guarantee a hand-written <c>.cs</c> file gets from the compiler on every other line.
/// </para>
/// </summary>
public sealed class ClientCallMap
{
    public const string FileName = "client-map.json";

    /// <summary>
    /// What <see cref="Load"/> returns for a project with no <c>client-map.json"</c> at all — the
    /// common case: most projects never opt into a client, and most that do never need an
    /// override. Absence is not a misconfiguration here the way it is for <c>intest.json</c>
    /// itself (<see cref="Configuration.ConfigLoader.Load"/>'s "Run `intest init` first" refusal);
    /// there is no scaffolding step that is expected to have written this file.
    /// </summary>
    public static readonly ClientCallMap Empty = new(new Dictionary<string, string>(StringComparer.Ordinal));

    public IReadOnlyDictionary<string, string> Overrides { get; }

    private ClientCallMap(IReadOnlyDictionary<string, string> overrides) => Overrides = overrides;

    public static ClientCallMap Load(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        var path = Path.Combine(projectRoot, FileName);
        if (!File.Exists(path))
        {
            return Empty;
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            throw new ClientCallMapFormatException($"{FileName} at '{path}' could not be read: {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ClientCallMapFormatException($"{FileName} at '{path}' could not be read: {ex.Message}", ex);
        }

        return Parse(text);
    }

    /// <summary>
    /// An absent "overrides" key is not malformed — it is a <c>client-map.json</c> that exists
    /// (perhaps scaffolded ahead of need, perhaps left over from a removed override) but currently
    /// names none, and it parses to <see cref="Empty"/> the same as a missing file would.
    /// </summary>
    public static ClientCallMap Parse(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException ex) { throw new ClientCallMapFormatException($"{FileName} is not valid JSON: {ex.Message}", ex); }

        if (root is not JsonObject obj)
        {
            throw new ClientCallMapFormatException($"{FileName} root must be a JSON object.");
        }

        if (obj["overrides"] is not { } overridesNode)
        {
            return Empty;
        }

        if (overridesNode is not JsonObject overridesObject)
        {
            throw new ClientCallMapFormatException(
            $"{FileName}'s 'overrides' must be a JSON object of operationKey/call-expression " +
            $"pairs, but found '{overridesNode.ToJsonString()}'.");
        }

        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in overridesObject)
        {
            string text;
            try
            {
                text = value?.GetValue<string>() ?? string.Empty;
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException)
            {
                throw new ClientCallMapFormatException(
                $"{FileName}'s 'overrides.{key}' must be a string, but found '{value}'.", ex);
            }

            // No other validation (see the class doc comment's trust-model paragraph): a blank
            // value is the one shape that is never a deliberate override — every real call
            // expression is non-empty text — so it is the only thing refused here.
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ClientCallMapFormatException(
                $"{FileName}'s 'overrides.{key}' is blank. It must be the C# expression that calls " +
                "the generated client for this operation — for example \"Orders[{id}].GetAsync\".");
            }

            overrides[key] = text;
        }

        return new ClientCallMap(overrides);
    }
}

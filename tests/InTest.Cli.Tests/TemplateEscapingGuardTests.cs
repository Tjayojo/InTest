using System.Reflection;
using System.Text.RegularExpressions;
using InTest.Cli.Rendering;
using Shouldly;

namespace InTest.Cli.Tests;

/// <summary>
/// Guards the recurrence direction the original defect actually took: an unescaped,
/// spec-derived value quoted directly into mstest-class.scriban. The '_literal' naming
/// convention (see TemplateRenderer.RenderClass's own comment) only helps a reader who already
/// knows to look for it — it does nothing to stop a future template edit from quoting a new
/// field that was never routed through CSharpLiteral.Escape in the first place. This test reads
/// the template itself and enforces the naming convention mechanically: every field the
/// template puts inside a quoted <c>{{ tc.name }}</c> interpolation must either be named
/// '_literal' or be explicitly allow-listed below with a reason.
/// <para>
/// Deliberately does not attempt to verify that a '_literal' field is <i>actually</i> escaped in
/// TemplateRenderer.cs — that would mean re-implementing a small C# parser to trace model
/// construction, which is not worth it here. This test only enforces the naming discipline that
/// TemplateRendererEscapingTests then exercises for real by rendering hostile input through
/// each field and asserting the escaped output. The two together cover both halves: this one
/// catches "a new quoted site was never named or escaped at all", the other catches "the escape
/// function itself is wrong".
/// </para>
/// <para>
/// Review finding: an earlier version of this test regexed for one literal shape —
/// <c>"{{ tc.name }}"</c>, bare delimiters, no whitespace-trim markers, the field alone inside
/// its own tag. Every real site in the template happened to match that shape, so the guard
/// reported clean, but it was blind to anything shaped differently: <c>{{~ tc.x ~}}</c> (the
/// template's own house idiom on roughly sixteen other lines), <c>"prefix-{{ tc.x }}suffix"</c>,
/// <c>"{{ tc.a }}/{{ tc.b }}"</c>, a Scriban filter (<c>{{ tc.x | string.upcase }}</c>), or
/// stray whitespace around the dot. A guard whose actual coverage is narrower than its stated
/// promise is worse than no guard, because it licenses trust it has not earned. This version
/// instead partitions every <c>tc.NAME</c> reference in the template — found with a permissive
/// pattern that does not care about tag markers, surrounding text, filters, or whitespace — into
/// "sits inside a quoted string" or "does not", and every reference in either partition must be
/// explicitly accounted for. Nothing is allowed to fall through unclassified.
/// </para>
/// </summary>
[TestClass]
public class TemplateEscapingGuardTests
{
    /// <summary>
    /// Field names allowed to appear inside a quoted string without a '_literal' suffix, and
    /// why each is safe there. Add to this list only for a field that truly cannot carry spec
    /// text — anything else belongs in TemplateRenderer.RenderClass's model, escaped and named
    /// with a '_literal' suffix instead.
    /// </summary>
    private static readonly HashSet<string> AllowedInQuotedPosition = new(StringComparer.Ordinal)
    {
        // Always the constant TestPlanBuilder.ContractCategory = "Contract"
        // (TestPlanBuilder.cs:12) — never spec-derived, so there is no spec text here for
        // CSharpLiteral.Escape to act on. Mechanically checked (not just asserted by this
        // comment) by GoldenFileTests.EveryCaseIsCategorizedContract.
        "category",
    };

    /// <summary>
    /// Field names known to compose C# outside literal position — a bare method name, an enum
    /// member expression, a boolean driving an if/~if, or text a helper method in
    /// TemplateRenderer.cs already quoted (or deliberately left unquoted) itself. A '_literal'
    /// field appearing bare (e.g. schema_key_literal in its own if condition) needs no entry
    /// here — its suffix already accounts for it wherever it appears. Add to this list only for
    /// a field that is never, anywhere, pasted directly between a pair of double quotes.
    /// </summary>
    private static readonly HashSet<string> AllowedInBarePosition = new(StringComparer.Ordinal)
    {
        "method_name",             // bare method identifier: public async Task {{ tc.method_name }}()
        "http_method_pascal",      // bare enum member: HttpMethod.{{ tc.http_method_pascal }}
        "expected_status",         // bare int literal
        "path_argument_list",      // TemplateRenderer.PathArguments already quotes+escapes internally
        "query_expression",        // TemplateRenderer.QueryExpression already quotes+escapes internally
        "required_scopes_args",    // already quotes+escapes internally; also a bare `if` condition
        "identity_override",       // bare enum member expression (IdentitySlot.X); also an `if` condition
        "mutates",                 // boolean, `if` condition only
        "identity_needs_guard",    // boolean, `if` condition only
        "emits_fixture_lookup",    // boolean, `if` condition only
        "has_body",                // boolean, `if` condition only
        "client_type_name",        // bare reference-position type argument: ApiClient<T>() — validated by CSharpIdentifier.TryValidateDottedName, not CSharpLiteral, at config-load time
        "client_call_expression",  // TemplateRenderer.BuildClientCallExpression already quotes+escapes internally; also an `if` condition
    };

    /// <summary>
    /// Matches a tc.NAME reference anywhere inside a Scriban tag, independent of the tag's own
    /// delimiters (<c>{{</c>, <c>{{~</c>, <c>{{-</c>), trailing filters (<c>| string.upcase</c>),
    /// or whitespace around the dot. Deliberately does not anchor to <c>}}</c> or require the
    /// reference to be the only thing in the tag — see <see cref="ExtractReferences"/>, which
    /// already isolates one tag's body before this runs.
    /// </summary>
    private static readonly Regex TcReference = new(@"tc\s*\.\s*(\w+)", RegexOptions.Compiled);

    [TestMethod]
    public void EveryTemplateFieldReferenceIsEscapedOrExplicitlyAllowed()
    {
        var template = LoadEmbeddedTemplate("mstest-class.scriban");
        var references = ExtractReferences(template);

        // If this is ever zero, tag extraction has stopped matching the template's actual
        // syntax — that is this guard silently going blind, not a clean bill of health, so it
        // must fail loudly rather than pass vacuously.
        references.Count.ShouldBeGreaterThan(0,
            "no tc.<name> references were found in mstest-class.scriban at all. Either tag " +
            "extraction in TemplateEscapingGuardTests no longer matches the template's syntax, " +
            "or this guard is passing vacuously — do not leave it silently disabled.");

        var offenders = references
            .Where(r => !IsAccountedFor(r.Name, r.QuotedAdjacent))
            .Select(r => $"{r.Name} ({(r.QuotedAdjacent ? "quoted" : "bare")})")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "mstest-class.scriban references tc.<name> for [" + string.Join(", ", offenders) +
            "] without accounting for it. A '(quoted)' entry sits inside a C# string literal: " +
            "either it carries spec text — apply CSharpLiteral.Escape to it in " +
            "TemplateRenderer.RenderClass and rename it with a '_literal' suffix — or it never " +
            "does, in which case add it to TemplateEscapingGuardTests.AllowedInQuotedPosition " +
            "with a one-line reason. A '(bare)' entry composes C# outside literal position: if " +
            "it is truly never pasted into a string, add it to " +
            "TemplateEscapingGuardTests.AllowedInBarePosition instead.");
    }

    private static bool IsAccountedFor(string name, bool quotedAdjacent)
        => name.EndsWith("_literal", StringComparison.Ordinal)
        || (quotedAdjacent && AllowedInQuotedPosition.Contains(name))
        || (!quotedAdjacent && AllowedInBarePosition.Contains(name));

    /// <summary>
    /// Scans the raw template character by character, tracking whether the current position is
    /// inside a C# string literal by toggling on every double quote encountered outside a
    /// Scriban tag — tag delimiters and tag bodies are skipped whole, so a filter argument or
    /// anything else inside a tag can never itself flip that state. Each tag's body is then
    /// scanned with <see cref="TcReference"/> for every reference it contains, and every one
    /// found is tagged with whatever quote state was active the moment the tag opened.
    /// <para>
    /// This is what makes <c>"{{ tc.a }}/{{ tc.b }}"</c> classify both references as quoted even
    /// though neither tag is textually adjacent to a quote character — the scan already knows it
    /// never left the string when it reached either one. A narrower "is this tag immediately
    /// preceded and followed by a quote" check would miss exactly that case.
    /// </para>
    /// </summary>
    private static IReadOnlyList<(string Name, bool QuotedAdjacent)> ExtractReferences(string template)
    {
        var results = new List<(string, bool)>();
        var insideQuotedString = false;
        var i = 0;

        while (i < template.Length)
        {
            if (template[i] == '{' && i + 1 < template.Length && template[i + 1] == '{')
            {
                var close = template.IndexOf("}}", i + 2, StringComparison.Ordinal);
                if (close < 0)
                {
                    break; // Malformed template — the zero-references check above catches this.
                }

                var tagBody = template[(i + 2)..close];
                foreach (Match match in TcReference.Matches(tagBody))
                {
                    results.Add((match.Groups[1].Value, insideQuotedString));
                }

                i = close + 2;
                continue;
            }

            if (template[i] == '"')
            {
                insideQuotedString = !insideQuotedString;
            }

            i++;
        }

        return results;
    }

    private static string LoadEmbeddedTemplate(string fileName)
    {
        var assembly = typeof(TemplateRenderer).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith(fileName, StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

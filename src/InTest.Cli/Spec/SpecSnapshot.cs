using System.Text.Json;
using InTest.Cli.Json;

namespace InTest.Cli.Spec;

/// <summary>
/// The committed snapshot of a URL-sourced OpenAPI document (§9). When <c>spec.source</c> names an
/// <c>http(s)://</c> URL, <c>generate</c> fetches it and writes the result here; every other read
/// of "the spec" in that project — <c>generate --check</c>, <c>fixtures repair</c> — reads this
/// file and never the network (<c>[no-refetch]</c>).
/// <para>
/// This type owns three things that would otherwise be scattered literals: the file's name, the
/// bytes it is written in, and the reprint step that produces them. The last is the load-bearing
/// one — see <see cref="Reprint"/>.
/// </para>
/// <para>
/// <b>It is generator-owned, like <c>Generated/</c> and <c>coverage-report.json</c></b>, and it
/// sits at the project root beside the latter rather than under <c>Generated/</c>. Two reasons,
/// and the first is not cosmetic: <c>generate</c> deletes <c>Generated/</c> wholesale on every
/// write run, and this file has to survive being read *from* on the very run that would delete it.
/// The second is that §9's build-time copy (not built — see the plan's "What does not change")
/// expects a stable project-relative path for the <c>.csproj</c> to point <c>InTestSpecSource</c>
/// at, which a directory that is periodically emptied cannot offer.
/// </para>
/// <para>
/// <b>It is deliberately not in <c>GenerateCommand.BuildOutputs</c>.</b> Under <c>--check</c> this
/// file is the *input* the outputs were rendered from, so comparing it against a re-render of
/// itself would be a tautology wearing the shape of a guarantee. It is also why the orphan sweep
/// never trips over it: that walks <c>Generated/</c>, and this lives a directory up.
/// </para>
/// </summary>
public static class SpecSnapshot
{
    /// <summary>
    /// Project-relative, and fixed. §9 names it, <c>docs/getting-started.md</c>'s commit table
    /// names it, and the <c>.gitattributes</c> <c>init</c> scaffolds pins it — a constant rather
    /// than four string literals that agree today.
    /// </summary>
    public const string FileName = "spec.json";

    /// <summary>
    /// Re-emits <paramref name="json"/> indented, CRLF, and with the relaxed encoder, returning
    /// the exact text to write to disk (trailing newline included).
    /// <para>
    /// <b>Why reprint at all rather than write the fetched bytes verbatim.</b> §9's whole
    /// justification for the snapshot is that a spec change "arrives as a reviewable diff". Real
    /// Swagger endpoints overwhelmingly serve <em>minified</em> JSON, and a 200 KB single-line
    /// <c>spec.json</c> produces a diff of precisely zero review value — the feature would work
    /// while delivering none of the thing it exists to deliver.
    /// </para>
    /// <para>
    /// <b>Four properties this has to have, all confirmed by direct experiment</b> against
    /// .NET 10's <c>System.Text.Json</c> before this approach was chosen, and each pinned by a
    /// test in <c>SpecSnapshotTests</c> so a future edit cannot quietly lose one:
    /// </para>
    /// <list type="bullet">
    /// <item><b>Number fidelity.</b> <c>1.0</c>, <c>0.1000</c>, <c>1e10</c> and a 23-digit integer
    /// all survive as their original raw text — <see cref="JsonElement.WriteTo"/> writes a
    /// number's source bytes rather than round-tripping it through a numeric type. This is not a
    /// nicety: a schema's <c>multipleOf</c>, <c>maximum</c> and <c>default</c> are load-bearing
    /// values that generated assertions are built from, and silently renormalising <c>1.0</c> to
    /// <c>1</c> would change a document InTest promised only to snapshot.</item>
    /// <item><b>Idempotence.</b> <c>Reprint(Reprint(x)) == Reprint(x)</c>. Without it a
    /// <c>generate</c>/<c>--check</c> pair could disagree forever about a file neither is
    /// changing.</item>
    /// <item><b>String round-trip.</b> Every string parses back to the identical value.</item>
    /// <item><b>Readability.</b> <c>café &amp; &lt;tag&gt;</c> stays literal instead of becoming
    /// <c>café & <tag></c>, which is what
    /// <see cref="CommittedJsonOptions.Value"/>'s default encoder produces and why this routes
    /// through <see cref="CommittedJsonOptions.SpecSnapshot"/> instead.</item>
    /// </list>
    /// </summary>
    /// <exception cref="SpecLoadException">
    /// <paramref name="json"/> is not JSON at all. Routed through <see cref="SpecFetcher.YamlReason"/>
    /// so that an adopter pointed at a YAML endpoint gets the same sentence regardless of which
    /// layer noticed — <see cref="SpecFetcher"/> from a <c>Content-Type</c> header, this from a
    /// body that will not parse.
    /// </exception>
    public static string Reprint(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new SpecLoadException(
                $"The OpenAPI document could not be read as JSON: {ex.Message} {SpecFetcher.YamlReason}", ex);
        }

        using (document)
        {
            // The trailing newline is appended by hand, exactly as every other writer in this
            // repository does it: indented JSON serialization never emits a line ending after the
            // final closing brace. See CommittedJsonOptions' own doc comment for why that is not
            // folded into the shared instance.
            //
            // Phrased that way rather than by naming the options property, matching
            // CoverageReport and FixtureDocument: JsonWritingOptionsGuardTests scans this file's
            // *text* for that property name, so writing it even in a comment trips a guard whose
            // subject is a writer declaring its own options. Caught by that guard, which is the
            // guard working — a text scan cannot tell prose from code, and the convention every
            // other writer already follows is what keeps it from having to.
            return JsonSerializer.Serialize(document.RootElement, CommittedJsonOptions.SpecSnapshot) + "\r\n";
        }
    }
}

using InTest.Cli.Spec;
using Shouldly;

namespace InTest.Cli.Tests;

/// <summary>
/// The four properties <see cref="SpecSnapshot.Reprint"/> has to have, each pinned separately so a
/// future edit cannot quietly lose one. All four were confirmed by direct experiment against
/// .NET 10's <c>System.Text.Json</c> <i>before</i> the reprinting approach was chosen over writing
/// the fetched bytes verbatim — these tests are that experiment, kept.
/// <para>
/// Reprinting exists because §9's justification for the snapshot is that a spec change "arrives as
/// a reviewable diff", and real Swagger endpoints overwhelmingly serve minified JSON. A 200 KB
/// single-line <c>spec.json</c> would leave the feature technically working and practically
/// pointless — see <see cref="MinifiedInputBecomesReviewable"/>, which is the test that says so.
/// </para>
/// </summary>
[TestClass]
public class SpecSnapshotTests
{
    private const string Minified =
        """{"openapi":"3.0.3","info":{"title":"Orders","version":"1.0"},"paths":{}}""";

    /// <summary>
    /// The property with the most to lose. A schema's <c>multipleOf</c>, <c>maximum</c>,
    /// <c>default</c> and <c>enum</c> values are load-bearing — generated assertions are built
    /// from them — so a snapshot that silently renormalises <c>1.0</c> to <c>1</c>, or rounds a
    /// 23-digit integer through a <c>double</c>, has changed a document InTest promised only to
    /// copy. <c>JsonElement.WriteTo</c> writes a number's original source bytes rather than
    /// round-tripping it through a numeric type, which is what makes this hold; asserting on the
    /// raw text rather than on a parsed value is what would catch it if that ever stopped being
    /// true.
    /// </summary>
    [TestMethod]
    [DataRow("1.0", DisplayName = "trailing zero after the point")]
    [DataRow("0.1000", DisplayName = "trailing zeros")]
    [DataRow("1e10", DisplayName = "exponent notation")]
    [DataRow("12345678901234567890123", DisplayName = "beyond every fixed-width integer type")]
    [DataRow("-0", DisplayName = "negative zero")]
    public void PreservesNumbersAsWritten(string number)
    {
        var reprinted = SpecSnapshot.Reprint($$"""{"openapi":"3.0.3","x":{{number}}}""");

        reprinted.ShouldContain($"\"x\": {number}",
            customMessage: "a number must survive as its own source text, not as a re-rendered value");
    }

    /// <summary>
    /// <c>Reprint(Reprint(x)) == Reprint(x)</c>. Without this a <c>generate</c>/<c>--check</c>
    /// pair could disagree forever about a file neither of them is changing: `generate` writes
    /// one byte sequence, `--check` reads it back, reprints it, gets a different sequence, and
    /// reports a difference that regenerating cannot fix.
    /// </summary>
    [TestMethod]
    public void IsIdempotent()
    {
        var once = SpecSnapshot.Reprint(Minified);

        SpecSnapshot.Reprint(once).ShouldBe(once);
    }

    /// <summary>
    /// The point of the whole exercise: a minified document — what a Swagger endpoint typically
    /// serves — becomes something a reviewer can read a diff of.
    /// </summary>
    [TestMethod]
    public void MinifiedInputBecomesReviewable()
    {
        var reprinted = SpecSnapshot.Reprint(Minified);

        Minified.ShouldNotContain("\n", customMessage: "the fixture is minified, or this proves nothing");
        reprinted.Split('\n').Length.ShouldBeGreaterThan(5,
            customMessage: "a single-line snapshot has a diff of no review value, which is the " +
                           "one thing §9 takes the snapshot for");
    }

    /// <summary>
    /// Why this routes through <c>CommittedJsonOptions.SpecSnapshot</c> rather than
    /// <c>CommittedJsonOptions.Value</c>. The default encoder escapes non-ASCII and
    /// HTML-sensitive characters aggressively, turning an ordinary description into
    /// <c>café & <tag></c> — valid JSON encoding the identical string, and
    /// unreadable in a pull request. This is the assertion that fails if someone later
    /// "simplifies" the two options instances back into one.
    /// </summary>
    [TestMethod]
    public void KeepsTextReadableRatherThanEscapingIt()
    {
        var reprinted = SpecSnapshot.Reprint(
            """{"openapi":"3.0.3","info":{"description":"café & <tag> — ✓"}}""");

        reprinted.ShouldContain("café & <tag> — ✓",
            customMessage: "a snapshot nobody can read a diff of is a snapshot that failed its purpose");
        reprinted.ShouldNotContain("\\u00E9",
            customMessage: "this is what CommittedJsonOptions.Value's default encoder would emit");
    }

    /// <summary>Every string parses back to the value it went in as, escaping notwithstanding.</summary>
    [TestMethod]
    public void RoundTripsStringsExactly()
    {
        const string awkward = "quote \" backslash \\ newline \n tab \t emoji 🚀";
        var reprinted = SpecSnapshot.Reprint(
            System.Text.Json.JsonSerializer.Serialize(new { openapi = "3.0.3", x = awkward }));

        System.Text.Json.JsonDocument.Parse(reprinted).RootElement
            .GetProperty("x").GetString().ShouldBe(awkward);
    }

    /// <summary>
    /// [crlf-everywhere] applies to the snapshot like every other committed file InTest writes.
    /// The trailing newline is asserted as exactly one because indented JSON serialization emits
    /// none at all after the final closing brace — the call site adds it by hand, and "by hand"
    /// is where an off-by-one lives.
    /// </summary>
    [TestMethod]
    public void WritesCrlfInteriorLineEndingsAndExactlyOneTrailingNewline()
    {
        var reprinted = SpecSnapshot.Reprint(Minified);

        reprinted.Replace("\r\n", string.Empty).ShouldNotContain("\n",
            customMessage: "every interior line ending must be CRLF, not a bare LF");
        reprinted.ShouldEndWith("}\r\n");
        reprinted.ShouldNotEndWith("}\r\n\r\n");
    }

    /// <summary>
    /// A body that is not JSON is the shape a YAML endpoint arrives in when nothing upstream
    /// noticed — <c>SpecFetcher</c> catches most of them from the <c>Content-Type</c> header or a
    /// leading <c>openapi:</c> line, and this is the backstop. It must say "YAML", because
    /// "unexpected character at line 1" sends an adopter looking for a corrupt file when what
    /// they actually did was point at the <c>.yaml</c> URL.
    /// </summary>
    [TestMethod]
    public void ExplainsAYamlBodyRatherThanReportingAParseError()
    {
        var reason = Should.Throw<SpecLoadException>(
            () => SpecSnapshot.Reprint("openapi: 3.0.3\ninfo:\n  title: Orders\n")).Message;

        reason.ShouldContain("YAML");
        reason.ShouldNotContain("unexpected failure");
    }
}

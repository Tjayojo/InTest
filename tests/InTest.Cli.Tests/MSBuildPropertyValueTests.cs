using System.Xml.Linq;
using InTest.Cli.Naming;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class MSBuildPropertyValueTests
{
    private static string Escape(string value)
    {
        MSBuildPropertyValue.TryEscape(value, "project.spec", out var escaped, out var reason).ShouldBeTrue(reason);
        return escaped;
    }

    [TestMethod]
    public void TryEscape_LeavesOrdinaryTextUnchanged()
    {
        Escape("orders.json").ShouldBe("orders.json");
    }

    // --- Layer 1: MSBuild ---

    [TestMethod]
    [DataRow("%", "%25")]
    [DataRow("$", "%24")]
    [DataRow("@", "%40")]
    [DataRow(";", "%3B")]
    [DataRow("?", "%3F")]
    [DataRow("*", "%2A")]
    public void TryEscape_EscapesEachMSBuildSpecialCharacter(string input, string expected)
    {
        Escape($"a{input}b").ShouldBe($"a{expected}b");
    }

    [TestMethod]
    public void TryEscape_LeavesASingleQuoteUnescaped()
    {
        // '\'' is in MSBuild's special-character set too, but is deliberately excluded: it is
        // inert in property-value text and only becomes special inside an MSBuild condition, a
        // context this call site never writes into. Escaping it would only make the generated
        // project file uglier for a case that cannot arise here.
        Escape("a'b").ShouldBe("a'b");
    }

    // --- Layer 2: XML ---

    [TestMethod]
    [DataRow("&", "&amp;")]
    [DataRow("<", "&lt;")]
    public void TryEscape_EscapesEachXmlSpecialCharacter(string input, string expected)
    {
        Escape($"a{input}b").ShouldBe($"a{expected}b");
    }

    [TestMethod]
    public void TryEscape_DoesNotEscapeCharactersLegalRawInXmlCharacterData()
    {
        // '>' and '\'' are legal raw in XML character data; escaping them would only make the
        // generated project file harder for an adopter to read, for no gain in safety.
        Escape("a>b'c").ShouldBe("a>b'c");
    }

    // --- Ordering hazards ---
    //
    // The dedicated tests below are a secondary guard, not the primary one: every DataRow in
    // TryEscape_EscapesEachMSBuildSpecialCharacter expects an output containing '%' ($->%24,
    // @->%40, ;->%3B, ?->%3F, *->%2A), so reordering any of those five past '%' already fails one
    // of those rows. These tests exist to make the hazard legible on its own and to cover the
    // two-characters-at-once case those single-character rows cannot.

    [TestMethod]
    public void TryEscape_EscapesPercentBeforeTheOtherMSBuildCharactersItWouldOtherwiseReEscape()
    {
        // '%' must be escaped first: '$', '@' and ';' each expand to a sequence that itself
        // contains a literal '%' (%24, %40, %3B). Escaping '%' after them would re-escape the
        // '%' those steps just introduced. A value containing both characters is the only way to
        // observe the divergence: the wrong order (escape '$' before '%') turns "%$" into
        // "%25%2524" instead of the correct "%25%24".
        Escape("%$").ShouldBe("%25%24");
    }

    [TestMethod]
    public void TryEscape_EscapesPercentBeforeQuestionMarkToo()
    {
        // Same hazard as the '%$' case above, now for the wildcard-glob pair added to the escaped
        // set for Include-globbing safety: '?' also expands to a sequence containing a literal
        // '%' (%3F). The wrong order (escape '?' before '%') would turn "%?" into "%25%253F"
        // instead of the correct "%25%3F".
        Escape("%?").ShouldBe("%25%3F");
    }

    [TestMethod]
    public void TryEscape_EscapesMSBuildCharactersBeforeXmlCharacters()
    {
        // MSBuild must run before XML. Escaping XML first would turn '&' into "&amp;", and the
        // MSBuild pass would then see the ';' that "&amp;" itself introduces and mangle a single
        // '&' into "&amp%3B" -- corrupting the escape XML just produced. Running MSBuild first
        // means the XML pass never sees a raw ';' to worry about, so a lone '&' just becomes
        // "&amp;".
        Escape("&").ShouldBe("&amp;");
    }

    [TestMethod]
    public void TryEscape_RoundTripsThroughARealXmlParser()
    {
        // Exercises both layers on one value: '$' needs MSBuild's %XX escaping, '&' needs XML's
        // entity escaping. Verified against a real XML parser rather than reasoned about -- a
        // hand-rolled substitute for what XML actually accepts is exactly the kind of gap
        // (U+FFFE/U+FFFF) that got past reasoning-only review once already on this type.
        var value = "R&D$orders.json";
        var escaped = Escape(value);

        var document = XDocument.Parse($"<a>{escaped}</a>");

        // Parsing undoes layer 2 (XML entities) but has no notion of layer 1 (%XX) -- what comes
        // back is exactly layer 1's output standing alone: the MSBuild escape applied, the XML
        // escape undone.
        document.Root!.Value.ShouldBe("R&D%24orders.json");
    }

    // --- Characters legal raw in both grammars ---

    [TestMethod]
    [DataRow(0x0009, "\t", DisplayName = "Tab")]
    [DataRow(0x000A, "\n", DisplayName = "LF")]
    [DataRow(0x000D, "\r", DisplayName = "CR")]
    public void TryEscape_PassesTabLineFeedAndCarriageReturnThroughUnescaped(int codePoint, string expectedChar)
    {
        var value = "a" + (char)codePoint + "b";
        Escape(value).ShouldBe("a" + expectedChar + "b");
    }

    [TestMethod]
    public void TryEscape_AcceptsTheReplacementCharacterNextToTheRefusedNoncharacters()
    {
        // U+FFFD is a legal XML character (XmlConvert.IsXmlChar accepts it) and sits immediately
        // next to the two refused noncharacters U+FFFE/U+FFFF below. Without this boundary check,
        // a future tweak to the refusal predicate could over-refuse this neighbour unnoticed.
        // Built from the numeric code point, not a pasted glyph, for the same reason the refusal
        // DataRows below are.
        var value = "a" + (char)0xFFFD + "b";

        Escape(value).ShouldBe(value);
    }

    // --- Refusal residue: characters XML 1.0 cannot represent in any form ---
    //
    // Every DataRow here builds its character from a numeric code point rather than pasting the
    // literal glyph -- the same reason CSharpLiteralTests does for its forbidden characters: most
    // of these are actual control codes (BEL rings a bell, BS moves the cursor) that would
    // corrupt this very source file, an editor, or a terminal if pasted raw.
    [TestMethod]
    [DataRow(0x0000, DisplayName = "NUL")]
    [DataRow(0x0007, DisplayName = "BEL")]
    [DataRow(0x0008, DisplayName = "BS")]
    [DataRow(0x000B, DisplayName = "VT")]
    [DataRow(0x000C, DisplayName = "FF")]
    [DataRow(0x000E, DisplayName = "SO")]
    [DataRow(0x001F, DisplayName = "US")]
    [DataRow(0xFFFE, DisplayName = "Noncharacter FFFE")]
    [DataRow(0xFFFF, DisplayName = "Noncharacter FFFF")]
    public void TryEscape_RefusesEachCharacterXmlCannotRepresent(int codePoint)
    {
        var value = "a" + (char)codePoint + "b";

        MSBuildPropertyValue.TryEscape(value, "project.spec", out _, out var reason).ShouldBeFalse();

        reason.ShouldContain("project.spec", Case.Sensitive);
        reason.ShouldContain($"U+{codePoint:X4}", Case.Sensitive);
        // The point of Display(): the offending character must be named by code point, never
        // pasted into the message raw. Asserting only ShouldContain("U+...") above would pass
        // even if Display() were deleted, since that substring comes from the message template
        // regardless -- this is the assertion that actually depends on Display() doing its job.
        reason.ShouldNotContain(((char)codePoint).ToString());
    }

    [TestMethod]
    public void TryEscape_RefusesAnUnpairedHighSurrogate()
    {
        var value = "a" + (char)0xD800 + "b";

        MSBuildPropertyValue.TryEscape(value, "project.spec", out _, out var reason).ShouldBeFalse();

        reason.ShouldContain("project.spec", Case.Sensitive);
        reason.ShouldContain("U+D800", Case.Sensitive);
        reason.ShouldNotContain(((char)0xD800).ToString());
    }

    [TestMethod]
    public void TryEscape_RefusesAnUnpairedLowSurrogate()
    {
        var value = "a" + (char)0xDC00 + "b";

        MSBuildPropertyValue.TryEscape(value, "project.spec", out _, out var reason).ShouldBeFalse();

        reason.ShouldContain("project.spec", Case.Sensitive);
        reason.ShouldContain("U+DC00", Case.Sensitive);
        reason.ShouldNotContain(((char)0xDC00).ToString());
    }

    [TestMethod]
    public void TryEscape_AcceptsAProperlyPairedSurrogate()
    {
        // A correctly paired surrogate (U+1F600 GRINNING FACE) is a legal XML character and must
        // not be confused with the unpaired-surrogate refusal cases above. Built from the numeric
        // UTF-16 pair rather than pasted as a literal emoji glyph, for the same reason
        // CSharpLiteral.cs avoids pasting NEL/LS/PS raw -- this documents the exact pair structure
        // the test exercises rather than relying on an editor to preserve an opaque glyph intact.
        var value = "a" + new string([(char)0xD83D, (char)0xDE00]) + "b";

        Escape(value).ShouldBe(value);
    }

    [TestMethod]
    public void TryEscape_SetsEscapedToADefinedValueOnTheFalsePath()
    {
        var value = "a" + (char)0x0000 + "b";

        MSBuildPropertyValue.TryEscape(value, "project.spec", out var escaped, out _).ShouldBeFalse();
        escaped.ShouldBe(string.Empty);
    }

    [TestMethod]
    public void TryEscape_ThrowsOnNullValue()
    {
        Should.Throw<ArgumentNullException>(() => MSBuildPropertyValue.TryEscape(null!, "project.spec", out _, out _));
    }

    [TestMethod]
    public void TryEscape_ThrowsOnNullSetting()
    {
        Should.Throw<ArgumentNullException>(() => MSBuildPropertyValue.TryEscape("orders.json", null!, out _, out _));
    }
}

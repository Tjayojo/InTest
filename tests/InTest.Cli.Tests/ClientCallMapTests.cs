using InTest.Cli.Clients;
using Shouldly;

namespace InTest.Cli.Tests;

/// <summary>
/// <see cref="ClientCallMap"/> follows <see cref="Fixtures.FixtureDocument"/>'s parse/refusal
/// idiom deliberately (both are committed, hand-edited files), so these tests follow
/// <c>FixtureDocumentTests</c>'s shape: a malformed field throws <see cref="ClientCallMapFormatException"/>
/// naming the offending field, never a raw framework exception.
/// </summary>
[TestClass]
public class ClientCallMapTests
{
    private string _root = null!;

    [TestInitialize]
    public void CreateProject()
    {
        _root = Path.Combine(Path.GetTempPath(), "intest-clientmap-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void RemoveProject()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void WriteMap(string json) => File.WriteAllText(Path.Combine(_root, ClientCallMap.FileName), json);

    [TestMethod]
    public void ParsesOverrides()
    {
        var map = ClientCallMap.Parse("""{ "overrides": { "getOrderById": "Orders[{id}].GetAsync" } }""");

        map.Overrides.ShouldContainKeyAndValue("getOrderById", "Orders[{id}].GetAsync");
    }

    [TestMethod]
    public void ParsesMultipleOverrides()
    {
        var map = ClientCallMap.Parse("""
        { "overrides": {
            "getOrderById": "Orders[{id}].GetAsync",
            "deleteOrder": "Orders[{id}].DeleteAsync"
        } }
        """);

        map.Overrides.Count.ShouldBe(2);
        map.Overrides.ShouldContainKeyAndValue("getOrderById", "Orders[{id}].GetAsync");
        map.Overrides.ShouldContainKeyAndValue("deleteOrder", "Orders[{id}].DeleteAsync");
    }

    // ---- missing file: empty map, not an error ---------------------------------------------

    [TestMethod]
    public void LoadReturnsEmptyWhenTheFileDoesNotExist()
    {
        var map = ClientCallMap.Load(_root);

        map.Overrides.ShouldBeEmpty();
    }

    [TestMethod]
    public void LoadReadsAnExistingFile()
    {
        WriteMap("""{ "overrides": { "getOrderById": "Orders[{id}].GetAsync" } }""");

        var map = ClientCallMap.Load(_root);

        map.Overrides.ShouldContainKeyAndValue("getOrderById", "Orders[{id}].GetAsync");
    }

    // ---- an absent "overrides" key is not malformed -----------------------------------------

    [TestMethod]
    public void ParsesAnEmptyDocumentAsAnEmptyMap()
    {
        var map = ClientCallMap.Parse("{}");

        map.Overrides.ShouldBeEmpty();
    }

    // ---- blank/whitespace value: refused loudly ---------------------------------------------

    [TestMethod]
    [DataRow("", DisplayName = "empty string")]
    [DataRow("   ", DisplayName = "whitespace only")]
    public void RefusesABlankOverrideValue(string value)
    {
        var exception = Should.Throw<ClientCallMapFormatException>(() =>
            ClientCallMap.Parse($$"""{ "overrides": { "getOrderById": "{{value}}" } }"""));

        exception.Message.ShouldContain("getOrderById", Case.Sensitive);
        exception.Message.ShouldContain("blank");
    }

    [TestMethod]
    public void RefusesANonStringOverrideValue()
    {
        var exception = Should.Throw<ClientCallMapFormatException>(() =>
            ClientCallMap.Parse("""{ "overrides": { "getOrderById": 42 } }"""));

        exception.Message.ShouldContain("getOrderById", Case.Sensitive);
        exception.Message.ShouldContain("string");
    }

    [TestMethod]
    public void RefusesAnOverridesSectionThatIsNotAnObject()
    {
        var exception = Should.Throw<ClientCallMapFormatException>(() =>
            ClientCallMap.Parse("""{ "overrides": ["getOrderById"] }"""));

        exception.Message.ShouldContain("overrides", Case.Sensitive);
        exception.Message.ShouldContain("object");
    }

    // ---- malformed JSON: the same refusal idiom FixtureDocument uses -------------------------

    [TestMethod]
    public void RefusesMalformedJsonRatherThanThrowingJsonException()
    {
        var exception = Should.Throw<ClientCallMapFormatException>(() =>
            ClientCallMap.Parse("""{ "overrides": { "getOrderById": """));

        exception.Message.ShouldContain(ClientCallMap.FileName, Case.Sensitive);
        exception.Message.ShouldContain("not valid JSON");
    }

    [TestMethod]
    public void RefusesARootThatIsNotAnObject()
    {
        var exception = Should.Throw<ClientCallMapFormatException>(() =>
            ClientCallMap.Parse("""["getOrderById"]"""));

        exception.Message.ShouldContain("object");
    }
}

using Microsoft.Extensions.Configuration;
using Shouldly;

namespace InTest.Runtime.Tests;

[TestClass]
public class FixtureStoreTests
{
    private string _root = null!;

    private static TokenResolver Resolver(params (string Key, string Value)[] configValues)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues.Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)))
            .Build();
        return new TokenResolver(configuration, runId: "run-fixed-1");
    }

    [TestInitialize]
    public void CreateRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), "intest-fixstore-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void RemoveRoot()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void WriteBase(string operationKey, string json)
    {
        var dir = Path.Combine(_root, "fixtures");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, operationKey + ".json"), json);
    }

    private void WriteOverlay(string profile, string operationKey, string json)
    {
        var dir = Path.Combine(_root, "fixtures", profile);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, operationKey + ".json"), json);
    }

    [TestMethod]
    public void AnAbsentFixturesDirectoryIsAnEmptyStoreNotAnError()
    {
        // A spec whose every operation is a parameterless GET needs no fixtures. That is the
        // shape GeneratedSuiteExecutionTests uses, so this must not throw.
        var store = FixtureStore.Load(Path.Combine(_root, "no-such-directory"), profile: null);

        store.Count.ShouldBe(0);
        Should.Throw<FixtureNotFoundException>(() => store.Get("anything"))
              .Message.ShouldContain("intest fixtures repair", Case.Sensitive);
    }

    [TestMethod]
    public void OverlayMergesPerPropertyRatherThanReplacingTheObject()
    {
        WriteBase("op", """
            {"$meta":{"tier":1,"operationId":"op","generatedBy":"t"},
             "body":{"a":1,"nested":{"x":1,"y":2}}}
            """);
        WriteOverlay("qa", "op", """
            {"$meta":{"tier":1,"operationId":"op","generatedBy":"t"},
             "body":{"nested":{"y":99}}}
            """);

        var store = FixtureStore.Load(_root, "qa");
        var body = store.Get("op").Body!;

        body["a"]!.GetValue<int>().ShouldBe(1, "untouched base properties survive");
        body["nested"]!["x"]!.GetValue<int>().ShouldBe(1, "sibling properties survive a nested merge");
        body["nested"]!["y"]!.GetValue<int>().ShouldBe(99, "the environment wins");
    }

    [TestMethod]
    public void LoadsEveryBaseFixture()
    {
        WriteBase("op-a", """{"$meta":{"tier":1,"operationId":"op-a","generatedBy":"t"},"$parameters":{"id":"1"}}""");
        WriteBase("op-b", """{"$meta":{"tier":1,"operationId":"op-b","generatedBy":"t"},"$parameters":{"id":"2"}}""");

        var store = FixtureStore.Load(_root, profile: null);

        store.Count.ShouldBe(2);
        store.Get("op-a").Parameters["id"].ShouldBe("1");
        store.Get("op-b").Parameters["id"].ShouldBe("2");
    }

    [TestMethod]
    public void QueryParametersFromTheOverlayWinOverTheBase()
    {
        WriteBase("op", """{"$meta":{"tier":1,"operationId":"op","generatedBy":"t"},"$parameters":{"id":"1","page":"2"}}""");
        WriteOverlay("qa", "op", """{"$meta":{"tier":1,"operationId":"op","generatedBy":"t"},"$parameters":{"page":"99"}}""");

        var parameters = FixtureStore.Load(_root, "qa").Get("op").Parameters;

        parameters["id"].ShouldBe("1", "untouched base parameters survive");
        parameters["page"].ShouldBe("99", "the environment wins");
    }

    [TestMethod]
    public void AnOverlayWithNoBaseFixtureIsAnErrorNamingTheFile()
    {
        WriteOverlay("qa", "orphan", """{"$meta":{"tier":1,"operationId":"orphan","generatedBy":"t"},"body":{"x":1}}""");

        Should.Throw<FixtureFormatException>(() => FixtureStore.Load(_root, "qa"))
              .Message.ShouldContain("orphan.json", Case.Sensitive);
    }

    [TestMethod]
    public void AMalformedFixtureReportsItsFilenameRatherThanABareJsonException()
    {
        WriteBase("broken", "{ not valid json");

        Should.Throw<FixtureFormatException>(() => FixtureStore.Load(_root, profile: null))
              .Message.ShouldContain("broken.json", Case.Sensitive);
    }

    // --- ResolvedBody -------------------------------------------------------------------

    [TestMethod]
    public void ResolvedBody_ResolvesTokensThroughNestedObjectsAndArrays()
    {
        WriteBase("op", """
            {"$meta":{"tier":1,"operationId":"op","generatedBy":"t"},
             "body":{"outer":{"inner":"{{config:X}}"},"list":["{{config:Y}}","plain"]}}
            """);

        var body = FixtureStore.Load(_root, profile: null)
            .ResolvedBody("op", Resolver(("X", "resolved-x"), ("Y", "resolved-y")))!;

        body["outer"]!["inner"]!.GetValue<string>().ShouldBe("resolved-x", "tokens inside a nested object must resolve");
        body["list"]![0]!.GetValue<string>().ShouldBe("resolved-y", "tokens inside an array element must resolve");
        body["list"]![1]!.GetValue<string>().ShouldBe("plain", "a plain string with no token is unchanged");
    }

    [TestMethod]
    public void ResolvedBody_LeavesNonStringLeavesUntouched()
    {
        WriteBase("op", """
            {"$meta":{"tier":1,"operationId":"op","generatedBy":"t"},
             "body":{"count":42,"price":19.99,"active":true,"tag":null}}
            """);

        var body = FixtureStore.Load(_root, profile: null).ResolvedBody("op", Resolver())!;

        body["count"]!.GetValue<int>().ShouldBe(42, "a numeric leaf is not a token and must not be stringified");
        body["price"]!.GetValue<double>().ShouldBe(19.99);
        body["active"]!.GetValue<bool>().ShouldBe(true);
        body["tag"].ShouldBeNull();
    }

    [TestMethod]
    public void ResolvedBody_ReturnsNullWhenFixtureHasNoBody()
    {
        WriteBase("op", """{"$meta":{"tier":1,"operationId":"op","generatedBy":"t"},"$parameters":{"id":"1"}}""");

        FixtureStore.Load(_root, profile: null).ResolvedBody("op", Resolver()).ShouldBeNull();
    }

    [TestMethod]
    public void ResolvedBody_ResolvesUtcNowFreshOnEveryCallRatherThanCachingIt()
    {
        WriteBase("op", """
            {"$meta":{"tier":1,"operationId":"op","generatedBy":"t"},
             "body":{"createdAt":"{{utcNow}}"}}
            """);
        var tick = 0;
        var resolver = new TokenResolver(
            new ConfigurationBuilder().Build(), "run-1", () => DateTimeOffset.UnixEpoch.AddSeconds(tick++));
        var store = FixtureStore.Load(_root, profile: null);

        var first = store.ResolvedBody("op", resolver)!["createdAt"]!.GetValue<string>();
        var second = store.ResolvedBody("op", resolver)!["createdAt"]!.GetValue<string>();

        second.ShouldNotBe(first, "each ResolvedBody call must re-resolve rather than reuse a cached node");
    }

    // --- ResolvedParameter ---------------------------------------------------------------

    [TestMethod]
    public void ResolvedParameter_ResolvesATokenInTheStoredValue()
    {
        WriteBase("op", """{"$meta":{"tier":1,"operationId":"op","generatedBy":"t"},"$parameters":{"id":"{{runId}}"}}""");

        FixtureStore.Load(_root, profile: null)
            .ResolvedParameter("op", "id", Resolver())
            .ShouldBe("run-fixed-1");
    }

    [TestMethod]
    public void ResolvedParameter_ThrowsNamingTheMissingParameter()
    {
        WriteBase("op", """{"$meta":{"tier":1,"operationId":"op","generatedBy":"t"},"$parameters":{"id":"1"}}""");

        Should.Throw<FixtureNotFoundException>(
            () => FixtureStore.Load(_root, profile: null).ResolvedParameter("op", "missing", Resolver()))
              .Message.ShouldContain("missing");
    }

    // --- ResolvedQueryParameters -----------------------------------------------------------

    [TestMethod]
    public void ResolvedQueryParameters_OmitsNamesAbsentFromTheFixtureRatherThanErroring()
    {
        WriteBase("op", """{"$meta":{"tier":1,"operationId":"op","generatedBy":"t"},"$parameters":{"page":"2"}}""");

        var result = FixtureStore.Load(_root, profile: null)
            .ResolvedQueryParameters("op", ["page", "sort"], Resolver());

        result.ShouldContainKey("page");
        result["page"].ShouldBe("2");
        result.ShouldNotContainKey("sort");
    }

    [TestMethod]
    public void ResolvedQueryParameters_ResolvesTokensInTheSuppliedValues()
    {
        WriteBase("op", """{"$meta":{"tier":1,"operationId":"op","generatedBy":"t"},"$parameters":{"page":"{{config:Page}}"}}""");

        var result = FixtureStore.Load(_root, profile: null)
            .ResolvedQueryParameters("op", ["page"], Resolver(("Page", "7")));

        result["page"].ShouldBe("7");
    }

    [TestMethod]
    public void ResolvedQueryParameters_ReturnsEmptyWhenNoNamesAreRequested()
    {
        WriteBase("op", """{"$meta":{"tier":1,"operationId":"op","generatedBy":"t"},"$parameters":{"page":"2"}}""");

        FixtureStore.Load(_root, profile: null)
            .ResolvedQueryParameters("op", [], Resolver())
            .ShouldBeEmpty();
    }

    [TestMethod]
    public void ResolvedQueryParameters_ReturnsEmptyWhenTheOperationHasNoFixtureAtAll()
    {
        // A query-only operation whose parameters are all optional-with-no-value never needs a
        // fixture file to exist at all — this must not throw FixtureNotFoundException.
        var store = FixtureStore.Load(_root, profile: null);

        store.ResolvedQueryParameters("op-with-no-fixture", ["page", "sort"], Resolver())
             .ShouldBeEmpty();
    }
}

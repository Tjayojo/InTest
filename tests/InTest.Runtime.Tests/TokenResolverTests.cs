using Microsoft.Extensions.Configuration;
using Shouldly;

namespace InTest.Runtime.Tests;

[TestClass]
public class TokenResolverTests
{
    private static TokenResolver Resolver(params (string Key, string Value)[] configValues)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues.Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)))
            .Build();
        return new TokenResolver(configuration, runId: "run-fixed-1");
    }

    private static TokenResolver ResolverWith(params (string Key, string Value)[] publishedFixtureValues)
    {
        var configuration = new ConfigurationBuilder().Build();
        var published = publishedFixtureValues.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        return new TokenResolver(configuration, runId: "run-fixed-1", publishedFixtureValues: published);
    }

    [TestMethod]
    public void ConfigTokenReadsConfiguration()
    {
        var resolver = Resolver(("Orders:ApiKey", "the-value"));

        resolver.Resolve("{{config:Orders:ApiKey}}", "create-order.json").ShouldBe("the-value");
    }

    [TestMethod]
    public void SecretTokenResolvesTheSameWayAsConfig()
    {
        var resolver = Resolver(("Orders:ApiKey", "super-secret-value"));

        resolver.Resolve("{{secret:Orders:ApiKey}}", "create-order.json").ShouldBe("super-secret-value");
    }

    [TestMethod]
    public void SecretValuesNeverAppearInAnErrorMessage()
    {
        var resolver = Resolver(("Orders:ApiKey", "super-secret-value"));

        var ex = Should.Throw<FixtureResolutionException>(
            () => resolver.Resolve("{{secret:Orders:Missing}}", "create-order.json"));

        ex.Message.ShouldNotContain("super-secret-value");
        ex.Message.ShouldContain("Orders:Missing", Case.Sensitive);
    }

    [TestMethod]
    public void ASecretResolvedElsewhereInTheSameFixtureNeverLeaksIntoAnUnrelatedFailure()
    {
        // The value from an earlier, successfully-resolved {{secret:}} token must not survive
        // into the exception thrown by a later token failing in the same fixture.
        var resolver = Resolver(("Orders:ApiKey", "super-secret-value"));

        resolver.Resolve("{{secret:Orders:ApiKey}}", "create-order.json").ShouldBe("super-secret-value");

        var ex = Should.Throw<FixtureResolutionException>(
            () => resolver.Resolve("prefix {{secret:Orders:ApiKey}} suffix {{bogus}}", "create-order.json"));

        ex.Message.ShouldNotContain("super-secret-value");
    }

    [TestMethod]
    public void RunIdTokenIsIdenticalAcrossTwoResolutions()
    {
        var resolver = Resolver();

        var first = resolver.Resolve("{{runId}}", "f.json");
        var second = resolver.Resolve("{{runId}}", "f.json");

        first.ShouldBe("run-fixed-1");
        second.ShouldBe(first);
    }

    [TestMethod]
    public void UtcNowDiffersBetweenResolutionsBecauseItIsPerRequestNotCached()
    {
        var tick = 0;
        var configuration = new ConfigurationBuilder().Build();
        var resolver = new TokenResolver(configuration, "run-1", () => DateTimeOffset.UnixEpoch.AddSeconds(tick++));

        var first = resolver.Resolve("{{utcNow}}", "f.json");
        var second = resolver.Resolve("{{utcNow}}", "f.json");

        second.ShouldNotBe(first, "{{utcNow}} must be evaluated per call, not cached");
    }

    [TestMethod]
    public void AnUnknownTokenFailsNamingTheTokenAndListingTheSupportedOnes()
    {
        var resolver = Resolver();

        var ex = Should.Throw<FixtureResolutionException>(
            () => resolver.Resolve("{{bogus}}", "create-order.json"));

        ex.Message.ShouldContain("bogus");
        ex.Message.ShouldContain("config:", Case.Sensitive);
        ex.Message.ShouldContain("secret:", Case.Sensitive);
        ex.Message.ShouldContain("runId", Case.Sensitive);
        ex.Message.ShouldContain("utcNow", Case.Sensitive);
        // SupportedTokens once omitted {{fixture:...}}; left unfixed, the "Unknown token" message
        // would keep recommending a list missing the token that now works.
        ex.Message.ShouldContain("{{fixture:", Case.Sensitive);
    }

    [TestMethod]
    public void AFixtureTokenForAnUnpublishedKeyFailsNamingTheKeyNotAsNotSupportedUntilV1B()
    {
        // This test used to pin "{{fixture:...}} is not supported until v1-b" for every fixture
        // token (decision 4, v1-a). Task 4 replaces that branch entirely, so this is repointed
        // rather than deleted: it now pins that the old failure mode is gone. If it still passed
        // unchanged, the new branch below would be unreachable.
        //
        // Uses Resolver(), which never passes publishedFixtureValues, so this exercises the
        // null-default path specifically — distinct from AResolverWithNoPublishedKeysStillFailsUsefully
        // below, which passes an explicitly empty dictionary.
        var resolver = Resolver();

        var ex = Should.Throw<FixtureResolutionException>(
            () => resolver.Resolve("{{fixture:seededCustomer.id}}", "create-order.json"));

        ex.Message.ShouldContain("seededCustomer.id", Case.Sensitive);
        ex.Message.ShouldNotContain("v1-b");
    }

    [TestMethod]
    public void APublishedFixtureKeyResolvesToItsValue()
    {
        var resolver = ResolverWith(("seededCustomer.id", "c1"));

        resolver.Resolve("{{fixture:seededCustomer.id}}", "create-order.json").ShouldBe("c1");
    }

    [TestMethod]
    public void AnUnpublishedKeyThrowsAResolutionFailureNotALifecycleFailure()
    {
        // FixtureValidation.CheckLeaf catches FixtureResolutionException and nothing else. Throw
        // FixtureLifecycleException here and an unresolvable key stops being a blocked operation
        // and becomes a dead run, defeating v1-a's per-operation blocking.
        Should.Throw<FixtureResolutionException>(
            () => ResolverWith().Resolve("{{fixture:missing}}", "create-order.json"));
    }

    [TestMethod]
    public void AnUnpublishedKeyListsWhatIsAvailable()
    {
        var resolver = ResolverWith(
            ("seededCustomer.id", "should-not-leak-customer-value"),
            ("seededRegion.code", "should-not-leak-region-value"));

        var ex = Should.Throw<FixtureResolutionException>(
            () => resolver.Resolve("{{fixture:seededTenant.id}}", "update-order.json"));

        // §10 specifies both halves. Naming only the missing key leaves the reader guessing at
        // the spelling of the one they meant.
        ex.Message.ShouldContain("seededTenant.id", Case.Sensitive);
        ex.Message.ShouldContain("seededCustomer.id", Case.Sensitive);
        ex.Message.ShouldContain("seededRegion.code", Case.Sensitive);

        // This file already pins "no published *value* leaks into a resolution-error message"
        // for {{secret:}} (SecretValuesNeverAppearInAnErrorMessage above). Published fixture
        // values are seeded data that can carry identifiers or tokens, so the same invariant
        // applies here: only key names may appear, never the values behind them. Distinctive
        // sentinel values, same as that test's "super-secret-value", so an incidental substring
        // match cannot make this pass.
        ex.Message.ShouldNotContain("should-not-leak-customer-value");
        ex.Message.ShouldNotContain("should-not-leak-region-value");
    }

    [TestMethod]
    public void TheAvailableKeyListIsOrdinalSortedRegardlessOfPublishOrder()
    {
        // Ordinal and the culture-sensitive default Order() must actually disagree on these keys,
        // or a wrong comparer would leave this test green. Ordinal compares by code point
        // ('Z' = 90 < 'a' = 97 < 'm' = 109); a culture-aware compare orders alphabetically
        // regardless of case (apple, mango, Zebra) — the two orders are near opposite, so
        // swapping comparers flips them and this test catches it.
        var resolver = ResolverWith(("mango", "1"), ("Zebra", "2"), ("apple", "3"));

        var ex = Should.Throw<FixtureResolutionException>(
            () => resolver.Resolve("{{fixture:missing}}", "f.json"));

        var indexZebra = ex.Message.IndexOf("Zebra", StringComparison.Ordinal);
        var indexApple = ex.Message.IndexOf("apple", StringComparison.Ordinal);
        var indexMango = ex.Message.IndexOf("mango", StringComparison.Ordinal);
        indexZebra.ShouldBeGreaterThanOrEqualTo(0);
        indexApple.ShouldBeGreaterThan(indexZebra);
        indexMango.ShouldBeGreaterThan(indexApple);
    }

    [TestMethod]
    public void AResolverWithNoPublishedKeysStillFailsUsefully()
    {
        var resolver = ResolverWith();

        var ex = Should.Throw<FixtureResolutionException>(
            () => resolver.Resolve("{{fixture:seededCustomer.id}}", "create-order.json"));

        ex.Message.ShouldContain("seededCustomer.id", Case.Sensitive);
        ex.Message.ShouldNotContain("v1-b");
        // "(none)" is TokenResolver's own design choice for an empty published set (as opposed
        // to, say, a trailing "Published keys: ." with nothing listed) — pin it explicitly so an
        // implementation that renders the empty case some other way is caught here.
        ex.Message.ShouldContain("(none)");
    }

    [TestMethod]
    public void APublishedValueContainingAnotherTokenIsNotReExpanded()
    {
        // A seeded value that came back from an API could itself contain token-shaped text. If
        // resolution recursively expanded it, a published value would become a second injection
        // point for tokens the fixture author never wrote (e.g. a stray {{secret:...}}).
        var resolver = ResolverWith(("seededPayload.raw", "{{secret:Orders:ApiKey}}"));

        resolver.Resolve("{{fixture:seededPayload.raw}}", "f.json").ShouldBe("{{secret:Orders:ApiKey}}");
    }

    [TestMethod]
    public void AFixtureKeyIsMatchedExactlyRatherThanBeingTrimmed()
    {
        var resolver = ResolverWith((" seededCustomer.id ", "c1"));

        resolver.Resolve("{{fixture: seededCustomer.id }}", "f.json").ShouldBe("c1");
    }

    [TestMethod]
    public void AFixtureKeyLookupIsCaseSensitive()
    {
        var resolver = ResolverWith(("seededCustomer.id", "c1"));

        Should.Throw<FixtureResolutionException>(
            () => resolver.Resolve("{{fixture:SeededCustomer.Id}}", "create-order.json"));
    }

    [TestMethod]
    public void ACallerSuppliedCaseInsensitiveDictionaryDoesNotMakeLookupCaseInsensitive()
    {
        // The constructor copies into its own Ordinal-keyed dictionary rather than trusting
        // whatever comparer the caller built with. Without that normalisation, handing in an
        // OrdinalIgnoreCase dictionary here would let {{fixture:SeededCustomer.Id}} match a key
        // published as "seededCustomer.id" — behaviour that would then depend on which comparer
        // the caller's dictionary happened to use, rather than a rule TokenResolver itself enforces.
        var published = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["seededCustomer.id"] = "c1"
        };
        var resolver = new TokenResolver(
            new ConfigurationBuilder().Build(), runId: "run-fixed-1", publishedFixtureValues: published);

        Should.Throw<FixtureResolutionException>(
            () => resolver.Resolve("{{fixture:SeededCustomer.Id}}", "create-order.json"));
    }

    [TestMethod]
    public void APublishedValueThatIsAnEmptyStringResolvesToEmptyString()
    {
        var resolver = ResolverWith(("seededCustomer.id", ""));

        resolver.Resolve("{{fixture:seededCustomer.id}}", "f.json").ShouldBe(string.Empty);
    }

    [TestMethod]
    public void AMissingConfigKeyFailsNamingTheKey()
    {
        var resolver = Resolver();

        var ex = Should.Throw<FixtureResolutionException>(
            () => resolver.Resolve("{{config:Orders:ApiKey}}", "create-order.json"));

        ex.Message.ShouldContain("Orders:ApiKey", Case.Sensitive);
    }

    [TestMethod]
    public void AValueContainingNoTokenIsReturnedUnchanged()
    {
        var resolver = Resolver();

        resolver.Resolve("plain string, no tokens here", "f.json").ShouldBe("plain string, no tokens here");
    }

    [TestMethod]
    public void TheFileNameAppearsInResolutionErrorsSoAReaderKnowsWhichFixtureFailed()
    {
        var resolver = Resolver();

        Should.Throw<FixtureResolutionException>(
            () => resolver.Resolve("{{config:Missing:Key}}", "update-order.json"))
              .Message.ShouldContain("update-order.json", Case.Sensitive);
    }
}

using Shouldly;

namespace InTest.Runtime.Tests;

[TestClass]
public class FixtureContextTests
{
    [TestMethod]
    public void PublishedValueCanBeReadBack()
    {
        var context = new FixtureContext();

        context.Publish("seededTenant.id", "tenant-1");

        context.Get("seededTenant.id").ShouldBe("tenant-1");
    }

    [TestMethod]
    public void PublishingTheSameKeyTwiceIsAnError()
    {
        var context = new FixtureContext();
        context.Publish("seededTenant.id", "a");

        // A silent overwrite would make {{fixture:…}} depend on which fixture ran last, which is
        // precisely the non-determinism topological ordering exists to remove.
        Should.Throw<FixtureLifecycleException>(() => context.Publish("seededTenant.id", "b"))
              .Message.ShouldContain("seededTenant.id", Case.Sensitive);
    }

    [TestMethod]
    public void OnCleanupRecordsWithoutRunning()
    {
        var ran = false;
        var context = new FixtureContext();
        context.OnCleanup(() => { ran = true; return Task.CompletedTask; });

        ran.ShouldBeFalse("the context records teardown; FixtureRunner decides when it runs");
        context.CleanupActions.Count.ShouldBe(1);
    }

    [TestMethod]
    public void GetOnAnUnpublishedKeyNamesTheKeyAndListsWhatIsAvailable()
    {
        var context = new FixtureContext();
        context.Publish("zebra.id", "z");
        context.Publish("apple.id", "a");
        context.Publish("Middle.id", "m");

        // Ordinal, not culture-aware: a stable, reproducible order for error messages that list
        // every published key, independent of the machine's locale. Mirrors the courtesy
        // TokenResolver's own {{fixture:...}} lookup gives a miss.
        var ex = Should.Throw<FixtureLifecycleException>(() => context.Get("seededTenant.id"));
        ex.Message.ShouldContain("seededTenant.id", Case.Sensitive);
        ex.Message.ShouldContain("Middle.id, apple.id, zebra.id", Case.Sensitive);
    }

    [TestMethod]
    public void GetOnAnEmptyContextReportsNoneAvailable()
    {
        var context = new FixtureContext();

        Should.Throw<FixtureLifecycleException>(() => context.Get("seededTenant.id"))
              .Message.ShouldContain("(none)");
    }

    [TestMethod]
    public void ANullKeyIsRejected()
    {
        var context = new FixtureContext();

        Should.Throw<ArgumentException>(() => context.Publish(null!, "value"));
    }

    [TestMethod]
    public void AWhitespaceKeyIsRejected()
    {
        var context = new FixtureContext();

        Should.Throw<ArgumentException>(() => context.Publish("   ", "value"));
    }

    [TestMethod]
    public void PublishedValuesReturnsEveryPublishedKeyAndValue()
    {
        var context = new FixtureContext();
        context.Publish("seededTenant.id", "tenant-1");
        context.Publish("seededUser.id", "user-1");

        // Compared by content, not by enumeration order: PublishedValues never documented an
        // order guarantee, and the concurrent-safe backing store behind it does not preserve
        // insertion order the way Dictionary incidentally does for a handful of entries.
        var values = context.PublishedValues;
        values.Count.ShouldBe(2);
        values["seededTenant.id"].ShouldBe("tenant-1");
        values["seededUser.id"].ShouldBe("user-1");
    }

    [TestMethod]
    public void PublishedValuesIsAFreshSnapshotEachCall()
    {
        var context = new FixtureContext();
        context.Publish("seededTenant.id", "tenant-1");
        var before = context.PublishedValues;

        context.Publish("seededUser.id", "user-1");

        // Same freshness contract as CleanupActions: a caller holding an earlier snapshot must
        // not see it grow as more fixtures publish.
        before.Count.ShouldBe(1);
        context.PublishedValues.Count.ShouldBe(2);
    }

    [TestMethod]
    public void OnCleanupRecordsMultipleActionsInRegistrationOrder()
    {
        var context = new FixtureContext();
        var first = () => Task.CompletedTask;
        var second = () => Task.CompletedTask;

        context.OnCleanup(first);
        context.OnCleanup(second);

        context.CleanupActions.Count.ShouldBe(2);
        context.CleanupActions[0].ShouldBeSameAs(first);
        context.CleanupActions[1].ShouldBeSameAs(second);
    }

    [TestMethod]
    public async Task ConcurrentPublishAndOnCleanupFromOneFixtureDoNotCorruptState()
    {
        // The realistic case final review flagged: a single fixture fanning seeding out with
        // Task.WhenAll(...) to keep AssemblyInitialize fast, each branch calling Publish and
        // OnCleanup independently. FixtureRunner itself still awaits fixtures sequentially, but
        // nothing stops one fixture's own InitializeAsync from doing this internally, so
        // FixtureContext must not corrupt under it.
        const int count = 200;
        var context = new FixtureContext();

        await Task.WhenAll(Enumerable.Range(0, count).Select(i => Task.Run(() =>
        {
            context.Publish($"seeded{i}.id", $"value-{i}");
            context.OnCleanup(() => Task.CompletedTask);
        })));

        context.PublishedValues.Count.ShouldBe(count);
        context.CleanupActions.Count.ShouldBe(count);
        for (var i = 0; i < count; i++)
        {
            context.Get($"seeded{i}.id").ShouldBe($"value-{i}");
        }
    }
}

using System.Collections.Concurrent;

namespace InTest.Runtime;

/// <summary>
/// The state one assembly run's fixtures publish into and register teardown against. One
/// instance is created by <c>TestHost</c>, passed to every <see cref="IAssemblyFixture"/>, and
/// retained in a static field so <c>AssemblyCleanup</c> can drain the same instance the fixtures
/// wrote to (v1-b decision 4). This type only records — it runs nothing itself; <see cref="FixtureRunner"/>
/// (Task 3) owns ordering fixtures, invoking them, and taking and running the cleanup actions
/// recorded here. Taking cleanup actions on drain (rather than merely reading them) is still
/// recording-side bookkeeping, not execution, so that responsibility belongs on this type rather
/// than in <c>FixtureRunner</c>.
/// <para>
/// <b>Thread-safety.</b> <see cref="Publish"/> and <see cref="OnCleanup"/> may be called
/// concurrently, including from multiple tasks a single fixture starts with
/// <c>Task.WhenAll(...)</c> to seed several independent rows without serializing them —
/// <see cref="FixtureRunner"/> itself still awaits fixtures strictly sequentially, but nothing
/// stops one fixture's own <see cref="IAssemblyFixture.InitializeAsync"/> from fanning out
/// internally, and a competent adopter keeping <c>AssemblyInitialize</c> fast is exactly the case
/// this exists to not corrupt. <see cref="PublishedValues"/> and <see cref="CleanupActions"/> are
/// each a snapshot taken under the same synchronization as a write, so a concurrent reader never
/// observes a torn dictionary or list; <see cref="TakeCleanupActions"/> similarly swaps the
/// backing list out atomically, so a drain racing a late <see cref="OnCleanup"/> call either sees
/// that action or leaves it for the next drain, never loses or duplicates it. What is not
/// guaranteed is a specific interleaving across threads — two concurrent <see cref="Publish"/>
/// calls may be recorded in either order, and reversing <see cref="CleanupActions"/> for drain
/// only undoes <em>a</em> valid registration order, not necessarily wall-clock order across
/// threads within the same fixture.
/// </para>
/// </summary>
public sealed class FixtureContext
{
    private readonly ConcurrentDictionary<string, string> _published = new(StringComparer.Ordinal);
    private readonly List<Func<Task>> _cleanupActions = [];
    private readonly Lock _cleanupLock = new();

    /// <summary>
    /// Every published key and its value, as a fresh snapshot — same freshness contract as
    /// <see cref="CleanupActions"/>: a caller holding an earlier snapshot must not see it change
    /// as more fixtures publish.
    /// </summary>
    public IReadOnlyDictionary<string, string> PublishedValues => new Dictionary<string, string>(_published, StringComparer.Ordinal);

    /// <summary>
    /// Teardown registered so far and not yet taken for draining, in registration order — the
    /// order <see cref="FixtureRunner"/> must drain in reverse (v1-b decision 4). A fresh snapshot on
    /// every read, like <see cref="PublishedValues"/>: the backing list can shrink once draining
    /// starts taking from it, and a caller holding an earlier <see cref="IReadOnlyList{T}"/> from
    /// this property must not see it empty out from under them.
    /// </summary>
    public IReadOnlyList<Func<Task>> CleanupActions
    {
        get
        {
            lock (_cleanupLock)
            {
                return _cleanupActions.ToList();
            }
        }
    }

    /// <summary>
    /// Makes <paramref name="value"/> available to <c>{{fixture:...}}</c> tokens under
    /// <paramref name="key"/>. Publishing the same key twice throws rather than overwriting —
    /// a silent overwrite would make token resolution depend on which fixture happened to run
    /// last, exactly the non-determinism topological ordering exists to remove.
    /// </summary>
    public void Publish(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        if (!_published.TryAdd(key, value))
        {
            throw new FixtureLifecycleException(
                $"Fixture key '{key}' was already published. Each key may be published once.");
        }
    }

    /// <summary>
    /// The value published under <paramref name="key"/> — the supported way for a fixture that
    /// <c>DependsOn</c> another to read what that dependency published, directly in code, without
    /// round-tripping through a <c>{{fixture:...}}</c> token. <see cref="FixtureGraph.Order"/>
    /// guarantees every type named in <see cref="IAssemblyFixture.DependsOn"/> finishes
    /// <see cref="IAssemblyFixture.InitializeAsync"/> — and therefore every <see cref="Publish"/>
    /// call it makes — before the dependent's own runs, so a correctly declared dependency makes
    /// this call safe.
    /// <para>
    /// A miss throws <see cref="FixtureLifecycleException"/> naming both the requested key and
    /// every key published so far, ordinal-sorted for a reproducible message — the same courtesy
    /// <see cref="TokenResolver"/>'s own <c>{{fixture:...}}</c> lookup gives a token that misses —
    /// rather than a bare <see cref="KeyNotFoundException"/> that names neither.
    /// </para>
    /// </summary>
    public string Get(string key)
    {
        if (_published.TryGetValue(key, out var value))
        {
            return value;
        }

        var available = _published.IsEmpty
            ? "(none)"
            : string.Join(", ", _published.Keys.Order(StringComparer.Ordinal));

        throw new FixtureLifecycleException(
            $"Fixture key '{key}' has not been published. Published keys: {available}. " +
            "Check the key name for a typo, or add the fixture that publishes it to this " +
            "fixture's DependsOn so FixtureGraph orders it to run first.");
    }

    /// <summary>
    /// Records <paramref name="action"/> to run during teardown, next to whatever created the
    /// thing it cleans up. Recording never runs it — <see cref="FixtureRunner"/> decides when,
    /// draining every registered action in reverse.
    /// </summary>
    public void OnCleanup(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (_cleanupLock)
        {
            _cleanupActions.Add(action);
        }
    }

    /// <summary>
    /// Removes and returns every action registered so far, in registration order, leaving none
    /// behind. Taking rather than merely reading is what makes draining the same context twice
    /// safe without <see cref="FixtureRunner"/> having to track "already drained" as separate
    /// state: a second call finds nothing left to take, so a second drain is a no-op for free,
    /// and an action registered after a drain is picked up correctly by the next one — neither
    /// is true of a flag that simply remembers a context was drained once. Swapped out under the
    /// same lock <see cref="OnCleanup"/> and <see cref="CleanupActions"/> use, so a late
    /// registration racing a drain is either fully included or fully deferred to the next drain,
    /// never half-recorded. Internal because <see cref="FixtureRunner.DrainAsync"/> is the only
    /// caller; nothing else should be able to empty a context's cleanup list out from under it.
    /// </summary>
    internal IReadOnlyList<Func<Task>> TakeCleanupActions()
    {
        lock (_cleanupLock)
        {
            var actions = _cleanupActions.ToList();
            _cleanupActions.Clear();
            return actions;
        }
    }
}

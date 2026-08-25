namespace InTest.Runtime;

/// <summary>
/// Orders <see cref="IAssemblyFixture"/> instances so every fixture's <see cref="IAssemblyFixture.DependsOn"/>
/// finishes before it runs (v1-b decision 3). Integer priorities were rejected in §13 — someone always
/// needs to slot a new fixture between 15 and 20 — so ordering is derived from the dependency
/// graph itself, which has no gaps to slot into. This type only computes the order; <c>FixtureRunner</c>
/// (Task 3) is the sole caller and owns everything about actually running a fixture.
/// </summary>
public static class FixtureGraph
{
    /// <summary>
    /// Returns <paramref name="fixtures"/> in an order where every dependency precedes its
    /// dependent. Independent fixtures keep their relative position from <paramref name="fixtures"/>
    /// (a depth-first post-order visit that walks the input in order and only ever appends,
    /// never reorders, gives this for free) — a suite whose seeding order drifts between runs is
    /// a suite whose failures cannot be reproduced.
    /// <para>
    /// Throws <see cref="FixtureLifecycleException"/> naming every type in a cycle; naming both
    /// the dependent and the missing type when a <c>DependsOn</c> entry points at a type nobody
    /// registered; naming a type registered more than once (<c>AddSingleton</c>, unlike
    /// <c>TryAddEnumerable</c>, does not dedupe a copy-pasted registration line, and collapsing
    /// that to a single run silently would hide exactly the non-determinism v1-b decision 3 exists to
    /// eliminate); or naming a fixture whose <c>DependsOn</c> is null. Every failure names the
    /// fixture(s) responsible rather than leaving the reader to re-derive the graph by hand.
    /// </para>
    /// </summary>
    public static IReadOnlyList<IAssemblyFixture> Order(IReadOnlyList<IAssemblyFixture> fixtures)
    {
        ArgumentNullException.ThrowIfNull(fixtures);

        var byType = new Dictionary<Type, IAssemblyFixture>();
        foreach (var fixture in fixtures)
        {
            byType[fixture.GetType()] = fixture;
        }

        if (byType.Count != fixtures.Count)
        {
            var duplicate = fixtures
                .GroupBy(f => f.GetType())
                .First(g => g.Count() > 1)
                .Key;

            throw new FixtureLifecycleException(
                $"Fixture type '{TypeName(duplicate)}' is registered more than once. Remove the " +
                $"duplicate services.AddSingleton<IAssemblyFixture, {duplicate.Name}>() " +
                "registration in TestStartup.cs.");
        }

        var ordered = new List<IAssemblyFixture>(fixtures.Count);
        var visited = new HashSet<Type>();
        var visiting = new List<Type>();

        foreach (var fixture in fixtures)
        {
            Visit(fixture, byType, visited, visiting, ordered);
        }

        return ordered;
    }

    private static void Visit(
        IAssemblyFixture fixture,
        Dictionary<Type, IAssemblyFixture> byType,
        HashSet<Type> visited,
        List<Type> visiting,
        List<IAssemblyFixture> ordered)
    {
        var type = fixture.GetType();
        if (visited.Contains(type))
        {
            return;
        }

        // visiting is the path from the outermost Visit call down to here, so it doubles as the
        // cycle-slice point: finding `type` already on it means the cycle is the tail of visiting
        // from that index onward, not the whole path — the path may include types entered before
        // the cycle that are not themselves part of it.
        var cycleStart = visiting.IndexOf(type);
        if (cycleStart >= 0)
        {
            var cycle = visiting.Skip(cycleStart).Append(type).Select(TypeName);
            throw new FixtureLifecycleException(
                $"Fixture dependency cycle detected: {string.Join(" -> ", cycle)}. " +
                "Remove one DependsOn edge to break the cycle.");
        }

        visiting.Add(type);

        var dependsOn = fixture.DependsOn ?? throw new FixtureLifecycleException(
            $"Fixture '{TypeName(type)}' has a null DependsOn array. Initialize it to an empty " +
            "array when the fixture has no dependencies.");

        foreach (var dependency in dependsOn)
        {
            if (!byType.TryGetValue(dependency, out var dependencyFixture))
            {
                throw new FixtureLifecycleException(
                    $"Fixture '{TypeName(type)}' depends on '{TypeName(dependency)}', which is " +
                    "not registered. Register it in TestStartup.cs with " +
                    $"services.AddSingleton<IAssemblyFixture, {dependency.Name}>(), or remove " +
                    $"'{dependency.Name}' from '{TypeName(type)}'.DependsOn.");
            }

            Visit(dependencyFixture, byType, visited, visiting, ordered);
        }

        visiting.RemoveAt(visiting.Count - 1);
        visited.Add(type);
        ordered.Add(fixture);
    }

    /// <summary>
    /// <see cref="Type.FullName"/>, falling back to <see cref="Type.Name"/> for the rare type
    /// that has none (e.g. a generic parameter). A bare <see cref="Type.Name"/> in a cycle or
    /// missing-dependency message would render two same-named fixtures in different namespaces
    /// as indistinguishable — a cycle message that looks like a self-cycle on one type, or a
    /// missing-dependency message that sends the reader hunting when they do have a fixture by
    /// that name, just not the one meant.
    /// </summary>
    private static string TypeName(Type type) => type.FullName ?? type.Name;
}

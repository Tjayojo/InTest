namespace InTest.Runtime;

/// <summary>
/// A team-written seed for one assembly run — registered with
/// <c>services.AddSingleton&lt;IAssemblyFixture, ...&gt;()</c> in <c>TestStartup.cs</c>, never
/// discovered by reflection (v1-b decision 2), so ordering and enablement stay explicit rather than a
/// side effect of which classes happen to exist. <see cref="FixtureRunner"/> (Task 3) topologically
/// orders every registered fixture over <see cref="DependsOn"/>, then calls
/// <see cref="InitializeAsync"/> on each in turn. There is deliberately no matching cleanup
/// method here — §13 registers teardown next to whatever created the thing, via
/// <see cref="FixtureContext.OnCleanup"/>, rather than through a second lifecycle method a team
/// would have to remember to keep in sync with the first.
/// </summary>
public interface IAssemblyFixture
{
    /// <summary>
    /// Other fixture types that must finish <see cref="InitializeAsync"/> before this one starts.
    /// A cycle, or a dependency on a type nobody registered, fails <c>AssemblyInitialize</c> by
    /// name (v1-b decision 3) rather than running in whatever order reflection happened to produce.
    /// </summary>
    Type[] DependsOn { get; }

    /// <summary>
    /// The profiles this fixture should run for, or empty (the default) to run for every
    /// profile. <see cref="FixtureRunner.RunAsync"/> compares this against the profile it is
    /// given and skips — logging why — a fixture whose non-empty <see cref="AppliesTo"/> does not
    /// contain the current one, so a fixture meant only for, say, a QA seed does not run (and
    /// does not silently do nothing without a trace) against local or production.
    /// </summary>
    string[] AppliesTo { get; }

    /// <summary>
    /// Seeds data and publishes whatever <c>{{fixture:...}}</c> tokens need, via
    /// <paramref name="ctx"/>. Runs after readiness (v1-b decision 1), so an HTTP client to the
    /// service under test is available; runs before <c>TokenResolver</c> is built, so
    /// publishing here is what makes <c>{{fixture:...}}</c> resolvable at all.
    /// </summary>
    Task InitializeAsync(FixtureContext ctx, CancellationToken ct);
}

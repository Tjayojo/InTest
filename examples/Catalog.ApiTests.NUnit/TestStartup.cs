using InTest.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Catalog.ApiTests.NUnit;

/// <summary>
/// Assembly-scope setup. NUnit's [SetUpFixture] is itself the lifecycle hook: NUnit
/// constructs one instance of the class it decorates and runs [OneTimeSetUp] before
/// any test in its scope runs, then [OneTimeTearDown] once every test in scope has
/// finished — the counterpart of MSTest's [AssemblyInitialize]/[AssemblyCleanup] and
/// xUnit's IAsyncLifetime-backed assembly fixture. A [SetUpFixture] declared with no
/// enclosing namespace (as here) scopes to the whole assembly, verified working
/// including teardown after a failing test and with an ignored test present.
/// </summary>
[SetUpFixture]
public class TestStartup
{
    [OneTimeSetUp]
    public async Task AssemblyInit()
    {
        TestHost.ConfigureServices = Register;
        await TestHost.InitializeAsync();
    }

    /// <summary>Drains any fixture teardown registered during AssemblyInit — runs even
    /// when AssemblyInit itself failed, and never fails the run: see
    /// TestHost.CleanupAsync for why a drain failure is written to the test log instead
    /// of thrown.</summary>
    [OneTimeTearDown]
    public async Task AssemblyCleanup()
    {
        await TestHost.CleanupAsync();
    }

    /// <summary>Team-owned registrations. Add configuration providers here. AuthHandler
    /// is already attached to InTestClients.Api; a secured API needs only an
    /// ITestTokenProvider registered below — do not also append a DelegatingHandler of
    /// your own, or two handlers will set Authorization and the last one registered
    /// silently wins. See "Auth" in Phase 3 of getting-started.md for a worked
    /// example.</summary>
    private static void Register(IServiceCollection services, IConfiguration configuration)
    {
        // StaticTokenProvider ships as the one-identity, one-token implementation; write
        // your own (like YourTokenProvider below) for more than one identity, which the
        // wrong-scope 403 cases need — and declare each identity's Scopes, or a read-only
        // identity's own read operations can never produce a provable 403. Catalog and
        // Inventory declare no `security` and register nothing at all — they cannot,
        // since StaticTokenProvider needs a real token neither has a source for — so this
        // stays commented for the same reason the IAssemblyFixture example below does: a
        // live registration here would reference a type that does not exist yet, breaking
        // every fresh scaffold's build before a team has written one. See "Auth" in Phase
        // 3 of getting-started.md for a worked example.
        // services.AddSingleton<ITestTokenProvider, YourTokenProvider>();

        // Per-request fixtures: path and query parameter values live in fixtures/, not
        // here — each operation that needs one has a fixture file with a "TODO:"
        // sentinel for every value it requires. Fill those in by hand, or run
        // `intest fixtures repair` after a spec change to add sentinels for anything
        // newly required.

        // A different kind of fixture: assembly fixtures seed data once before any test
        // runs, registered here rather than under fixtures/. Order is resolved
        // automatically from DependsOn; profile-restrict with AppliesTo. See "fixtures"
        // in Phase 5 of getting-started.md for a worked example.
        // services.AddSingleton<IAssemblyFixture, YourFixture>();
    }
}

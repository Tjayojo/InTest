using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace InTest.Runtime;

/// <summary>
/// The NUnit counterpart of <c>InTest.Runtime.MSTest</c>'s and <c>InTest.Runtime.xUnit</c>'s
/// <c>TestHost</c>: a facade over <see cref="InTestRun"/>, the assembly-scope composition root.
/// <para>
/// Same name, same namespace, same passthroughs as the other two adapters — an adopter migrating
/// between frameworks changes a <c>PackageReference</c> id and their <c>ConfigureServices</c>
/// registration keeps compiling untouched.
/// </para>
/// <para>
/// Like the xUnit adapter and unlike the MSTest one, <c>InitializeAsync</c> takes no context
/// parameter: NUnit's <c>[SetUpFixture]</c> is itself the lifecycle hook and
/// <c>TestContext.CurrentContext</c> is ambient. The profile argument is a literal
/// <see langword="null"/> — NUnit has no run-settings equivalent, so <c>INTEST_PROFILE</c> and the
/// config default are what remain of <c>InTestRun.ResolveProfile</c>'s precedence chain.
/// </para>
/// </summary>
public static class TestHost
{
    public static IConfiguration Configuration => InTestRun.Configuration;

    public static IServiceProvider Root => InTestRun.Root;

    public static SchemaBundle Schemas => InTestRun.Schemas;

    public static string RunIdValue => InTestRun.RunIdValue;

    public static string Profile => InTestRun.Profile;

    public static FixtureStore Fixtures => InTestRun.Fixtures;

    public static FixtureValidation.Report FixtureValidationReport => InTestRun.FixtureValidationReport;

    public static TokenResolver FixtureTokens => InTestRun.FixtureTokens;

    public static Action<IServiceCollection, IConfiguration>? ConfigureServices
    {
        get => InTestRun.ConfigureServices;
        set => InTestRun.ConfigureServices = value;
    }

    public static Task InitializeAsync(CancellationToken cancellationToken = default) =>
        // profileFromRunSettings, not profile — that is the neutral method's actual parameter
        // name; the wrong name is CS1739. Mirrors the xUnit adapter's own comment on this call.
        InTestRun.InitializeAsync(profileFromRunSettings: null, new NUnitDiagnostics(), cancellationToken);

    public static Task CleanupAsync() => InTestRun.CleanupAsync(new NUnitDiagnostics());

    /// <summary>
    /// [error-is-the-sink]: <see cref="IRunDiagnostics.Warn"/> must reach the operator even when the
    /// run passes and exits 0, and under NUnit exactly one sink does that.
    /// <para>
    /// <b>This is the one place where copying the xUnit adapter is actively wrong.</b> Measured
    /// against NUnit 4.6.1 on a default, passing, flagless run: <c>Console.WriteLine</c> — which is
    /// the xUnit adapter's answer — is <b>silent at assembly scope at every verbosity, and throws
    /// nothing</b>. So is <c>TestContext.WriteLine</c> and <c>TestContext.Out</c>.
    /// <c>TestContext.Progress</c> appears only at raised verbosity, the same flag-gated failure
    /// xUnit's <c>SendDiagnosticMessage</c> has. Only <c>TestContext.Error</c> reaches captured
    /// process output unconditionally, at both test scope and <c>[SetUpFixture]</c> assembly scope.
    /// </para>
    /// <para>
    /// Both <c>Note</c> and <c>Warn</c> therefore use it. A future editor "tidying" <c>Note</c> to
    /// <c>TestContext.Out</c> would silently lose it at the scope <c>InTestRun.InitializeAsync</c>
    /// uses — which is why this comment is this long.
    /// </para>
    /// </summary>
    internal sealed class NUnitDiagnostics : IRunDiagnostics
    {
        public void Note(string message) => TestContext.Error.WriteLine(message);

        public void Warn(string message) => TestContext.Error.WriteLine(message);
    }
}

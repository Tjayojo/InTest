using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InTest.Runtime;

/// <summary>
/// [adapter-mirrors-mstest]: the xUnit counterpart of <c>InTest.Runtime.MSTest</c>'s <c>TestHost</c>: a facade over
/// <see cref="InTestRun"/>, the assembly-scope composition root.
/// <para>
/// <b>Same name, same namespace, same passthroughs — deliberately.</b> An adopter migrating between
/// frameworks changes a <c>PackageReference</c> id; their <c>TestHost.ConfigureServices</c>
/// registration keeps compiling untouched.
/// </para>
/// <para>
/// <b>What does not mirror: the <c>TestContext</c> parameter.</b> MSTest's
/// <c>InitializeAsync(TestContext)</c> exists because MSTest hands the assembly hook a context and
/// because the run-settings profile is read from it. xUnit has neither — the assembly fixture object
/// is itself the lifecycle hook, and <c>TestContext.Current</c> is ambient. So the parameter is
/// dropped rather than faked, and the profile argument is a literal <see langword="null"/>: see
/// <c>[profile-loses-its-first-source]</c> for what that costs an xUnit adopter and what replaces it
/// (<c>INTEST_PROFILE</c>).
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

    /// <summary>
    /// Called from the adopter's assembly fixture. The profile is <see langword="null"/> because
    /// xUnit has no run-settings equivalent — <c>INTEST_PROFILE</c> and the config default are what
    /// remain in <c>InTestRun.ResolveProfile</c>'s precedence chain.
    /// </summary>
    public static Task InitializeAsync(CancellationToken cancellationToken = default) =>
        // profileFromRunSettings, not profile — that is the neutral method's actual parameter name
        // (InTestRun.cs:114). The named form is kept because it documents
        // [profile-loses-its-first-source]; the wrong name is CS1739.
        InTestRun.InitializeAsync(profileFromRunSettings: null, new XunitDiagnostics(), cancellationToken);

    public static Task CleanupAsync() => InTestRun.CleanupAsync(new XunitDiagnostics());

    /// <summary>
    /// [warn-needs-a-real-sink]: <see cref="IRunDiagnostics.Warn"/> must reach the operator even
    /// when the run passes and exits 0, and under xUnit v3 only one sink does that unconditionally.
    /// <para>
    /// Measured against xunit.v3 4.0.0. <c>TestContext.SendDiagnosticMessage</c> prints nothing
    /// without <c>-diagnostics</c> on the command line. <c>TestContext.Current.TestOutputHelper</c>
    /// is <see langword="null"/> outside a running test — which is exactly the assembly scope
    /// <c>InTestRun.InitializeAsync</c> and the fixture report use it from.
    /// <c>AddWarning</c> at assembly scope is refused, and refused <em>silently</em>: it returns
    /// without throwing and only logs "Attempted to log a test warning message while not running a
    /// test" under <c>-diagnostics</c>. <c>Console.WriteLine</c> reaches process output on a passing
    /// default run at both assembly-init and assembly-dispose scope, which is what
    /// <c>GeneratedSuiteExecutionTests.ValidationReportWithAProblemSurfacesOnAPassingRun</c> asserts
    /// on.
    /// </para>
    /// <para>
    /// So <see cref="Warn"/> always writes to the console, and additionally calls
    /// <c>AddWarning</c> when a test is running so the message also surfaces in the runner's own
    /// reporting. The console write is the one that satisfies the contract; the second is a bonus
    /// and must never be the only sink.
    /// </para>
    /// </summary>
    internal sealed class XunitDiagnostics : IRunDiagnostics
    {
        public void Note(string message)
        {
            var helper = TestContext.Current.TestOutputHelper;
            if (helper is null)
            {
                Console.WriteLine(message);
                return;
            }

            helper.WriteLine(message);
        }

        public void Warn(string message)
        {
            Console.WriteLine(message);

            if (TestContext.Current.Test is not null)
            {
                TestContext.Current.AddWarning(message);
            }
        }
    }
}

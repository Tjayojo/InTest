using InTest.Runtime;
using Xunit;

namespace InTest.Runtime.XUnit.Tests;

/// <summary>
/// [warn-needs-a-real-sink]: Warn must reach the operator on a passing run. The sinks that do not
/// are all silent rather than throwing, so a test that merely calls Warn and does not assert on
/// output would pass against every wrong implementation.
/// </summary>
public class XunitDiagnosticsTests
{
    [Fact]
    public void WarnWritesToTheConsoleEvenWhenNoTestIsRunning()
    {
        var original = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            IRunDiagnostics diagnostics = new TestHost.XunitDiagnostics();
            diagnostics.Warn("WARN_MARKER");
        }
        finally
        {
            Console.SetOut(original);
        }

        // Shouldly is not referenced by this project (see the .csproj) -- Assert.Contains is
        // xunit's own assertion for the one call this test needs, rather than adding a
        // dependency for a single ShouldContain.
        Assert.Contains("WARN_MARKER", captured.ToString());
    }
}

namespace InTest.Runtime;

/// <summary>
/// Raised by <see cref="FixtureValidation.Report.ThrowIfBlocked"/> for an operation whose fixture
/// has at least one unresolved sentinel or token. Names the fixture file and the property path so
/// a reader can go straight to the fix without re-deriving which file is at fault.
/// </summary>
public sealed class FixtureUnresolvedException(string message) : Exception(message);
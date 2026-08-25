namespace InTest.Runtime;

/// <summary>
/// Raised by <see cref="FixtureStore.Get"/> for an operation with no fixture on disk. The
/// message names the repair command rather than just the missing key, because the fix is
/// always the same command and a reader should not have to know that separately.
/// </summary>
public sealed class FixtureNotFoundException(string message) : Exception(message);
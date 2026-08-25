namespace InTest.Runtime;

/// <summary>
/// Raised when a response fails its contract. A dedicated type rather than a framework
/// assertion exception, so the neutral layer names no test framework (§3).
/// </summary>
public sealed class ContractAssertionException(string message) : Exception(message);

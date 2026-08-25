namespace InTest.Runtime;

/// <summary>
/// Raised when a fixture file cannot be parsed. Fixtures are committed and hand-edited, so a
/// malformed field — an unquoted number, a nested object where a string belongs — is a
/// realistic typo, not adversarial input, and every failure names the offending field with its
/// inner exception preserved, the same idiom <c>InTest.Cli.Fixtures.FixtureDocument</c> uses on
/// the writer side. Letting a framework exception like <c>JsonException</c> escape instead would
/// turn one malformed fixture into an unhandled crash for the whole suite at
/// <c>AssemblyInitialize</c>.
/// </summary>
public sealed class FixtureFormatException(string message, Exception? inner = null) : Exception(message, inner);
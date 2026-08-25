namespace InTest.Runtime;

public sealed class ReadinessTimeoutException(string message) : Exception(message);
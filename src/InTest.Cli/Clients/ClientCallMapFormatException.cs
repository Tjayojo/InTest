namespace InTest.Cli.Clients;

public sealed class ClientCallMapFormatException(string message, Exception? inner = null) : Exception(message, inner);

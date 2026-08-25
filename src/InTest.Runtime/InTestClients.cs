namespace InTest.Runtime;

public static class InTestClients
{
    public const string Api = "InTest.Api";

    /// <summary>
    /// A separate named client for <see cref="Readiness.WaitAsync"/>, registered with no
    /// handlers beyond <c>RunIdHandler</c> (F10). <see cref="Api"/> is the one adopters attach
    /// auth handlers to via <c>ConfigureServices</c>; if the readiness probe shared that client,
    /// an unreachable identity provider made every anonymous <c>/health/ready</c> request throw
    /// too, and a dead identity server was reported as a dead API after a 120-second timeout.
    /// </summary>
    public const string Readiness = "InTest.Readiness";
}

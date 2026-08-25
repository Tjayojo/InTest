namespace InTest.Runtime;

public sealed class ReadinessOptions
{
    public bool Enabled { get; set; } = true;
    public string Path { get; set; } = "/health/ready";
    public int ExpectStatus { get; set; } = 200;
    /// <summary>During a slot swap or rolling deploy a single success can come from the
    /// instance being replaced, so more than one is required by default.</summary>
    public int ConsecutiveSuccesses { get; set; } = 2;
    public int TimeoutSeconds { get; set; } = 120;
    public int IntervalSeconds { get; set; } = 3;
}
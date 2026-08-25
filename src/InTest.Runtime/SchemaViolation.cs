namespace InTest.Runtime;

/// <summary>A single schema violation, flattened for message construction.</summary>
public sealed record SchemaViolation(string Kind, string Path);
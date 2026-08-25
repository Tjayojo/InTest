namespace InTest.Runtime;

/// <summary>Environment facts RunId derives from. Injected so the derivation is testable.</summary>
public sealed record RunIdEnvironment(IReadOnlyDictionary<string, string> Variables, string UserName)
{
    public static RunIdEnvironment Current()
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "TF_BUILD", "BUILD_BUILDID", "GITHUB_ACTIONS", "GITHUB_RUN_ID", "CI" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
            {
                vars[name] = value;
            }
        }
        return new RunIdEnvironment(vars, Environment.UserName);
    }

    public string? Get(string key) => Variables.TryGetValue(key, out var v) ? v : null;
    public bool Has(string key) => !string.IsNullOrEmpty(Get(key));
}
using System.Text.Json.Nodes;

namespace InTest.Runtime;

/// <summary>
/// Scans every fixture <see cref="FixtureStore"/> has loaded for <c>TODO:</c> sentinels
/// (decision 3) and tokens <see cref="TokenResolver"/> cannot resolve, and aggregates every
/// problem across every fixture into one report — decision 2. Only operations with an actual
/// problem end up in the blocked set: an operation with no fixture at all is never in
/// <see cref="FixtureStore.Keys"/> to begin with, and a fixture where everything resolves adds
/// nothing to it either, so <see cref="Report.ThrowIfBlocked"/> is a no-op for both.
/// <para>
/// <see cref="ApiTestCore.RequireFixture"/> is the only way a generated test reaches this, and it
/// consults the <see cref="Report"/> — never <see cref="FixtureStore.Get"/> directly — precisely
/// so "no fixture" (the majority case) can never be confused with "unresolved fixture". Get
/// throws for an unknown key by design (Task 5); delegating to it here would fail every
/// parameterless operation that legitimately carries no fixture.
/// </para>
/// </summary>
public static class FixtureValidation
{
    /// <summary>
    /// Walks every fixture the store loaded, checking parameters and body alike. Uses
    /// <see cref="FixtureStore.Get"/> (raw, unresolved) rather than a resolving accessor — Task 6
    /// reserves the resolved form for generated tests sending real requests, not for inspecting
    /// whether a token *would* resolve.
    /// </summary>
    public static Report Build(FixtureStore store, TokenResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(resolver);

        var problemsByOperation = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var key in store.Keys)
        {
            var fixture = store.Get(key);
            var fileName = key + ".json";
            var problems = new List<string>();

            foreach (var (name, value) in fixture.Parameters)
            {
                CheckLeaf(value, name, fileName, resolver, problems);
            }

            if (fixture.Body is not null)
            {
                WalkBody(fixture.Body, path: null, fileName, resolver, problems);
            }

            if (problems.Count > 0)
            {
                problemsByOperation[key] = problems;
            }
        }

        return new Report(problemsByOperation);
    }

    private static void WalkBody(JsonNode node, string? path, string fileName, TokenResolver resolver, List<string> problems)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj)
                {
                    if (child is null)
                    {
                        continue;
                    }
                    WalkBody(child, path is null ? name : $"{path}.{name}", fileName, resolver, problems);
                }
                break;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var element = array[i];
                    if (element is null)
                    {
                        continue;
                    }
                    WalkBody(element, $"{path}[{i}]", fileName, resolver, problems);
                }
                break;

            case JsonValue value when value.TryGetValue<string>(out var text):
                CheckLeaf(text, path ?? "(root)", fileName, resolver, problems);
                break;
        }
    }

    /// <summary>
    /// A <c>TODO:</c> sentinel is always a problem regardless of type (decision 3). Anything else
    /// is only a problem if resolving it actually fails — a plain string, or a token that resolves
    /// cleanly (<c>{{runId}}</c>, a configured <c>{{config:}}</c>), is not.
    /// </summary>
    private static void CheckLeaf(string value, string path, string fileName, TokenResolver resolver, List<string> problems)
    {
        if (value.StartsWith("TODO:", StringComparison.Ordinal))
        {
            problems.Add($"'{path}' in {fileName} is still unfilled ({value}).");
            return;
        }

        try
        {
            resolver.Resolve(value, fileName);
        }
        catch (FixtureResolutionException ex)
        {
            problems.Add($"'{path}' in {fileName}: {ex.Message}");
        }
    }

    /// <summary>
    /// One validation pass over every loaded fixture. <see cref="Message"/> is reported to
    /// <c>TestContext</c> exactly once by <c>TestHost</c> so every problem is visible even though
    /// only the affected operations actually fail (decision 2) — see <c>TestHost</c>'s own
    /// <c>InitializeAsync</c> for which <c>TestContext</c> method actually makes that true on a
    /// passing run, which turned out not to be the obvious one.
    /// </summary>
    public sealed class Report
    {
        private readonly IReadOnlyDictionary<string, List<string>> _problemsByOperation;

        internal Report(IReadOnlyDictionary<string, List<string>> problemsByOperation)
        {
            _problemsByOperation = problemsByOperation;
            Message = BuildMessage(problemsByOperation);
        }

        /// <summary>The full aggregated report, every problem across every fixture in one message.</summary>
        public string Message { get; }

        /// <summary>
        /// Whether any operation's fixture has at least one unresolved value. <c>TestHost</c>
        /// uses this to pick <see cref="Message"/>'s <c>TestContext.DisplayMessage</c> severity —
        /// there is something worth surfacing prominently only when this is true.
        /// </summary>
        public bool HasProblems => _problemsByOperation.Count > 0;

        /// <summary>Whether this operation's fixture has at least one unresolved value.</summary>
        public bool IsBlocked(string operationKey) => _problemsByOperation.ContainsKey(operationKey);

        /// <summary>
        /// No-op for an operation with no fixture, or one that resolved cleanly. Throws
        /// <see cref="FixtureUnresolvedException"/> naming the file and every unresolved property
        /// only for an operation actually in the blocked set.
        /// </summary>
        public void ThrowIfBlocked(string operationKey)
        {
            if (!_problemsByOperation.TryGetValue(operationKey, out var problems))
            {
                return;
            }

            throw new FixtureUnresolvedException(
            $"Fixture for operation '{operationKey}' has unresolved values:\n" +
            string.Join("\n", problems.Select(p => "  - " + p)));
        }

        private static string BuildMessage(IReadOnlyDictionary<string, List<string>> problemsByOperation)
        {
            var total = problemsByOperation.Sum(kv => kv.Value.Count);
            if (total == 0)
            {
                return "All fixtures resolved cleanly.";
            }

            var lines = new List<string>
            {
                $"{total} problem{(total == 1 ? "" : "s")} found across fixtures. " +
                "Run `intest fixtures repair` or fill them in by hand:"
            };

            foreach (var (operationKey, problems) in problemsByOperation)
            {
                lines.Add($"  {operationKey}:");
                lines.AddRange(problems.Select(p => "    - " + p));
            }

            return string.Join("\n", lines);
        }
    }
}

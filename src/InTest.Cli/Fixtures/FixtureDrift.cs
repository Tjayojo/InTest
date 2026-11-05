using System.Text.Json.Nodes;

namespace InTest.Cli.Fixtures;

/// <summary>
/// Compares an existing, possibly hand-edited fixture against a freshly composed one for the
/// same operation. Read-only by design: it reports what changed on either side, and leaves the
/// decision of what to do about it — add, retain, report — to the caller (§10, decision in
/// Task 3's plan section: repair never overwrites a value a human wrote).
/// </summary>
public static class FixtureDrift
{
    public static FixtureDriftResult Compare(FixtureDocument existing, FixtureDocument composed)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(composed);

        var existingBody = PropertyPaths(existing.Body);
        var composedBody = PropertyPaths(composed.Body);

        var missingProperties = Shallowest(composedBody.All, existingBody.All, existingBody.Objects);
        var staleProperties = Shallowest(existingBody.All, composedBody.All, composedBody.Objects);

        var missingParameters = composed.Parameters.Keys
            .Where(name => !existing.Parameters.ContainsKey(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        return new FixtureDriftResult(missingProperties, staleProperties, missingParameters);
    }

    /// <summary>
    /// Every property path in a JSON object body, dotted, at every depth — plus the subset whose
    /// value is itself an object.
    /// <para>
    /// Top-level names were enough until they were not: a property added under a nested object
    /// produced no drift at all, so <c>generate</c> and <c>--check</c> both passed and
    /// <c>fixtures repair</c> reported nothing to do, while the suite sent a body the API answered
    /// 400 to. Measured against the same spec change applied one level up, which was caught.
    /// </para>
    /// <para>
    /// Arrays stay opaque, as a non-object body always has: a composed array carries exactly one
    /// skeleton element while a hand-filled one carries however many the team wrote, so element
    /// paths would report drift on every well-filled fixture.
    /// </para>
    /// </summary>
    private static (HashSet<string> All, HashSet<string> Objects) PropertyPaths(JsonNode? body)
    {
        var all = new HashSet<string>(StringComparer.Ordinal);
        var objects = new HashSet<string>(StringComparer.Ordinal);
        Collect(body, prefix: null, all, objects);
        return (all, objects);
    }

    private static void Collect(JsonNode? node, string? prefix, HashSet<string> all, HashSet<string> objects)
    {
        if (node is not JsonObject obj)
        {
            return;
        }

        foreach (var (name, child) in obj)
        {
            var path = prefix is null ? name : $"{prefix}.{name}";
            all.Add(path);
            if (child is JsonObject)
            {
                objects.Add(path);
            }

            Collect(child, path, all, objects);
        }
    }

    /// <summary>
    /// The paths in <paramref name="source"/> absent from <paramref name="other"/>, keeping only
    /// the shallowest of each run: a whole subtree that is missing or stale reports as one path,
    /// not once per descendant. A path whose parent exists on the other side but is not an object
    /// there is dropped — there is nothing to write into, and a human who put a scalar where a
    /// subtree belongs did so deliberately.
    /// </summary>
    private static List<string> Shallowest(
        HashSet<string> source, HashSet<string> other, HashSet<string> otherObjects) =>
        source
            .Where(path => !other.Contains(path))
            .Where(path =>
            {
                var cut = path.LastIndexOf('.');
                return cut < 0 || otherObjects.Contains(path[..cut]);
            })
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
}

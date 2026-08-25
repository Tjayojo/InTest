using System.Text.Json.Nodes;

namespace InTest.Runtime;

/// <summary>
/// Loads every fixture under <c>{root}/fixtures/*.json</c> and, when <paramref name="profile"/>
/// is given, deep-merges any <c>{root}/fixtures/{profile}/*.json</c> overlay over it — the
/// environment wins, property by property, not object by object. <c>root</c> is the directory
/// that <em>contains</em> <c>fixtures/</c>, not <c>fixtures/</c> itself; <c>TestHost</c> passes
/// <c>AppContext.BaseDirectory</c>.
/// <para>
/// An absent <c>fixtures/</c> directory loads to an empty store rather than throwing: a spec
/// whose every operation is a parameterless GET needs no fixtures at all, and
/// <c>GeneratedSuiteExecutionTests</c> depends on that shape continuing to work.
/// </para>
/// </summary>
public sealed class FixtureStore
{
    private readonly Dictionary<string, Fixture> _fixtures;

    private FixtureStore(Dictionary<string, Fixture> fixtures) => _fixtures = fixtures;

    /// <summary>Number of operations with a loaded fixture, base and overlay combined.</summary>
    public int Count => _fixtures.Count;

    /// <summary>
    /// Operation keys with a loaded fixture, base and overlay combined. Startup validation
    /// (Task 7) walks exactly this set — an operation absent from it needs no fixture and is
    /// never blocked, without either side having to duplicate the directory scan above.
    /// </summary>
    public IReadOnlyCollection<string> Keys => _fixtures.Keys;

    public static FixtureStore Load(string root, string? profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var fixturesDir = Path.Combine(root, "fixtures");
        var fixtures = new Dictionary<string, Fixture>(StringComparer.Ordinal);

        if (!Directory.Exists(fixturesDir))
        {
            return new FixtureStore(fixtures);
        }

        foreach (var file in Directory.GetFiles(fixturesDir, "*.json", SearchOption.TopDirectoryOnly))
        {
            fixtures[KeyOf(file)] = ParseFile(file);
        }

        if (!string.IsNullOrEmpty(profile))
        {
            var overlayDir = Path.Combine(fixturesDir, profile);
            if (Directory.Exists(overlayDir))
            {
                foreach (var file in Directory.GetFiles(overlayDir, "*.json", SearchOption.TopDirectoryOnly))
                {
                    var key = KeyOf(file);
                    var fileName = Path.GetFileName(file);

                    if (!fixtures.TryGetValue(key, out var baseFixture))
                    {
                        throw new FixtureFormatException(
                        $"fixtures/{profile}/{fileName} overlays an operation with no base " +
                        $"fixture 'fixtures/{fileName}'. Run `intest fixtures repair` first, " +
                        "or remove the overlay.");
                    }

                    fixtures[key] = Merge(baseFixture, ParseFile(file));
                }
            }
        }

        return new FixtureStore(fixtures);
    }

    /// <summary>
    /// The raw fixture, tokens unresolved. Startup validation (Task 7) inspects tokens, so it
    /// must call this rather than a resolving accessor.
    /// </summary>
    public Fixture Get(string key)
    {
        if (_fixtures.TryGetValue(key, out var fixture))
        {
            return fixture;
        }
        throw new FixtureNotFoundException(
            $"No fixture is defined for operation '{key}'. Run `intest fixtures repair` to generate one.");
    }

    /// <summary>
    /// The fixture's body with every <c>{{...}}</c> token resolved — a fresh <see cref="JsonNode"/>
    /// on every call, per Task 6's pinned interface, so <c>{{utcNow}}</c> differs between requests
    /// rather than being resolved once and reused. Null when the operation's fixture carries no
    /// body at all, which is the normal shape for an operation with no request body.
    /// </summary>
    public JsonNode? ResolvedBody(string key, TokenResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var body = Get(key).Body;
        return body is null ? null : ResolveNode(body, key + ".json", resolver);
    }

    /// <summary>
    /// A single resolved parameter value. Every caller of this overload is a path parameter —
    /// decision 1 makes those unconditionally required — so a name absent from
    /// <c>$parameters</c> is a bug to surface loudly rather than send a literal unsubstituted
    /// path segment.
    /// </summary>
    public string ResolvedParameter(string key, string name, TokenResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var fixture = Get(key);
        if (!fixture.Parameters.TryGetValue(name, out var raw))
        {
            throw new FixtureNotFoundException(
            $"Fixture 'fixtures/{key}.json' has no '$parameters.{name}'. " +
            "Run `intest fixtures repair` to generate one.");
        }

        return resolver.Resolve(raw, key + ".json");
    }

    /// <summary>
    /// Resolved values for whichever of <paramref name="names"/> the fixture actually supplies.
    /// A name absent from <c>$parameters</c> — an optional query parameter the spec gave no
    /// example or default, decision 1's fourth row — is silently omitted rather than treated as
    /// an error, and an operation with no fixture at all yields an empty result the same way: a
    /// query-only operation whose parameters are all optional-with-no-value never needs a fixture
    /// file to exist at all.
    /// </summary>
    public IReadOnlyDictionary<string, string> ResolvedQueryParameters(
        string key, IReadOnlyCollection<string> names, TokenResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(resolver);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (names.Count == 0 || !_fixtures.TryGetValue(key, out var fixture))
        {
            return result;
        }

        var fileName = key + ".json";
        foreach (var name in names)
        {
            if (fixture.Parameters.TryGetValue(name, out var raw))
            {
                result[name] = resolver.Resolve(raw, fileName);
            }
        }

        return result;
    }

    private static JsonNode ResolveNode(JsonNode node, string fileName, TokenResolver resolver)
    {
        switch (node)
        {
            case JsonObject obj:
                var newObj = new JsonObject();
                foreach (var (name, child) in obj)
                {
                    newObj[name] = child is null ? null : ResolveNode(child, fileName, resolver);
                }
                return newObj;

            case JsonArray array:
                var newArray = new JsonArray();
                foreach (var element in array)
                {
                    newArray.Add(element is null ? null : ResolveNode(element, fileName, resolver));
                }
                return newArray;

            case JsonValue value when value.TryGetValue<string>(out var text):
                return JsonValue.Create(resolver.Resolve(text, fileName));

            default:
                return node.DeepClone();
        }
    }

    private static string KeyOf(string path) => Path.GetFileNameWithoutExtension(path);

    private static Fixture ParseFile(string path)
    {
        try
        {
            return Fixture.Parse(File.ReadAllText(path));
        }
        catch (FixtureFormatException ex)
        {
            // Fixture.Parse knows the offending field but not the file it came from — only the
            // caller iterating the directory knows that, so the filename is added here.
            throw new FixtureFormatException($"{Path.GetFileName(path)}: {ex.Message}", ex);
        }
    }

    private static Fixture Merge(Fixture baseFixture, Fixture overlay)
    {
        var parameters = new SortedDictionary<string, string>(baseFixture.Parameters, StringComparer.Ordinal);
        foreach (var (key, value) in overlay.Parameters)
        {
            parameters[key] = value;
        }

        return new Fixture
        {
            Parameters = parameters,
            Body = MergeBody(baseFixture.Body, overlay.Body)
        };
    }

    /// <summary>
    /// Merges per property rather than replacing the object wholesale: an overlay that overrides
    /// one nested property must leave its siblings — from either side — untouched.
    /// </summary>
    private static JsonNode? MergeBody(JsonNode? baseBody, JsonNode? overlayBody)
    {
        if (overlayBody is null)
        {
            return baseBody?.DeepClone();
        }
        if (baseBody is not JsonObject baseObj || overlayBody is not JsonObject overlayObj)
        {
            return overlayBody.DeepClone();
        }

        var merged = new JsonObject();
        foreach (var (key, value) in baseObj)
        {
            merged[key] = value?.DeepClone();
        }

        foreach (var (key, value) in overlayObj)
        {
            var baseChild = merged.TryGetPropertyValue(key, out var existing) ? existing : null;
            merged[key] = baseChild is JsonObject && value is JsonObject
                ? MergeBody(baseChild, value)
                : value?.DeepClone();
        }

        return merged;
    }
}

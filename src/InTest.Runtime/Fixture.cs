using System.Text.Json;
using System.Text.Json.Nodes;

namespace InTest.Runtime;

/// <summary>
/// The runtime <em>read</em> model for a fixture. Deliberately separate from
/// <c>InTest.Cli.Fixtures.FixtureDocument</c> — decision 5: <c>InTest.Runtime</c> is the library
/// every generated test project takes a <c>PackageReference</c> on, so it never references
/// <c>InTest.Cli</c> and gets its own model over the same JSON shape. It carries only what a test
/// needs at request time — no <c>FileNameFor</c>, no <c>TryValidateOperationKey</c>, no tier
/// composition and no <c>$meta</c>; those are generate-time concerns that stay in the CLI.
/// <c>InTest.Cli.Tests.FixtureContractTests</c> round-trips a written fixture through this type
/// so the two models cannot silently drift apart.
/// </summary>
public sealed class Fixture
{
    public SortedDictionary<string, string> Parameters { get; init; } = new(StringComparer.Ordinal);
    public JsonNode? Body { get; init; }

    public static Fixture Parse(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException ex) { throw new FixtureFormatException($"Fixture is not valid JSON: {ex.Message}", ex); }

        if (root is not JsonObject obj)
        {
            throw new FixtureFormatException("Fixture root must be a JSON object.");
        }

        var fixture = new Fixture { Body = obj["body"]?.DeepClone() };

        // An explicit JSON null reads as absent, same as 'body' above — that is how a
        // hand-editor writes "there are none". Any other non-object shape is malformed and must
        // say so: silently skipping it would load the fixture clean with every parameter
        // missing, and the request would go out malformed with nothing to point at.
        if (obj["$parameters"] is { } parametersNode)
        {
            if (parametersNode is not JsonObject parameters)
            {
                throw new FixtureFormatException(
                $"Fixture '$parameters' must be a JSON object of name/value pairs, but found " +
                $"'{parametersNode.ToJsonString()}'. Regenerate it with `intest fixtures repair`.");
            }

            foreach (var (key, value) in parameters)
            {
                try
                {
                    fixture.Parameters[key] = value?.GetValue<string>() ?? string.Empty;
                }
                catch (Exception ex) when (ex is InvalidOperationException or FormatException)
                {
                    throw new FixtureFormatException(
                        $"Fixture '$parameters.{key}' must be a string, but found '{value}'. " +
                        "Regenerate it with `intest fixtures repair`.", ex);
                }
            }
        }

        return fixture;
    }
}

using System.Text.Json.Nodes;
using InTest.Cli.Planning;
using Microsoft.OpenApi;

namespace InTest.Cli.Schemas;

/// <summary>
/// Produces the runtime schema bundle: every schema the plan can reference, under
/// 'definitions', with component references rewritten. Bundling rather than inlining is
/// what makes self-referential schemas terminate.
/// </summary>
public static class SchemaBundleBuilder
{
    private const string ComponentPrefix = "#/components/schemas/";
    private const string DefinitionPrefix = "#/definitions/";
    private const string JsonMediaType = "application/json";

    public static string Build(OpenApiDocument document, TestPlan plan)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(plan);

        var definitions = new JsonObject();

        // Not '?? []': Components.Schemas is IDictionary<,>, which is not a constructible
        // collection-expression target, so the collection expression cannot be target-typed.
        var componentSchemas = document.Components?.Schemas ?? new Dictionary<string, IOpenApiSchema>();

        foreach (var (name, schema) in componentSchemas.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            definitions[name] = Serialize(schema);
        }

        foreach (var (key, schema) in InlineResponseSchemas(document, plan))
        {
            definitions[key] = Serialize(schema);
        }

        var bundle = new JsonObject { ["definitions"] = definitions };
        // NewLine = "\n" pins the interior line endings to LF (System.Text.Json otherwise uses
        // Environment.NewLine, CRLF on Windows) — same fix, same reasoning, as the other three
        // ToJsonString(WriteIndented: true) sites in this project (CoverageReport,
        // GenerateCommand's spec-paths.json, FixtureDocument). Unlike those three, this call
        // previously appended nothing after ToJsonString, so the file had no trailing newline at
        // all; the appended "\n" below is a deliberate addition, not a preserved behaviour,
        // chosen so every JSON file `generate` writes ends the same way — a single trailing LF —
        // rather than leaving spec-schemas.json as the one file in Generated/ without one.
        return bundle.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, NewLine = "\n" })
                     .Replace(ComponentPrefix, DefinitionPrefix, StringComparison.Ordinal)
                     + "\n";
    }

    private static IEnumerable<(string Key, IOpenApiSchema Schema)> InlineResponseSchemas(OpenApiDocument document, TestPlan plan)
    {
        var wanted = plan.Classes
            .SelectMany(c => c.Cases)
            .Where(c => c.SchemaKey is not null && c.SchemaKey.StartsWith("op:", StringComparison.Ordinal))
            .ToDictionary(c => (c.PathTemplate, c.HttpMethod, c.ExpectedStatus), c => c.SchemaKey!);

        foreach (var (path, pathItem) in document.Paths)
        {
            foreach (var (method, operation) in pathItem.Operations ?? [])
            {
                foreach (var (code, response) in operation.Responses ?? [])
                {
                    if (!int.TryParse(code, out var status))
                    {
                        continue;
                    }
                    if (!wanted.TryGetValue((path, method.Method.ToUpperInvariant(), status), out var key))
                    {
                        continue;
                    }
                    // An 'out var' declared inside a null-conditional access is not definitely
                    // assigned afterwards; test the receiver separately, as TestPlanBuilder does.
                    if (response.Content is null
                        || !response.Content.TryGetValue(JsonMediaType, out var media)
                        || media.Schema is null)
                    {
                        continue;
                    }

                    yield return (key, media.Schema);
                }
            }
        }
    }

    /// <summary>
    /// Serializes as OpenAPI 3.1, which is JSON Schema 2020-12. This is what turns an
    /// OpenAPI 3.0 'nullable: true' into '"type": ["null", …]' — the object model does the
    /// dialect translation, so nothing downstream needs to know which dialect the spec used.
    /// </summary>
    private static JsonNode Serialize(IOpenApiSchema schema)
    {
        using var writer = new StringWriter();
        schema.SerializeAsV31(new OpenApiJsonWriter(writer));
        return JsonNode.Parse(writer.ToString())
               ?? throw new InvalidOperationException("Schema serialized to null JSON.");
    }
}

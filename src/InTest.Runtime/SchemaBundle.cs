using NJsonSchema;

namespace InTest.Runtime;

/// <summary>
/// Response schema validation. Framework-neutral.
/// Schemas are bundled under 'definitions' and referenced by key rather than inlined,
/// because self-referential schemas are common and inlining them does not terminate.
/// </summary>
public sealed class SchemaBundle
{
    private readonly JsonSchema _root;

    private SchemaBundle(JsonSchema root) => _root = root;

    public static SchemaBundle FromJson(string bundleJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleJson);
        return new SchemaBundle(JsonSchema.FromJsonAsync(bundleJson).GetAwaiter().GetResult());
    }

    public static SchemaBundle FromFile(string path) => FromJson(File.ReadAllText(path));

    public IReadOnlyList<SchemaViolation> Validate(string schemaKey, string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaKey);

        if (!_root.Definitions.TryGetValue(schemaKey, out var schema))
        {
            throw new KeyNotFoundException(
            $"Schema '{schemaKey}' is not in the bundle. Available: {string.Join(", ", _root.Definitions.Keys.Order())}");
        }

        try
        {
            return schema.Validate(payload ?? string.Empty)
                         .Select(e => new SchemaViolation(e.Kind.ToString(), e.Path ?? "#"))
                         .ToList();
        }
        catch (Exception ex) when (ex is not KeyNotFoundException)
        {
            return [new SchemaViolation("MalformedJson", "#")];
        }
    }
}

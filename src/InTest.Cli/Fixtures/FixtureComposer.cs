using System.Text.Json.Nodes;
using InTest.Cli.Spec;
using Microsoft.OpenApi;

namespace InTest.Cli.Fixtures;

/// <summary>
/// Implements §10's four-tier precedence for composing a fixture from an operation: a
/// media-type example wins outright over per-property examples, which win over declared
/// defaults, which win over a schema-shaped skeleton of <c>TODO:</c> sentinels. The recorded
/// <see cref="FixtureMeta.Tier"/> is the worst of those sources used anywhere in the document —
/// one unresolved property is enough to mark the whole fixture as needing attention.
/// </summary>
public static class FixtureComposer
{
    private const string JsonMediaType = "application/json";

    /// <summary>
    /// Whether composing this operation would produce a fixture at all — a JSON request body, or
    /// at least one path/query parameter <see cref="ParameterValue"/> actually emits a value for.
    /// The sole authority for that question, so a caller deciding whether a fixture file will
    /// exist (and therefore whether its filename needs to be usable) never drifts from what
    /// <see cref="Compose"/> itself does.
    /// <para>
    /// <paramref name="parameters"/> is the caller's already-merged
    /// <see cref="EffectiveParameters.Resolve"/> result (<c>[effective-parameters]</c>), not
    /// <c><paramref name="operation"/>.Parameters</c> read again here — <see cref="TestPlanBuilder.Build"/>
    /// computes it once per operation, where <c>pathItem</c> is in scope, and this is one of the
    /// several places it gets passed to rather than re-derived. <paramref name="operation"/> is
    /// still needed on its own for <see cref="HasJsonBodyToCompose"/>, which has nothing to do with
    /// the path-item merge.
    /// </para>
    /// </summary>
    public static bool NeedsFixture(IReadOnlyList<IOpenApiParameter> parameters, OpenApiOperation operation)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(operation);

        if (HasJsonBodyToCompose(operation))
        {
            return true;
        }

        return parameters.Any(p =>
            p.In is (ParameterLocation.Path or ParameterLocation.Query) && ParameterValue(p, null) is not null);
    }

    /// <summary>
    /// <c>[effective-parameters]</c>: <paramref name="path"/> is enough to resolve
    /// <c>pathItem</c> the same way this method already resolves <paramref name="operation"/>
    /// itself (<c>document.Paths[path]</c>, one property away from the operation lookup already
    /// on the next line) — so, unlike <see cref="NeedsFixture"/>, this method's own callers
    /// (<c>FixturesRepairCommand</c>, <c>GenerateCommand.DetectFixtureDrift</c>) need no signature
    /// change to supply the merge; <see cref="EffectiveParameters.Resolve"/> is called exactly
    /// once, right here, from the same data those callers already pass in.
    /// </summary>
    public static FixtureDocument Compose(
        OpenApiDocument document, string path, string httpMethod, string operationKey, string generatedBy)
    {
        ArgumentNullException.ThrowIfNull(document);

        var pathItem = document.Paths[path];
        var operation = pathItem.Operations![new HttpMethod(httpMethod)];
        var effectiveParameters = EffectiveParameters.Resolve(pathItem, operation);
        var tier = new TierTracker();

        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in effectiveParameters)
        {
            if (parameter.In is not (ParameterLocation.Path or ParameterLocation.Query))
            {
                continue;
            }

            var value = ParameterValue(parameter, tier);
            if (value is not null)
            {
                parameters[parameter.Name!] = value;
            }
        }

        JsonNode? body = null;
        if (HasJsonBodyToCompose(operation))
        {
            body = ComposeBody(operation.RequestBody!.Content![JsonMediaType], tier);
        }

        return new FixtureDocument
        {
            Meta = new FixtureMeta { Tier = tier.Value, OperationId = operationKey, GeneratedBy = generatedBy },
            Parameters = parameters,
            Body = body
        };
    }

    /// <summary>
    /// A path parameter is always sentinelled, whatever the document claims about its
    /// <c>required</c> flag — see decision 1. A query parameter is sentinelled only when it is
    /// genuinely required; an optional one is surfaced solely when the spec gives it a real
    /// value (an <c>example</c> or a <c>default</c>), and is omitted (returns <see langword="null"/>)
    /// otherwise so it is never sent. <paramref name="tier"/> is <see langword="null"/> when the
    /// caller (<see cref="NeedsFixture"/>) only wants to know whether a value would be emitted and
    /// has no <see cref="TierTracker"/> of its own to record into.
    /// </summary>
    private static string? ParameterValue(IOpenApiParameter parameter, TierTracker? tier)
    {
        var alwaysSentinelled = parameter.In is ParameterLocation.Path
            || (parameter.Required && parameter.In is ParameterLocation.Query);

        if (alwaysSentinelled)
        {
            tier?.Record(4);
            return $"TODO:{parameter.Name}";
        }

        if (FirstExample(parameter.Schema) is { } example)
        {
            tier?.Record(2);
            return ParameterScalarToString(example);
        }

        if (parameter.Schema?.Default is { } defaultValue)
        {
            tier?.Record(3);
            return ParameterScalarToString(defaultValue);
        }

        return null;
    }

    /// <summary>
    /// A <c>requestBody</c> can declare an <c>application/json</c> entry with no <c>schema</c> at
    /// all (valid OpenAPI) — there is nothing to compose a value from, so that counts as no body,
    /// the same as no <c>application/json</c> entry existing in the first place. Shared by
    /// <see cref="Compose"/> and <see cref="NeedsFixture"/> so the two can never disagree on it.
    /// Also <c>internal</c> so <c>TestPlanBuilder</c> can set <c>TestCasePlan.HasRequestBody</c>
    /// from this exact check rather than re-deriving it — the same reasoning as
    /// <see cref="NeedsFixture"/> being the sole authority on whether an operation gets a fixture.
    /// </summary>
    internal static bool HasJsonBodyToCompose(OpenApiOperation operation) =>
        operation.RequestBody?.Content?.TryGetValue(JsonMediaType, out var media) is true && media.Schema is not null;

    private static string ParameterScalarToString(JsonNode node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node.ToJsonString();

    /// <summary>
    /// Microsoft.OpenApi 3.10.0 marks the singular <see cref="IOpenApiSchema.Example"/> obsolete
    /// in favor of the plural <see cref="IOpenApiSchema.Examples"/> — but for an OpenAPI 3.0.x
    /// document's singular <c>example</c> keyword, <c>Examples</c> is left empty; <c>Example</c>
    /// is the one actually populated. Confirmed against the installed package rather than
    /// assumed. <c>Example</c> is read deliberately, so the suppression is scoped to this line.
    /// An OpenAPI 3.1 document's singular <c>example</c> keyword populates <c>Example</c> the
    /// same way — confirmed the same way, by
    /// <c>FixtureComposerTests.AUnionBranchWithAnExampleRecordsTier2NotASentinel</c>, which is a
    /// 3.1 document (3.1 is the dialect F6 exists for). If a future package version routes 3.1's
    /// <c>example</c> into <c>Examples</c> instead, that test goes red here.
    /// </summary>
    private static JsonNode? FirstExample(IOpenApiSchema? schema)
    {
#pragma warning disable CS0618
        return schema?.Example;
#pragma warning restore CS0618
    }

    /// <summary>
    /// Tier 1: the media type's own example is used verbatim, with no per-property composition
    /// at all. Anything else falls to <see cref="ComposeFromSchema"/> for tiers 2 through 4.
    /// </summary>
    private static JsonNode? ComposeBody(IOpenApiMediaType media, TierTracker tier)
    {
        if (media.Example is not null)
        {
            tier.Record(1);
            return media.Example.DeepClone();
        }

        return ComposeFromSchema(media.Schema, "body", tier, []);
    }

    /// <summary>
    /// Recursively composes a value for one schema. <paramref name="propertyName"/> names the
    /// property this schema belongs to, used only if composition bottoms out at a sentinel.
    /// <paramref name="visitedRefs"/> tracks component schema ids currently on the recursion
    /// path; revisiting one (a self- or mutually-referencing schema) emits <see langword="null"/>
    /// and stops instead of recursing forever.
    /// </summary>
    private static JsonNode? ComposeFromSchema(
        IOpenApiSchema? schema, string propertyName, TierTracker tier, HashSet<string> visitedRefs)
    {
        if (schema is null)
        {
            return null;
        }

        if (schema is OpenApiSchemaReference reference)
        {
            var id = reference.Reference?.Id ?? string.Empty;
            if (!visitedRefs.Add(id))
            {
                return null;
            }
            try { return ComposeFromSchema(reference.Target, propertyName, tier, visitedRefs); }
            finally { visitedRefs.Remove(id); }
        }

        if (FirstExample(schema) is { } example)
        {
            tier.Record(2);
            return example.DeepClone();
        }

        if (schema.Default is not null)
        {
            tier.Record(3);
            return schema.Default.DeepClone();
        }

        if (schema.Type?.HasFlag(JsonSchemaType.Object) is true)
        {
            var obj = new JsonObject();
            foreach (var (name, propertySchema) in schema.Properties ?? new Dictionary<string, IOpenApiSchema>())
            {
                obj[name] = ComposeFromSchema(propertySchema, name, tier, visitedRefs);
            }
            return obj;
        }

        if (schema.Type?.HasFlag(JsonSchemaType.Array) is true && schema.Items is not null)
        {
            return new JsonArray(ComposeFromSchema(schema.Items, propertyName, tier, visitedRefs));
        }

        // Deliberately last — after the object and array checks, not before them. A schema can
        // legitimately carry both `type: object` (with its own `properties`) and an `allOf`; if
        // this check ran first it would divert into the union branch and silently drop those
        // declared properties instead of composing them.
        if (SoleUnionBranch(schema) is { } branch)
        {
            return ComposeFromSchema(branch, propertyName, tier, visitedRefs);
        }

        tier.Record(4);
        return JsonValue.Create($"TODO:{propertyName}");
    }

    /// <summary>
    /// Resolves an un-navigated <c>oneOf</c>/<c>anyOf</c>/<c>allOf</c> union to the single branch
    /// worth composing from. OpenAPI 3.1's idiom for a nullable reference to another schema is
    /// <c>oneOf: [{type: null}, {$ref: ...}]</c> — exactly what the built-in
    /// Microsoft.AspNetCore.OpenApi producer emits — and such a schema is not itself a reference,
    /// has no <c>example</c> or <c>default</c>, and no <c>type</c> of its own, so it falls through
    /// every check above this one. A branch that declares the JSON Schema <c>null</c> type
    /// (<c>{"type": "null"}</c>) is discarded — that is not the same thing as a branch with no
    /// declared type at all, such as a bare <c>$ref</c>, which is exactly the branch composition
    /// needs to keep. If discarding null-typed branches leaves exactly one candidate, it is
    /// unambiguously the answer. Zero remaining candidates, or more than one, is a genuine
    /// ambiguity that composing a value would be guessing at, so this method returns
    /// <see langword="null"/> and the caller falls through to the sentinel instead.
    /// </summary>
    private static IOpenApiSchema? SoleUnionBranch(IOpenApiSchema schema)
    {
        var candidates = (schema.OneOf ?? [])
            .Concat(schema.AnyOf ?? [])
            .Concat(schema.AllOf ?? [])
            // Discards only branches that declare the JSON `null` type — not branches with no
            // declared type at all (a bare $ref has none), which must survive the filter.
            .Where(branch => branch.Type != JsonSchemaType.Null)
            .ToList();

        return candidates.Count == 1 ? candidates[0] : null;
    }

    /// <summary>Tracks the worst (highest-numbered) tier used anywhere while composing a fixture.</summary>
    private sealed class TierTracker
    {
        public int Value { get; private set; } = 1;

        public void Record(int candidateTier)
        {
            if (candidateTier > Value)
            {
                Value = candidateTier;
            }
        }
    }
}

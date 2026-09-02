using Microsoft.OpenApi;

namespace InTest.Cli.Spec;

/// <summary>
/// <c>[effective-parameters]</c>: OpenAPI 3.x lets a path parameter be declared once on the
/// <em>path item</em> (sibling to <c>get</c>/<c>put</c>/<c>delete</c>) rather than repeated on
/// every operation beneath it — a common way to declare an <c>id</c> once for
/// <c>GET</c>/<c>PUT</c>/<c>DELETE</c> on <c>/orders/{id}</c>. Before this type existed, every
/// read in this codebase (<c>TestPlanBuilder</c>, <c>FixtureComposer</c>) went straight to
/// <c>operation.Parameters</c>, so such a parameter was invisible — see issue #7. This is the
/// single canonical merge every one of those read sites now goes through, computed once per
/// operation by the caller (<see cref="TestPlanBuilder"/>'s <c>Build</c> loop, where
/// <c>pathItem</c> is already in scope, and <see cref="Fixtures.FixtureComposer.Compose"/>, which
/// derives it from the same <c>document</c>/<c>path</c> it already resolves the operation from) and
/// passed down — not re-derived at each site. CLAUDE.md names re-derivation as this codebase's
/// recurring defect; this type exists so the merge rule is written exactly once.
/// <para>
/// <b>The merge rule</b> (OpenAPI 3.x, "Path Item Object" / "Operation Object"): an operation-level
/// parameter <em>overrides</em> a path-item one only when both <c>name</c> <em>and</em>
/// <c>in</c> match. Matching on <c>name</c> alone is wrong — <c>{id}</c> declared <c>in: path</c>
/// and a separate <c>id</c> declared <c>in: query</c> are different parameters under the OpenAPI
/// data model (different binding sources), and a spec is free to declare both on the same
/// operation; a name-only match would silently drop one of them.
/// </para>
/// <para>
/// <b>Ordering is deterministic</b> (this codebase pins generated output byte-for-byte —
/// <c>TestPlanBuilderTests.IsDeterministic</c> and the golden suite both depend on stable
/// ordering surviving any refactor, and <c>DeclaredPathParameterOrder</c>'s
/// <c>[nswag-path-parameter-order]</c> consumer treats declaration order as meaningful, not
/// incidental): the result walks <paramref name="pathItem"/>'s own parameters first, in their
/// declared order, substituting the matching operation-level entry in place wherever one
/// overrides it, then appends any operation-level parameters that did not match a path-item entry,
/// in their own declared order. When <paramref name="pathItem"/> declares no parameters at all —
/// the common case, and the only shape every spec under <c>samples/</c>, <c>tests/**/Specs/</c>
/// and <c>examples/</c> uses today — this walk is empty and the result is exactly
/// <paramref name="operation"/>'s own parameter list, in its own declared order: byte-identical to
/// what every read site returned before this type existed. That is what keeps this change inert
/// for every spec that does not use the path-item shape.
/// </para>
/// </summary>
public static class EffectiveParameters
{
    public static IReadOnlyList<IOpenApiParameter> Resolve(IOpenApiPathItem pathItem, OpenApiOperation operation)
    {
        ArgumentNullException.ThrowIfNull(pathItem);
        ArgumentNullException.ThrowIfNull(operation);

        var pathItemParameters = pathItem.Parameters ?? [];
        var operationParameters = operation.Parameters ?? [];

        if (pathItemParameters.Count == 0)
        {
            return operationParameters is IReadOnlyList<IOpenApiParameter> alreadyReadOnly
                ? alreadyReadOnly
                : [.. operationParameters];
        }

        // The set of operation-level parameters actually consumed as an override below, tracked
        // by reference so the second loop can append only the ones that were never matched — an
        // operation can legitimately declare two parameters with the same name in different
        // locations (see the class doc comment's {id}-in-path-vs-query example), so identity, not
        // (name, in), is what "already placed" has to mean here.
        var consumed = new HashSet<IOpenApiParameter>();
        var merged = new List<IOpenApiParameter>(pathItemParameters.Count + operationParameters.Count);

        foreach (var pathParameter in pathItemParameters)
        {
            var overriddenBy = operationParameters.FirstOrDefault(candidate =>
                candidate.In == pathParameter.In &&
                string.Equals(candidate.Name, pathParameter.Name, StringComparison.Ordinal));

            if (overriddenBy is not null)
            {
                merged.Add(overriddenBy);
                consumed.Add(overriddenBy);
            }
            else
            {
                merged.Add(pathParameter);
            }
        }

        foreach (var operationParameter in operationParameters)
        {
            if (!consumed.Contains(operationParameter))
            {
                merged.Add(operationParameter);
            }
        }

        return merged;
    }
}

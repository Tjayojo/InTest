namespace InTest.Cli.Planning;

/// <summary>
/// The OpenAPI-declared shape of a path parameter — originally added (decision 6, Task 4's review
/// finding) so <see cref="TemplateRenderer"/> could pick an unmatchable-but-well-typed value for a
/// declared-error/auth case: rendering <c>Guid.NewGuid().ToString()</c> for every path parameter
/// regardless of declared type sends an ill-typed value against a `type: integer` parameter — an
/// ASP.NET Core `[ApiController]` binding `int id` without a route constraint answers 400 from
/// model binding before the action's <c>NotFound()</c> path ever runs, so the generated 404 case
/// fails on every run.
/// <para>
/// <c>[typed-path-parameters]</c>: this enum now also drives a second, unrelated consumer —
/// <see cref="TemplateRenderer"/>'s client-routed branch, which converts a fixture's <c>string</c>
/// value to the type Kiota's per-parameter item-builder indexer actually declares, so the
/// generated call binds the indexer overload the declared type predicts rather than the
/// deprecated <c>this[string]</c> one every non-string kind used to fall back to (see the
/// typed-client-invocation plan's risk section, "Generator-version fragility", for the measured
/// finding this closes). <see cref="Long"/> and <see cref="Guid"/> exist for that consumer alone —
/// <see cref="TestPlanBuilder.TryPlanDeclaredNotFound"/>'s unmatchable-value use of this enum only
/// ever needed to distinguish "numeric" from "not", so <see cref="UnmatchableValueFor"/>
/// (`TemplateRenderer.cs`) still renders the same numeric literal for both <see cref="Integer"/>
/// and <see cref="Long"/>, and the same fresh GUID for both <see cref="String"/> and
/// <see cref="Guid"/> — only the client-routed branch tells the four kinds apart.
/// </para>
/// <para>
/// Deliberately narrow — see the typed-path-parameters task's own instruction not to
/// speculatively add date/decimal/etc.: these four cover id-shaped path parameters, which is what
/// path parameters overwhelmingly are, and <see cref="ClientCallPlanner"/> never needs a fifth
/// "unsupported kind" gate as a result — every schema shape <see cref="TestPlanBuilder"/> can
/// resolve a path parameter to already maps onto one of these four by construction
/// (<see cref="TestPlanBuilder"/>'s own <c>ResolvePathParameterKinds</c> is exhaustive over its
/// input, not partial).
/// </para>
/// </summary>
public enum PathParameterKind
{
    /// <summary>No declared type/format that any other member below claims, or no path parameter
    /// schema declared at all — the default a fresh GUID has always been well-typed for.</summary>
    String,

    /// <summary><c>type: integer</c> or <c>type: number</c>, with no <c>format</c> or any format
    /// other than <c>int64</c> — fits <c>int.Parse(...)</c> in the client-routed branch.</summary>
    Integer,

    /// <summary><c>type: integer</c>, <c>format: int64</c> — fits <c>long.Parse(...)</c> in the
    /// client-routed branch; <see cref="TemplateRenderer.UnmatchableValueFor"/> still treats this
    /// the same as <see cref="Integer"/> for the raw-HTTP declared-error/auth branch, since both
    /// are "well-typed numeric" there.</summary>
    Long,

    /// <summary><c>type: string</c>, <c>format: uuid</c> — fits <c>Guid.Parse(...)</c> in the
    /// client-routed branch; <see cref="TemplateRenderer.UnmatchableValueFor"/> still treats this
    /// the same as <see cref="String"/> for the raw-HTTP declared-error/auth branch, since a fresh
    /// GUID was already a well-typed unmatchable value for a uuid-formatted string.</summary>
    Guid
}
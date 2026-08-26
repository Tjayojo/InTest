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
/// path parameters overwhelmingly are.
/// </para>
/// <para>
/// <b>Corrected finding, not the original design.</b> This type's own doc comment used to claim
/// <see cref="ClientCallPlanner"/> never needs a fifth "unsupported kind" gate, reasoning that
/// <see cref="TestPlanBuilder"/>'s <c>ResolvePathParameterKind</c> is exhaustive over every schema
/// shape it can see and therefore always lands on one of these four members. That conflated two
/// different claims: the method is total (it always returns *something*), but it was not
/// *correct* — a real kiota 1.34.1 client's per-parameter item-builder indexer is typed per the
/// spec's declared <c>type</c>/<c>format</c> far more finely than this four-member enum
/// distinguishes (measured directly: <c>type: string, format: date-time</c> gets
/// <c>this[DateTimeOffset]</c>; <c>type: number, format: double</c> gets <c>this[double]</c>), and
/// the totality-by-fallback-to-<see cref="String"/>/<see cref="Integer"/> behaviour that made the
/// method total silently misrouted both of those into the wrong bucket — a bare string splice
/// against a <c>DateTimeOffset</c> indexer (binding the deprecated <c>this[string]</c> overload)
/// and an <c>int.Parse(...)</c> against a <c>double</c> fixture value (a runtime
/// <see cref="FormatException"/> on any non-integral value such as <c>"1.5"</c>) respectively. Both
/// measured against real generator output, not inferred from documentation — see the
/// typed-client-invocation plan's `[typed-path-parameters]` section for the correction and the
/// gate this now feeds. <c>ResolvePathParameterKind</c> now returns <c>PathParameterKind?</c>:
/// <see langword="null"/> for any shape outside this enum's four members, and
/// <see cref="ClientCallPlanner.Resolve"/> withholds convention when any path parameter resolves to
/// <see langword="null"/>, exactly the fifth gate this comment used to say could never be needed.
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
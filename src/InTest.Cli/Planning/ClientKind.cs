namespace InTest.Cli.Planning;

/// <summary>
/// Which generator produced the adopter's pre-generated API client — governs whether
/// <see cref="ClientCallPlanner"/> can derive a call expression by convention at all, per
/// <c>[convention-plus-override]</c> and <c>[refit-override-only]</c>
/// (docs/superpowers/plans/2026-08-25-intest-typed-client-invocation.md). Mirrors
/// <see cref="Configuration.LoadedClientConfig.Kind"/>'s three accepted spellings exactly —
/// <c>Configuration.ConfigLoader</c> is the only place a string reaches this enum, so the mapping
/// never needs to handle a fourth value.
/// </summary>
public enum ClientKind
{
    /// <summary>
    /// Gets a convention guess (see <see cref="ClientCallPlanner.BuildKiotaConvention"/>) —
    /// measured against a real kiota 1.34.1 client generated from
    /// samples/Orders.Api/Orders.Api.json to confirm the fluent-builder shape a
    /// no-<c>operationId</c> ASP.NET Core document actually produces.
    /// </summary>
    Kiota,

    /// <summary>
    /// Gets a convention guess, but only when the spec's <c>operationId</c> makes NSwag's own
    /// naming deterministic — <c>[nswag-needs-operationid]</c>. The original v1 measurement (no
    /// <c>operationId</c> anywhere in the spec) found NSwag's synthesized
    /// <c>{Resource}{VERB}Async</c> naming both unpredictable (a collection-vs-item split no
    /// path-segment convention can see coming) and uncompilable (strongly-typed parameters with no
    /// string overload). Measured again with a real <c>operationId</c> present: nswag 14.7.1 emits
    /// exactly <c>{PascalCase(operationId)}Async</c> on the configured client class — see
    /// <see cref="ClientCallPlanner"/>'s own doc comment for the full measured finding, including
    /// the separate underscore-splitting hazard that still withholds convention even with an
    /// <c>operationId</c> present.
    /// </summary>
    NSwag,

    /// <summary>
    /// Override-map-only, unconditionally: "Refit" names an interface <i>shape</i> a team can
    /// reach from more than one generator (Refitter, NSwag's own Refit template, or a
    /// hand-written interface), not one tool with one naming convention to derive — there is no
    /// single convention to guess at all.
    /// </summary>
    Refit
}

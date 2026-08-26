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
    /// Override-map-only in v1, on measured evidence rather than caution: NSwag's generated
    /// methods take <b>strongly typed</b> parameters with no string overload
    /// (<c>OrdersGETAsync(System.Guid id)</c>), so a fixture value — always a <see cref="string"/>
    /// — cannot be spliced into a derived call and compile. See
    /// <see cref="ClientCallPlanner"/>'s own doc comment for the full measured finding.
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

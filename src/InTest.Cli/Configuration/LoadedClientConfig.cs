namespace InTest.Cli.Configuration;

/// <summary>
/// The validated "client" section of intest.json — present only when the adopter opted into
/// routing generated Success cases through their own pre-generated API client (Kiota, NSwag or
/// Refit) instead of raw HTTP. See
/// docs/superpowers/plans/2026-08-25-intest-typed-client-invocation.md's
/// <c>[convention-plus-override]</c> for why this exists at all and
/// <see cref="ConfigLoader"/>'s own doc comment for the layering: this is ConfigLoader's own
/// output, produced by one loader and read by every command the same way — <c>ConfigLoader.Load</c>
/// is the only producer, so by the time a <see cref="LoadedClientConfig"/> exists both fields are
/// already known-good.
/// <para>
/// Deliberately narrower than what <see cref="Planning.ClientPlanningConfig"/> later needs:
/// <c>client-map.json</c>'s overrides are a distinct, separately-owned file
/// (<see cref="Clients.ClientCallMap"/>) that <see cref="ConfigLoader"/> never reads, so this
/// record carries only what <c>intest.json</c> itself declares. Assembling the two into a
/// <see cref="Planning.ClientPlanningConfig"/> for <c>TestPlanBuilder.Build</c> is a command's job
/// (stage 3 — <c>GenerateCommand</c>), not this type's.
/// </para>
/// </summary>
/// <param name="Kind">
/// Ordinal-exact: <c>"kiota"</c>, <c>"nswag"</c> or <c>"refit"</c> — the same lowercase-only
/// discipline <see cref="ConfigLoader.RequireSupportedFramework"/> applies to
/// <c>project.framework"</c>, for the same reason: this is adopter-facing JSON, not a C#
/// identifier with case-insensitive lookup rules, and <c>Planning.ClientKind</c> has exactly one
/// accepted spelling per member.
/// </param>
/// <param name="TypeName">
/// The generated client's fully-qualified C# type name — <c>Orders.ApiClient.OrdersApiClient</c>,
/// for example. Reaches <c>mstest-class.scriban</c> in <b>reference position</b>
/// (<c>ApiClient&lt;Orders.ApiClient.OrdersApiClient&gt;()</c>), not inside a string literal, so it
/// is validated with <see cref="Naming.CSharpIdentifier.TryValidateDottedName"/> — the same rule
/// that governs <c>project.rootNamespace</c> and <c>project.testBaseClass</c> — rather than
/// <see cref="Naming.CSharpLiteral"/>, which escapes text that lands inside quotes.
/// </param>
public sealed record LoadedClientConfig(string Kind, string TypeName);

namespace InTest.Cli.Planning;

/// <summary>
/// Everything <see cref="TestPlanBuilder.Build"/> needs to resolve a client call expression for a
/// qualifying Success case — kind, typeName and the adopter's override map — carried as one record
/// rather than three loose parameters. CLAUDE.md: "<c>TestCasePlan</c> deliberately carries
/// verdicts computed elsewhere ... rather than letting downstream code re-derive them" is about
/// <see cref="TestPlanBuilder.Build"/>'s <i>output</i>; this is the matching discipline on its
/// <i>input</i> side — one shaped parameter <c>Build</c> gains, not an accumulation of loose ones
/// across call sites, so every existing caller (<c>GenerateCommand</c>, <c>FixturesRepairCommand</c>,
/// every test that calls <c>TestPlanBuilder.Build(document)</c> with one argument) keeps compiling
/// unchanged.
/// <para>
/// <see cref="TypeName"/> is carried here even though nothing in <c>Planning</c> reads it —
/// resolving a call expression needs only <see cref="Kind"/> and <see cref="Overrides"/>. It rides
/// along because this record is the one shape stage 3's renderer will need for
/// <c>client_type_name</c> (the typed-client-invocation plan's bare template field), and splitting
/// "kind + overrides" from "kind + typeName + overrides" into two records a caller must keep in
/// sync would be the very re-derivation CLAUDE.md warns against, one layer up.
/// </para>
/// <para>
/// Deliberately distinct from <see cref="Configuration.LoadedClientConfig"/>: that type is
/// <c>ConfigLoader</c>'s own output, produced from <c>intest.json</c> alone and carrying no
/// overrides, because <c>ConfigLoader</c> never reads <c>client-map.json</c>
/// (<see cref="Clients.ClientCallMap"/> is a separate, separately-owned file). Assembling a
/// <see cref="ClientPlanningConfig"/> from a <see cref="Configuration.LoadedClientConfig"/> plus a
/// parsed <see cref="Clients.ClientCallMap"/> is a command's job (stage 3 — <c>GenerateCommand</c>),
/// not this type's.
/// </para>
/// </summary>
public sealed record ClientPlanningConfig(ClientKind Kind, string TypeName, IReadOnlyDictionary<string, string> Overrides);

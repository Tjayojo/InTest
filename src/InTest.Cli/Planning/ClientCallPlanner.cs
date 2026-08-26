using System.Text;
using InTest.Cli.Clients;
using InTest.Cli.Naming;

namespace InTest.Cli.Planning;

/// <summary>
/// Resolves one operation to a client call expression — sibling to
/// <see cref="Fixtures.FixtureComposer"/> and in the same relationship to <see cref="TestCasePlan"/>
/// that <c>NeedsFixture</c>/<c>HasRequestBody</c> already have: this is the single place that
/// decides the verdict <see cref="TestCasePlan.ClientCallExpression"/> carries, so nothing
/// downstream (stage 3's renderer, chiefly) ever has to re-derive it.
/// <para>
/// <b>Override wins outright.</b> If <c>client-map.json</c> names the operation key, that value is
/// returned verbatim, unconditionally — no gate below applies to it. <c>[compiler-is-oracle]</c>:
/// a bad override fails the adopter's own build at a generated line, loudly, which is the intended
/// failure mode for a value this type never validates beyond "is it non-blank"
/// (<see cref="ClientCallMap"/>'s own doc comment states the trust model this relies on).
/// </para>
/// <para>
/// <b>Otherwise, convention — Kiota unconditionally, NSwag once gated
/// (<c>[nswag-needs-operationid]</c>, below), Refit never.</b>
/// <b>Measured against a real <c>kiota</c> 1.34.1 client generated from
/// <c>samples/Orders.Api/Orders.Api.json</c></b> — a document that declares no <c>operationId</c>
/// anywhere, the common ASP.NET Core case. Confirmed directly from the generated builder classes,
/// not inferred from Kiota's documentation:
/// <list type="bullet">
/// <item>Each literal path segment becomes a PascalCase property on a fluent builder chain
/// (<c>OrdersApiClient.Api</c> → <c>ApiRequestBuilder</c>; <c>.Customers</c> / <c>.Orders</c> →
/// collection builders), and each <c>{param}</c> segment becomes an indexer on the current
/// builder, so <c>GET /api/orders/{id}</c> is <c>client.Api.Orders[id].GetAsync()</c>.</item>
/// <item>Every item builder Kiota emits carries <b>both</b> a <c>this[Guid position]</c> indexer
/// and an <c>[Obsolete]</c>-marked <c>this[string position]</c> overload
/// (<c>OrdersItemRequestBuilder.cs</c>, <c>CustomersItemRequestBuilder.cs</c>) — which is exactly
/// why splicing <c>FixtureParameter("opKey","param")</c> (stage 3's helper, which returns
/// <see cref="string"/>) into the indexer compiles. Without that overload this whole convention
/// would need a per-parameter type conversion, the same problem that rules NSwag out below.</item>
/// <item>Verb methods are <c>GetAsync</c>, <c>PostAsync</c>, <c>PutAsync</c>, <c>PatchAsync</c>,
/// <c>DeleteAsync</c> — confirmed on every one of the four qualifying Orders operations plus the
/// two non-qualifying ones (<c>OrdersRequestBuilder.PostAsync</c>,
/// <c>OrdersItemRequestBuilder.DeleteAsync</c>).</item>
/// <item><c>PostAsync</c> takes a <b>typed model object</b> as its first positional parameter
/// (<c>PostAsync(CreateOrderRequest body, ...)</c>), never a JSON string, and query parameters are
/// bound through a <c>RequestConfiguration&lt;...&gt;</c> lambda, never positional arguments — both
/// are exactly why <see cref="TestPlanBuilder"/> gates convention on "no request body" and "no
/// query parameters" before ever calling <see cref="BuildKiotaConvention"/>: there is no fixture
/// value this planner could splice into either shape and have it compile.</item>
/// </list>
/// </para>
/// <para>
/// <b><c>[nswag-needs-operationid]</c> — NSwag gets a convention guess, but only when
/// <c>operationId</c> makes its own naming deterministic.</b> The original measurement (against
/// the same no-<c>operationId</c> spec Kiota was measured against) found NSwag's synthesized
/// <c>{Resource}{VERB}Async</c> naming both unpredictable — a collection-vs-item split
/// (<c>CustomersAllAsync()</c> for the list, <c>CustomersGETAsync(id)</c> for the item) no
/// path-segment convention can see coming without also knowing NSwag's own naming settings — and
/// uncompilable, since those synthesized methods take <b>strongly typed</b> parameters with no
/// string overload (<c>CustomersGETAsync(System.Guid id)</c>). That measurement never exercised
/// the case where the spec actually declares an <c>operationId</c> — the common case for a
/// hand-written or already-adopted spec, distinct from the auto-generated-controller case the
/// original measurement used. Measured directly (nswag 14.7.1, <c>openapi2csclient</c>, an
/// explicit <c>/classname</c>): with <c>operationId: "getOrderById"</c> present, the generated
/// client emits exactly <c>GetOrderByIdAsync(System.Guid id)</c> plus a sibling overload
/// <c>GetOrderByIdAsync(System.Guid id, System.Threading.CancellationToken cancellationToken)</c>
/// on the single configured class — i.e. <c>{PascalCase(operationId)}Async</c>, deterministic and
/// independent of path shape, resource naming or verb. <see cref="BuildNSwagConvention"/> targets
/// the cancellation-token overload by argument name, the same <c>cancellationToken:</c>-by-name
/// discipline <see cref="BuildKiotaConvention"/>'s caller already uses and for the same reason:
/// positional binding to the wrong overload is not a risk worth taking when naming the parameter
/// removes it entirely.
/// </para>
/// <para>
/// <b>The underscore hazard — measured, not merely warned about.</b> NSwag's default
/// <c>operationGenerationMode</c> (<c>MultipleClientsFromOperationId</c>) splits an
/// <c>operationId</c> containing <c>'_'</c> on that character: the text before the last <c>'_'</c>
/// becomes a <i>separate client class</i> (named from the classname template, one instance per
/// distinct prefix), and only the text after it feeds the method name. Confirmed by direct
/// generation: a spec with <c>operationId: "Orders_GetById"</c> on one operation and
/// <c>operationId: "Customers_GetById"</c> on another, run through <c>nswag openapi2csclient</c>
/// with no <c>operationGenerationMode</c> override, emits <b>two</b> separate
/// <c>public partial class</c> client types — <c>OrdersClient.GetByIdAsync(...)</c> and
/// <c>CustomersClient.GetByIdAsync(...)</c> — never the single class an adopter's one configured
/// <c>client.typeName</c> names. A convention that ignored this would derive
/// <c>{PascalCase(operationId)}Async</c> (e.g. <c>OrdersGetByIdAsync</c>) against a client that
/// never declares a method by that name at all, on a class the operation may not even belong to —
/// <c>[compiler-is-oracle]</c> would still catch the resulting CS1061 loudly, but knowingly
/// shipping a convention already measured to name the wrong receiver is the same "not a guess
/// worth making" call the strongly-typed-parameter finding above already settled.
/// <see cref="ClientCallPlanner.Resolve"/> therefore withholds convention for any <c>operationId</c>
/// containing <c>'_'</c>, unconditionally — it cannot tell from the operation alone whether the
/// adopter's spec-wide <c>operationGenerationMode</c> actually splits on it (a non-default
/// <c>SingleClientFromOperationId</c> setting would not), so it treats the hazard as always live
/// rather than guessing that a particular project opted out of NSwag's own default.
/// </para>
/// <para>
/// <b>Refit gets no convention guess at all, permanently</b> (<c>[refit-override-only]</c>) — not
/// gated on anything the spec could supply, unlike NSwag above. "Refit" names an interface
/// <i>shape</i> reachable from more than one source — Refitter, NSwag's own Refit template output,
/// or a hand-written interface — each free to name its methods however its author chose, with no
/// spec-derived fact (an <c>operationId</c> included) that could ever make that naming
/// deterministic. There is nothing to derive here regardless of what the spec declares, which is
/// why this is a permanent limitation and not a gate like NSwag's two above.
/// </para>
/// </summary>
public static class ClientCallPlanner
{
    private static readonly Dictionary<string, string> KiotaVerbMethods = new(StringComparer.Ordinal)
    {
        ["GET"] = "GetAsync",
        ["POST"] = "PostAsync",
        ["PUT"] = "PutAsync",
        ["PATCH"] = "PatchAsync",
        ["DELETE"] = "DeleteAsync"
    };

    /// <summary>
    /// One resolution attempt's outcome: either a call expression, or a reason nothing was
    /// resolved — never both, never neither. <see cref="TestPlanBuilder"/> is the only consumer:
    /// it sets <see cref="TestCasePlan.ClientCallExpression"/> from <see cref="Expression"/>, and
    /// when that is <see langword="null"/> it turns <see cref="UnresolvedReason"/> into a
    /// <see cref="CoverageNote"/> — the same "note, not a silent gap" idiom
    /// <see cref="TestPlanBuilder.TryPlanDeclaredNotFound"/> already uses for a withheld 404 case.
    /// </summary>
    public readonly record struct Resolution(string? Expression, string? UnresolvedReason);

    /// <summary>
    /// Resolves one operation. <paramref name="hasQueryParameters"/> and
    /// <paramref name="hasRequestBody"/> are supplied by the caller rather than re-derived here —
    /// <see cref="TestPlanBuilder"/> already computes both from the same authorities
    /// (<c>QueryParameters</c> and <see cref="Fixtures.FixtureComposer.HasJsonBodyToCompose"/>) for
    /// <see cref="TestCasePlan.QueryParameterNames"/> and <see cref="TestCasePlan.HasRequestBody"/>,
    /// and CLAUDE.md's "re-deriving is the recurring defect in this codebase" applies here exactly
    /// as much as it does to <c>NeedsFixture</c>. <paramref name="hasOperationId"/> is the same
    /// idiom applied to <c>[nswag-needs-operationid]</c>'s gate: <c>TestPlanBuilder</c> already
    /// knows whether the spec declared one — <c>OperationKey.Resolve</c> synthesizes
    /// <paramref name="operationKey"/> from method and path precisely when it did not, so
    /// <c>OperationKey.Synthesized</c> (negated) is that fact already computed, not a second read
    /// of the operation. When <paramref name="hasOperationId"/> is <see langword="true"/>,
    /// <paramref name="operationKey"/> <i>is</i> the declared <c>operationId</c> (trimmed) —
    /// <c>OperationKey.Resolve</c>'s only non-synthesized branch returns it verbatim — so this
    /// method never needs a separate operationId parameter to derive the NSwag convention from.
    /// <para>
    /// The gate order matters and is deliberate: the override lookup runs <i>before</i> any flag
    /// is even inspected, because an explicit override in <c>client-map.json</c> bypasses every
    /// gate below it — the adopter wrote real C# and owns it, and a query-parameter or
    /// request-body Success case is exactly the shape an override exists to cover in the first
    /// place.
    /// </para>
    /// <para>
    /// <b>Refit: unconditional withhold.</b> No spec-derived fact could ever make this
    /// deterministic (<c>[refit-override-only]</c>, this type's own doc comment above) — checked
    /// first among the per-kind gates because, unlike NSwag's two below, nothing else could ever
    /// change the verdict.
    /// </para>
    /// <para>
    /// <b>NSwag: the operationId-presence gate, then the underscore gate.</b> Both are
    /// <c>[nswag-needs-operationid]</c>'s contribution — see this type's own doc comment for the
    /// measured evidence behind each. No <c>operationId</c> reproduces the original v1 measurement
    /// (NSwag's synthesized, uncompilable, unpredictable naming) exactly, so convention is
    /// withheld the same way it always was for that case. An <c>operationId</c> containing
    /// <c>'_'</c> is withheld for the separate, measured reason that NSwag's default
    /// <c>operationGenerationMode</c> would route the call onto a client class this planner cannot
    /// name from the operation alone.
    /// </para>
    /// <para>
    /// <b>The query-parameter and request-body gates apply to both Kiota and NSwag.</b> Neither
    /// generator's convention-derived call has a fixture value to splice into a query-binding
    /// shape (Kiota's <c>RequestConfiguration&lt;...&gt;</c> lambda; NSwag emits its own,
    /// differently-shaped optional parameters for query parameters), and neither takes a JSON
    /// request body as a positional string argument (both take a typed model object) — so an
    /// operation with either withholds convention regardless of which of the two kinds it is,
    /// with the same <see cref="CoverageNote"/> pointing at <see cref="ClientCallMap.FileName"/>.
    /// </para>
    /// <para>
    /// <b>The verb gate — Kiota only, added after a reproduced crash, not a hypothetical.</b> A
    /// spec with a <c>head</c>/<c>options</c>/<c>trace</c> operation and a <c>client</c> section
    /// configured used to reach <see cref="BuildKiotaConvention"/> unconditionally once the
    /// query-parameter and request-body gates both passed (neither of those methods ever carries
    /// either), and that method throws <see cref="ArgumentException"/> for any verb outside
    /// GET/POST/PUT/PATCH/DELETE — confirmed by direct reproduction: a spec with
    /// <c>head: { responses: { "200": … } }</c> on <c>/api/ping</c> generated cleanly with no
    /// <c>client</c> section, then crashed `generate` with <c>intest: unexpected failure:
    /// ArgumentException: 'HEAD' has no known Kiota verb-method convention</c>, exit 2, the instant
    /// one was added. Throwing is the right contract for <see cref="BuildKiotaConvention"/> itself
    /// — a <b>public</b> method with no gating of its own, so it must fail loudly rather than hand
    /// back a nonsense expression — but <see cref="Resolve"/> is exactly the kind of caller
    /// <see cref="TestPlanBuilder"/> already asks of the query-parameter and request-body gates:
    /// absorb an unsupported shape into a <see cref="CoverageNote"/> naming
    /// <see cref="ClientCallMap.FileName"/>, the same "note, not a crash" idiom every other
    /// withheld-convention reason on this type already uses, rather than letting an exception a
    /// planning-time caller never expected escape all the way out of `generate`. NSwag needs no
    /// equivalent gate: <see cref="BuildNSwagConvention"/> derives the method name from
    /// <paramref name="operationKey"/> alone, never from <paramref name="httpMethod"/>, so no verb
    /// can make it throw.
    /// </para>
    /// <para>
    /// <b>The path-parameter-kind gate — corrected finding, not the original design.</b> This
    /// method's own doc comment used to say a fifth gate for an "unsupported path-parameter kind"
    /// was impossible to need, reasoning that <c>TestPlanBuilder.ResolvePathParameterKind</c> is
    /// exhaustive over every schema shape it can see and therefore always lands on one of
    /// <see cref="PathParameterKind"/>'s four members. That conflated "always returns something"
    /// with "always returns the right thing": a real kiota 1.34.1 client types a
    /// <c>date-time</c>-formatted string parameter as <c>this[DateTimeOffset]</c> and a
    /// <c>number</c>-typed one as <c>this[double]</c> — neither is <see cref="PathParameterKind.String"/>
    /// or <see cref="PathParameterKind.Integer"/>, but the old exhaustive-by-fallback mapping
    /// silently filed both under one of those two anyway, producing a bare-string splice that binds
    /// the deprecated <c>this[string]</c> overload in the first case and an <c>int.Parse(...)</c>
    /// that throws <see cref="FormatException"/> at runtime on a non-integral fixture value like
    /// <c>"1.5"</c> in the second. See <see cref="PathParameterKind"/>'s own doc comment for the
    /// full measured evidence. <c>TestPlanBuilder.ResolvePathParameterKind</c> now returns
    /// <c>PathParameterKind?</c>, <see langword="null"/> for any shape outside the four typable
    /// ones, and <paramref name="hasUntypablePathParameter"/> — computed once by
    /// <see cref="TestPlanBuilder"/> from that same per-parameter list, never re-derived here — is
    /// exactly the fifth gate this comment used to say could never be needed: any operation with at
    /// least one untypable path parameter withholds convention, for both Kiota and NSwag alike,
    /// since <c>TemplateRenderer.WrapForClientCall</c>'s per-kind conversion is the one mechanism
    /// both conventions' <c>{param}</c> placeholders share.
    /// </para>
    /// </summary>
    public static Resolution Resolve(
        ClientKind kind,
        string operationKey,
        bool hasOperationId,
        string httpMethod,
        string pathTemplate,
        IReadOnlyList<string> declaredPathParameterOrder,
        bool hasQueryParameters,
        bool hasRequestBody,
        bool hasUntypablePathParameter,
        IReadOnlyDictionary<string, string> overrides)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(httpMethod);
        ArgumentException.ThrowIfNullOrWhiteSpace(pathTemplate);
        ArgumentNullException.ThrowIfNull(declaredPathParameterOrder);
        ArgumentNullException.ThrowIfNull(overrides);

        if (overrides.TryGetValue(operationKey, out var overrideExpression))
        {
            return new Resolution(overrideExpression, null);
        }

        if (kind == ClientKind.Refit)
        {
            return new Resolution(null,
            $"has no {kind} convention for typed-client invocation ([refit-override-only]); " +
            $"add an entry to {ClientCallMap.FileName} to route it through the client");
        }

        // [nswag-needs-operationid]: both NSwag-only gates run before the query-parameter/
        // request-body gate below, which applies to Kiota too — an operation this planner cannot
        // even name a method for (no operationId) or cannot trust the receiver class of (an
        // underscore) is withheld regardless of how simple its parameter shape otherwise is.
        if (kind == ClientKind.NSwag && !hasOperationId)
        {
            return new Resolution(null,
            "has no operationId, which the NSwag convention needs to derive a deterministic " +
            "method name ([nswag-needs-operationid]) — add one to the spec, or add an entry to " +
            $"{ClientCallMap.FileName} to route it through the client");
        }

        if (kind == ClientKind.NSwag && operationKey.Contains('_', StringComparison.Ordinal))
        {
            return new Resolution(null,
            $"has an operationId ('{operationKey}') containing '_', which NSwag's default " +
            "operationGenerationMode (MultipleClientsFromOperationId) splits into a separate " +
            "client class per prefix, making the single configured client.typeName the wrong " +
            $"receiver for it ([nswag-needs-operationid]) — add an entry to {ClientCallMap.FileName} " +
            "to route it through the client");
        }

        // [nswag-path-parameter-order]: a third NSwag-only gate, for the same reason as the two
        // above — this planner cannot trust an argument order it cannot fully derive. Kiota is
        // unaffected: BuildKiotaConvention derives its indexer placeholders from the path
        // template's own structure, never from declaredPathParameterOrder, so a mismatch here
        // cannot affect it.
        if (kind == ClientKind.NSwag)
        {
            var declaredNames = new HashSet<string>(declaredPathParameterOrder, StringComparer.Ordinal);
            var undeclared = PathTemplatePlaceholderNames(pathTemplate)
                .FirstOrDefault(name => !declaredNames.Contains(name));

            if (undeclared is not null)
            {
                return new Resolution(null,
                $"has a path parameter ('{undeclared}') in its path template with no matching " +
                "'in: path' entry in the declared parameters, so the NSwag convention cannot " +
                $"determine its declared argument order — add an entry to {ClientCallMap.FileName} " +
                "to route it through the client");
            }
        }

        if (hasQueryParameters || hasRequestBody)
        {
            var reason = (hasQueryParameters, hasRequestBody) switch
            {
                (true, true) => "query parameters and a request body",
                (true, false) => "query parameters",
                _ => "a request body"
            };

            return new Resolution(null,
            $"has {reason}, which the {kind} convention does not attempt to bind — add an entry " +
            $"to {ClientCallMap.FileName} to route it through the client");
        }

        // [typed-path-parameters], corrected: applies to both Kiota and NSwag equally, since
        // TemplateRenderer.WrapForClientCall's per-kind conversion is what both conventions'
        // {param} placeholders share — see this method's own doc comment above for the measured
        // evidence and the reasoning this gate corrects.
        if (hasUntypablePathParameter)
        {
            return new Resolution(null,
            $"has a path parameter whose declared schema has no client-side type conversion " +
            $"InTest can produce ([typed-path-parameters]) — add an entry to {ClientCallMap.FileName} " +
            "to route it through the client");
        }

        if (kind == ClientKind.NSwag)
        {
            return new Resolution(BuildNSwagConvention(pathTemplate, operationKey, declaredPathParameterOrder), null);
        }

        // Checked ahead of the call rather than caught around it: BuildKiotaConvention's throw is
        // meant for a caller with no other way to learn a verb is unsupported, and Resolve already
        // has the answer in hand via the same lookup table that method itself consults. Catching
        // the exception instead would work too, but would make "an unsupported verb" and "a bug in
        // BuildKiotaConvention" both surface as the same caught ArgumentException here — this way,
        // any exception BuildKiotaConvention does throw is unambiguously a defect in this planner,
        // not an expected, already-handled case.
        if (!KiotaVerbMethods.ContainsKey(httpMethod.ToUpperInvariant()))
        {
            return new Resolution(null,
            $"is an HTTP {httpMethod} operation, which has no known Kiota verb-method convention " +
            $"(supported: {string.Join(", ", KiotaVerbMethods.Keys)}) — add an entry to " +
            $"{ClientCallMap.FileName} to route it through the client");
        }

        return new Resolution(BuildKiotaConvention(httpMethod, pathTemplate), null);
    }

    /// <summary>
    /// The Kiota convention alone, with no override lookup and no gate-checking —
    /// <see cref="Resolve"/> is the only production caller, and it has already confirmed there is
    /// no override, the kind is <see cref="ClientKind.Kiota"/>, and the operation has neither a
    /// query parameter nor a request body before calling this. Public so
    /// <c>ClientCallPlannerTests</c> can assert the derived expression directly against captured
    /// real generator output, independent of the gating <see cref="Resolve"/> layers on top.
    /// <para>
    /// Splits <paramref name="pathTemplate"/> on <c>/</c>; each literal segment becomes a
    /// PascalCase property (<see cref="CSharpIdentifier.ToPascalCase"/> — the same helper
    /// <see cref="TestPlanBuilder"/> already uses for tag and method names, not a second one); each
    /// <c>{param}</c> segment becomes an indexer carrying the placeholder <b>intact</b> —
    /// <c>[{id}]</c>, not a resolved value — because stage 3's renderer is what substitutes
    /// <c>FixtureParameter("opKey","param")</c> into it, reusing
    /// <c>TemplateRenderer.PathArguments</c>'s existing placeholder-resolution rather than this
    /// planner inventing a second one. The verb method is appended last. No leading receiver
    /// (stage 3's renderer owns <c>ApiClient&lt;T&gt;()</c>) and no trailing <c>()</c> (stage 3
    /// owns the call arguments — a <c>RequestConfiguration</c> lambda or a typed body for the
    /// operations this convention never reaches, and nothing at all for the ones it does).
    /// </para>
    /// </summary>
    public static string BuildKiotaConvention(string httpMethod, string pathTemplate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(httpMethod);
        ArgumentException.ThrowIfNullOrWhiteSpace(pathTemplate);

        if (!KiotaVerbMethods.TryGetValue(httpMethod.ToUpperInvariant(), out var verbMethod))
        {
            throw new ArgumentException(
            $"'{httpMethod}' has no known Kiota verb-method convention. Supported: " +
            $"{string.Join(", ", KiotaVerbMethods.Keys)}.", nameof(httpMethod));
        }

        var builder = new StringBuilder();

        foreach (var segment in pathTemplate.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var isPathParameter = segment.Length >= 2 && segment[0] == '{' && segment[^1] == '}';

            if (isPathParameter)
            {
                // No leading '.' — the indexer attaches directly to whatever preceded it, exactly
                // as Kiota's own `Orders[id]` shape does; a literal segment immediately after it
                // (the `builder.Length > 0` branch below) supplies its own leading '.'.
                builder.Append('[').Append(segment).Append(']');
            }
            else
            {
                if (builder.Length > 0)
                {
                    builder.Append('.');
                }
                builder.Append(CSharpIdentifier.ToPascalCase(segment));
            }
        }

        builder.Append('.').Append(verbMethod);
        return builder.ToString();
    }

    /// <summary>
    /// The NSwag convention alone, with no override lookup and no gate-checking —
    /// <see cref="Resolve"/> is the only production caller, and it has already confirmed there is
    /// no override, the kind is <see cref="ClientKind.NSwag"/>, an <c>operationId</c> was declared
    /// and contains no <c>'_'</c>, and the operation has neither a query parameter nor a request
    /// body before calling this ([nswag-needs-operationid]). Public for the same reason
    /// <see cref="BuildKiotaConvention"/> is: <c>ClientCallPlannerTests</c> asserts the derived
    /// expression directly against captured real generator output, independent of the gating
    /// <see cref="Resolve"/> layers on top.
    /// <para>
    /// Unlike <see cref="BuildKiotaConvention"/>, <paramref name="httpMethod"/> plays no part —
    /// measured directly (nswag 14.7.1, <c>openapi2csclient</c>): the generated method name is
    /// exactly <c>{PascalCase(operationId)}Async</c> on the single configured client class,
    /// independent of the operation's verb, its path shape, or its resource naming. There is
    /// therefore no builder-chain receiver to construct at all — <c>[nswag-needs-operationid]</c>'s
    /// own doc comment on this type puts it plainly: "the configured client type IS the receiver".
    /// </para>
    /// <para>
    /// <b>Argument order — corrected finding, not the original design.</b> This doc comment used to
    /// claim <paramref name="pathTemplate"/> alone supplies the argument order, "the same order
    /// <c>TestCasePlan.PathParameterNames</c> ... [is] built in" — i.e. path-template order. That
    /// is wrong: NSwag binds a generated method's positional path-parameter arguments in the
    /// spec's declared <c>parameters</c>-array order, not path-template order, and every piece of
    /// evidence this convention originally shipped on had at most one path parameter — a shape
    /// where the two orders are indistinguishable by construction. Measured directly (nswag
    /// 14.7.1): a path <c>/customers/{customerId}/orders/{orderId}</c> whose <c>parameters</c>
    /// array declares <c>orderId</c> before <c>customerId</c> generates
    /// <c>GetCustomerOrderAsync(System.Guid orderId, System.Guid customerId, ...)</c> — path order
    /// and declared order disagree, and because both parameters happen to share a type the
    /// wrong-order call still compiles, silently asserting against the wrong resource.
    /// <paramref name="declaredPathParameterOrder"/> — <c>TestPlanBuilder.DeclaredPathParameterOrder</c>,
    /// computed once from <c>operation.Parameters</c> and carried rather than re-derived here
    /// (<c>[nswag-path-parameter-order]</c>) — is the actual argument order; <paramref
    /// name="pathTemplate"/> now contributes only the *set* of placeholder names (via
    /// <see cref="PathTemplatePlaceholderNames"/>) used to filter that order down to the
    /// parameters this path template actually declares, left placeholder-intact — <c>{id}</c>, not
    /// a resolved value — for <c>TemplateRenderer.BuildClientCallExpression</c> to substitute
    /// exactly as it already does for <see cref="BuildKiotaConvention"/>'s output, one
    /// implementation of path-parameter fixture resolution shared by both conventions. Kiota is
    /// unaffected by this correction — <see cref="BuildKiotaConvention"/> derives its indexer
    /// placeholders from the path template's own structure, a builder chain that is structurally
    /// path-ordered by construction, and never reads a declared order at all.
    /// </para>
    /// <para>
    /// The cancellation token is spliced in by name
    /// (<c>cancellationToken: TestContext.CancellationToken</c>), not appended positionally or left
    /// for the renderer to add — measured directly: NSwag emits the token-carrying overload as a
    /// distinct sibling method (<c>GetOrderByIdAsync(Guid id)</c> alongside
    /// <c>GetOrderByIdAsync(Guid id, CancellationToken cancellationToken)</c>), not one method with
    /// an optional parameter, so naming the argument is what selects the right overload rather than
    /// merely being stylistic. This is also why the returned expression always ends in a closing
    /// <c>')'</c> — <c>TemplateRenderer.BuildClientCallExpression</c>'s own "already closes its own
    /// argument list" check (the same one a self-closing <c>client-map.json</c> override relies on)
    /// reads that as "do not append a second argument list", which is exactly correct here: this
    /// method's own trailing <c>')'</c> is the call's real, final one, not an override's.
    /// </para>
    /// </summary>
    public static string BuildNSwagConvention(
        string pathTemplate, string operationId, IReadOnlyList<string> declaredPathParameterOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathTemplate);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentNullException.ThrowIfNull(declaredPathParameterOrder);

        var methodName = CSharpIdentifier.ToPascalCase(operationId) + "Async";

        // Filtered to this path template's own placeholders (a defensive no-op in the reachable,
        // gated-by-Resolve case, where every placeholder is already known to be declared) rather
        // than spliced verbatim — declaredPathParameterOrder is the operation's whole `in: path`
        // parameter list, and a caller bypassing Resolve's own gate (a direct unit test, e.g.)
        // should not get a declared-but-unused name's placeholder into the argument list.
        var placeholders = new HashSet<string>(PathTemplatePlaceholderNames(pathTemplate), StringComparer.Ordinal);

        var arguments = declaredPathParameterOrder
            .Where(placeholders.Contains)
            .Select(name => $"{{{name}}}")
            .ToList();
        arguments.Add("cancellationToken: TestContext.CancellationToken");

        return $"{methodName}({string.Join(", ", arguments)})";
    }

    /// <summary>
    /// Every <c>{param}</c> path-template segment's bare name (no braces), in path-template order —
    /// the same segment scan <see cref="BuildKiotaConvention"/> performs inline for its own
    /// purpose, factored out here so <see cref="Resolve"/>'s <c>[nswag-path-parameter-order]</c>
    /// gate and <see cref="BuildNSwagConvention"/>'s own filtering share one implementation of
    /// "which names does this path template declare" rather than each re-splitting the string.
    /// </summary>
    private static IEnumerable<string> PathTemplatePlaceholderNames(string pathTemplate)
        => pathTemplate
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment.Length >= 2 && segment[0] == '{' && segment[^1] == '}')
            .Select(segment => segment[1..^1]);
}

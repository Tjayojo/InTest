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
/// <b>Otherwise, convention — Kiota only.</b>
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
/// <b>NSwag and Refit get no convention guess in v1 — override-map-only</b>
/// (<c>[refit-override-only]</c>). NSwag was measured too, against a real <c>nswag</c> 14.7.1
/// client generated from the same spec, and the plan's original <c>{OperationId}Async</c>
/// convention turned out wrong twice over: with no <c>operationId</c>, NSwag synthesizes
/// <c>{Resource}{VERB}Async</c> and invents a collection-vs-item distinction
/// (<c>CustomersAllAsync()</c> for the list, <c>CustomersGETAsync(id)</c> for the item) that a
/// path-segment convention like Kiota's cannot predict without also knowing NSwag's own naming
/// settings. Fatally, its parameters are <b>strongly typed with no string overload</b> —
/// <c>CustomersGETAsync(System.Guid id)</c> — so splicing a fixture's <see cref="string"/> value
/// there would not compile; <c>[compiler-is-oracle]</c> would catch it loudly, which is the design
/// working, but shipping a convention already measured to emit uncompilable code is not a guess
/// worth making. "Refit" is not one generator with one convention at all — it names an interface
/// shape reachable from Refitter, NSwag's own Refit template, or a hand-written interface — so
/// there is nothing to derive.
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
    /// as much as it does to <c>NeedsFixture</c>.
    /// <para>
    /// The gate order matters and is deliberate: the override lookup runs <i>before</i> either
    /// flag is even inspected, because an explicit override in <c>client-map.json</c> bypasses
    /// every gate below it — the adopter wrote real C# and owns it, and a query-parameter or
    /// request-body Success case is exactly the shape an override exists to cover in the first
    /// place.
    /// </para>
    /// <para>
    /// <b>The verb gate — added after a reproduced crash, not a hypothetical.</b> A spec with a
    /// <c>head</c>/<c>options</c>/<c>trace</c> operation and a <c>client</c> section configured
    /// used to reach <see cref="BuildKiotaConvention"/> unconditionally once the query-parameter
    /// and request-body gates both passed (neither of those methods ever carries either), and that
    /// method throws <see cref="ArgumentException"/> for any verb outside GET/POST/PUT/PATCH/DELETE
    /// — confirmed by direct reproduction: a spec with <c>head: { responses: { "200": … } }</c> on
    /// <c>/api/ping</c> generated cleanly with no <c>client</c> section, then crashed `generate`
    /// with <c>intest: unexpected failure: ArgumentException: 'HEAD' has no known Kiota
    /// verb-method convention</c>, exit 2, the instant one was added. Throwing is the right
    /// contract for <see cref="BuildKiotaConvention"/> itself — a <b>public</b> method with no
    /// gating of its own, so it must fail loudly rather than hand back a nonsense expression — but
    /// <see cref="Resolve"/> is exactly the kind of caller <see cref="TestPlanBuilder"/> already
    /// asks of the query-parameter and request-body gates: absorb an unsupported shape into a
    /// <see cref="CoverageNote"/> naming <see cref="ClientCallMap.FileName"/>, the same "note, not
    /// a crash" idiom every other withheld-convention reason on this type already uses, rather than
    /// letting an exception a planning-time caller never expected escape all the way out of
    /// `generate`.
    /// </para>
    /// </summary>
    public static Resolution Resolve(
        ClientKind kind,
        string operationKey,
        string httpMethod,
        string pathTemplate,
        bool hasQueryParameters,
        bool hasRequestBody,
        IReadOnlyDictionary<string, string> overrides)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(httpMethod);
        ArgumentException.ThrowIfNullOrWhiteSpace(pathTemplate);
        ArgumentNullException.ThrowIfNull(overrides);

        if (overrides.TryGetValue(operationKey, out var overrideExpression))
        {
            return new Resolution(overrideExpression, null);
        }

        if (kind != ClientKind.Kiota)
        {
            return new Resolution(null,
            $"has no {kind} convention for typed-client invocation in v1 ([refit-override-only]); " +
            $"add an entry to {ClientCallMap.FileName} to route it through the client");
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
            $"has {reason}, which the kiota convention does not attempt to bind — add an entry " +
            $"to {ClientCallMap.FileName} to route it through the client");
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
}

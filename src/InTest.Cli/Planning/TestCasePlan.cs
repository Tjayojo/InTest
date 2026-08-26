namespace InTest.Cli.Planning;

public sealed record TestCasePlan(
    string MethodName,
    string DisplayName,
    string OperationKey,
    bool OperationKeySynthesized,
    string HttpMethod,
    string PathTemplate,
    IReadOnlyList<string> PathParameterNames,
    int ExpectedStatus,
    string? SchemaKey,
    string Category,
    // Part of the case's identity, not a derived property computed at render time — decision 4.
    // TestPlanBuilder's dedupe machinery keys its proposed-name dictionary on operation key
    // *and* role together: two cases for the same operation (a success and a declared error)
    // deliberately get different method names, and only get the same *hash suffix* input when
    // they also share a role — collapsing role into the operation key alone reassigns every
    // case for an operation the same deduped name, which is CS0111 the moment an operation
    // emits more than one case. Defaults to Success so every call site that predates decision 5
    // — none of which had a role to state — is read as what it always was.
    CaseRole Role = CaseRole.Success,
    // Carries FixtureComposer.NeedsFixture's verdict for this operation so that no other caller
    // (fixtures repair, chiefly) ever has to recompute or restate it — a divergence between a
    // second copy of this logic and the composer's own is a defect this branch already fixed
    // twice. Defaults to true so call sites outside fixture handling, which never asked for a
    // NeedsFixture opinion, are unaffected.
    bool NeedsFixture = true,
    // All declared `in: query` parameter names, whether or not the composer ends up emitting a
    // fixture entry for each one (decision 1). The template only needs this to decide whether an
    // operation has query parameters at all, so it knows whether to look any up at runtime — it
    // is not a restatement of FixtureComposer's tiered precedence, just a presence check. Null
    // (the default) means "not computed", read as empty by every consumer.
    IReadOnlyList<string>? QueryParameterNames = null,
    // Whether the operation has an `application/json` request body with a schema to compose from
    // — FixtureComposer.HasJsonBodyToCompose is the sole authority on this (same reasoning as
    // NeedsFixture above), so this is set from that method directly rather than re-derived here.
    bool HasRequestBody = false,
    // Parallel to PathParameterNames — same order, same length when set. Originally populated only
    // by TestPlanBuilder's declared-error and auth branches, to render an unmatchable-but-well-typed
    // value (decision 6); [typed-path-parameters] widened this to the Success case too, because
    // TemplateRenderer's client-routed branch needs the same per-parameter kind to convert a
    // fixture's string value to the type Kiota's item-builder indexer declares, before splicing it
    // into the indexer. Every call site that predates either use — including every hand-built
    // TestCasePlan in a test that does not care about kinds — leaves this null, read as "kind
    // unknown", which TemplateRenderer treats identically to String in both branches: the same GUID
    // the declared-error/auth branch always rendered, and the same bare FixtureParameter(...) the
    // client-routed branch always spliced, so no existing behaviour changes silently.
    //
    // Element type is PathParameterKind? , not PathParameterKind — a corrected finding, not the
    // original shape (see PathParameterKind's own doc comment for the measured evidence):
    // TestPlanBuilder.ResolvePathParameterKind can no longer force every schema into one of the
    // enum's four members, because a real generator types some shapes (`number`, `date-time`, and
    // others) more finely than those four distinguish, and misclassifying one of those into
    // String/Integer used to silently bind a deprecated overload or produce a runtime
    // FormatException. A null element means "this generator's client-side type for this parameter
    // is outside what InTest can convert a fixture value into" — ClientCallPlanner.Resolve
    // withholds convention entirely for a Success case with any null element (a note pointing at
    // client-map.json, the same idiom as the query-parameter and request-body gates), while
    // TemplateRenderer.UnmatchableValueFor keeps treating a null element exactly like String for
    // the declared-error/auth branch, since that branch never splices a real fixture value and the
    // fresh-GUID fallback was already well-typed for it regardless of the parameter's real shape.
    IReadOnlyList<PathParameterKind?>? PathParameterKinds = null,
    // [nswag-path-parameter-order]: the operation's `in: path` parameters, in the order the spec's
    // own `parameters` array declares them — never the path-template order PathParameterNames
    // carries. Exists solely to feed ClientCallPlanner.Resolve/BuildNSwagConvention: NSwag binds a
    // generated method's positional arguments in *declared* order, not path-template order, and the
    // two are only guaranteed to agree when an operation has at most one path parameter (every
    // piece of evidence this convention originally shipped on). Measured directly against nswag
    // 14.7.1: a path `/customers/{customerId}/orders/{orderId}` whose `parameters` array declares
    // `orderId` before `customerId` generates `GetCustomerOrderAsync(System.Guid orderId,
    // System.Guid customerId, ...)` — path order and declared order disagree, and both parameters
    // share a type, so a wrong-order call still compiles and silently asserts against the wrong
    // resource. TestPlanBuilder computes this once, from the same operation.Parameters read
    // PathParameterKinds already uses, and it is the single source ClientCallPlanner.Resolve reads
    // rather than re-deriving order from pathTemplate itself (CLAUDE.md: verdicts are carried, not
    // re-derived downstream). Null for every call site that predates this field, and for every
    // DeclaredError/Auth case ([success-only] — neither role ever resolves a client call), read as
    // "no declared order known"; Kiota is unaffected regardless, since BuildKiotaConvention derives
    // its indexer placeholders from the path template's own structure and never reads this field.
    IReadOnlyList<string>? DeclaredPathParameterOrder = null,
    // Decision 7: which identity a CaseRole.Auth case authenticates as. Defaults to Default, the
    // no-override slot, so every call site that predates Task 5 — none of which had a slot to
    // state, including every Success and DeclaredError case — renders exactly as it always did:
    // TemplateRenderer emits nothing for Default.
    IdentitySlot Slot = IdentitySlot.Default,
    // The distinct union of OAuth scopes the operation's `security` declares, across every
    // requirement and every scheme within it — carried, not recomputed, for the same reason
    // Role above is: a later task's template/render phase needs to pass these scopes to a
    // runtime guard for the wrong-scope 403 case, and it must not have to re-parse
    // OpenApiOperation.Security itself to get them. This plan is the single source of truth for
    // what the spec declared; a render-time re-derivation is a second copy of that logic that
    // could drift from this one, the same class of defect Role's comment already warns about for
    // NeedsFixture. Defaults to an empty array, never null, so every call site that predates
    // this member — every Success and DeclaredError case, and the 401 case, none of which have a
    // scope requirement to state — reads as "nothing required" rather than an absent value a
    // consumer would have to null-check. This holds unconditionally, not because
    // TestPlanBuilder.PlanAuthCases (the only site that assigns a non-default value) happens to
    // be careful about it: the property below backs this parameter with a field and coalesces
    // null in its `init` accessor, so every construction path — this default, an explicit
    // constructor call, and a `with` expression alike — reads back a non-null empty collection.
    // Caveat (see TestPlanBuilder.RequiredScopes, [containment], for the full reasoning): this is
    // a union across every security requirement, which enlarges the required set beyond what any
    // single requirement demands. For a multi-requirement spec that can make a case run and fail
    // against a status the API is correct to return, because it gets compared against every
    // requirement's scopes rather than just the one the identity actually satisfies. Every sample
    // spec today declares exactly one requirement, so this gap is real but untriggered, not safe.
    IReadOnlyList<string>? RequiredScopes = null,
    // The verdict ClientCallPlanner.Resolve computed for this case, carried rather than
    // re-derived downstream (CLAUDE.md's recurring-defect warning, same as every other carried
    // verdict on this record) — stage 3's renderer splices this expression bare rather than
    // re-consulting client-map.json or re-running the Kiota convention per render. Null in every
    // case that predates this field (every existing call site, none of which had a client to
    // resolve against) and in every case this plan ever withholds a client call for: every
    // DeclaredError and Auth case unconditionally ([success-only] — TestPlanBuilder never even
    // attempts a resolution for those roles, regardless of config), and any Success case that
    // either declared no `client` config, matched no override, or failed one of
    // ClientCallPlanner.Resolve's gates (Refit unconditionally; NSwag with no operationId or an
    // underscored one; a query parameter or a request body for either Kiota or NSwag) —
    // TestPlanBuilder emits a CoverageNote pointing at client-map.json in that last case rather
    // than reporting the operation as unsupported, since its raw-HTTP Success case still generated
    // and still runs.
    string? ClientCallExpression = null)
{
    // Collection-typed record parameters cannot default to a non-constant expression
    // (Array.Empty<string>() is not a compile-time constant) directly in the parameter list, so
    // the primary constructor parameter above stays nullable and this explicitly-declared
    // property — which overrides the compiler-generated one for the same-named positional
    // parameter — normalizes it to an empty array instead.
    //
    // The coalesce has to live in the init accessor, not only in the field initializer below: a
    // `with` expression drives the init accessor directly (the compiler-generated copy
    // constructor plus init setters), bypassing the field initializer entirely. A version of this
    // fix that only coalesced in the initializer would guarantee non-null for a freshly
    // constructed plan but not for `plan with { RequiredScopes = null! }`, which would read back
    // a null reference. Backing the property with an explicit field and coalescing in `init`
    // makes the never-null guarantee hold for both construction paths, not just the first one.
    private readonly IReadOnlyList<string> _requiredScopes = RequiredScopes ?? Array.Empty<string>();

    public IReadOnlyList<string> RequiredScopes
    {
        get => _requiredScopes;
        init => _requiredScopes = value ?? Array.Empty<string>();
    }
}
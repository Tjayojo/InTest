using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace InTest.Golden.Tests;

/// <summary>
/// A minimal, stateful HTTP stub the golden execution tests point a generated suite's
/// <c>Api:BaseUrl</c> at, standing in for the API under test. Runs in this test process on a
/// free local port and records enough state — every request path served, how many times
/// <c>/health/ready</c> has answered — for a test to assert what actually reached the wire,
/// rather than trusting the generated suite's own "Passed!" (see
/// <c>GeneratedSuiteExecutionTests</c> for what each test checks against it and why).
/// <para>
/// Extracted out of <c>GeneratedSuiteExecutionTests</c> (M7 of Task 6's third review round) once
/// that file had doubled in size from Task 6's own additions and a further test was already
/// planned for the same file.
/// </para>
/// <para>
/// Task 8a adds a small in-memory item store behind <c>POST /api/items</c> and
/// <c>DELETE /api/items/{id}</c> only — every other path below is untouched, including the
/// permissive <c>/api/status/</c> catch-all <c>FixtureParameterReachesALiveRequestEndToEnd</c>
/// depends on. This is the narrower of the two options Task 8a's own plan step lays out: confine
/// statefulness to the paths the new guard test needs, rather than pre-seeding every existing
/// permissive arm. A duplicate <c>sku</c> 409s while that id is still live, and deleting an id
/// the store does not (or no longer) know about 404s — that pair is the exact shape F7 reproduced
/// against <c>samples/Catalog.Api</c> (<c>docs/v0-acceptance.md</c>'s v1-b section). A successful
/// delete frees its <c>sku</c> for reuse: <c>samples/Catalog.Api</c>'s
/// <c>ProductsController</c> checks uniqueness with <c>Products.AnyAsync(p => p.Sku ==
/// request.Sku)</c>, a query over live rows only, so a deleted product's SKU really does become
/// available again there — a review round on Task 8a caught an earlier version of this doc
/// claiming the opposite. A <c>sku</c> the store still refuses on a second run is therefore never
/// because delete failed to free it; it is because nothing ever deleted that particular row (see
/// <c>GoldenFixtureSources.RepeatableSeedFixture</c>'s own doc for exactly which one, and why).
/// </para>
/// </summary>
internal sealed class GoldenApiStub : IDisposable
{
    /// <summary>
    /// Matches the scaffold's default <c>InTest:Readiness:ConsecutiveSuccesses</c> (see
    /// <c>InitCommand</c>'s appsettings.json template). <c>Readiness.WaitAsync</c> cannot return
    /// before this many consecutive 200s from <c>/health/ready</c>, so <c>/api/seed</c> uses it
    /// as a gate: seeding that runs before readiness has genuinely completed gets a 503, not a
    /// value that happened to work anyway. <c>GeneratedSuiteExecutionTests.PointAtStub</c> pins
    /// the scaffold's own copy of this setting to this constant (rather than trusting the two to
    /// happen to agree), so a scaffold default change fails loudly there instead of silently
    /// changing what this gate actually proves.
    /// </summary>
    public const int RequiredReadyProbes = 2;

    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _serverCancellation;
    private readonly ConcurrentBag<string> _receivedPaths = [];
    private readonly ConcurrentDictionary<string, string> _itemsById = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _skusInUse = new(StringComparer.Ordinal);
    private int _readyProbeCount;
    private StatusOverride? _statusOverride;

    public int Port { get; }

    /// <summary>
    /// Every request path the stub has served. A <see cref="ConcurrentBag{T}"/>, so arrival
    /// order is not preserved; assertions against this must be membership-only
    /// (<c>ShouldContain</c>), never order- or index-based.
    /// </summary>
    public IReadOnlyCollection<string> ReceivedPaths => _receivedPaths;

    /// <summary>
    /// Item rows currently in the store — created by <c>POST /api/items</c>, removed by a
    /// matching <c>DELETE /api/items/{id}</c>. Only <c>TheGeneratedSuitePassesTwiceAgainstTheSameStore</c>
    /// reads this, to confirm a create genuinely happened each run rather than the second run
    /// passing merely because nothing was attempted (Task 8a's own stated worry about a
    /// vacuously-passing guard).
    /// </summary>
    public int ItemCount => _itemsById.Count;

    /// <summary>
    /// Overrides GET /api/status's response for the remainder of this stub's lifetime — used only
    /// by the [capture-not-deserialize] golden tests in GeneratedSuiteExecutionTests
    /// (ClientRoutedSuccessCaseCatchesASchemaViolationAfterTheClientDeserializes,
    /// ClientRoutedSuccessCaseReceivesAUsableDeserializedResult,
    /// ClientRoutedSuccessCaseSurfacesInTestsOwnContractFailureNotTheClientsException) that prove
    /// a client-routed test case still catches a raw-bytes schema violation, or InTest's own
    /// status-mismatch verdict, after a typed client has deserialized the response
    /// ResponseCaptureHandler replaced. Every other golden test in this file relies on the
    /// unconditional 200/{"state":"ok"} default in ServeAsync's switch below and never calls this.
    /// <para>
    /// Volatile.Write/Read — matching this class's own _readyProbeCount pattern just above —
    /// rather than a lock: whichever golden test calls this always does so from its own setup
    /// code, well before the generated suite's separate `dotnet test` process ever sends its
    /// first request, so there is no genuine concurrent writer to arbitrate. This only needs to
    /// guarantee the one write is actually visible to whichever server thread later handles the
    /// request, not resolve a race between writers that never happens.
    /// </para>
    /// </summary>
    public void OverrideStatusResponse(int status, string body) =>
        Volatile.Write(ref _statusOverride, new StatusOverride(status, body));

    private sealed record StatusOverride(int Status, string Body);

    public GoldenApiStub()
    {
        Port = FreePort();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        _listener.Start();

        _serverCancellation = new CancellationTokenSource();
        _ = ServeAsync(_serverCancellation.Token);
    }

    public void Dispose()
    {
        _serverCancellation.Cancel();
        try { _listener.Stop(); } catch (ObjectDisposedException) { }
        ((IDisposable)_listener).Dispose();
        _serverCancellation.Dispose();
    }

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception) { return; }

            var path = context.Request.Url?.AbsolutePath ?? "/";
            _receivedPaths.Add(path);

            // The two new stateful paths are dispatched on (method, path) before falling through
            // to the original path-only switch below, which stays exactly as it was — Task 8a,
            // Step 1's narrower option (see this class's own doc): nothing here changes what any
            // existing arm, including the permissive "/api/status/" catch-all, returns.
            var (status, body) = context.Request.HttpMethod switch
            {
                "POST" when path == "/api/items" =>
                    await HandleCreateItemAsync(context.Request, cancellationToken),
                "DELETE" when path.StartsWith("/api/items/", StringComparison.Ordinal) =>
                    HandleDeleteItem(path),
                _ => path switch
                {
                    "/health/ready" => HandleHealthCheck(),
                    // Volatile.Read'ing OverrideStatusResponse's write — see that method's own doc
                    // for why this is the one arm in the switch below that is not a fixed literal.
                    "/api/status" => Volatile.Read(ref _statusOverride) is { } o ? (o.Status, o.Body) : (200, """{"state":"ok"}"""),
                    // Task 5 Step 2's live wire proof — the one path in this stub that actually
                    // inspects Authorization, everything else here trusts the request unconditionally.
                    "/api/secure" => HandleSecureResource(context.Request),
                    // Task 4 / F11's live wire proof — see HandleScopedSecureResource's own doc for
                    // why this path authorizes any bearer token rather than discriminating identities.
                    "/api/secure-scoped" => HandleScopedSecureResource(context.Request),
                    // Task 4 / F11's other half — see HandleScopedSecureResourceRequiringDelete's
                    // own doc for why this path, unlike the one above, does discriminate identities.
                    "/api/secure-scoped-delete" => HandleScopedSecureResourceRequiringDelete(context.Request),
                    // Belt-and-braces, not the primary catch: RequireFixture already throws before a
                    // request carrying an unresolved sentinel is ever built (confirmed by sabotaging
                    // the replace step in FixtureParameterReachesALiveRequestEndToEnd — the failure
                    // surfaces as FixtureUnresolvedException, not a live 400). This exists so the
                    // live proof still fails loudly, rather than hanging on a request that never
                    // reaches the stub, if that call were ever removed from the template without a
                    // unit test catching it first.
                    "/api/status/TODO:id" => (400, """{"error":"unresolved fixture sentinel"}"""),
                    // Only SeedIdFixture (APublishedFixtureKeyReachesALiveRequest) calls this. 503
                    // until readiness has genuinely been satisfied — see RequiredReadyProbes' own
                    // doc — so a fixture that ran before Readiness.WaitAsync returned gets a real
                    // failure instead of a value that happened to work anyway.
                    "/api/seed" => HandleSeed(),
                    _ when path.StartsWith("/api/status/", StringComparison.Ordinal) => (200, """{"state":"ok"}"""),
                    _ => (404, """{"error":"not found"}""")
                }
            };

            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
            context.Response.Close();
        }
    }

    /// <summary>
    /// Answers a generated auth case's request the way a real secured API would: no
    /// <c>Authorization</c> header at all is 401 (the no-token case's whole mechanism — decision
    /// 3); a header carrying <c>"Bearer token-for-default"</c> — <c>GoldenTokenProviderSources.
    /// TwoIdentityTokenProvider</c>'s own naming convention — is the success arm; anything else,
    /// specifically <c>"Bearer token-for-secondary"</c>, is 403. This is the only arm in this
    /// stub that inspects <c>Authorization</c> at all; every other path here trusts whatever the
    /// generated suite sends.
    /// </summary>
    private static (int, string) HandleSecureResource(HttpListenerRequest request)
    {
        var authorization = request.Headers["Authorization"];
        if (string.IsNullOrEmpty(authorization))
        {
            return (401, """{"error":"unauthorized"}""");
        }

        return authorization == "Bearer token-for-default"
            ? (200, """{"state":"ok"}""")
            : (403, """{"error":"forbidden"}""");
    }

    /// <summary>
    /// Task 4 / F11's live wire proof. Deliberately does not discriminate default from secondary
    /// the way <see cref="HandleSecureResource"/> does — this operation's whole point (in the test
    /// that uses it) is that the secondary identity actually holds the scope it declares, so a
    /// real, correctly-implemented API would authorize it too. Any request carrying a bearer token
    /// at all gets 200; only a missing <c>Authorization</c> header gets 401. If the generated
    /// wrong-scope 403 case ever reached this over the wire, it would see 200 and fail its own
    /// assertion of 403 — proving <c>RequireSecondaryIdentityLacks</c> must skip it before the
    /// request is ever built, not merely that the case happens to still pass.
    /// </summary>
    private static (int, string) HandleScopedSecureResource(HttpListenerRequest request)
    {
        var authorization = request.Headers["Authorization"];
        return string.IsNullOrEmpty(authorization)
            ? (401, """{"error":"unauthorized"}""")
            : (200, """{"state":"ok"}""");
    }

    /// <summary>
    /// Task 4 / F11's other live wire proof, alongside <see cref="HandleScopedSecureResource"/>:
    /// that guard's over-skip failure mode (containment flipped from <c>All</c> to <c>Any</c>, or
    /// the empty-<c>requiredScopes</c> early return removed) is invisible if every scoped operation
    /// in the golden suite happens to be one the secondary identity is authorized for — the whole
    /// run stays green whether the guard skips correctly or skips everything. This path requires
    /// both <c>"orders.write"</c> and <c>"orders.delete"</c> — the secondary identity in
    /// <c>GoldenTokenProviderSources.TwoIdentityTokenProvider</c> holds only the former, so it
    /// does not hold everything this path requires and <c>RequireSecondaryIdentityLacks</c> must
    /// let the generated 403 case run rather than skip it. Discriminates identity the same way
    /// <see cref="HandleSecureResource"/> does — <c>"Bearer token-for-default"</c> is authorized,
    /// anything else is 403 — rather than authorizing any bearer token the way
    /// <see cref="HandleScopedSecureResource"/> does, because this path's whole point is that the
    /// secondary identity is genuinely unauthorized here and a real 403 must come back over the wire.
    /// </summary>
    private static (int, string) HandleScopedSecureResourceRequiringDelete(HttpListenerRequest request)
    {
        var authorization = request.Headers["Authorization"];
        if (string.IsNullOrEmpty(authorization))
        {
            return (401, """{"error":"unauthorized"}""");
        }

        return authorization == "Bearer token-for-default"
            ? (200, """{"state":"ok"}""")
            : (403, """{"error":"forbidden"}""");
    }

    private (int, string) HandleHealthCheck()
    {
        Interlocked.Increment(ref _readyProbeCount);
        return (200, """{"status":"ready"}""");
    }

    private (int, string) HandleSeed() =>
        Volatile.Read(ref _readyProbeCount) >= RequiredReadyProbes
            ? (200, """{"seedValue":"seeded-42"}""")
            : (503, """{"error":"not ready for seeding yet"}""");

    /// <summary>
    /// Creates an item row keyed by its <c>sku</c>, 409ing on a duplicate <em>still-live</em>
    /// <c>sku</c> exactly the way <c>samples/Catalog.Api</c>'s <c>ProductsController</c> does
    /// (an <c>AnyAsync</c> query over live rows) — F7's "literal fixture values collide with
    /// unique constraints" reproduced in-process. <c>sku</c> is read directly off the request
    /// body rather than validated against a schema: this stub answers whatever a live generated
    /// suite actually sends, the same trust level every other arm here already gives the request.
    /// <para>
    /// A malformed body (not valid JSON, or a non-string <c>sku</c>) 400s rather than throwing —
    /// this is the first stub arm that parses a request body at all, and <see cref="ServeAsync"/>'s
    /// own loop has no try/catch around request handling (only around
    /// <see cref="HttpListener.GetContextAsync"/>): an unhandled exception here would fault the
    /// whole serve loop, and every request after it — including the ones from the run that
    /// caused the fault — would hang to the caller's own timeout instead of failing cleanly. Low
    /// probability in practice (<c>FixtureValidation</c> guarantees the body a generated suite
    /// actually sends), but cheap enough to close outright.
    /// </para>
    /// </summary>
    private async Task<(int, string)> HandleCreateItemAsync(HttpListenerRequest request, CancellationToken cancellationToken)
    {
        string sku;
        try
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var requestBody = await reader.ReadToEndAsync(cancellationToken);
            using var document = JsonDocument.Parse(requestBody);
            sku = document.RootElement.TryGetProperty("sku", out var skuElement) && skuElement.ValueKind == JsonValueKind.String
                ? skuElement.GetString()!
                : "";
        }
        catch (JsonException)
        {
            return (400, """{"error":"malformed request body"}""");
        }

        if (!_skusInUse.TryAdd(sku, 0))
        {
            return (409, $$"""{"error":"sku '{{sku}}' already exists"}""");
        }

        var id = Guid.NewGuid().ToString("N");
        _itemsById[id] = sku;
        return (201, $$"""{"id":"{{id}}","sku":"{{sku}}"}""");
    }

    /// <summary>
    /// Removes an item row, 404ing if <paramref name="path"/>'s id is not currently in the store
    /// — never created, or already deleted by an earlier call. F7's other reproduced failure: a
    /// deleted row does not come back for a second run that targets it by the same, literal id.
    /// Frees the removed row's <c>sku</c> for reuse — matching <c>ProductsController</c>'s
    /// live-rows-only uniqueness query, not a permanent reservation (see this class's own doc for
    /// why an earlier version of this comment claimed the opposite).
    /// </summary>
    private (int, string) HandleDeleteItem(string path)
    {
        var id = path["/api/items/".Length..];
        if (!_itemsById.TryRemove(id, out var sku))
        {
            return (404, """{"error":"not found"}""");
        }

        _skusInUse.TryRemove(sku, out _);
        return (204, "");
    }

    private static int FreePort()
    {
        using var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }
}

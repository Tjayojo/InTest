namespace InTest.Golden.Tests;

/// <summary>
/// C# source the golden execution tests write into a scaffolded project to prove
/// <c>[capture-not-deserialize]</c> — the opt-in-typed-client feature's whole viability, per
/// <c>docs/superpowers/plans/2026-08-25-intest-typed-client-invocation.md</c>'s own words — end
/// to end, before a single line of the generator work (<c>ClientCallPlanner</c>, the template
/// branch) exists. See each constant's own doc for exactly what it proves and which golden test
/// uses it. Kept separate from <see cref="GoldenFixtureSources"/>,
/// <see cref="GoldenTokenProviderSources"/> and <see cref="GoldenAuthHandlerSources"/> for the
/// same reason those three are separate from each other: a distinct concern, in its own file.
/// <para>
/// Stage 1 of that plan is deliberately generator-free: <c>ClientCallPlanner</c> does not exist
/// yet, so nothing can generate a client-routed test case. What can be proven instead — and what
/// this file exists for — is that the runtime mechanism a generated case would eventually call
/// into (<c>ApiTestCore.ApiClient&lt;TClient&gt;()</c>, <c>ResponseCaptureHandler</c>,
/// <c>ApiResponseAssertions.ShouldMatchCapturedContractAsync</c>, all built in the prior stage-1
/// task) actually works against a real, compiled, running typed client — not merely a unit test's
/// in-process double. <see cref="FakeStatusClient"/> stands in for that generator's output by
/// hand; <see cref="ClientRoutedStatusTests"/> stands in for the template branch stage 3 will
/// eventually emit.
/// </para>
/// </summary>
internal static class GoldenTypedClientSources
{
    /// <summary>
    /// A small typed client mimicking the SHAPE of a Kiota- or NSwag-generated client for the
    /// <c>getStatus</c> operation (<c>GET /api/status</c>) that
    /// <c>GeneratedSuiteExecutionTests.Spec</c> already declares and every other test in that file
    /// already exercises over raw HTTP. Takes an <c>HttpClient</c> through its constructor —
    /// exactly the shape a real generated client takes when constructed over
    /// <c>IHttpClientFactory.CreateClient(InTestClients.Api)</c>, per
    /// <c>[client-rides-the-api-pipeline]</c> — rather than reaching for
    /// <c>IHttpClientFactory</c> itself, which is a generator convention this fake deliberately
    /// does not need to fabricate: <c>GoldenTypedClientSources.RegisterFakeStatusClient</c> (in
    /// <c>GeneratedSuiteExecutionTests</c>) is what resolves <c>IHttpClientFactory</c> and hands
    /// the client its <c>HttpClient</c>, the same shape <c>ApiTestCore.Client</c> itself is built
    /// with.
    /// <para>
    /// <b>Deserializes via <c>ReadAsStreamAsync</c>, never <c>ReadAsStringAsync</c> — this is
    /// non-negotiable and is the entire point of this fake existing as hand-written source rather
    /// than reusing some existing test double.</b> <c>ResponseCaptureHandler</c> buffers the real
    /// network response into a byte array, then <b>replaces</b> <c>response.Content</c> with a
    /// fresh <c>ByteArrayContent</c> built from those bytes, so that a downstream consumer — this
    /// client — is handed a second, different <c>HttpContent</c> instance than whatever the
    /// network actually produced. The risk under test is specifically whether that replacement
    /// content is still readable through the exact API surface a real generated client calls:
    /// Kiota and NSwag both deserialize a response via <c>ReadAsStreamAsync</c>, never
    /// <c>ReadAsStringAsync</c>. A fake that used <c>ReadAsStringAsync</c> instead would make
    /// every golden test in this file pass — <c>ByteArrayContent</c> supports both reads equally
    /// well — while proving nothing whatsoever about the one API surface a real client actually
    /// exercises. Do not "simplify" this to <c>ReadAsStringAsync</c>; doing so silently deletes
    /// the proof this whole file exists to provide.
    /// </para>
    /// <para>
    /// Throws <see cref="FakeApiException"/> on a non-2xx status, mimicking NSwag's own
    /// <c>ApiException</c> and Kiota's per-status generated error mappings — both throw their own
    /// generator-specific exception type before their own deserialization ever runs.
    /// <c>FakeStatusResult.State</c> is deliberately a plain nullable <see cref="string"/>, not a
    /// <c>required</c> member: the decisive schema-violation golden test
    /// (<c>ClientRoutedSuccessCaseCatchesASchemaViolationAfterTheClientDeserializes</c>) sends a
    /// body missing <c>"state"</c> entirely, and this type must deserialize that successfully —
    /// proving the stream really was read — rather than throw its own
    /// <see cref="System.Text.Json.JsonException"/> for an unrelated reason. The schema-violation
    /// failure that test exists to prove must come from InTest's own raw-bytes validation
    /// (<c>ApiResponseAssertions.ShouldMatchCapturedContractAsync</c>), never from this fake
    /// client's own deserializer failing first.
    /// </para>
    /// </summary>
    public const string FakeStatusClient = """
    using System.Net.Http;
    using System.Text.Json;

    namespace Stub.ApiTests;

    public sealed class FakeStatusClient(HttpClient httpClient)
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public async Task<FakeStatusResult> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            using var response = await httpClient.GetAsync("/api/status", cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new FakeApiException((int)response.StatusCode);
            }

            // Deliberately ReadAsStreamAsync, never ReadAsStringAsync — see this constant's own
            // doc comment (GoldenTypedClientSources.FakeStatusClient) in GoldenTypedClientSources.cs
            // for the full account of why. ResponseCaptureHandler has already replaced
            // response.Content with a fresh ByteArrayContent by the time this runs; the point of
            // this whole file is proving that replacement is still readable this way.
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var result = await JsonSerializer.DeserializeAsync<FakeStatusResult>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return result ?? new FakeStatusResult();
        }
    }

    /// <summary>Mimics a generator's own strongly-typed response model for getStatus's 200
    /// response (the Status schema: required "state", string). State is deliberately nullable,
    /// not `required` — see FakeStatusClient's own doc for why.</summary>
    public sealed class FakeStatusResult
    {
        public string? State { get; set; }
    }

    /// <summary>Stands in for the generator-specific exception every real typed client throws on
    /// a non-2xx response (NSwag's ApiException, Kiota's per-status error mappings) —
    /// [captured-response-is-the-verdict] exists precisely so an adopter never sees this
    /// exception's own message, which names nothing about run id, expected status, or a body
    /// excerpt the way InTest's own ContractAssertionException does.</summary>
    public sealed class FakeApiException(int statusCode)
        : Exception($"FakeStatusClient: request failed with status {statusCode}.")
    {
        public int StatusCode { get; } = statusCode;
    }
    """;

    /// <summary>
    /// A hand-written <c>partial</c> extension of the generated <c>StatusTests</c> class — the
    /// sanctioned mechanism the mstest-class.scriban template's own header comment names ("Hand-
    /// written tests belong in a partial class in a non-.g.cs file"), rather than a wholly
    /// separate class. Reusing <c>StatusTests</c> this way needs no <c>[TestClass]</c> attribute
    /// or base-class declaration of its own — both already live on the <c>.g.cs</c> partial
    /// declaration `generate` writes for <c>getStatus</c>, and C# merges a partial type's
    /// attributes and base-class list across every declaration — and it is exactly the shape a
    /// real client-routed case will eventually take once stage 3's template branch exists: one
    /// generated class freely mixing a client-routed Success case (this method) alongside its
    /// raw-HTTP sibling (<c>GetStatus_Contract</c>, already in <c>StatusTests.g.cs</c>).
    /// <para>
    /// One test method, reused byte-for-byte across all three golden tests that write this
    /// source into a scaffold — <c>ClientRoutedSuccessCaseCatchesASchemaViolationAfterTheClientDeserializes</c>,
    /// <c>ClientRoutedSuccessCaseReceivesAUsableDeserializedResult</c>, and
    /// <c>ClientRoutedSuccessCaseSurfacesInTestsOwnContractFailureNotTheClientsException</c> — because
    /// what differs between those three is never this method's own code, only what
    /// <c>GoldenApiStub.OverrideStatusResponse</c> configures the stub to answer with before the
    /// generated suite runs. Splitting three near-identical methods here would duplicate the
    /// pinned try/filter/catch shape below for no reason; the stub is what actually varies.
    /// </para>
    /// <para>
    /// <b>The pinned shape, verbatim from the plan's own
    /// <c>[captured-response-is-the-verdict]</c> decision</b> — the template's stage-3 branch will
    /// eventually emit this same shape for every client-routed Success case, but here, in
    /// hand-written stage-1 source, it is spelled out directly rather than templated:
    /// <list type="bullet">
    /// <item><description>The client call happens inside a <c>try</c>. On a non-2xx status,
    /// <c>FakeStatusClient</c> throws <see cref="FakeStatusClient.FakeApiException"/> before its
    /// own deserialization ever runs — mimicking NSwag's <c>ApiException</c> / Kiota's
    /// generator-specific error mapping.</description></item>
    /// <item><description>The first <c>catch</c> is an exception filter testing
    /// <c>InTestAmbient.LastCapturedResponse.Value?.Value is null</c> — two <c>?.</c>s, per
    /// <c>InTestAmbient.LastCapturedResponse</c>'s own doc: the first covers "no slot, no test
    /// scope active", the second "a slot exists but nothing was captured into it yet". When true,
    /// nothing reached the API through <c>ResponseCaptureHandler</c> at all — most likely because
    /// the client was built over a bare <c>HttpClient</c> rather than
    /// <c>InTestClients.Api</c> — and this rethrows the original exception (an authority-mismatch
    /// <see cref="InvalidOperationException"/>, say) unmolested, rather than letting the second
    /// catch below swallow a genuine misconfiguration.</description></item>
    /// <item><description>The second, unconditional <c>catch</c> swallows whatever the client
    /// threw — <see cref="FakeStatusClient.FakeApiException"/> in every golden test that reaches
    /// it — deliberately doing nothing with it: <c>ResponseCaptureHandler</c> already stashed the
    /// real response before <c>FakeStatusClient</c> ever saw it, so there is a strictly better
    /// verdict waiting outside this try/catch.</description></item>
    /// <item><description><c>ShouldMatchCapturedContractAsync</c> runs unconditionally after the
    /// try/catch — not inside either catch — so it runs exactly once regardless of which path was
    /// taken above: a genuinely successful call falls through the try body to it directly; a
    /// swallowed client exception falls through the second catch to the same
    /// line.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// The final two lines — reached only when <c>ShouldMatchCapturedContractAsync</c> did
    /// <em>not</em> throw, i.e. only on a genuinely schema-conforming 200 — are
    /// <c>ClientRoutedSuccessCaseReceivesAUsableDeserializedResult</c>'s whole proof: they assert
    /// on <c>result</c>, the actual deserialized object <c>FakeStatusClient.GetStatusAsync</c>
    /// returned, showing the stream-read downstream of <c>ResponseCaptureHandler</c>'s
    /// <c>Content</c> replacement produced something real and usable — not merely that no
    /// exception escaped constructing it. Placed after the assertion, not before, so the decisive
    /// schema-violation test — whose body is missing <c>"state"</c>, so <c>result.State</c> is
    /// null but the client itself never throws — fails inside
    /// <c>ShouldMatchCapturedContractAsync</c> for the right reason (a schema violation) and never
    /// reaches these two lines at all, rather than failing here first on an unrelated null check.
    /// </para>
    /// </summary>
    public const string ClientRoutedStatusTests = """
    using System.Diagnostics;
    using InTest.Runtime;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Shouldly;

    namespace Stub.ApiTests;

    public partial class StatusTests
    {
        [TestMethod]
        public async Task GetStatus_ClientRouted()
        {
            var stopwatch = Stopwatch.StartNew();
            FakeStatusResult? result = null;

            try
            {
                var client = ApiClient<FakeStatusClient>();
                result = await client.GetStatusAsync(TestContext.CancellationToken);
            }
            catch (Exception) when (InTestAmbient.LastCapturedResponse.Value?.Value is null)
            {
                // [client-rides-the-api-pipeline]: nothing was captured at all, so whatever this
                // exception is, it is not the API's own answer — rethrow it unmolested rather than
                // letting the catch below swallow a genuine misconfiguration.
                throw;
            }
            catch (Exception)
            {
                // [captured-response-is-the-verdict]: ResponseCaptureHandler already stashed the
                // real response before FakeStatusClient ever saw it. Swallowed here on purpose —
                // the assertion below reports InTest's own contract failure (run id, expected vs
                // actual status, elapsed, body excerpt) instead of this bare client exception,
                // which names none of that.
            }

            await ApiResponseAssertions.ShouldMatchCapturedContractAsync(
                LastCapturedResponse, 200, "Status", Schemas, TestId, stopwatch.Elapsed, TestContext.CancellationToken);

            // Reached only when the assertion above did not throw — see this constant's own doc
            // comment (GoldenTypedClientSources.ClientRoutedStatusTests) for why that ordering is
            // deliberate. Proves the deserialized result is real and usable, not merely that no
            // exception escaped producing it.
            result.ShouldNotBeNull();
            result!.State.ShouldBe("ok");
        }
    }
    """;

    /// <summary>
    /// Stage 3's counterpart to <see cref="FakeStatusClient"/>: same behaviour (deserializes via
    /// <c>ReadAsStreamAsync</c>, throws <see cref="FakeApiException"/>-equivalent on a non-2xx
    /// status, tolerant <c>FakeStatusResult</c> — see <see cref="FakeStatusClient"/>'s own doc
    /// comment for why each of those choices matters and is not "simplifiable"), but shaped as a
    /// <c>.Api.&lt;Segment&gt;...</c> fluent builder chain rather than one flat method, because
    /// that is the shape <c>ClientCallPlanner.BuildKiotaConvention</c> actually derives
    /// (<c>GET /api/status</c> → <c>Api.Status.GetAsync</c> — no path parameter on this operation,
    /// so nothing here exercises the indexer/<c>FixtureParameter</c> substitution
    /// <c>TemplateRenderer.BuildClientCallExpression</c> also has to do; that substitution is
    /// covered directly by <c>TemplateRendererClientTests</c> in <c>InTest.Cli.Tests</c> instead,
    /// against a hand-built plan, since proving it needs no live HTTP round trip).
    /// <para>
    /// <c>GetAsync</c> additionally takes a leading, unused
    /// <c>Action&lt;FakeRequestConfiguration&gt;? requestConfiguration = default</c> parameter —
    /// present only so the call this file's golden tests actually exercise
    /// (<c>generate</c>'s own template output, not hand-written source) has to pass
    /// <c>cancellationToken</c> <b>by name</b> to reach it, the same way it would have to against a
    /// real Kiota-generated verb method (see <c>ClientCallPlanner</c>'s own doc comment: every
    /// Kiota verb method takes <c>(Action&lt;RequestConfiguration&lt;...&gt;&gt;?
    /// requestConfiguration = default, CancellationToken cancellationToken = default)</c>). A
    /// method with only a trailing <c>cancellationToken</c> parameter would let
    /// <c>TemplateRenderer.BuildClientCallExpression</c> get away with a positional argument and
    /// still compile, silently losing the proof that the by-name call this renderer emits is
    /// actually necessary.
    /// </para>
    /// <para>
    /// [stage-3b]: <see cref="FakePingRequestBuilder.GetAsync"/> is this file's second operation,
    /// added alongside <see cref="FakeStatusRequestBuilder"/> rather than replacing it — every
    /// existing golden test against <c>Api.Status.GetAsync</c> stays unaffected. It answers
    /// <c>GET /api/ping</c>, a bodiless-204 operation
    /// (<c>GeneratedSuiteExecutionTests.SpecWithBodilessClientRoutedOperation</c>), and deliberately
    /// returns a bare <see cref="Task"/> rather than <c>Task&lt;T&gt;</c>: there is no schema, so
    /// nothing here has anything to deserialize — mirroring a real Kiota client's own <c>void</c>-
    /// content-type verb methods, which return <see cref="Task"/> too.
    /// </para>
    /// <para>
    /// [finding-3]: <see cref="FakeStatusRequestBuilder"/>'s <c>this[string position]</c> indexer,
    /// added alongside its existing <c>GetAsync()</c> rather than replacing it, carries the exact
    /// <see cref="ObsoleteAttribute"/> text a real kiota 1.34.1 item builder's own deprecated
    /// overload does (confirmed directly against <c>OrdersItemRequestBuilder.cs</c> and
    /// <c>CustomersItemRequestBuilder.cs</c> — see this plan's own risk section for the full
    /// account). Before this addition, no golden test compiled a client-routed case with a path
    /// parameter at all — <c>FakeOrdersApiClient</c> had no indexer anywhere, so
    /// <c>ClientCallPlanner.BuildKiotaConvention</c>'s <c>Api.Status[{id}].GetAsync</c> shape went
    /// unexercised end to end, which is exactly how the CS0618-with-no-pragma defect this addition
    /// covers went unnoticed.
    /// </para>
    /// <para>
    /// <c>[typed-path-parameters]</c>: at the time [finding-3] added it, this overload was the one
    /// InTest's generated call actually bound — <c>FixtureParameter</c> returns
    /// <see cref="string"/>, spliced bare — with <c>this[Guid position]</c> alongside it present
    /// only so this fake matched the real shape's two-overload indexer, unused by anything InTest
    /// generated. That is now reversed: <c>TestPlanBuilder.ResolvePathParameterKind</c> resolves a
    /// uuid-formatted path parameter to <c>PathParameterKind.Guid</c>, and
    /// <c>TemplateRenderer.WrapForClientCall</c> wraps the spliced value in <c>Guid.Parse(...)</c>
    /// before it reaches the indexer — so <c>this[Guid position]</c> is the overload
    /// <c>GeneratedClientRoutedSuccessCaseWithAUuidPathParameterCompilesAgainstTheTypedIndexer</c>
    /// now actually exercises, and <c>this[string position]</c> sits unused, kept only so this
    /// fake still matches the real, still-two-overload shape a real kiota client carries until its
    /// next major version actually removes the deprecated one (this plan's risk section).
    /// </para>
    /// </summary>
    public const string FakeOrdersApiClient = """
    using System.Net.Http;
    using System.Text.Json;

    namespace Stub.ApiTests;

    public sealed class FakeRequestConfiguration;

    public sealed class FakeOrdersApiClient(HttpClient httpClient)
    {
        public FakeApiRequestBuilder Api { get; } = new(httpClient);

        // [warn-on-swallowed-exception]: mirrors the reviewer's exact double-call failure mode a
        // client-map.json override can produce — one call that genuinely reaches the wire (so
        // ResponseCaptureHandler captures it, same as Api.Status.GetAsync always does) followed by
        // a second failure that never reaches the wire at all (here, a synthetic throw standing in
        // for a serialization error, a null argument, or an adapter misconfiguration). Used only by
        // GeneratedSuiteExecutionTests.GeneratedClientRoutedCaseWarnsWhenAnExceptionIsSwallowedAfterACapture,
        // via a client-map.json override naming this method instead of the getStatus convention.
        public async Task<FakeStatusResult> GetStatusThenThrowAsync(CancellationToken cancellationToken = default)
        {
            await Api.Status.GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("simulated failure after the first call already captured a response");
        }
    }

    public sealed class FakeApiRequestBuilder(HttpClient httpClient)
    {
        public FakeStatusRequestBuilder Status { get; } = new(httpClient);
        public FakePingRequestBuilder Ping { get; } = new(httpClient);

        // [success-only]-and-[mixed-idiom-execution]: GET /api/secure, a secured operation whose
        // Success case is client-routed while its 401/403 siblings (built from TestPlanBuilder's
        // separate PlanAuthCases helper, never touched by ClientCallPlanner per [success-only])
        // stay raw HTTP in the very same generated class. Used only by
        // GeneratedSuiteExecutionTests.GeneratedMixedIdiomClassRunsTheClientRoutedSuccessCaseAlongsideItsRawHttpAuthSiblings
        // — every other test in this file that registers FakeOrdersApiClient exercises an
        // unsecured operation, so nothing before that test ever proved AuthHandler's token
        // actually reaches a client-routed call the way it already does for a raw-HTTP one
        // (AuthCasesReceiveRealStatusesOverTheWireAndSuccessCasesStillPass).
        public FakeSecureRequestBuilder Secure { get; } = new(httpClient);
    }

    /// <summary>[stage-3b]: GET /api/ping, a bodiless-204 operation — see FakeOrdersApiClient's own
    /// doc comment (GoldenTypedClientSources.FakeOrdersApiClient) for why this returns a bare
    /// Task rather than Task&lt;T&gt;.</summary>
    public sealed class FakePingRequestBuilder(HttpClient httpClient)
    {
        public async Task GetAsync(
            Action<FakeRequestConfiguration>? requestConfiguration = default, CancellationToken cancellationToken = default)
        {
            using var response = await httpClient.GetAsync("/api/ping", cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new FakeApiException((int)response.StatusCode);
            }

            // No stream to read at all — unlike FakeStatusRequestBuilder.GetAsync, a bodiless
            // operation has nothing for this fake to deserialize. A genuinely 2xx-but-mismatched
            // status (204 declared, 200 actually returned) still reaches ApiResponseAssertions.
            // ShouldMatchCapturedStatusAsync via ResponseCaptureHandler's already-stashed capture —
            // this method's own IsSuccessStatusCode check has no way to see the mismatch, on
            // purpose: mirroring a real generated client, which would not either.
        }
    }

    /// <summary>[mixed-idiom-execution]: GET /api/secure, mirroring FakeStatusRequestBuilder.GetAsync's
    /// own body (same non-2xx exception, same ReadAsStreamAsync deserialization) — the golden test
    /// that uses this needs no schema-violation variant of its own, so this builder returns the
    /// same FakeStatusResult shape rather than inventing a parallel result type. Deliberately no
    /// Authorization header of its own: [client-rides-the-api-pipeline] means AuthHandler, already
    /// attached to InTestClients.Api, is what has to put the bearer token on this request for it to
    /// reach GoldenApiStub.HandleSecureResource's 200 arm — the same mechanism the raw-HTTP
    /// GetSecureResource_Contract case relied on before this operation's client got configured, now
    /// proven to reach a client-routed call too.</summary>
    public sealed class FakeSecureRequestBuilder(HttpClient httpClient)
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public async Task<FakeStatusResult> GetAsync(
            Action<FakeRequestConfiguration>? requestConfiguration = default, CancellationToken cancellationToken = default)
        {
            using var response = await httpClient.GetAsync("/api/secure", cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new FakeApiException((int)response.StatusCode);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var result = await JsonSerializer.DeserializeAsync<FakeStatusResult>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return result ?? new FakeStatusResult();
        }
    }

    public sealed class FakeStatusRequestBuilder(HttpClient httpClient)
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public async Task<FakeStatusResult> GetAsync(
            Action<FakeRequestConfiguration>? requestConfiguration = default, CancellationToken cancellationToken = default)
        {
            using var response = await httpClient.GetAsync("/api/status", cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new FakeApiException((int)response.StatusCode);
            }

            // Deliberately ReadAsStreamAsync, never ReadAsStringAsync — see FakeStatusClient's own
            // doc comment (GoldenTypedClientSources.FakeStatusClient) for the full account.
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var result = await JsonSerializer.DeserializeAsync<FakeStatusResult>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return result ?? new FakeStatusResult();
        }

        // [finding-3]: word-for-word the same deprecation text a real kiota 1.34.1 item builder's
        // this[string] overload carries (OrdersItemRequestBuilder.cs, CustomersItemRequestBuilder.cs
        // -- see GoldenTypedClientSources.FakeOrdersApiClient's own doc comment and this plan's risk
        // section). [typed-path-parameters]: unused by anything InTest generates today -- a
        // uuid-formatted path parameter now converts through Guid.Parse(...) before reaching the
        // indexer (TemplateRenderer.WrapForClientCall), so this deprecated overload never binds.
        // Kept, still Obsolete, so this fake still matches the real, still-two-overload shape a
        // real kiota client carries until its own next major version removes it.
        [Obsolete("This indexer is deprecated and will be removed in the next major version. Use the one with the typed parameter instead.")]
        public FakeStatusItemRequestBuilder this[string position] => new(httpClient, position);

        // [typed-path-parameters]: the typed overload real kiota output carries alongside the
        // deprecated one -- and, since that change, the one a uuid-formatted path parameter's
        // client-routed case now actually binds (see this[string]'s own comment above). Before
        // that change this overload was present only so this fake's indexer shape matched the
        // real one measured (two overloads, not one) without anything InTest generated reaching
        // it; GeneratedClientRoutedSuccessCaseWithAUuidPathParameterCompilesAgainstTheTypedIndexer
        // is the golden proof that it now does.
        public FakeStatusItemRequestBuilder this[Guid position] => new(httpClient, position.ToString());
    }

    /// <summary>[finding-3]: the item builder a path-parameter client-routed case's indexer call
    /// returns -- GET /api/status/{id}, mirroring FakeStatusRequestBuilder.GetAsync's own body
    /// (same deserialization shape, same non-2xx exception) against the id the indexer captured.
    /// </summary>
    public sealed class FakeStatusItemRequestBuilder(HttpClient httpClient, string id)
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public async Task<FakeStatusResult> GetAsync(
            Action<FakeRequestConfiguration>? requestConfiguration = default, CancellationToken cancellationToken = default)
        {
            using var response = await httpClient.GetAsync($"/api/status/{id}", cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new FakeApiException((int)response.StatusCode);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var result = await JsonSerializer.DeserializeAsync<FakeStatusResult>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return result ?? new FakeStatusResult();
        }
    }

    /// <summary>Mimics a generator's own strongly-typed response model for getStatus's 200
    /// response (the Status schema: required "state", string). State is deliberately nullable,
    /// not `required` — see FakeStatusClient's own doc for why.</summary>
    public sealed class FakeStatusResult
    {
        public string? State { get; set; }
    }

    /// <summary>Stands in for the generator-specific exception every real typed client throws on
    /// a non-2xx response — see FakeStatusClient.FakeApiException's own doc comment.</summary>
    public sealed class FakeApiException(int statusCode)
        : Exception($"FakeOrdersApiClient: request failed with status {statusCode}.")
    {
        public int StatusCode { get; } = statusCode;
    }
    """;
}

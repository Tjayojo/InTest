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
}

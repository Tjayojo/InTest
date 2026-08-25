namespace InTest.Runtime;

/// <summary>
/// The raw HTTP facts <see cref="ResponseCaptureHandler"/> stashes before a client-routed
/// request's response ever reaches an adopter's typed client for deserialization —
/// [capture-not-deserialize], the whole viability of routing a generated test's call through a
/// team's own Kiota/NSwag/Refit client rather than by-hand <c>HttpRequestMessage</c> construction.
/// <see cref="SchemaBundle.Validate"/> needs the exact bytes the API actually sent; a typed client
/// deserializes and discards them on its way to producing a strongly-typed result, so nothing
/// downstream of the client can recover them. This struct is what <see cref="ApiResponseAssertions.ShouldMatchCapturedContractAsync"/>
/// validates against instead — the same raw text a hand-written test would have gotten straight off
/// <c>HttpResponseMessage.Content</c>.
/// </summary>
/// <param name="Status">The numeric HTTP status code the API returned.</param>
/// <param name="Body">
/// The raw response body, decoded as UTF-8 text — the same value
/// <c>ApiResponseAssertions.ReadBodyAsync</c> would have produced via <c>ReadAsStringAsync</c> for
/// a raw-HTTP case. See <see cref="ResponseCaptureHandler"/>'s own doc for what this means when the
/// response actually carries a <c>Content-Encoding</c> the pipeline never decompresses.
/// </param>
/// <param name="RequestMethod">
/// The outgoing request's HTTP method, or null when unavailable. Mirrors
/// <c>ApiResponseAssertions.Failure</c>'s existing tolerance for a missing
/// <c>HttpResponseMessage.RequestMessage</c> — a captured response should degrade the same way a
/// raw one already does, not introduce a second failure vocabulary for the same absence.
/// </param>
/// <param name="RequestUri">The outgoing request's URI, or null under the same circumstance.</param>
public readonly record struct CapturedResponse(int Status, string Body, string? RequestMethod, string? RequestUri);

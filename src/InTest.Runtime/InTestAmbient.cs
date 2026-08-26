namespace InTest.Runtime;

/// <summary>
/// Ambient per-test state. This must be AsyncLocal rather than a DI-scoped service:
/// handlers created by IHttpClientFactory are not scoped to the DI scope, so a scoped
/// service cannot be injected into one.
/// </summary>
public static class InTestAmbient
{
    public static readonly AsyncLocal<string?> TestId = new();

    /// <summary>
    /// The identity <see cref="AuthHandler"/> requests a token for. Same reason as
    /// <see cref="TestId"/>: AsyncLocal, not DI-scoped, because handlers built by
    /// IHttpClientFactory are not scoped to the DI container's scope.
    /// <para>
    /// <c>ApiTestCore.BeginTest</c> sets this to the resolved <c>Default</c> slot —
    /// <c>Identities[0]</c>, or <see cref="InTestIdentities.None"/> when the registered provider
    /// advertises none — and <c>ApiTestCore.EndTest</c> clears it, exactly as both already do for
    /// <see cref="TestId"/>. The MSTest adapter, <c>ApiTestBase</c>, is what actually triggers
    /// those two calls, from its <c>[TestInitialize]</c> and <c>[TestCleanup]</c>-attributed
    /// methods respectively — <c>BeginTest</c>/<c>EndTest</c> are the neutral bodies that do the
    /// work; the MSTest attributes are only the adapter's hook for calling them at the right
    /// point in a test's lifecycle. A generated auth case overrides this value before sending its
    /// request to select a different slot (v1-c decision 7): the wrong-scope 403 case sets
    /// <c>Identities[1]</c>, the 401 case sets <see cref="InTestIdentities.None"/>.
    /// </para>
    /// <para>
    /// Null outside any test scope — fixtures and <c>AssemblyInitialize</c> issue requests
    /// through <c>InTestClients.Api</c> before <c>ApiTestCore.BeginTest</c> has ever run.
    /// <see cref="AuthHandler"/> treats null the same as "no override" and forwards it as-is to
    /// <see cref="ITestTokenProvider.GetTokenAsync"/>'s own <c>identity</c> parameter, which is
    /// already documented to mean the provider's default.
    /// </para>
    /// </summary>
    public static readonly AsyncLocal<string?> Identity = new();

    /// <summary>
    /// The most recent response <see cref="ResponseCaptureHandler"/> observed on
    /// <see cref="InTestClients.Api"/> for the current test — AsyncLocal for the same reason as
    /// <see cref="TestId"/> and <see cref="Identity"/>: <see cref="ResponseCaptureHandler"/> is
    /// built by <c>IHttpClientFactory</c>, so it is not scoped to the DI container's scope and
    /// cannot receive or report a per-test value any other way.
    /// <para>
    /// <b>Why this is <see cref="AsyncLocal{T}"/> of a mutable <see cref="CapturedResponseSlot"/>
    /// rather than of <see cref="CapturedResponse"/> itself — confirmed by direct experiment, not
    /// reasoned about, because the naive shape silently does not work.</b> The intuitive design —
    /// <c>ResponseCaptureHandler.SendAsync</c> doing <c>LastCapturedResponse.Value = new
    /// CapturedResponse(...)</c> directly, then a generated test method reading that same static
    /// property after its <c>await client.SomeCall()</c> returns — was built and measured first,
    /// and it does not work: <see cref="AsyncLocal{T}"/> reassignments made inside a nested,
    /// genuinely-suspending <c>await</c> are isolated to that nested call's own continuation and are
    /// reverted the moment control returns to the awaiting caller, by the same
    /// <c>ExecutionContext</c> capture/restore mechanism that (correctly, and by design) already
    /// makes <see cref="TestId"/> and <see cref="Identity"/> flow <em>downward</em> from
    /// <c>ApiTestCore.BeginTest</c> into every handler a test's requests pass through. That downward
    /// flow is not in question — it is exactly what makes this field reachable inside
    /// <see cref="ResponseCaptureHandler"/> at all. The direction that fails is the one this field
    /// specifically needs: a value set <em>deep inside</em> an awaited call, read back by the
    /// <em>caller</em> once that call returns. Verified with two isolated repros (a bare nested
    /// <c>async</c> method, and the exact <see cref="System.Net.Http.HttpMessageInvoker"/>-over-
    /// <see cref="DelegatingHandler"/> shape this handler actually runs in): both showed the naive
    /// reassignment reverting to its prior value once the outer <c>await</c> returned.
    /// </para>
    /// <para>
    /// The fix that <em>was</em> confirmed to work, by the same direct-experiment standard: flow a
    /// mutable reference cell (<see cref="CapturedResponseSlot"/>) downward via this
    /// <see cref="AsyncLocal{T}"/> — exactly the direction that already works — and have
    /// <see cref="ResponseCaptureHandler"/> mutate the cell's own <see cref="CapturedResponseSlot.Value"/>
    /// field rather than ever reassigning <see cref="LastCapturedResponse"/>'s <c>Value</c> itself.
    /// Mutating an already-shared reference type is ordinary heap mutation, independent of
    /// <c>ExecutionContext</c> propagation entirely, so it survives the same await-return boundary
    /// that a plain <see cref="AsyncLocal{T}"/> reassignment does not.
    /// </para>
    /// <para>
    /// <c>ApiTestCore.BeginTest</c> assigns a fresh <see cref="CapturedResponseSlot"/> here — not
    /// merely clears a stale one — so a test that makes no client-routed call can never observe a
    /// previous test's leftover capture purely by construction (a brand-new object holds nothing
    /// yet), and <c>ApiTestCore.EndTest</c> clears this back to null afterward, exactly as both
    /// already do for <see cref="TestId"/> and <see cref="Identity"/>.
    /// </para>
    /// <para>
    /// Public and directly nameable (not merely reachable through <c>ApiTestCore.LastCapturedResponse</c>)
    /// because the pinned <c>try</c>/exception-filter/<c>catch</c> the mstest-class template emits
    /// around a client-routed call must read this slot directly rather than the throwing
    /// <c>ApiTestCore.LastCapturedResponse</c> property — see that property's own doc for why using
    /// the throwing property there would replace a genuine
    /// <c>[client-rides-the-api-pipeline]</c> authority-mismatch message with the unrelated "nothing
    /// was captured" one. That filter must therefore test <c>InTestAmbient.LastCapturedResponse.Value?.Value
    /// is null</c> — both <c>?.</c>s, one for "no slot exists at all" (no test scope active) and one
    /// for "a slot exists but nothing has been captured into it yet" — not the single-<c>Value</c>
    /// shape the original plan document sketched, which predates this task's own discovery of why
    /// that shape cannot work.
    /// </para>
    /// <para>
    /// The <see cref="AsyncLocal{T}"/> itself is null before <c>ApiTestCore.BeginTest</c> has run
    /// for the current test and after <c>ApiTestCore.EndTest</c> has cleared it — including for
    /// every request issued during <c>AssemblyInitialize</c>/<c>AssemblyCleanup</c> (fixtures,
    /// readiness), when no test is in scope at all. <see cref="ResponseCaptureHandler"/> treats a
    /// null slot the same way <see cref="AuthHandler"/> already treats a null
    /// <see cref="Identity"/> override: nothing to do, not an error.
    /// </para>
    /// </summary>
    public static readonly AsyncLocal<CapturedResponseSlot?> LastCapturedResponse = new();
}

/// <summary>
/// The mutable reference cell <see cref="InTestAmbient.LastCapturedResponse"/> actually flows —
/// see that field's own doc for the direct-experiment evidence behind why a bare
/// <see cref="AsyncLocal{T}"/> of <see cref="CapturedResponse"/> itself cannot do this job, and why
/// this indirection is not optional ceremony.
/// </summary>
public sealed class CapturedResponseSlot
{
    /// <summary>
    /// Null until <see cref="ResponseCaptureHandler"/> mutates it — this is the field
    /// <see cref="ResponseCaptureHandler"/> writes and every reader (<c>ApiTestCore.LastCapturedResponse</c>,
    /// a generated case's exception filter) reads, rather than ever reassigning the
    /// <see cref="AsyncLocal{T}"/> that carries this object.
    /// </summary>
    public CapturedResponse? Value { get; set; }
}

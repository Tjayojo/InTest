namespace InTest.Runtime;

/// <summary>
/// Which identity a generated auth case authenticates as (v1-c decision 7) — never a literal
/// identity name, since the CLI generates this code long before any adopter has written an
/// <see cref="ITestTokenProvider"/> and cannot know one. <see cref="ApiTestCore.UseIdentity"/>
/// resolves a slot to a concrete identity (or the no-token sentinel) at the point a generated
/// test overrides <see cref="InTestAmbient.Identity"/>, immediately before building its request.
/// </summary>
public enum IdentitySlot
{
    /// <summary>The ordinary authenticated identity — <c>Identities[0]</c>, or
    /// <see cref="InTestIdentities.None"/> when the provider advertises none. Already the
    /// ambient value every test starts with (<c>ApiTestBase.ApiTestInitialize</c>), so a case in
    /// this slot never needs to call <see cref="ApiTestCore.UseIdentity"/> at all — it is
    /// carried on <c>TestCasePlan</c> only so the template has a value that means "no
    /// override."</summary>
    Default,

    /// <summary>Some other identity than <see cref="Default"/> — <c>Identities[1]</c> — for the
    /// wrong-scope 403 case. Selecting this slot requires
    /// <c>ApiTestBase.RequireMultipleIdentities</c> to have already confirmed a second identity
    /// exists; nothing here re-checks that.</summary>
    Secondary,

    /// <summary>Send no <c>Authorization</c> header at all — <see cref="InTestIdentities.None"/>,
    /// independent of whatever the registered provider advertises. The no-token 401 case's
    /// entire mechanism.</summary>
    None
}

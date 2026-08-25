namespace InTest.Runtime;

/// <summary>
/// The sentinel meaning "send no <c>Authorization</c> header at all" — the 401 auth case's
/// entire mechanism (v1-c decision 3). The 401 case does not send a bad or expired token; it
/// sends none, because that is what an anonymous caller actually does. <see cref="AuthHandler"/>
/// checks the ambient identity against this value before ever calling
/// <see cref="ITestTokenProvider.GetTokenAsync"/>, so a provider is never asked to issue a token
/// for it.
/// <para>
/// Not a real identity name: no <see cref="ITestTokenProvider.Identities"/> list should ever
/// contain this string, and nothing in this codebase treats it as a lookup key into that list —
/// it only ever flows through <see cref="InTestAmbient.Identity"/> as a value <see cref="AuthHandler"/>
/// recognises directly.
/// </para>
/// </summary>
public static class InTestIdentities
{
    public const string None = "__intest-no-token__";
}

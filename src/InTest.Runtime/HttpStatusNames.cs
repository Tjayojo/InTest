using System.Net;

namespace InTest.Runtime;

/// <summary>
/// Maps a numeric HTTP status to the name InTest uses for it, in failure messages here and in
/// generated code on the CLI side.
/// <para>
/// <b>This table is duplicated in <c>InTest.Cli</c>'s <c>Naming/HttpStatusExpression.cs</c> and the
/// two must agree.</b> That is deliberate, not an oversight: <c>InTest.Cli</c> takes no reference to
/// <c>InTest.Runtime</c> — it generates code *against* the runtime rather than consuming it — and
/// coupling the two packages to share six strings would be a worse trade than duplicating them.
/// <c>InTest.Architecture.Tests</c>' <c>HttpStatusNameCouplingTests</c> makes the coupling
/// mechanical by reading both files as text, the same way <c>PackageVersionCouplingTests</c> guards
/// the deliberate three-way package-version duplication.
/// </para>
/// <para>
/// <b>Why an explicit table rather than <c>((HttpStatusCode)status).ToString()</c>.</b> Six values
/// carry two enum members each, and <c>ToString()</c>'s choice between them is not a documented
/// contract — it falls out of metadata declaration order. For 307 it returns
/// <c>RedirectKeepVerb</c>, the legacy <c>WebRequest</c>-era name that no OpenAPI document uses,
/// rather than <c>TemporaryRedirect</c>. Since generated output is compared byte-for-byte against a
/// golden file, deriving names from <c>ToString()</c> would leave that output hostage to an ordering
/// nobody promises to keep. All six are listed even though five happen to agree with
/// <c>ToString()</c> today, so the table reads as a decision rather than as a patch.
/// </para>
/// </summary>
internal static class HttpStatusNames
{
    private static readonly Dictionary<int, string> Preferred = new()
    {
        [300] = "MultipleChoices",
        [301] = "MovedPermanently",
        [302] = "Found",
        [303] = "SeeOther",
        [307] = "TemporaryRedirect",
        [422] = "UnprocessableEntity",
    };

    /// <summary>
    /// The name for <paramref name="status"/>, or null when .NET names no member for it — callers
    /// then use the bare number. Null is the normal case for vendor ranges, not an error.
    /// </summary>
    internal static string? For(int status)
    {
        if (Preferred.TryGetValue(status, out var preferred))
        {
            return preferred;
        }

        return Enum.IsDefined(typeof(HttpStatusCode), status)
            ? ((HttpStatusCode)status).ToString()
            : null;
    }
}

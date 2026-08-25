using System.Security.Cryptography;
using System.Text;

namespace InTest.Runtime;

/// <summary>Correlation identifiers. Framework-neutral: must not reference any test framework.</summary>
public static class InTestId
{
    /// <summary>Total cap. TestId travels in an HTTP header, not in entity names, so it is
    /// looser than RunId's 40-character cap.</summary>
    public const int MaxLength = 120;

    private const int HashLength = 6;

    /// <summary>
    /// Combines a run identifier with a test display name into an ASCII, collision-free id.
    /// HttpClient throws on non-ASCII header values, so transliteration is mandatory; a short
    /// stable hash is appended whenever transliteration loses information, so that variation
    /// cases differing only in non-ASCII content remain distinguishable.
    /// </summary>
    public static string ForTest(string runId, string? displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        displayName ??= string.Empty;

        var (slug, lossy) = Slugify(displayName);
        var suffix = lossy ? "-h" + ShortHash(displayName) : string.Empty;

        var budget = MaxLength - runId.Length - 1 - suffix.Length;
        if (budget < 1)
        {
            return runId[..Math.Min(runId.Length, MaxLength)];
        }
        if (slug.Length > budget)
        {
            slug = slug[..budget].TrimEnd('-');
        }

        return string.Concat(runId, "-", slug, suffix);
    }

    private static (string Slug, bool Lossy) Slugify(string value)
    {
        var sb = new StringBuilder(value.Length);
        var lossy = false;
        var lastWasHyphen = false;

        foreach (var ch in value)
        {
            char mapped;
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                mapped = ch;
            }
            else if (ch is >= 'A' and <= 'Z')
            {
                mapped = char.ToLowerInvariant(ch);
            }
            else
            {
                // Non-alphanumeric ASCII collapses to a separator without loss of identity;
                // anything outside ASCII does lose identity and must be recorded as lossy.
                if (ch >= 128)
                {
                    lossy = true;
                }
                mapped = '-';
            }

            if (mapped == '-')
            {
                if (lastWasHyphen || sb.Length == 0)
                {
                    continue;
                }
                lastWasHyphen = true;
            }
            else
            {
                lastWasHyphen = false;
            }

            sb.Append(mapped);
        }

        var slug = sb.ToString().TrimEnd('-');
        if (slug.Length == 0) { slug = "test"; lossy = true; }
        return (slug, lossy);
    }

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(bytes)[..HashLength];
    }
}

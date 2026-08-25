using System.Security.Cryptography;
using System.Text;

namespace InTest.Runtime;

/// <summary>Run identity. Framework-neutral: must not reference any test framework.</summary>
public static class RunId
{
    /// <summary>Run ids land in entity names, email local-parts and external reference ids,
    /// so the cap is tight.</summary>
    public const int MaxLength = 40;

    private const int PrefixMaxLength = 12;

    public static string Create(RunIdEnvironment environment, DateTimeOffset utcNow, string? configuredPrefix)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var prefix = SanitizePrefix(configuredPrefix ?? DerivePrefix(environment));
        var timestamp = utcNow.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'");
        var entropy = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));

        var id = $"{prefix}-{timestamp}-{entropy}";
        return id.Length <= MaxLength ? id : $"{prefix[..Math.Max(1, prefix.Length - (id.Length - MaxLength))]}-{timestamp}-{entropy}";
    }

    /// <summary>Convenience overload for production use.</summary>
    public static string Create(string? configuredPrefix)
        => Create(RunIdEnvironment.Current(), DateTimeOffset.UtcNow, configuredPrefix);

    private static string DerivePrefix(RunIdEnvironment env)
    {
        // Azure DevOps: Build.BuildId is unique; Build.BuildNumber is a display string that can repeat.
        if (env.Has("TF_BUILD") && env.Has("BUILD_BUILDID"))
        {
            return "ci" + env.Get("BUILD_BUILDID");
        }
        if (env.Has("GITHUB_ACTIONS") && env.Has("GITHUB_RUN_ID"))
        {
            return "ci" + env.Get("GITHUB_RUN_ID");
        }
        return env.Has("CI") ? "ci" : env.UserName;
    }

    private static string SanitizePrefix(string value)
    {
        var sb = new StringBuilder(value.Length);
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
            if (sb.Length == PrefixMaxLength)
            {
                break;
            }
        }

        var prefix = sb.ToString().TrimEnd('-');
        return prefix.Length == 0 ? "local" : prefix;
    }
}

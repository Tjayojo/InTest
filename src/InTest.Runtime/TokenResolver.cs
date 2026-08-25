using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace InTest.Runtime;

/// <summary>
/// Resolves <c>{{...}}</c> runtime tokens inside one fixture value, per §10's resolution-timing
/// table. <c>{{config:...}}</c> and <c>{{secret:...}}</c> read <see cref="IConfiguration"/>, which
/// <c>TestHost</c> already builds once at <c>AssemblyInitialize</c> — resolving through it needs no
/// extra caching here, since the read itself is already "once per run, after configuration build".
/// <c>{{runId}}</c> is a fixed string handed in at construction, so it is identical for the life of
/// this resolver. Only <c>{{utcNow}}</c> must vary per call: it invokes the clock (real time in
/// production, injectable for tests) every time <see cref="Resolve"/> runs, never once and reused —
/// see <c>FixtureStore.ResolvedBody</c>, which relies on that to differ between requests.
/// <c>{{fixture:...}}</c> resolves from an immutable snapshot of published values handed in at
/// construction; a miss throws <see cref="FixtureResolutionException"/>, deliberately not the
/// lifecycle exception type — see that type's doc for why. <c>TestHost</c> constructs this after
/// <c>FixtureRunner.RunAsync</c> has seeded, passing every key fixtures published, so
/// <c>{{fixture:...}}</c> resolves against the real set rather than an empty one.
/// </summary>
public sealed class TokenResolver(
    IConfiguration configuration,
    string runId,
    Func<DateTimeOffset>? utcNowProvider = null,
    IReadOnlyDictionary<string, string>? publishedFixtureValues = null)
{
    private const string SupportedTokens = "{{config:...}}, {{secret:...}}, {{runId}}, {{utcNow}}, {{fixture:...}}";

    private static readonly Regex TokenPattern = new(@"\{\{(?<token>[^{}]+)\}\}", RegexOptions.Compiled);

    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly string _runId = runId ?? throw new ArgumentNullException(nameof(runId));
    private readonly Func<DateTimeOffset> _utcNow = utcNowProvider ?? (() => DateTimeOffset.UtcNow);

    /// <summary>
    /// Copied rather than referenced, for two reasons. The one that matters for correctness:
    /// <c>new(source, StringComparer.Ordinal)</c> normalises the comparer regardless of what the
    /// caller built the dictionary with, so a caller that handed in an
    /// <see cref="StringComparer.OrdinalIgnoreCase"/> dictionary cannot make
    /// <c>{{fixture:SeededCustomer.Id}}</c> match a key actually published as
    /// <c>seededCustomer.id</c> — key lookup here is always case-sensitive, not whatever the
    /// caller's dictionary happened to be built with. Secondarily: <c>publishedFixtureValues</c>
    /// stays in scope for the life of this instance, same as <c>configuration</c> and
    /// <c>runId</c> above, so without a copy a caller still holding the original mutable
    /// dictionary could change what this resolver sees as published after construction.
    /// </summary>
    private readonly Dictionary<string, string> _publishedFixtures = publishedFixtureValues is null
        ? new Dictionary<string, string>(StringComparer.Ordinal)
        : new Dictionary<string, string>(publishedFixtureValues, StringComparer.Ordinal);

    /// <summary>
    /// Resolves every <c>{{...}}</c> token in <paramref name="value"/>. <paramref name="fileName"/>
    /// is used only to identify the fixture in an error message — it never becomes part of a
    /// resolved value.
    /// </summary>
    public string Resolve(string value, string fileName)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(fileName);

        return TokenPattern.Replace(value, match => ResolveToken(match.Groups["token"].Value, fileName));
    }

    private string ResolveToken(string token, string fileName)
    {
        if (token == "runId")
        {
            return _runId;
        }
        if (token == "utcNow")
        {
            return _utcNow().ToString("O");
        }

        if (token.StartsWith("config:", StringComparison.Ordinal))
        {
            return ResolveConfig(token["config:".Length..], fileName);
        }
        if (token.StartsWith("secret:", StringComparison.Ordinal))
        {
            return ResolveConfig(token["secret:".Length..], fileName);
        }

        if (token.StartsWith("fixture:", StringComparison.Ordinal))
        {
            return ResolveFixture(token["fixture:".Length..], fileName);
        }

        throw new FixtureResolutionException(
            $"Unknown token '{{{{{token}}}}}' in '{fileName}'. Supported tokens: {SupportedTokens}.");
    }

    private string ResolveConfig(string key, string fileName)
    {
        var value = _configuration[key];
        if (value is null)
        {
            throw new FixtureResolutionException(
            $"Configuration key '{key}' required by '{fileName}' is not set.");
        }
        return value;
    }

    /// <summary>
    /// Looks up <paramref name="key"/> in the published-fixture snapshot. On failure the message
    /// names both halves, per §10: the key that was requested, so a typo is obvious, and every
    /// key actually published, ordinal-sorted so the list reads identically from one run to the
    /// next regardless of which fixture happened to publish first.
    /// </summary>
    private string ResolveFixture(string key, string fileName)
    {
        if (_publishedFixtures.TryGetValue(key, out var value))
        {
            return value;
        }

        var available = _publishedFixtures.Count == 0
            ? "(none)"
            : string.Join(", ", _publishedFixtures.Keys.Order(StringComparer.Ordinal));

        throw new FixtureResolutionException(
            $"Fixture key '{key}' required by '{fileName}' is not published. Published keys: {available}. " +
            "Check the key name for a typo, or confirm the fixture that publishes it is registered and " +
            "its AppliesTo includes the active profile.");
    }
}

using System.Text;

namespace InTest.Runtime;

/// <summary>URL composition. Framework-neutral: must not reference any test framework.</summary>
public static class InTestUrl
{
    /// <summary>
    /// Returns an absolute base URI guaranteed to end in '/'. Without the trailing slash,
    /// <c>new Uri(base, relative)</c> silently discards the last path segment.
    /// </summary>
    public static Uri NormalizeBase(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("Base URL must not be null or whitespace.", nameof(baseUrl));
        }

        var trimmed = baseUrl.Trim();
        if (!trimmed.EndsWith('/'))
        {
            trimmed += "/";
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"Base URL '{baseUrl}' is not an absolute URI.", nameof(baseUrl));
        }

        return uri;
    }

    /// <summary>
    /// Builds a relative path from an OpenAPI path template, substituting '{placeholder}'
    /// segments left to right. The leading '/' that OpenAPI paths always carry is stripped,
    /// because a leading slash resets resolution to the host root.
    /// </summary>
    public static string Build(string pathTemplate, params string[] values)
    {
        ArgumentNullException.ThrowIfNull(pathTemplate);
        ArgumentNullException.ThrowIfNull(values);

        var result = new StringBuilder(pathTemplate.Length + 16);
        var valueIndex = 0;
        var i = 0;

        while (i < pathTemplate.Length)
        {
            var open = pathTemplate.IndexOf('{', i);
            if (open < 0) { result.Append(pathTemplate, i, pathTemplate.Length - i); break; }

            var close = pathTemplate.IndexOf('}', open);
            if (close < 0)
            {
                throw new ArgumentException($"Unterminated placeholder in path template '{pathTemplate}'.", nameof(pathTemplate));
            }

            result.Append(pathTemplate, i, open - i);

            if (valueIndex >= values.Length)
            {
                throw new ArgumentException(
                $"Path template '{pathTemplate}' has more placeholders than the {values.Length} value(s) supplied.",
                nameof(values));
            }

            result.Append(Uri.EscapeDataString(values[valueIndex++] ?? string.Empty));
            i = close + 1;
        }

        if (valueIndex != values.Length)
        {
            throw new ArgumentException(
            $"Path template '{pathTemplate}' has {valueIndex} placeholder(s) but {values.Length} value(s) were supplied.",
            nameof(values));
        }

        var path = result.ToString();
        return path.StartsWith('/') ? path[1..] : path;
    }

    /// <summary>
    /// Fails when the configured base URL repeats a path prefix that the spec's own paths
    /// already carry.
    /// <para>
    /// InTest ignores the spec's <c>servers[]</c> block, so the configured base URL takes its
    /// place and operation paths are appended to it. A base of <c>https://host/api/</c> against
    /// paths beginning <c>/api/</c> therefore produces <c>/api/api/…</c>. Every request 404s
    /// against configuration that looks entirely correct, which is why this is detected rather
    /// than documented.
    /// </para>
    /// </summary>
    public static void EnsureNoPrefixDuplication(Uri baseAddress, string? operationPathPrefix)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);

        if (string.IsNullOrWhiteSpace(operationPathPrefix))
        {
            return;
        }

        var baseSegments = Segments(baseAddress.AbsolutePath);
        if (baseSegments.Length == 0)
        {
            return;
        }

        var pathSegments = Segments(operationPathPrefix);
        if (pathSegments.Length == 0)
        {
            return;
        }

        var overlap = Math.Min(baseSegments.Length, pathSegments.Length);
        for (var i = 0; i < overlap; i++)
        {
            if (!string.Equals(baseSegments[i], pathSegments[i], StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        var duplicated = string.Join("/", baseSegments.Take(overlap));

        throw new InvalidOperationException(
            $"Base URL '{baseAddress}' and the spec's operation paths both start with '/{duplicated}', " +
            $"so every request would resolve to '/{duplicated}/{duplicated}/...' and return 404." + Environment.NewLine +
            "The base URL substitutes for the spec's servers[0].url, and operation paths are appended " +
            "to it — so it must not repeat a prefix the paths already carry." + Environment.NewLine +
            $"Set Api:BaseUrl to '{baseAddress.GetLeftPart(UriPartial.Authority)}/' instead.");
    }

    /// <summary>
    /// Builds a query string from name/value pairs, or the empty string when there are none.
    /// Keys are sorted so the same parameters always render in the same order regardless of
    /// dictionary enumeration order, keeping generated URLs stable across runs.
    /// </summary>
    public static string BuildQuery(IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (parameters.Count == 0)
        {
            return string.Empty;
        }

        var pairs = parameters
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}");

        return "?" + string.Join("&", pairs);
    }

    private static string[] Segments(string path)
        => path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

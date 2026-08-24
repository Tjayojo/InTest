using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace InTest.Cli.Spec;

/// <summary>
/// Loads an OpenAPI document. Microsoft.OpenApi 3.10.0 reads Swagger 2.0 and OpenAPI 3.0,
/// 3.1 and 3.2, normalizing dialect differences into one object model — which is what makes
/// a single downstream schema path possible.
/// </summary>
public static class SpecLoader
{
    public static async Task<LoadedSpec> LoadFromTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        ReadResult result;
        try
        {
            result = OpenApiDocument.Parse(text, "json", new OpenApiReaderSettings());
        }
        catch (Exception ex)
        {
            throw new SpecLoadException($"The OpenAPI document could not be parsed: {ex.Message}", ex);
        }

        var document = result.Document
            ?? throw new SpecLoadException("The OpenAPI document could not be parsed: no document was produced.");

        var errors = result.Diagnostic?.Errors;
        if (errors is { Count: > 0 })
        {
            throw new SpecLoadException(
            "The OpenAPI document could not be parsed:" + Environment.NewLine +
            string.Join(Environment.NewLine, errors.Select(e => "  " + e.Message)));
        }

        if (document.Paths is null || document.Paths.Count == 0)
        {
            throw new SpecLoadException("The OpenAPI document declares no operations, so there is nothing to generate.");
        }

        await Task.CompletedTask;
        return new LoadedSpec(document, result.Diagnostic?.SpecificationVersion ?? OpenApiSpecVersion.OpenApi3_0, text);
    }

    /// <summary>
    /// Whether a spec source names a URL rather than a path — the routing question §9's snapshot
    /// turns on. A URL is fetched by <see cref="SpecFetcher"/> and materialized as
    /// <see cref="SpecSnapshot"/>; a path is read straight through
    /// <see cref="LoadFromFileAsync"/>.
    /// <para>
    /// <b>This predicate carries more weight than it used to.</b> Until URL support landed it
    /// only chose which refusal to print, next to a <c>UrlReason</c> constant that apologised for
    /// the capability being absent; both it and that constant were documented as going away
    /// together when §9 was built. What actually happened is that the constant went and this
    /// stayed, promoted to deciding <i>which code path runs</i>. A wrong answer here no longer
    /// produces a differently-worded error — it produces a command reading the wrong document.
    /// </para>
    /// <para>
    /// The prefix test is deliberately narrow, and a general "is this an absolute URI" check is
    /// deliberately <i>not</i> used: <c>Uri.TryCreate("C:/specs/orders.json", UriKind.Absolute, …)</c>
    /// succeeds with scheme <c>file</c>, so the general check would route the single most
    /// ordinary <c>spec.source</c> value on Windows down the fetch path. Only <c>http</c> and
    /// <c>https</c> are treated as URLs, because those are the two schemes
    /// <see cref="SpecFetcher"/> can actually fetch. Anything else with a scheme is tried as a
    /// path, which is what it is. <c>ConfigLoaderTests.LoadsASpecSourceThatIsNotAUrl</c> pins the
    /// false positives this narrowness exists to avoid.
    /// </para>
    /// <para>
    /// Well-formedness is a separate question, asked afterwards by
    /// <see cref="SpecFetcher.TryValidateUrl"/>: <c>https://</c> alone passes this test and is
    /// not a URL anyone can fetch.
    /// </para>
    /// </summary>
    public static bool IsUrl(string source) =>
        source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public static Task<LoadedSpec> LoadFromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            throw new SpecLoadException($"Spec file not found: {path}");
        }

        return LoadFromTextAsync(File.ReadAllText(path), cancellationToken);
    }
}

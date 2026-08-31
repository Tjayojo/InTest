using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using InTest.Cli.Clients;
using InTest.Cli.Configuration;
using InTest.Cli.Coverage;
using InTest.Cli.Fixtures;
using InTest.Cli.Json;
using InTest.Cli.Planning;
using InTest.Cli.Rendering;
using InTest.Cli.Schemas;
using InTest.Cli.Spec;
using Microsoft.OpenApi;

namespace InTest.Cli.Commands;

public static class GenerateCommand
{
    /// <summary>Longest leading run of path segments shared by every generated operation.</summary>
    internal static string CommonPathPrefix(Planning.TestPlan plan)
    {
        var paths = plan.Classes.SelectMany(c => c.Cases).Select(c => c.PathTemplate).ToList();
        if (paths.Count == 0)
        {
            return string.Empty;
        }

        var segmentLists = paths
            .Select(p => p.Split('/', StringSplitOptions.RemoveEmptyEntries))
            .ToList();

        var shortest = segmentLists.Min(s => s.Length);
        var shared = new List<string>();

        for (var i = 0; i < shortest; i++)
        {
            var candidate = segmentLists[0][i];
            if (candidate.StartsWith('{'))
            {
                break;
            }
            if (!segmentLists.All(s => string.Equals(s[i], candidate, StringComparison.OrdinalIgnoreCase)))
            {
                break;
            }
            shared.Add(candidate);
        }

        return shared.Count == 0 ? string.Empty : "/" + string.Join("/", shared);
    }

    // ---- [no-write]'s seam, named before it was written -------------------------------------
    //
    // `--check` must compare without writing. The plan calls out two shapes that both look like
    // "nothing was written" and are not the same guarantee:
    //
    //   (a) write, then diff against a backup, then restore exactly — observable if the process
    //       dies mid-run, but NOT observable by any test that only inspects the project's files
    //       after RunAsync returns: a before/after byte snapshot cannot tell "never wrote" apart
    //       from "wrote and restored exactly", because that is what "restore exactly" means.
    //       Only a mid-run observer (a FileSystemWatcher, a read-only ACL) could see it happen.
    //       An earlier version of this comment claimed the enforcement test below (the "wrote
    //       nothing" tests in GenerateCheckCommandTests) caught this shape "backup or no
    //       backup" — it does not, and a literal write-fresh-content-then-restore-original
    //       mutation at the top of RunCheckAsync proves it: every test in
    //       GenerateCheckCommandTests still passes with that mutation in place.
    //   (b) render to a temp directory and diff directory-to-directory — also invisible to that
    //       same snapshot, because nothing under the project ever changes. This is the shape
    //       [no-write]'s own text warns cannot be caught mechanically: "nothing under the project
    //       changes in that case."
    //
    // Neither shape, then, is ruled out by anything a mutation test can observe — they are ruled
    // out by construction. The seam chosen here: BuildOutputs (below) renders every artefact
    // `generate` owns into an in-memory `IReadOnlyDictionary<string, string>` — project-relative
    // path to file content — from (document, plan, config) alone, with no filesystem access at
    // all. Write mode and check mode then branch on that same map:
    //
    //   * write mode deletes Generated/ wholesale and writes every map entry to disk, exactly as
    //     `generate` always has;
    //   * check mode never calls File.WriteAllText/Bytes, Directory.CreateDirectory,
    //     Directory.Delete, Path.GetTempFileName/GetTempPath, or any other mutating or temp-path
    //     API — it only *reads* whatever already exists on disk (File.Exists,
    //     File.ReadAllBytesAsync) and compares it against the map's content, plus a directory
    //     walk of the existing Generated/ tree to catch a file the map does not mention at all
    //     (the orphan case: an operation dropped from the spec leaves its old .g.cs behind, and a
    //     rendered file that was never dirtied by this run's write path could not report on
    //     because it never runs one).
    //
    // RunCheckAsync's own doc comment enumerates its full reachable call graph. That graph
    // containing no write and no temp-path call — not "check mode's mutations happen to cancel
    // out" — is what makes both (a) and (b) unreachable by construction rather than merely
    // untested. The GenerateCheckCommandTests "wrote nothing" assertions are real and
    // mutation-verified, but for a narrower claim than either shape above: they prove *outcome*
    // (the files present before a --check run are present, byte-identical, after it), which
    // catches a stray extra file or a modified existing file. They cannot and do not prove
    // *shape* — that no write-then-restore or temp-directory render happened along the way.
    private static IReadOnlyDictionary<string, string> BuildOutputs(
        OpenApiDocument document, Planning.TestPlan plan, LoadedConfig config)
    {
        var outputs = new Dictionary<string, string>(StringComparer.Ordinal);

        var renderer = new TemplateRenderer(config.Framework);
        foreach (var testClass in plan.Classes)
        {
            outputs[$"Generated/{testClass.ClassName}.g.cs"] =
                renderer.RenderClass(testClass, config.RootNamespace, config.TestBaseClass, config.Client?.TypeName);
        }

        outputs["Generated/spec-schemas.json"] = SchemaBundleBuilder.Build(document, plan);

        // The prefix every operation path shares, if any. TestHost uses it to detect a base URL
        // that repeats it; otherwise every request 404s and nothing says why.
        var pathManifest = new JsonObject { ["operationPathPrefix"] = CommonPathPrefix(plan) };

        // [capture-is-opt-in]: the key is present, and only ever `true`, when at least one case
        // in this plan resolved a non-null TestCasePlan.ClientCallExpression — never written as
        // `false` and never written at all otherwise, matching InTestRun.ReadSpecPaths's own
        // "absent/false → false" contract on the runtime side (InTestRun.cs). Checked against the
        // plan's own ClientCallExpression, not TemplateRenderer's per-case client_call_expression:
        // the latter can additionally fall back to null for a schema-less case
        // (TemplateRenderer.BuildClientCallExpression's own doc comment), but registering
        // ResponseCaptureHandler is harmless for a case that ends up rendering raw HTTP anyway —
        // Client and every typed client both resolve over the same InTestClients.Api pipeline
        // (ApiTestCore.cs), so the handler simply has nothing extra to do for those cases.
        if (plan.Classes.SelectMany(c => c.Cases).Any(c => c.ClientCallExpression is not null))
        {
            pathManifest["clientCaptureEnabled"] = true;
        }

        // CommittedJsonOptions pins interior line endings to CRLF; see its own doc comment for
        // why. The trailing "+ \"\r\n\"" is the final newline after the closing brace, which
        // indented JSON serialization never emits on its own.
        outputs["Generated/spec-paths.json"] = pathManifest.ToJsonString(CommittedJsonOptions.Value) + "\r\n";

        // Not under Generated/: §5's invariant table is explicit that `generate` also writes
        // coverage-report.json at the project root, and §8/[no-write] both require --check to
        // compare it alongside Generated/ for the same reason — it is the one generated artefact
        // whose content tracks the spec's *shape* rather than the templates, so a spec change
        // that only adds a coverage-report line (a new untagged operation, a synthesized
        // operationId, a newly unevaluatable keyword) would otherwise pass --check silently.
        outputs["coverage-report.json"] = CoverageReport.ToJson(plan);

        return outputs;
    }

    /// <summary>
    /// Relative-path segments are always written with '/' (matching the keys <see
    /// cref="BuildOutputs"/> produces and the paths a message names), converted to the host's
    /// separator only at the point a filesystem API needs one.
    /// </summary>
    private static string ToFullPath(string projectRoot, string relativePath)
        => Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <param name="specTransport">
    /// Test seam, <c>null</c> in production, matching <paramref name="report"/>'s shape and
    /// purpose. Passed through to <see cref="SpecFetcher.FetchAsync"/> so a test can drive the
    /// URL path without a socket — including the tests that prove a socket is <i>never</i> opened
    /// (a transport that throws when invoked is how <c>[no-refetch]</c> is pinned mechanically
    /// rather than by reading the code).
    /// </param>
    public static async Task<int> RunAsync(
        string projectRoot, CancellationToken cancellationToken, TextWriter? report = null,
        bool check = false, HttpMessageHandler? specTransport = null)
    {
        report ??= Console.Out;

        try
        {
            // --project is an argument the adopter typed, so it is refused, not reported as a
            // crash. Without this it reached ConfigLoader.Load's ThrowIfNullOrWhiteSpace and came
            // back through Program's crash floor as "intest: unexpected failure: ArgumentException:
            // ... (Parameter 'projectRoot')" — the right exit code attached to the wrong sentence,
            // naming a C# parameter the adopter never wrote. Same rule and same shape as `init`
            // uses; see CommandArguments.
            if (!CommandArguments.TryRequireValue(
                    projectRoot, "--project", CommandArguments.ProjectRule, out var projectReason))
            {
                Console.Error.WriteLine(projectReason);
                return ExitCode.ToolError;
            }

            // Every intest.json setting this command reads is validated here, before anything is
            // written — including the two that reach generated code as declaration syntax. See
            // ConfigLoader for why that lives in one loader rather than at each read site.
            var config = ConfigLoader.Load(projectRoot);

            // [frozen-axis-becomes-reachable]: checked immediately after the config loads, before
            // anything else — the spec is not even loaded yet. §5 makes the test framework a
            // frozen axis and promises that changing one "fails with a real error"; with only
            // "mstest" ever accepted, that promise was unreachable, because there was no second
            // value to disagree with a scaffolded project's adapter reference. Accepting "xunit"
            // makes it reachable: an adopter could otherwise edit project.framework in intest.json
            // and get a wholesale-rewritten Generated/ (this command deletes and rewrites
            // Generated/ wholesale) targeting a framework the project's own .csproj does not
            // reference — code that will not compile, discovered only after generation, not
            // before it.
            var frameworkMismatch = DetectFrameworkMismatch(projectRoot, config.Framework);
            if (frameworkMismatch is not null)
            {
                Console.Error.WriteLine(frameworkMismatch);
                return ExitCode.ToolError;
            }

            // [exact-match], checked first: before the spec is even loaded, let alone before any
            // output comparison. §8 requires the version check to fail "before comparing any
            // output" — a version mismatch and a real diff must report as 4, not 1, or a stale
            // tool silently narrates its own drift as "the spec changed". Cheapest place to put
            // it is also the correct one: it needs nothing this command has not already loaded.
            //
            // `generate` (no --check) never reaches this branch. §5's command table gives plain
            // `generate` no exit 4 row at all — intestVersion existing is not a promise every
            // regeneration re-validates it, only that `--check` (and, later, `upgrade`) can.
            if (check && config.IntestVersion is not null && config.IntestVersion != CliVersion.Current)
            {
                ReportVersionMismatch(report, config.IntestVersion, CliVersion.Current);
                return ExitCode.VersionMismatch;
            }

            // What "the spec" means depends on the kind of source, and the answer is carried on
            // the config rather than re-derived here (see LoadedConfig.SpecSourceIsUrl).
            // ResolveSpecAsync returns null only when it has already reported a §5 exit code of
            // its own — today the one case is --check with no snapshot yet.
            var resolved = await ResolveSpecAsync(
                projectRoot, config, check, specTransport, report, cancellationToken).ConfigureAwait(false);
            if (resolved.Spec is null)
            {
                return resolved.ExitCode;
            }

            var spec = resolved.Spec;

            // Assembling a ClientPlanningConfig from LoadedConfig.Client (intest.json's own
            // "client" section) plus client-map.json's overrides is this command's job, not
            // ConfigLoader's or ClientCallMap's — see LoadedClientConfig's own doc comment for why
            // the two stay separate types. Null short-circuits to null: no `client` section means
            // TestPlanBuilder.Build's second argument stays the default it already has for every
            // project that predates this feature, so a project with no client config resolves
            // exactly as it always has ([capture-is-opt-in]'s opening premise).
            var clientPlanningConfig = config.Client is { } clientConfig
                ? new ClientPlanningConfig(
                    ResolveClientKind(clientConfig.Kind), clientConfig.TypeName, ClientCallMap.Load(projectRoot).Overrides)
                : null;

            var plan = TestPlanBuilder.Build(spec.Document, clientPlanningConfig);

            // Fixture drift is checked — and reported — before anything is written, in both
            // modes. It is read-only already (DetectFixtureDrift never touches fixtures/), so
            // sharing it between `generate` and `generate --check` costs check mode nothing and
            // keeps one answer to "does this project need fixtures repair" rather than two.
            //
            // The decision this plan asked for: under --check, drift and an output difference
            // are both reported as exit 1, and deliberately so — but they are NOT the same
            // meaning wearing the same number by accident. §5's own text for exit 1 already lists
            // them as two members of one code: "fixture drift, validation failures, `--check`
            // differences". The number is shared by design (both are "real work outstanding that
            // a human must do", §5's exit-1 definition verbatim) while the message is not: drift
            // names the operation and points at `fixtures repair` (unchanged from plain
            // `generate`, below); an output difference names the file and points at `generate`.
            // A CI script branching only on the exit code cannot tell them apart, and does not
            // need to — both mean "do not merge yet" — but a human reading the output always can,
            // because the two messages never share a sentence.
            var drift = DetectFixtureDrift(spec.Document, plan, projectRoot);
            if (drift.Count > 0)
            {
                foreach (var message in drift)
                {
                    report.WriteLine(message);
                }
                report.WriteLine("Run 'intest fixtures repair' to create or update the fixture(s) listed above.");
                return ExitCode.WorkOutstanding;
            }

            var outputs = BuildOutputs(spec.Document, plan, config);

            if (check)
            {
                return await RunCheckAsync(projectRoot, outputs, report, cancellationToken).ConfigureAwait(false);
            }

            var generated = Path.Combine(projectRoot, "Generated");

            if (Directory.Exists(generated))
            {
                Directory.Delete(generated, recursive: true);
            }
            Directory.CreateDirectory(generated);

            foreach (var (relativePath, content) in outputs)
            {
                var fullPath = ToFullPath(projectRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                await File.WriteAllTextAsync(fullPath, content, cancellationToken).ConfigureAwait(false);
            }

            Console.WriteLine($"Generated {plan.Classes.Sum(c => c.Cases.Count)} test(s) across {plan.Classes.Count} class(es).");
            if (plan.Skipped.Count > 0)
            {
                Console.WriteLine($"Skipped {plan.Skipped.Count} operation(s) — see coverage-report.json.");
            }
            if (plan.Notes.Count > 0)
            {
                Console.WriteLine($"Noted {plan.Notes.Count} operation(s) — see coverage-report.json.");
            }

            return ExitCode.Ok;
        }
        catch (ConfigLoadException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ExitCode.ToolError;
        }
        catch (SpecLoadException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ExitCode.ToolError;
        }
        catch (ClientCallMapFormatException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ExitCode.ToolError;
        }
    }

    /// <summary>
    /// Maps <c>LoadedClientConfig.Kind</c>'s already-validated string to <see cref="ClientKind"/>.
    /// The default arm should be unreachable in production: <c>ConfigLoader.ReadOptionalClientConfig</c>
    /// refuses any <c>client.kind</c> other than <c>"kiota"</c>, <c>"nswag"</c> or <c>"refit"</c>
    /// before a <see cref="LoadedClientConfig"/> ever exists, so a fourth string reaching here
    /// would mean that validation itself regressed, not that an adopter wrote bad JSON — hence an
    /// exception naming the defect rather than another <see cref="ConfigLoadException"/> aimed at
    /// the adopter.
    /// </summary>
    private static ClientKind ResolveClientKind(string kind) => kind switch
    {
        "kiota" => ClientKind.Kiota,
        "nswag" => ClientKind.NSwag,
        "refit" => ClientKind.Refit,
        _ => throw new InvalidOperationException(
            $"client.kind \"{kind}\" reached GenerateCommand.ResolveClientKind unvalidated. " +
            "ConfigLoader.ReadOptionalClientConfig should have already refused any value other " +
            "than \"kiota\", \"nswag\" or \"refit\" — this indicates a defect in that validation, " +
            "not bad adopter input.")
    };

    /// <summary>
    /// [frozen-axis-becomes-reachable]: refuses when <c>project.framework</c> disagrees with the
    /// adapter <c>PackageReference</c> already present in the project's own <c>.csproj</c> — the
    /// same comparison <see cref="UpgradeCommand.DetectRuntimeReferenceMismatch"/> already makes
    /// for a version drift, applied here to the framework identity instead. §5's "Frozen vs.
    /// additive axes" table makes the test framework a frozen axis for exactly this reason:
    /// lifecycle, parameterization and parallelism differ between frameworks, and every
    /// hand-written partial targets one of them — a suite cannot be migrated in place by editing
    /// this one string.
    /// <para>
    /// <b>Mirrors <see cref="UpgradeCommand.DetectRuntimeReferenceMismatch"/>'s
    /// silence-on-ambiguity discipline, not just its comparison.</b> No <c>.csproj</c> found, more
    /// than one, an unreadable one, or one whose text names neither adapter's
    /// <c>PackageReference</c> — all return <see langword="null"/> (no refusal) rather than
    /// guess. A detector confident enough to guess a project's framework from an unrecognised
    /// shape is not thereby confident enough to be right, and a false-positive refusal blocking an
    /// adopter's ordinary `generate` on an unusual project layout is a worse failure than silently
    /// trusting <c>intest.json</c> the way this command always has — which is exactly what ran,
    /// bug-free, for every project this repository ships before this method existed.
    /// </para>
    /// <para>
    /// Returns the message <see cref="RunAsync"/> should print, or <see langword="null"/> when no
    /// mismatch is detected (including every "could not tell" case above) — same shape as
    /// <see cref="UpgradeCommand.DetectRuntimeReferenceMismatch"/>, for the same reason: this only
    /// ever reads, and never writes to <paramref name="projectRoot"/>.
    /// </para>
    /// </summary>
    internal static string? DetectFrameworkMismatch(string projectRoot, string configuredFramework)
    {
        string[] csprojFiles;
        try
        {
            csprojFiles = Directory.GetFiles(projectRoot, "*.csproj", SearchOption.TopDirectoryOnly);
        }
        catch (IOException)
        {
            return null;
        }

        // Zero: nothing to check against — a missing .csproj is not this method's job to
        // diagnose. More than one: ambiguous which project this generate is about; guessing
        // wrong would misdirect an adopter, so this says nothing rather than pick one. Same
        // reasoning as DetectRuntimeReferenceMismatch's identical guard.
        if (csprojFiles.Length != 1)
        {
            return null;
        }

        string csprojText;
        try
        {
            csprojText = File.ReadAllText(csprojFiles[0]);
        }
        catch (IOException)
        {
            return null;
        }

        var mstestMatch = MsTestAdapterReferencePattern.IsMatch(csprojText);
        var xunitMatch = XunitAdapterReferencePattern.IsMatch(csprojText);
        var nunitMatch = NunitAdapterReferencePattern.IsMatch(csprojText);

        // Exactly one pattern matched (a reformatted or hand-edited .csproj this narrow pattern no
        // longer recognises — see the doc comment above), or more than one matched (never
        // produced by `init`, and guessing which one is "the" adapter reference is worse than
        // silence): say nothing either way.
        string? adapterFramework = (mstestMatch, xunitMatch, nunitMatch) switch
        {
            (true, false, false) => "mstest",
            (false, true, false) => "xunit",
            (false, false, true) => "nunit",
            _ => null,
        };

        if (adapterFramework is null || adapterFramework == configuredFramework)
        {
            return null;
        }

        var fileName = Path.GetFileName(csprojFiles[0]);
        var adapterPackageId = adapterFramework switch
        {
            "mstest" => "InTest.Runtime.MSTest",
            "xunit" => "InTest.Runtime.xUnit",
            "nunit" => "InTest.Runtime.NUnit",
            // Unreachable: adapterFramework is one of exactly the three strings the switch above
            // can produce, and the null case already returned above — same defensive-default
            // discipline as ResolveClientKind, guarding against a regression in that switch above
            // rather than describing a real adopter input.
            _ => throw new InvalidOperationException(
                $"adapterFramework \"{adapterFramework}\" reached DetectFrameworkMismatch's " +
                "adapterPackageId switch unhandled — the switch above should only ever produce " +
                "\"mstest\", \"xunit\" or \"nunit\"."),
        };
        return
            $"{fileName} references {adapterPackageId}, but intest.json declares " +
            $"project.framework \"{configuredFramework}\". The test framework is a frozen axis " +
            "(§5 \"Frozen vs. additive axes\") — lifecycle, parameterization and parallelism " +
            "differ between frameworks, and hand-written partials target the one the project was " +
            "scaffolded with, so a suite cannot be migrated in place by editing this value. " +
            $"Generate a fresh project with `intest init --framework {configuredFramework}`, port " +
            "hand-written tests across manually, and delete this one — or set project.framework " +
            $"back to \"{adapterFramework}\" if intest.json was the value edited by mistake.";
    }

    /// <summary>
    /// Matches the exact shape <c>InitCommand</c>'s scaffold writes for the MSTest adapter's
    /// <c>PackageReference</c> — <c>Include</c> before <c>Version</c>, a self-closing tag — the
    /// version itself is not needed here, only whether the reference exists at all, so unlike
    /// <see cref="UpgradeCommand"/>'s equivalent pattern this one captures nothing.
    /// </summary>
    private static readonly Regex MsTestAdapterReferencePattern =
        new(@"<PackageReference\s+Include=""InTest\.Runtime\.MSTest""", RegexOptions.Compiled);

    /// <summary>The xUnit adapter's counterpart to <see cref="MsTestAdapterReferencePattern"/>.</summary>
    private static readonly Regex XunitAdapterReferencePattern =
        new(@"<PackageReference\s+Include=""InTest\.Runtime\.xUnit""", RegexOptions.Compiled);

    /// <summary>The NUnit adapter's counterpart to <see cref="MsTestAdapterReferencePattern"/>.</summary>
    private static readonly Regex NunitAdapterReferencePattern =
        new(@"<PackageReference\s+Include=""InTest\.Runtime\.NUnit""", RegexOptions.Compiled);

    /// <summary>
    /// Either the loaded spec, or the exit code a caller should return because this step already
    /// reported why it could not produce one. Only <see cref="ResolveSpecAsync"/> constructs it;
    /// a null <see cref="Spec"/> always comes with a message already written to the report.
    /// </summary>
    private readonly record struct ResolvedSpec(LoadedSpec? Spec, int ExitCode);

    /// <summary>
    /// Answers "what is the spec, right now" for both kinds of <c>spec.source</c>, and — for a
    /// URL in write mode — takes the §9 snapshot that makes every later read of it local.
    ///
    /// <para><b>Three paths, and only one of them opens a socket:</b></para>
    /// <list type="bullet">
    /// <item><b>A path source</b> reads the file, exactly as `generate` always has.</item>
    /// <item><b>A URL under <c>--check</c></b> reads the committed snapshot and <i>never</i>
    /// fetches (<c>[no-refetch]</c>). §9 requires this: "`--check` does not re-fetch. It compares
    /// against the committed snapshot, so CI stays hermetic and does not depend on the service
    /// being reachable." A missing snapshot is reported as <see cref="ExitCode.WorkOutstanding"/>
    /// rather than a tool error, because that is what it is — a human has to run `generate` —
    /// and it is the same voice every other `--check` difference is reported in.</item>
    /// <item><b>A URL in write mode</b> fetches, reprints, parses, then writes.</item>
    /// </list>
    ///
    /// <para>
    /// <b>That write-mode order is load-bearing in both directions, and neither half is
    /// incidental.</b> Parsing the <i>reprinted</i> text rather than the raw response is what
    /// guarantees the document this run plans from is byte-identical to what lands on disk, so a
    /// subsequent <c>--check</c> reading the snapshot reaches the identical plan. Parsing
    /// <i>before</i> writing is what guarantees an unparseable response never overwrites a good
    /// snapshot — the fetch succeeded, the document is still garbage, and the last known-good
    /// snapshot is worth more than a fresh copy of nonsense.
    /// </para>
    ///
    /// <para>
    /// <b><c>[snapshot-is-input]</c> — why the write is here, above the fixture-drift gate.</b>
    /// CLAUDE.md says `generate` "detects fixture drift before writing anything and exits 1", and
    /// this is the deliberate, documented exception to that sentence rather than a violation of
    /// it: what the invariant protects is <i>generated output</i> — nothing under Generated/, no
    /// coverage-report.json — and <c>spec.json</c> is not output. It is the materialized
    /// <i>input</i>: the bytes the rest of the run reasons from, which for a path source already
    /// exist on disk before `generate` is invoked at all. Writing it here puts a URL source in
    /// exactly the state a path source is permanently in.
    /// </para>
    /// <para>
    /// Moving it down beside the other writes — which looks tidier, and is the shape a future
    /// reader will reach for — deadlocks the tool. Worked through, because the loop is not
    /// obvious from the call site: the spec changes upstream and adds a required property;
    /// `generate` fetches, sees fixture drift, exits 1 <i>without</i> writing the snapshot;
    /// `fixtures repair` reads the old snapshot and repairs against the old spec; `generate`
    /// fetches, sees the same drift, exits 1. Forever, with every command behaving exactly as
    /// documented. <c>GenerateCommandTests.WritesTheSnapshotEvenWhenFixtureDriftEndsTheRun</c>
    /// is the regression test; deleting this ordering fails it.
    /// </para>
    /// </summary>
    private static async Task<ResolvedSpec> ResolveSpecAsync(
        string projectRoot, LoadedConfig config, bool check, HttpMessageHandler? specTransport,
        TextWriter report, CancellationToken cancellationToken)
    {
        if (!config.SpecSourceIsUrl)
        {
            return new ResolvedSpec(
                await SpecLoader.LoadFromFileAsync(
                    Path.Combine(projectRoot, config.SpecSource), cancellationToken).ConfigureAwait(false),
                ExitCode.Ok);
        }

        var snapshotPath = Path.Combine(projectRoot, SpecSnapshot.FileName);

        if (check)
        {
            if (!File.Exists(snapshotPath))
            {
                // Phrased like the other --check differences ("<file> is missing.") rather than
                // like a spec-load failure, because that is the category it belongs to: the
                // committed state does not yet contain something `generate` is supposed to have
                // put there. Routing it through SpecLoadException instead would report exit 2
                // and tell CI the tool broke, when what actually happened is that a human has
                // not run `generate` yet.
                report.WriteLine($"{SpecSnapshot.FileName} is missing.");
                report.WriteLine(
                    $"spec.source is a URL, so {SpecSnapshot.FileName} is the committed snapshot " +
                    "--check compares against. Run 'intest generate' to fetch it, and commit the result.");
                return new ResolvedSpec(null, ExitCode.WorkOutstanding);
            }

            return new ResolvedSpec(
                await SpecLoader.LoadFromFileAsync(snapshotPath, cancellationToken).ConfigureAwait(false),
                ExitCode.Ok);
        }

        // [fail-closed]: every failure below throws SpecLoadException and is caught by RunAsync as
        // §5's exit 2, with nothing written — including when a perfectly good spec.json is sitting
        // right there. Falling back to it would make "I regenerated against the current spec" and
        // "I regenerated against whatever I had lying around" produce identical output and an
        // identical exit code, which is the quiet-green failure README.md's "Fail loudly"
        // principle exists to reject.
        var fetched = await SpecFetcher
            .FetchAsync(config.SpecSource, specTransport, cancellationToken).ConfigureAwait(false);

        var snapshot = SpecSnapshot.Reprint(fetched);
        var spec = await SpecLoader.LoadFromTextAsync(snapshot, cancellationToken).ConfigureAwait(false);

        await WriteSnapshotAsync(snapshotPath, snapshot, cancellationToken).ConfigureAwait(false);

        return new ResolvedSpec(spec, ExitCode.Ok);
    }

    /// <summary>
    /// Writes the snapshot atomically — to a sibling temp file, then <see cref="File.Move(string,
    /// string, bool)"/> over the target — and translates a write failure into a sentence rather
    /// than a stack trace.
    ///
    /// <para>
    /// <b>Why this one write is atomic when the <c>Generated/</c> writes are not.</b> Not
    /// inconsistency: <c>spec.json</c> is the only artefact written <i>before</i> the drift gate,
    /// on the routine path, and it is an <i>input</i> to every command that follows. A Ctrl+C or
    /// a full disk part-way through a plain write leaves a truncated snapshot sitting in the
    /// project, and from that point `--check` and `fixtures repair` both fail on a corrupt file —
    /// reporting a spec problem the adopter did not cause and cannot diagnose from the message.
    /// A half-written file under <c>Generated/</c> is recoverable by re-running `generate`; a
    /// half-written <c>spec.json</c> is what `generate` reads.
    /// </para>
    /// <para>
    /// <see cref="File.Move(string, string, bool)"/> rather than <c>File.Replace</c>: replace
    /// requires the destination to exist, which it does not on the first run.
    /// </para>
    /// <para>
    /// <b>A read-only <c>spec.json</c> is refused on every platform</b>, by an explicit check
    /// rather than by whatever the move happens to do — see the comment on that check for why
    /// leaving it to the platform produced two different answers to the same command. The catch
    /// below covers what remains: an unwritable <i>directory</i> — a full disk, a read-only
    /// checkout, a permissions problem.
    /// </para>
    /// </summary>
    private static async Task WriteSnapshotAsync(
        string snapshotPath, string content, CancellationToken cancellationToken)
    {
        // Beside the target, not in the system temp directory: File.Move is only atomic within a
        // single volume, and a project on a different drive from %TEMP% would silently degrade to
        // a copy — which is exactly the non-atomic write this method exists to avoid.
        var temporaryPath = snapshotPath + ".tmp";

        // Uniformity, chosen rather than inherited. The atomic write makes this behaviour
        // platform-divergent if left alone: on POSIX the move is rename(2), which checks write
        // permission on the *directory* and ignores the target file's own mode, so a read-only
        // spec.json is replaced; on Windows MoveFileEx honours the read-only attribute and fails.
        // The same command, two answers, decided by whichever OS is running it — caught by CI's
        // windows-latest leg after the ubuntu leg had gone green.
        //
        // This repository already rejected that shape once, for line endings, and the reasoning
        // transfers verbatim (CommittedJsonOptions: "one fixed convention, chosen deliberately,
        // beats one that tracks whatever the writing platform's default happens to be").
        //
        // Refusal is the convention chosen, for two reasons. It is what a plain
        // File.WriteAllText did on both platforms before this write became atomic, so it is not a
        // behaviour change smuggled in alongside an unrelated fix. And a read-only bit is a
        // deliberate signal from whoever set it: generator-owned (§5) means `generate` is the
        // only thing that *writes* spec.json, not that it should overrule a filesystem that has
        // been told to protect it.
        if (File.Exists(snapshotPath) && new FileInfo(snapshotPath).IsReadOnly)
        {
            throw new SpecLoadException(
                $"The spec snapshot at '{snapshotPath}' is read-only, so `generate` cannot refresh " +
                "it. spec.json is generator-owned and must stay writable — clear the read-only " +
                "flag, or point spec.source at a path source if this project is not meant to " +
                "snapshot from a URL.");
        }

        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, snapshotPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Otherwise this escapes both of RunAsync's catches and lands on Program's crash
            // floor: `chmod 444 spec.json` reported "intest: unexpected failure:
            // UnauthorizedAccessException", which names neither the file nor anything the adopter
            // can act on. A read-only or full checkout is an ordinary condition, not a defect in
            // the tool.
            throw new SpecLoadException(
                $"The spec snapshot could not be written to '{snapshotPath}': {ex.Message} " +
                "spec.json is generator-owned and must be writable — check the file is not " +
                "read-only and that there is space on the volume.", ex);
        }
        finally
        {
            // A failed Move leaves the temp file behind, and a stray spec.json.tmp in a committed
            // project is its own small confusion. Best-effort: a cleanup failure must not replace
            // the real error above with a less useful one.
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // Deliberately swallowed — see above.
            }
        }
    }

    /// <summary>
    /// §8's worked message, verbatim, with one addition: when the running tool's own version is
    /// <see cref="CliVersion.FallbackVersion"/>, that case gets its own sentence and does not
    /// mention `upgrade` at all — see <see cref="CliVersion.FallbackVersion"/>'s own doc comment
    /// for why §8's usual remedy is actively wrong advice there. Both cases return
    /// <see cref="ExitCode.VersionMismatch"/> — the CI-facing contract ("this is not a real
    /// diff") is identical either way; only the human-facing remedy differs.
    /// <para>
    /// <paramref name="runningVersion"/> is passed in rather than read from
    /// <see cref="CliVersion.Current"/> directly, so <c>GenerateCheckCommandTests</c> can exercise
    /// the <see cref="CliVersion.FallbackVersion"/> branch without needing a binary actually built
    /// without version metadata — the one CliVersion.Current value a normal test run cannot
    /// produce, since this repository's own <c>Directory.Build.props</c> pins a real version. The
    /// call site still always passes <see cref="CliVersion.Current"/>; only the seam moved.
    /// </para>
    /// </summary>
    internal static void ReportVersionMismatch(TextWriter report, string declaredVersion, string runningVersion)
    {
        if (runningVersion == CliVersion.FallbackVersion)
        {
            report.WriteLine(
                $"intest.json was generated by intest {declaredVersion}; the running tool reports " +
                $"\"{CliVersion.FallbackVersion}\", which means it was built without version " +
                "metadata rather than that it is actually version 0.0.0.");
            report.WriteLine(
                "This is a build problem, not a version to adopt — do not run `intest upgrade` here, " +
                "it would only write \"0.0.0\" into intestVersion and hide the defect permanently. " +
                "Rebuild intest so its assembly carries a real informational version, then re-run --check.");
            return;
        }

        report.WriteLine(
            $"intest.json was generated by intest {declaredVersion}; running tool is {runningVersion}.");
        report.WriteLine(
            $"Regenerate with the pinned version, or run `intest upgrade` to adopt {runningVersion} deliberately.");
    }

    /// <summary>
    /// The compare half of [no-write]. Every call here is a read: <see cref="File.Exists"/>,
    /// <see cref="File.ReadAllBytesAsync(string, CancellationToken)"/>, and
    /// <see cref="Directory.EnumerateFiles(string, string, SearchOption)"/> for the orphan sweep.
    /// That is this method's complete reachable call graph — no write, no
    /// <see cref="Path.GetTempFileName"/>/<see cref="Path.GetTempPath"/>, nothing that creates,
    /// deletes, or modifies a file. See the [no-write] seam comment above
    /// <see cref="BuildOutputs"/> for what that property does and does not let a test prove.
    /// </summary>
    private static async Task<int> RunCheckAsync(
        string projectRoot, IReadOnlyDictionary<string, string> outputs, TextWriter report,
        CancellationToken cancellationToken)
    {
        var differences = new List<string>();

        foreach (var (relativePath, expectedContent) in outputs)
        {
            var fullPath = ToFullPath(projectRoot, relativePath);
            if (!File.Exists(fullPath))
            {
                differences.Add($"{relativePath} is missing.");
                continue;
            }

            // Bytes, not text: File.ReadAllTextAsync performs BOM-based encoding detection, so a
            // committed file re-encoded as UTF-16LE, or one with a stray UTF-8 BOM prepended,
            // decodes back to a string equal to `expectedContent` and this check would report
            // "match" for a file `generate` would rewrite byte-for-byte differently. The artefact
            // `generate` owns is a specific sequence of bytes (BuildOutputs always renders plain
            // UTF-8, no BOM, via File.WriteAllTextAsync's default encoding), so the comparison
            // has to say so directly rather than routing through a decoder that guesses.
            var actualBytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
            var expectedBytes = Encoding.UTF8.GetBytes(expectedContent);
            if (!actualBytes.AsSpan().SequenceEqual(expectedBytes))
            {
                differences.Add($"{relativePath} differs from a fresh render.");
            }
        }

        // The stale-file case: an operation (or a whole tag) dropped from the spec leaves its old
        // .g.cs sitting under Generated/ with no corresponding key in `outputs` at all, so the
        // loop above — which only ever walks `outputs`, never the disk — cannot see it by
        // construction. A naive for-each-rendered-file comparison reports 0 here, which is
        // exactly the silently-permissive gap the plan calls out. coverage-report.json needs no
        // equivalent sweep: it is one named file at the project root, never a directory `generate`
        // owns wholesale, so there is no sibling for a stale entry to hide among.
        var generatedDir = Path.Combine(projectRoot, "Generated");
        if (Directory.Exists(generatedDir))
        {
            var expected = outputs.Keys
                .Where(k => k.StartsWith("Generated/", StringComparison.Ordinal))
                .ToHashSet(StringComparer.Ordinal);

            // AllDirectories, not TopDirectoryOnly: every artefact BuildOutputs produces today is
            // flat under Generated/, but a stray file written by a bug (or by hand) has no reason
            // to respect that, and this sweep is the only thing standing between such a file and
            // a --check run that reports 0 while something sits there unaccounted for. Pinned by
            // GenerateCheckCommandTests.ReturnsWorkOutstandingForAStrayFileInASubdirectoryOfGenerated
            // — switching this to TopDirectoryOnly fails that test.
            foreach (var file in Directory.EnumerateFiles(generatedDir, "*", SearchOption.AllDirectories))
            {
                var relativePath = "Generated/" + Path.GetRelativePath(generatedDir, file).Replace('\\', '/');
                if (!expected.Contains(relativePath))
                {
                    // File.Exists above matches case-insensitively on Windows, so a file that
                    // differs from an expected one only by case passes that check silently and
                    // is caught here instead — but by then all this sweep has is the on-disk
                    // name, which is the *wrong* one. Naming the expected casing too (when a
                    // case-insensitive match exists) turns "orderstests.g.cs exists on disk but
                    // a fresh render does not produce it" — true, but reads as an unrelated
                    // stray file — into a message that says what actually happened: a rename.
                    var expectedCasing = expected.FirstOrDefault(
                        e => string.Equals(e, relativePath, StringComparison.OrdinalIgnoreCase));
                    differences.Add(expectedCasing is null
                        ? $"{relativePath} exists on disk but a fresh render does not produce it."
                        : $"{relativePath} exists on disk but a fresh render names it " +
                          $"{expectedCasing} instead (case differs).");
                }
            }
        }

        if (differences.Count > 0)
        {
            foreach (var difference in differences.OrderBy(d => d, StringComparer.Ordinal))
            {
                report.WriteLine(difference);
            }
            report.WriteLine("Run 'intest generate' to update.");
            return ExitCode.WorkOutstanding;
        }

        report.WriteLine("Generated/ and coverage-report.json match a fresh render.");
        return ExitCode.Ok;
    }

    /// <summary>
    /// One message per operation whose fixture is missing entirely, or missing a property or
    /// parameter that <see cref="FixtureComposer.Compose"/> would put there today — the same
    /// comparison <c>fixtures repair</c> uses (<see cref="FixtureDrift"/>), but read-only: nothing
    /// here is written back. Iterates <paramref name="plan"/> rather than the raw document for the
    /// same reason repair does — <see cref="TestPlanBuilder"/> is the sole authority on which
    /// operations exist, so drift here can never disagree with what repair would create.
    /// </summary>
    private static List<string> DetectFixtureDrift(OpenApiDocument document, Planning.TestPlan plan, string projectRoot)
    {
        var messages = new List<string>();
        var fixturesDir = Path.Combine(projectRoot, "fixtures");
        var generatedBy = $"intest {CliVersion.Current}";

        foreach (var testCase in plan.Classes.SelectMany(c => c.Cases)
                                              .Where(c => c.NeedsFixture)
                                              .OrderBy(c => c.OperationKey, StringComparer.Ordinal))
        {
            var fixturePath = Path.Combine(fixturesDir, FixtureDocument.FileNameFor(testCase.OperationKey));

            if (!File.Exists(fixturePath))
            {
                messages.Add($"{testCase.OperationKey}: no fixture found.");
                continue;
            }

            var existing = FixtureDocument.Parse(File.ReadAllText(fixturePath));
            var composed = FixtureComposer.Compose(
                document, testCase.PathTemplate, testCase.HttpMethod, testCase.OperationKey, generatedBy);

            var comparison = FixtureDrift.Compare(existing, composed);
            var missing = comparison.MissingProperties.Concat(comparison.MissingParameters)
                                     .OrderBy(name => name, StringComparer.Ordinal)
                                     .ToList();

            if (missing.Count > 0)
            {
                messages.Add($"{testCase.OperationKey}: fixture is missing {string.Join(", ", missing)}.");
            }
        }

        return messages;
    }
}

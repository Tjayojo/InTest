using System.Text.Json.Nodes;
using System.Text.Json;
using InTest.Cli.Configuration;
using InTest.Cli.Coverage;
using InTest.Cli.Fixtures;
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

    public static async Task<int> RunAsync(
        string projectRoot, CancellationToken cancellationToken, TextWriter? report = null)
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

            var spec = await SpecLoader.LoadFromFileAsync(Path.Combine(projectRoot, config.SpecSource), cancellationToken)
                                       .ConfigureAwait(false);

            var plan = TestPlanBuilder.Build(spec.Document);

            // Drift is checked — and reported — before anything is written. generate never
            // writes under fixtures/ (that is fixtures repair's job alone, Task 3); it only
            // compares what each operation needing a fixture would compose today against what
            // is actually committed, so a spec change that silently invalidates a fixture is
            // caught here instead of surfacing as a confusing runtime failure in Task 9's suite.
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

            var generated = Path.Combine(projectRoot, "Generated");

            if (Directory.Exists(generated))
            {
                Directory.Delete(generated, recursive: true);
            }
            Directory.CreateDirectory(generated);

            var renderer = new TemplateRenderer();
            foreach (var testClass in plan.Classes)
            {
                var source = renderer.RenderClass(testClass, config.RootNamespace, config.TestBaseClass);
                await File.WriteAllTextAsync(Path.Combine(generated, testClass.ClassName + ".g.cs"), source, cancellationToken)
                          .ConfigureAwait(false);
            }

            await File.WriteAllTextAsync(Path.Combine(generated, "spec-schemas.json"),
                SchemaBundleBuilder.Build(spec.Document, plan), cancellationToken).ConfigureAwait(false);

            // The prefix every operation path shares, if any. TestHost uses it to detect a
            // base URL that repeats it; otherwise every request 404s and nothing says why.
            var pathManifest = new JsonObject { ["operationPathPrefix"] = CommonPathPrefix(plan) };
            // NewLine = "\n" pins the interior line endings to LF (System.Text.Json otherwise
            // uses Environment.NewLine, CRLF on Windows); the trailing "+ \"\\n\"" is the final
            // newline after the closing brace, which WriteIndented never emits on its own. Same
            // fix, same reasoning, as CoverageReport.ToJson and FixtureDocument's writer.
            await File.WriteAllTextAsync(
                Path.Combine(generated, "spec-paths.json"),
                pathManifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" }) + "\n",
                cancellationToken).ConfigureAwait(false);

            await File.WriteAllTextAsync(Path.Combine(projectRoot, "coverage-report.json"),
                CoverageReport.ToJson(plan), cancellationToken).ConfigureAwait(false);

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

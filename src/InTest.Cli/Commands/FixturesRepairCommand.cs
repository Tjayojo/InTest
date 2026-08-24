using System.Text.Json.Nodes;
using InTest.Cli.Configuration;
using InTest.Cli.Fixtures;
using InTest.Cli.Planning;
using InTest.Cli.Spec;

namespace InTest.Cli.Commands;

/// <summary>
/// The only command that writes under <c>fixtures/</c> (Task 3's plan section, "owning creation,
/// sentinel addition and stale flagging"). It creates a fixture for every operation the test plan
/// covers but has none yet, adds properties and parameters a schema change made required since a
/// fixture was last written, and reports — without touching — properties a fixture still carries
/// that the schema no longer declares. It never overwrites a value already present: that is the
/// one invariant a hand-edited, committed fixture depends on.
/// </summary>
public static class FixturesRepairCommand
{
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

            // The whole of intest.json, through the same loader `generate` uses — not just the
            // spec.source this command reads. A config that is valid for repair but not for
            // generate is a state nobody can reason about: repair succeeds, the adopter concludes
            // their config is sound, and they lose that conclusion one command later. §5's exit 2
            // ("malformed intest.json") is a property of the document, not of a read set. Nothing
            // is given up by refusing here, because repair cannot repair intest.json.
            var config = ConfigLoader.Load(projectRoot);

            // Repair never fetches ([no-refetch]). A URL spec.source is read from the committed
            // snapshot `generate` took (§9), for the same reason --check reads it: deciding what
            // the spec now says is `generate`'s job, deliberately, on a branch, where the
            // resulting spec.json diff is reviewable. A command whose entire output is fixtures/
            // has no business making that call, and if it did, `generate` and `repair` could plan
            // against two different upstream revisions — a skew that would surface as drift
            // repair cannot fix.
            var specPath = Path.Combine(
                projectRoot, config.SpecSourceIsUrl ? SpecSnapshot.FileName : config.SpecSource);

            // The snapshot has to exist before repair can say anything about fixtures, and
            // letting LoadFromFileAsync report that is a worse sentence than it looks: it would
            // say "Spec file not found: <projectRoot>/spec.json", naming a file the adopter never
            // wrote, never chose the name of, and cannot create by hand. That is the same defect
            // class the pre-§9 URL refusal existed to fix (an accurate sentence about the wrong
            // thing); reintroducing it one file over would be a poor trade for four saved lines.
            if (config.SpecSourceIsUrl && !File.Exists(specPath))
            {
                throw new SpecLoadException(
                    $"spec.source is a URL and no {SpecSnapshot.FileName} snapshot exists yet, so " +
                    "there is nothing for `fixtures repair` to read. Run `intest generate` first — " +
                    "it fetches the document and writes the snapshot — then run this again.");
            }

            var spec = await SpecLoader.LoadFromFileAsync(specPath, cancellationToken)
                                       .ConfigureAwait(false);

            // Iterates the test plan, not the raw document: TestPlanBuilder is the sole authority
            // on which operations exist (it already applies FixtureComposer.NeedsFixture and skips
            // non-JSON bodies and responses with no 2xx/3xx). Iterating the document directly would
            // create fixtures `generate`'s drift check disagrees with — see the plan's Task 3 note.
            var plan = TestPlanBuilder.Build(spec.Document);
            var fixturesDir = Path.Combine(projectRoot, "fixtures");
            var generatedBy = $"intest {CliVersion.Current}";

            var created = 0;
            var updated = 0;
            var failed = 0;

            foreach (var testCase in plan.Classes.SelectMany(c => c.Cases)
                                                  .Where(c => c.NeedsFixture)
                                                  .OrderBy(c => c.OperationKey, StringComparer.Ordinal))
            {
                // NeedsFixture is FixtureComposer's own verdict, carried on the plan by
                // TestPlanBuilder — restating that decision here (e.g. inspecting Compose's
                // output for emptiness) is exactly the second copy that has drifted from the
                // composer twice before. An operation that doesn't need one is left alone
                // entirely, whether or not a fixture already happens to exist for it.

                // Every key reaching here already passed FixtureDocument.TryValidateOperationKey
                // inside TestPlanBuilder (an operation with an unusable key is recorded as skipped
                // and never produces a TestCasePlan). FileNameFor throwing here means that
                // invariant broke — a bug to surface, not a condition to defensively swallow.
                var fixturePath = Path.Combine(fixturesDir, FixtureDocument.FileNameFor(testCase.OperationKey));

                try
                {
                    var composed = FixtureComposer.Compose(
                        spec.Document, testCase.PathTemplate, testCase.HttpMethod, testCase.OperationKey, generatedBy);

                    if (!File.Exists(fixturePath))
                    {
                        Directory.CreateDirectory(fixturesDir);
                        await File.WriteAllTextAsync(fixturePath, composed.ToJson(), cancellationToken).ConfigureAwait(false);
                        created++;
                        continue;
                    }

                    var existingText = await File.ReadAllTextAsync(fixturePath, cancellationToken).ConfigureAwait(false);
                    var existing = FixtureDocument.Parse(existingText);
                    var drift = FixtureDrift.Compare(existing, composed);

                    var changed = false;

                    if (drift.MissingProperties.Count > 0)
                    {
                        var body = existing.Body as JsonObject ?? new JsonObject();
                        var composedBody = (JsonObject)composed.Body!;
                        foreach (var name in drift.MissingProperties)
                        {
                            body[name] = composedBody[name]?.DeepClone();
                        }
                        existing.Body = body;
                        changed = true;
                    }

                    foreach (var name in drift.MissingParameters)
                    {
                        existing.Parameters[name] = composed.Parameters[name];
                        changed = true;
                    }

                    // Stale properties are reported, never deleted (§10) — a property no longer in
                    // the schema may be deliberate, and silent deletion is how that intent is lost.
                    foreach (var name in drift.StaleProperties)
                    {
                        report.WriteLine(
                        $"{testCase.OperationKey}: '{name}' is no longer in schema (kept — remove by hand if it was not intentional).");
                    }

                    if (changed)
                    {
                        await File.WriteAllTextAsync(fixturePath, existing.ToJson(), cancellationToken).ConfigureAwait(false);
                        updated++;
                    }
                }
                catch (FixtureFormatException ex)
                {
                    // One bad committed fixture is that operation's problem, not the whole run's:
                    // every other operation's legitimate repair — creation or sentinel addition —
                    // must still happen. The run as a whole still reports a tool error (below),
                    // since the malformed fixture itself is unresolved.
                    failed++;
                    report.WriteLine($"{testCase.OperationKey}: {ex.Message}");
                }
            }

            report.WriteLine(created + updated == 0
                ? "Nothing to repair."
                : $"Created {created} fixture(s), updated {updated} fixture(s).");

            return failed == 0 ? ExitCode.Ok : ExitCode.ToolError;
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
        catch (FixtureFormatException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ExitCode.ToolError;
        }
    }
}

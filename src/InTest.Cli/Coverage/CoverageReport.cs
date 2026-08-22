using System.Text.Json;
using System.Text.Json.Nodes;
using InTest.Cli.Planning;

namespace InTest.Cli.Coverage;

/// <summary>
/// Everything InTest did not cover, or covered less thoroughly than a full contract test.
/// Committed and compared by `--check`, because it is the one generated artefact whose
/// content tracks the shape of the spec rather than the templates.
/// </summary>
public static class CoverageReport
{
    // Task 5 review finding: the explanation text used to live twice — once paraphrased in a
    // `//` comment above each key, once hand-written into the JsonObject literal below — with
    // nothing binding the two, so rewording one silently left the other a stale paraphrase.
    // Hoisted here so each explanation is one string, referenced once, the way
    // TestPlanBuilder.NoPathParameterNoteReason is a single string referenced by both the note
    // text and CoverageReport's match on it rather than two hand-copied literals.
    private const string AuthTestsGatedOnSecondIdentityExplanation =
        "How many generated *_Forbidden tests require a second identity to run at all. These " +
        "skip rather than run when the suite has fewer than two identities; whether that " +
        "happens is decided when the suite runs, by the ITestTokenProvider your project " +
        "registers, which this generator does not execute.";

    private const string AuthTestsRequiringAnUnderScopedSecondIdentityExplanation =
        "How many generated *_Forbidden tests belong to operations that declare required " +
        "scopes. These skip rather than fail when the second identity holds those scopes; " +
        "which ones skip is decided when the suite runs, by the ITestTokenProvider your " +
        "project registers, which this generator does not execute.";

    public static string ToJson(TestPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var cases = plan.Classes.SelectMany(c => c.Cases).ToList();

        var skipped = new JsonArray();
        foreach (var s in plan.Skipped)
        {
            skipped.Add(new JsonObject { ["operation"] = s.OperationKey, ["reason"] = s.Reason });
        }

        // Review finding on Task 4: TestPlan.Notes was populated by TestPlanBuilder's three
        // withholding branches and read by nothing — a withheld declared-error case was a
        // completely silent omission, exactly what §12 legislates against ("skips remove tests.
        // Notes do not" only holds if something reports them). Same {operation, reason} shape as
        // `skipped` above, deliberately: a note is "the operation's other cases still generated"
        // rather than "nothing generated", not a lesser amount of detail about it.
        var withheld = new JsonArray();
        foreach (var n in plan.Notes)
        {
            withheld.Add(new JsonObject { ["operation"] = n.OperationKey, ["reason"] = n.Reason });
        }

        // Task 6: an operation can now emit more than one case (declared-error and auth cases,
        // decisions 5 and 3), so a per-operation count is no longer the same number as a
        // per-case count. authCases is also reused below by both the generated and gated counts.
        var authCases = cases.Where(c => c.Role == CaseRole.Auth).ToList();

        var report = new JsonObject
        {
            ["title"] = plan.Title,
            // Left as a case count, deliberately: GenerateCommand.cs's own console line
            // ("Generated N test(s)") already fixes what this field means, so redefining it here
            // would contradict output the same run just printed. operationsGenerated, below, is
            // the field that now carries what "generated" only meant by coincidence before
            // declared-error and auth cases existed — §12's own example ("Operations in spec: 148
            // / Generated: 113") is that older, 1:1 meaning.
            ["generated"] = cases.Count,
            ["operationsGenerated"] = cases.Select(c => c.OperationKey).Distinct(StringComparer.Ordinal).Count(),
            ["skipped"] = skipped,
            ["notes"] = new JsonObject
            {
                ["withheld"] = withheld,
                // Both metrics name *operations*, not cases. An operation can emit more than one
                // case since declared-error and auth cases arrived (decisions 5 and 3) —
                // counting cases here double-counts every operation that also gets a non-success
                // case, and every sample spec in the repo declares a 404, so this is a Distinct
                // over Role.Success cases only, not a Count/Sum or a Distinct over every role.
                // Filtering to Success is what actually enforces "one entry per operation" —
                // TestPlanBuilder only ever emits a non-success case for an operation whose
                // success case already generated (TestPlanBuilder.cs:100-103), so a role filter
                // and a bare Distinct happen to produce the same number today, but only the
                // filter says so structurally rather than leaning on that cross-file invariant.
                ["untaggedOperations"] = plan.Classes.Where(c => c.Tag == "Default")
                    .SelectMany(c => c.Cases).Where(c => c.Role == CaseRole.Success)
                    .Select(c => c.OperationKey).Distinct(StringComparer.Ordinal).Count(),
                ["synthesizedOperationIds"] = cases.Where(c => c.Role == CaseRole.Success && c.OperationKeySynthesized)
                    .Select(c => c.OperationKey).Distinct(StringComparer.Ordinal).Count(),
                // Role.Success only — not because a non-success case's SchemaKey is always null
                // (it is not: TestPlanBuilder.cs:171 asks the 404 response itself for a schema,
                // and every 404 in every shipped sample declares one, so a real DeclaredError
                // case usually carries "ProblemDetails", not null). The filter exists because a
                // non-success case's null SchemaKey, on the rare operation where it does occur,
                // is never the gap this note names ("no response schema declared — fixable in
                // the spec"): decision 3's auth pair reads no declared response at all, so it
                // has no such question to have failed, and a declared-error case's 404 is not a
                // success-contract gap even when its schema is absent. Either way, a non-success
                // case has nothing to say about success-contract completeness, so it is excluded
                // regardless of what its SchemaKey happens to be.
                ["statusOnlyContractTests"] = cases.Count(c => c.SchemaKey is null && c.Role == CaseRole.Success),
                ["inlineResponseSchemas"] = cases.Count(c => c.SchemaKey?.StartsWith("op:", StringComparison.Ordinal) == true),
                ["declaredErrorTestsGenerated"] = cases.Count(c => c.Role == CaseRole.DeclaredError),
                ["authTestsGenerated"] = authCases.Count,
                // Not a skip count: whether a generated case actually gets skipped is decided at
                // runtime by RequireMultipleIdentities against whatever ITestTokenProvider a
                // project registers (decision 3) — the CLI generates this report long before any
                // provider exists (decision 7) and cannot know that number. See "explanations"
                // below for the adopter-facing statement of what this key does say.
                ["authTestsGatedOnSecondIdentity"] = authCases.Count(c => c.Slot == IdentitySlot.Secondary),
                // Also not a skip count, for the same reason, plus one more: reporting an actual
                // skip count here would make `generate --check` report drift on an unchanged spec
                // the moment a provider's runtime behaviour changed which of these cases skip.
                ["authTestsRequiringAnUnderScopedSecondIdentity"] =
                    authCases.Count(c => c.Slot == IdentitySlot.Secondary && c.RequiredScopes.Count > 0),
                // Matched against TestPlanBuilder.NoPathParameterNoteReason — the constant the
                // builder's no-path-parameter branch builds its note text from — rather than a
                // second hand-copied literal here. A reword of that constant changes both sides
                // at once, since there is only one string, not a restatement of it: this count
                // cannot drift from the message a reader of `withheld` actually sees, because
                // both are the same object in memory, not two copies that happen to agree today.
                ["notFoundWithoutPathParameter"] = plan.Notes.Count(n =>
                    n.Reason.Contains(TestPlanBuilder.NoPathParameterNoteReason, StringComparison.Ordinal)),
                // Review finding on Task 5: JSON carries no comments, so the reasoning above never
                // reached a reader of the artefact itself — only a reader of this source file.
                // "explanations" is a sibling of the counts it explains, keyed by the same
                // property name, so a reader who has just read a suspicious number can look it up
                // by the name they already have. A flat sibling string per key (e.g. an
                // "...Explanation" key next to each count) was the other option; this shape was
                // chosen because it scales to future keys needing the same treatment without
                // doubling the key count of "notes" itself, and because most keys here need no
                // such caveat — folding them all into one small, opt-in object keeps the ones that
                // are self-explanatory (like authTestsGenerated) uncluttered. Unconditional, not
                // present only when a spec has security-gated operations: `--check` compares a
                // committed artefact against a fresh run of the *same* spec, so a spec-dependent
                // key would still be deterministic and never drift — the real reason is that a
                // stable key set is easier to diff and parse. Both string constants below are
                // fixed literals, not derived from plan data, so neither can vary between two runs
                // against the same spec and cannot cause --check drift. Placed last in "notes",
                // after the counts it explains, rather than between them.
                ["explanations"] = new JsonObject
                {
                    ["authTestsGatedOnSecondIdentity"] = AuthTestsGatedOnSecondIdentityExplanation,
                    ["authTestsRequiringAnUnderScopedSecondIdentity"] =
                        AuthTestsRequiringAnUnderScopedSecondIdentityExplanation
                }
            }
        };

        // NewLine pins the *interior* line endings this writer emits between properties to LF;
        // without it, System.Text.Json defaults to Environment.NewLine, which is CRLF on Windows.
        // The trailing "+ \"\\n\"" below is unrelated — WriteIndented never emits a line ending
        // after the final closing brace, so that final newline is still added by hand. Before
        // this fix the two disagreed: every interior line was CRLF, only the appended last line
        // was LF. This is a committed, `--check`-compared artefact, so one line ending throughout
        // matters for the same reason it matters in any file a human diffs.
        return report.ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" }) + "\n";
    }
}

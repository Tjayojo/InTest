using Shouldly;

namespace InTest.Cli.Tests;

/// <summary>
/// Guards the recurrence direction the v1-e line-endings defect actually took: a JSON writer
/// constructing its own <c>System.Text.Json.JsonSerializerOptions { WriteIndented = true }</c>
/// inline, silently defaulting <c>NewLine</c> to <c>Environment.NewLine</c> (LF on Linux/macOS)
/// instead of routing through <c>InTest.Cli.Json.CommittedJsonOptions</c>, which pins it to
/// <c>"\r\n"</c>. Before that fix three of the four writers repeated the same ~7-line comment
/// explaining the same fix instead of sharing the one value that mattered — a convention that
/// lived only in comments, which is exactly the shape of gap this repository closes
/// mechanically elsewhere (see <c>TemplateEscapingGuardTests</c>, which enforces the
/// <c>_literal</c> naming convention against the template source rather than trusting a
/// reader to remember it).
/// <para>
/// Modeled on <c>InTest.Architecture.Tests.NeutralityTests</c>: source-level, not
/// reflection-based, because the rule is about what a future writer's source code says, not
/// about anything observable after compilation. Scans every <c>.cs</c> file under
/// <c>src/InTest.Cli</c> for the literal text <c>"WriteIndented"</c> — after consolidation, that
/// string appears exactly once in the whole project, inside
/// <c>CommittedJsonOptions.cs</c> itself. A fifth writer that constructs its own
/// <c>JsonSerializerOptions { WriteIndented = true, ... }</c> instead of referencing
/// <c>CommittedJsonOptions.Value</c> reintroduces that literal text somewhere else, which this
/// test catches without needing to parse C# or understand what a "writer" is.
/// </para>
/// </summary>
[TestClass]
public class JsonWritingOptionsGuardTests
{
    private const string Marker = "WriteIndented";

    /// <summary>
    /// File names allowed to mention <see cref="Marker"/> outside <c>CommittedJsonOptions.cs</c>,
    /// and why. Add to this list only for a JSON writer that is genuinely not one of the
    /// committed, byte-significant artefacts <c>CommittedJsonOptions</c> exists for — anything
    /// else belongs behind <c>CommittedJsonOptions.Value</c> instead.
    /// </summary>
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "CommittedJsonOptions.cs", // the shared instance itself — this is its one legitimate home.
    };

    private static string CliSourceDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "InTest.sln")))
        {
            dir = dir.Parent;
        }
        dir.ShouldNotBeNull("Could not locate the repository root (InTest.sln).");
        return Path.Combine(dir!.FullName, "src", "InTest.Cli");
    }

    [TestMethod]
    public void NoWriterConstructsItsOwnWriteIndentedOptions()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(CliSourceDirectory(), "*.cs", SearchOption.AllDirectories))
        {
            // Excludes bin/ and obj/: this guard is about what a human wrote, not about build
            // output (generated AssemblyInfo, ref assemblies' companion files, etc.) that happens
            // to live under the same source tree and is never a hand-authored writer.
            var relative = Path.GetRelativePath(CliSourceDirectory(), file);
            var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Contains("bin") || segments.Contains("obj"))
            {
                continue;
            }

            var name = Path.GetFileName(file);
            if (Allowed.Contains(name))
            {
                continue;
            }

            if (File.ReadAllText(file).Contains(Marker, StringComparison.Ordinal))
            {
                offenders.Add(name);
            }
        }

        offenders.ShouldBeEmpty(
            "These files mention 'WriteIndented' outside InTest.Cli.Json.CommittedJsonOptions: " +
            string.Join(", ", offenders.OrderBy(n => n, StringComparer.Ordinal)) + ". A JSON " +
            "writer that constructs its own JsonSerializerOptions here silently defaults NewLine " +
            "to Environment.NewLine (LF on Linux/macOS) instead of the pinned \"\\r\\n\" — " +
            "reference InTest.Cli.Json.CommittedJsonOptions.Value instead, or add the file to " +
            "JsonWritingOptionsGuardTests.Allowed with a one-line reason if it genuinely is not " +
            "one of the committed artefacts CommittedJsonOptions exists for.");
    }

    /// <summary>
    /// If this ever finds zero files, the source directory resolution above has stopped working
    /// — that is this guard silently going blind, not a clean bill of health, so it must fail
    /// loudly rather than pass vacuously (mirrors
    /// <c>TemplateEscapingGuardTests.EveryTemplateFieldReferenceIsEscapedOrExplicitlyAllowed</c>'s
    /// own zero-references check).
    /// </summary>
    [TestMethod]
    public void SourceDirectoryIsNotEmpty()
    {
        Directory.EnumerateFiles(CliSourceDirectory(), "*.cs", SearchOption.AllDirectories)
                 .ShouldNotBeEmpty("The WriteIndented guard would pass vacuously against an empty directory.");
    }
}

namespace InTest.Cli.Configuration;

/// <summary>
/// The settings <c>intest.json</c> carries that a command actually reads, after validation.
/// A <see cref="LoadedConfig"/> only exists because <see cref="ConfigLoader.Load"/> did not
/// throw, so every property is known-good — but "known-good" allows <c>null</c> where a setting
/// is legitimately optional; see <see cref="IntestVersion"/>.
/// </summary>
/// <param name="IntestVersion">
/// The <c>intest</c> version that generated this config, when the config declares one — null
/// when <c>intestVersion</c> is absent, which is expected for a config predating it or one that
/// was hand-edited without it. Validated only as a non-empty string: deciding what a version
/// <i>means</i> (comparing it against the running CLI) is <c>generate --check</c>'s job, not
/// <see cref="ConfigLoader"/>'s.
/// </param>
/// <param name="SpecSourceIsUrl">
/// Whether <paramref name="SpecSource"/> names an <c>http(s)://</c> URL rather than a path — the
/// verdict that decides, at three call sites, whether "the spec" means a file the adopter
/// maintains or the <c>spec.json</c> snapshot <c>generate</c> takes (§9).
/// <para>
/// <b>Carried, not re-derived.</b> <c>CLAUDE.md</c> names re-deriving a verdict downstream as the
/// recurring defect in this codebase, and this one has more reason than most to be decided once:
/// <see cref="Spec.SpecLoader.IsUrl"/> was promoted by the URL work from "which error message do
/// we print" to "which code path runs at all", so two call sites disagreeing about it would no
/// longer produce a differently-worded failure, it would produce <c>generate</c> and
/// <c>fixtures repair</c> reading two different documents. <see cref="ConfigLoader.Load"/> asks
/// once; everything else reads the answer.
/// </para>
/// </param>
public sealed record LoadedConfig(
    string SpecSource,
    string RootNamespace,
    string TestBaseClass,
    string? IntestVersion,
    bool SpecSourceIsUrl);

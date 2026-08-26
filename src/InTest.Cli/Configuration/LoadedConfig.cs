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
/// <param name="Framework">
/// The test framework declared at <c>project.framework</c> — today always <c>"mstest"</c>, since
/// <see cref="ConfigLoader.Load"/> refuses any other value (see its own doc comment for why the
/// setting is required rather than defaulted). §3 designs InTest for three frameworks and ships
/// one; this is the plug-in point a second template would select on, once a second template
/// exists. It does not yet: <see cref="Rendering.TemplateRenderer"/> hardcodes
/// <c>mstest-class.scriban</c> regardless of this value, so today <see cref="Framework"/> is
/// carried on <see cref="LoadedConfig"/> and read by nothing downstream.
/// <para>
/// <b>Carried, not re-derived.</b> Matching <see cref="SpecSourceIsUrl"/> below:
/// <c>CLAUDE.md</c> names re-deriving a verdict downstream as the recurring defect in this
/// codebase. <see cref="ConfigLoader.Load"/> is where the value is validated against the
/// supported set; a future template-selection call site reads this field rather than
/// re-inspecting <c>intest.json</c> or re-running that validation itself.
/// </para>
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
/// <param name="Client">
/// The validated "client" section, or <see langword="null"/> when <c>intest.json</c> declares
/// none — the common case today, and the one every existing config exercises. Optional by design,
/// not by omission: absence must leave every existing behaviour byte-identical (the
/// typed-client-invocation plan's opening line), so this is the one field on <see cref="LoadedConfig"/>
/// downstream code is expected to null-check rather than trust as always-present, the same way
/// <see cref="IntestVersion"/> already is.
/// </param>
public sealed record LoadedConfig(
    string SpecSource,
    string RootNamespace,
    string TestBaseClass,
    string Framework,
    string? IntestVersion,
    bool SpecSourceIsUrl,
    LoadedClientConfig? Client = null);

namespace InTest.Cli.Configuration;

/// <summary>
/// The settings <c>intest.json</c> carries that a command actually reads, after validation.
/// Every property is non-null and known-good: a <see cref="LoadedConfig"/> only exists because
/// <see cref="ConfigLoader.Load"/> did not throw.
/// </summary>
/// <param name="IntestVersion">
/// The <c>intest</c> version that generated this config, when the config declares one — null
/// when <c>intestVersion</c> is absent, which is expected for a config predating it or one that
/// was hand-edited without it. Validated only as a non-empty string: deciding what a version
/// <i>means</i> (comparing it against the running CLI) is <c>generate --check</c>'s job, not
/// <see cref="ConfigLoader"/>'s.
/// </param>
public sealed record LoadedConfig(
    string SpecSource,
    string RootNamespace,
    string TestBaseClass,
    string? IntestVersion = null);

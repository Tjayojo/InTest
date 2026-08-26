using System.Text.Encodings.Web;
using System.Text.Json;
using InTest.Cli.Configuration;
using InTest.Cli.Naming;
using InTest.Cli.Spec;

namespace InTest.Cli.Commands;

public static class InitCommand
{
    // See JsonSpecSource below for why this uses the relaxed encoder.
    private static readonly JsonSerializerOptions SpecSourceJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The one remedy sentence for <c>--spec</c>, appended to whichever rule rejected the value.
    /// Three rules can reject it and they are deliberately different questions — is there a value
    /// at all (<see cref="CommandArguments"/>), is a value that looks like a URL a well-formed one
    /// (<see cref="Spec.SpecFetcher.TryValidateUrl"/>), and can XML 1.0 represent the value there
    /// is (<see cref="Naming.MSBuildPropertyValue"/>) — but the adopter's next move is the same
    /// either way, so they must not answer it in three voices. One constant, not three literals
    /// that agree today.
    /// </summary>
    private const string SpecRemedy =
        "Pass the path to the OpenAPI document to `intest init --spec` — for example " +
        "\"../Orders/bin/Debug/net10.0/orders.json\" — or the URL it is served from, for example " +
        "\"https://orders-staging.example.com/swagger/v1/swagger.json\". If you own a generated " +
        "client but not the document, `intest init --client-lockfile <path-to-kiota-lock.json>` " +
        "recovers it from what the generator recorded.";

    /// <summary>
    /// The exact bytes `init` scaffolds at <c>.gitattributes</c> — hoisted to a named constant,
    /// internal rather than private, so <c>UpgradeCommand</c> can write the identical file for a
    /// project scaffolded before <c>[crlf-everywhere]</c> shipped without a second hand-copied
    /// literal that could silently drift from this one. Modeled on this repository's own
    /// <c>.gitattributes</c>, which pins the identical case (*.g.cs.txt golden files, *.scriban
    /// templates) for the identical reason — with one deliberate difference: no
    /// <c>* text=auto</c> line. That line normalizes *every* path under wherever this file lives,
    /// not just the three patterns below, and a .gitattributes in a subdirectory outranks one at
    /// the adopting team's repo root for paths beneath it — so it would silently reverse a
    /// deliberate root policy such as `* -text` for TestStartup.cs, appsettings*.json, this
    /// project's own .csproj, and anything the team adds later (the "everything else | the
    /// adopting team | InTest never touches" row of CLAUDE.md's ownership table). `eol=crlf` on
    /// its own already implies `text` for the paths it names, so it needs no help from a blanket
    /// normalization line — confirmed by mutation under the LF-direction predecessor of this
    /// scaffold: deleting `* text=auto` left GitattributesSurvivesAnAutocrlfInputCheckout passing;
    /// the three `eol=` lines carry the fix alone, and that mutation result does not depend on
    /// which letter `eol` names.
    /// <para>
    /// Every path pinned here is InTest-owned: `generate` deletes and rewrites Generated/
    /// wholesale and writes coverage-report.json, `fixtures repair` writes fixtures/**/*.json —
    /// base fixtures and every profile overlay subdirectory alike, since FixtureStore.Load deep-
    /// merges fixtures/{profile}/*.json over fixtures/*.json and both are committed, hand-edited
    /// files — and all of it is now pure-CRLF content (TemplateRenderer.Normalize for the .g.cs
    /// classes, CommittedJsonOptions.NewLine for the JSON writers). Without this file, a clone
    /// with core.autocrlf=input rewrites every one of them to LF on checkout, because nothing else
    /// tells git these particular paths must stay CRLF — core.autocrlf=false does no such thing
    /// (it applies no conversion in either direction, so a file this project's writers already
    /// emit as CRLF round-trips unchanged, coincidentally safe the same way core.autocrlf=true is
    /// under this convention; see GitattributesSurvivesAnAutocrlfInputCheckout in
    /// InitCommandTests.cs for the direct experiment that established this). That checkout-time
    /// rewrite is invisible to `fixtures repair` (FixtureDrift.Compare works on parsed
    /// FixtureDocument objects, not bytes) but not to a byte-for-byte comparison such as
    /// `generate --check`.
    /// </para>
    /// <para>
    /// <c>spec.json</c> joins the list for a URL <c>spec.source</c> (§9): `generate` writes it,
    /// it is committed, and <see cref="Spec.SpecSnapshot.Reprint"/> emits it as pure CRLF like
    /// every other writer here. It is scaffolded unconditionally, for a path source too, because
    /// `init` cannot know that a project will never switch — and a pin for a file that does not
    /// exist matches nothing and costs nothing.
    /// </para>
    /// <para>
    /// <b>One gap worth naming rather than relying on silently:</b> <c>UpgradeCommand</c> writes
    /// this file only when a project has none, never overwriting one that exists (CLAUDE.md's
    /// ownership table). A project scaffolded before <c>spec.json</c> was pinned here, which then
    /// switches to a URL source, therefore keeps a <c>.gitattributes</c> without that line. That
    /// is tolerable only because nothing byte-compares <c>spec.json</c> — it is deliberately not
    /// in <c>GenerateCommand.BuildOutputs</c> (see <see cref="Spec.SpecSnapshot"/>), so a checkout
    /// that flattens it to LF costs a noisy diff on the next `generate`, not a wrong verdict from
    /// `--check`. The remedy is one hand-added line; no command will add it for them.
    /// </para>
    /// </summary>
    internal const string GitattributesContent = """
                                                 # InTest writes these files with CRLF interior line endings (a template Normalize step for
                                                 # generated .g.cs classes, JsonSerializerOptions.NewLine = "\r\n" for the JSON files). A
                                                 # clone with core.autocrlf=input would rewrite them to LF on checkout, with nothing on disk
                                                 # to show why.
                                                 Generated/** text eol=crlf
                                                 coverage-report.json text eol=crlf
                                                 fixtures/**/*.json text eol=crlf
                                                 spec.json text eol=crlf
                                                 """;

    /// <summary>
    /// The exact shape `init` scaffolds at <c>.config/dotnet-tools.json</c>, parameterised only by
    /// the pinned version — hoisted to a single method, internal rather than private, so
    /// <c>UpgradeCommand</c>'s "no manifest found" remedy message can show an adopter this literal
    /// shape instead of a second hand-typed copy that could silently drift from what `init`
    /// actually writes. The same rule this repository already applies to
    /// <see cref="GitattributesContent"/> (a review of the v1-e upgrade work applied it here too,
    /// after finding <c>UpgradeCommand</c> had re-typed this exact JSON by hand).
    /// </summary>
    internal static string DotnetToolsJsonContent(string version) => $$"""
                                                                       {
                                                                         "version": 1,
                                                                         "isRoot": true,
                                                                         "tools": {
                                                                           "intest.cli": { "version": "{{version}}", "commands": ["intest"] }
                                                                         }
                                                                       }
                                                                       """;

    /// <summary>
    /// The remedy for giving both source arguments at once. Named once, because the refusal below
    /// and <c>InitCommandTests</c> must not answer "which one wins" in two voices — neither does:
    /// there is no silent priority pick, per Task 5 (<c>[lockfile-recovery]</c>).
    /// </summary>
    private const string MutuallyExclusiveSourceRemedy =
        "Pass --spec to name the OpenAPI document directly, or --client-lockfile to recover it " +
        "from a client generator's own lockfile (kiota-lock.json) — not both.";

    /// <summary>
    /// Every refusal below runs before the first write, and none of them catches: an exception
    /// this command does not anticipate is §5's exit 2 by way of <c>Program</c>'s crash floor,
    /// which covers every command rather than only the three that remembered to catch. This was
    /// a <c>Run</c>/<c>Scaffold</c> pair until that floor moved up; the wrapper existed only to
    /// hold the catch-all, so it went with it.
    /// <para>
    /// <paramref name="clientLockfilePath"/> is Task 5's <c>[lockfile-recovery]</c> addition — a
    /// team that owns a generated client but not the OpenAPI document it came from can point
    /// <c>--client-lockfile</c> at the client generator's own lockfile (<c>kiota-lock.json</c>)
    /// instead of <c>--spec</c>. <see cref="ClientLockfile.Recover"/> reads the spec location the
    /// generator itself recorded, and — where the lockfile names a client — the
    /// <c>client.kind</c>/<c>client.typeName</c> pair too, both of which get scaffolded into
    /// <c>intest.json</c> alongside <c>spec</c>. Mutually exclusive with <paramref name="specSource"/>:
    /// giving both is refused, naming both, with no silent priority pick — see
    /// <see cref="MutuallyExclusiveSourceRemedy"/>. Defaults to <c>""</c> rather than
    /// <see langword="null"/>, matching <paramref name="specSource"/>'s own convention on this
    /// surface (blank, not absent, is what "not given" means here — see
    /// <see cref="CommandArguments.TryRequireValue"/>), and keeping every pre-existing 3-argument
    /// call site — production and test alike — compiling unchanged.
    /// </para>
    /// </summary>
    public static int Run(string projectRoot, string projectName, string specSource, string clientLockfilePath = "")
    {
        // Every argument, refused the same way, before the first write — §5's exit 2 is "Nothing
        // was written", and an argument is the one thing that can be judged with nothing on disk
        // yet. First failure wins, matching ConfigLoader: a second reporting model is the split
        // ConfigLoader was built to remove, one layer up. See CommandArguments for the shape and
        // for what these three refusals replaced.
        if (!CommandArguments.TryRequireValue(projectRoot, "--project", CommandArguments.ProjectRule, out var projectReason))
        {
            Console.Error.WriteLine(projectReason);
            return ExitCode.ToolError;
        }

        // projectName seeds project.rootNamespace, project.testBaseClass, baseClassName, and the
        // `namespace` declaration of two scaffolded files (TestStartup.cs and
        // <Name>TestBase.cs) — refusing an invalid --name here is what stops a scaffold that
        // cannot compile from ever being written. Checked before the intest.json-already-exists
        // check below: an invalid name is invalid regardless of what is already on disk. No blank
        // check precedes this one: TryValidateDottedName already refuses a blank value, and the
        // ThrowIfNullOrWhiteSpace that used to sit here pre-empted it — a worse guard in front of
        // a better one, turning the message below into a stack trace for the one input that
        // needed it most.
        if (!CSharpIdentifier.TryValidateDottedName(projectName, "--name", out var nameReason))
        {
            Console.Error.WriteLine($"{nameReason} Pass a valid C# name to `intest init --name` — for example \"Orders.ApiTests\".");
            return ExitCode.ToolError;
        }

        // [lockfile-recovery]: --spec and --client-lockfile name the same thing two different
        // ways, so giving both is a contradiction, not a preference — refused before either is
        // acted on, naming both values, rather than one silently winning. Checked ahead of every
        // remaining --spec guard: a blank check, a URL check or the escaping check below could
        // all pass for whichever value happened to be inspected first, silently discarding the
        // other, which is exactly the ambiguity this guard exists to close.
        if (!string.IsNullOrWhiteSpace(specSource) && !string.IsNullOrWhiteSpace(clientLockfilePath))
        {
            Console.Error.WriteLine(
            $"--spec ('{specSource}') and --client-lockfile ('{clientLockfilePath}') cannot " +
            $"both be given. {MutuallyExclusiveSourceRemedy}");
            return ExitCode.ToolError;
        }

        // The recovered client identity, if the lockfile named one — carried past the blank/URL/
        // escaping guards below (which judge specSource, now possibly overwritten from the
        // lockfile) and read again just before the intest.json write.
        string? recoveredClientKind = null;
        string? recoveredClientTypeName = null;

        if (!string.IsNullOrWhiteSpace(clientLockfilePath))
        {
            // ClientLockfile.Recover fails loudly (ClientLockfileException) rather than handing
            // back a null or blank spec source — see that type's own doc comment for why a silent
            // null would resurface, far from here, as ConfigLoader's "spec.source is empty"
            // refusal. Caught the same way GenerateCommand already catches SpecLoadException and
            // ConfigLoadException: print the message bare, exit 2, rather than Program's crash-
            // floor phrasing.
            try
            {
                var recovered = ClientLockfile.Recover(clientLockfilePath);
                specSource = recovered.SpecSource;
                recoveredClientKind = recovered.ClientKind;
                recoveredClientTypeName = recovered.ClientTypeName;
            }
            catch (ClientLockfileException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return ExitCode.ToolError;
            }

            // recoveredClientTypeName reaches mstest-class.scriban in reference position
            // (ApiClient<Orders.ApiClient.OrdersApiClient>()), exactly like intest.json's
            // hand-written client.typeName — the same reasoning ConfigLoader.ReadOptionalClientConfig
            // already applies, and the same guard, checked here (before any write) because a
            // lockfile is adopter-controlled input like any other, not InTest's own output.
            if (recoveredClientTypeName is not null &&
                !CSharpIdentifier.TryValidateDottedName(recoveredClientTypeName, "the client type recovered from --client-lockfile", out var typeNameReason))
            {
                Console.Error.WriteLine(
                $"{typeNameReason} --client-lockfile '{clientLockfilePath}' named a client type " +
                "intest cannot scaffold. Fix the lockfile, or omit --client-lockfile and add a " +
                "\"client\" section to intest.json by hand afterward.");
                return ExitCode.ToolError;
            }
        }

        // Normalised once, before either escaping step, and — [lockfile-recovery] — after
        // specSource may have been overwritten from a recovered lockfile above, never before: this
        // must normalise whatever `specSource` actually ends up meaning, or a lockfile-recovered
        // source would be normalised from its stale pre-recovery value (empty, when only
        // --client-lockfile was given) instead of the value that was actually recovered. For a
        // path source it is reused at both sites below — the intest.json JSON string and the
        // csproj's <InTestSpecSource> element must agree on the same slash-normalised value, or
        // ConfigLoader.Load and the built project would disagree on what "the spec" is. For a URL
        // source only intest.json carries it, because the csproj names the snapshot instead (see
        // buildTimeSpecPath below); the two still agree, they just agree that the URL is the
        // source and spec.json is where the build finds it.
        //
        // Harmless on a URL, not merely tolerable: a backslash is not valid unescaped in a URL
        // path, so a well-formed URL contains none for this to rewrite. Pinned rather than
        // assumed — InitCommandTests asserts a URL reaches intest.json byte-for-byte as typed.
        var normalizedSpecSource = specSource.Replace("\\", "/");

        // Two questions about --spec, asked in the order that makes the second one meaningful:
        // is there a value at all, and can XML 1.0 represent the value there is. A blank --spec
        // is not an escaping problem — MSBuildPropertyValue would escape "" perfectly happily and
        // hand back "", which then reaches ConfigLoader.Load as an empty spec.source: a state
        // that command already has to refuse separately. Refusing it here means `init` never
        // writes a config it knows `generate` will reject. Unchanged by [lockfile-recovery]: a
        // recovered lockfile's specSource has already replaced the parameter above, so this guard
        // sees exactly the same kind of value either way — "--spec" is still the right name for
        // it, because a lockfile-recovered spec.source is written into intest.json's "spec"
        // section exactly like a directly-typed one, through the same variable.
        if (!CommandArguments.TryRequireValue(specSource, "--spec", SpecRemedy, out var blankSpecReason))
        {
            Console.Error.WriteLine(blankSpecReason);
            return ExitCode.ToolError;
        }

        // The same rule as the blank guard above — `init` never writes a config it knows
        // `generate` will reject. A URL is a supported kind of source now (§9), so what is left
        // to judge is whether it is a well-formed one; "https://" alone clears SpecLoader.IsUrl
        // and is not a URL anyone can fetch.
        //
        // This guard's ancestor refused every URL, and the defect it was built for is worth
        // keeping on the record because it is what justifies judging --spec here at all rather
        // than leaving it to `generate`. Measured before any guard existed:
        // `init --spec https://example.com/openapi.json` printed "Initialised Orders.ApiTests.
        // Next: `intest generate`." and exited 0, writing the whole scaffold — then displaced the
        // contradiction onto a different command one step later, where it surfaced as
        // "Spec file not found: <projectRoot>\https://example.com/openapi.json". A malformed URL
        // reaches exactly that outcome today if it gets past here.
        //
        // Ahead of the escaping guard below rather than after it: a URL is perfectly
        // representable in XML, so TryEscape would pass it through and this sentence would only
        // be reached for a URL that also carried something XML 1.0 cannot represent. "Is this a
        // source InTest can read" is the question that makes the escaping question meaningful,
        // the same ordering the blank check above already uses.
        //
        // Judged on the normalised value, like the escaping guard below and unlike the blank
        // check above: normalisation runs before both, and it is the normalised value that
        // reaches intest.json, so this judges exactly what would have been written.
        var specSourceIsUrl = SpecLoader.IsUrl(normalizedSpecSource);
        if (specSourceIsUrl &&
            !SpecFetcher.TryValidateUrl(normalizedSpecSource, "--spec", out var urlReason))
        {
            Console.Error.WriteLine($"{urlReason} {SpecRemedy}");
            return ExitCode.ToolError;
        }

        // specSource reaches the generated .csproj as bare XML inside <InTestSpecSource> — a
        // value XML 1.0 cannot represent in any form would fail the build before any InTest code
        // runs, with an MSBuild error that has no visible connection to `--spec`. Same reasoning
        // as --name above: an invalid value is invalid regardless of what is already on disk, so
        // this is checked before the intest.json-already-exists check too.
        //
        // This guard refuses before *both* writes below, even though a C0 control character is
        // representable in a JSON string (intest.json alone could carry it). Two reasons the
        // csproj alone doesn't cover: (1) `init` writes an atomic scaffold — a valid intest.json
        // paired with a .csproj that cannot load is not a partial success, it is a project that
        // cannot build, with the failure displaced away from the flag that caused it; (2) the
        // same guard is what stops an unpaired surrogate reaching JsonSerializer below, which
        // would otherwise silently substitute U+FFFD and hand the adopter a *different* path with
        // no error at all.
        if (!MSBuildPropertyValue.TryEscape(normalizedSpecSource, "--spec", out var specSourceEscaped, out var specReason))
        {
            Console.Error.WriteLine($"{specReason} {SpecRemedy}");
            return ExitCode.ToolError;
        }

        // What <InTestSpecSource> names is a *local file the build can copy*, which for a URL
        // source is the snapshot rather than the source. MSBuild cannot copy from https://, and
        // §9 is explicit that a URL source's .csproj copies "that local file … exactly as above" —
        // the snapshot is precisely what makes the two source kinds identical from the build's
        // point of view. intest.json still records the URL: that is the *source*, and spec.json
        // is its materialization (SpecSnapshot).
        //
        // Note this property has no consumer in the scaffold today — §9's
        // <Content Include="$(InTestSpecSource)" Link="spec.json" …> item is designed and not
        // built, and TestHost reads spec-schemas.json rather than spec.json. Setting it correctly
        // now costs nothing and means that item needs no second fix when it lands. See the plan's
        // "What does not change".
        //
        // Escaping still runs on the value the adopter typed, above, even when its result is
        // discarded here: TryEscape is also what stops an unpaired surrogate reaching
        // JsonSerializer below, where it would silently become U+FFFD and write a *different*
        // spec.source into intest.json with no error at all. That hazard belongs to the value,
        // not to where it ends up.
        var buildTimeSpecPath = specSourceIsUrl ? SpecSnapshot.FileName : specSourceEscaped;

        if (File.Exists(Path.Combine(projectRoot, "intest.json")))
        {
            Console.Error.WriteLine("intest.json already exists. `init` never overwrites; edit it or delete it first.");
            return ExitCode.AlreadyInitialised;
        }

        var baseClassName = projectName.Split('.')[0] + "TestBase";
        Directory.CreateDirectory(Path.Combine(projectRoot, ".config"));

        // Present only when --client-lockfile named a client (recoveredClientTypeName already
        // validated as a dotted C# name above). Built as a standalone C# string rather than
        // inline in the raw-string template below because the template is a DOUBLE-$ raw string
        // ($$""") — {{ }} is its interpolation hole and a bare { or } is literal text, the
        // opposite of the SINGLE-$ raw string the scaffolded .csproj uses further down. Composing
        // this fragment out here means it is spliced in as one {{clientSection}} hole, with no
        // risk of its own braces being misread as more interpolation holes by the outer template.
        // recoveredClientKind is always "kiota" whenever recoveredClientTypeName is non-null —
        // ClientLockfile.Recover's only supported shape — so no further validation of kind is
        // needed here; ConfigLoader.ReadOptionalClientConfig re-validates it as any other
        // hand-edited intest.json would be, the same one-loader-validates-everything discipline
        // every other scaffolded setting on this surface already gets.
        var clientSection = recoveredClientTypeName is not null
            ? $",\n  \"client\": {{ \"kind\": \"{recoveredClientKind}\", \"typeName\": \"{recoveredClientTypeName}\" }}"
            : string.Empty;

        Write(projectRoot, "intest.json", $$"""
                                            {
                                              "schemaVersion": {{ConfigLoader.SupportedSchemaVersion}},
                                              "intestVersion": "{{CliVersion.Current}}",
                                              "spec": { "source": {{JsonSpecSource(normalizedSpecSource)}}, "producer": "auto" },
                                              "project": {
                                                "name": "{{projectName}}",
                                                "rootNamespace": "{{projectName}}",
                                                "framework": "mstest",
                                                "assertions": ["shouldly"],
                                                "testBaseClass": "{{projectName}}.{{baseClassName}}"
                                              }{{clientSection}}
                                            }
                                            """);

        // [scaffold-reads-itself] (docs/superpowers/plans/2026-08-23-trunk-based-versioning.md,
        // Task 1): InTest.Runtime's PackageReference below used to hardcode "0.1.0". A CLI built
        // as a prerelease (say 0.1.0-preview.3) would then scaffold a project asking for
        // InTest.Runtime 0.1.0 exactly — a version that will not exist on nuget.org until the
        // first stable release, so the scaffolded restore could never succeed. CliVersion.Current
        // is already in hand a couple of hundred lines above (intestVersion, in the intest.json
        // write); emitting it here too makes the scaffold self-consistent by construction —
        // whatever version this CLI was built as, that is what it references, with no literal to
        // drift.
        //
        // Neither of this repository's two escaping rules applies to the interpolation below, and
        // that is a deliberate reading rather than an oversight:
        //   - Naming.CSharpLiteral governs values pasted inside a C# string literal in *generated*
        //     source — mstest-class.scriban's output, read by a C# compiler later. This string is
        //     a C# interpolated raw string literal in InitCommand.cs itself; raw string literals
        //     have no escape sequences to begin with, and an interpolation hole is filled by
        //     .ToString() verbatim, so there is no C#-literal-escaping step here at all, let alone
        //     one CSharpLiteral would need to perform.
        //   - Naming.MSBuildPropertyValue governs adopter-supplied text landing in MSBuild
        //     *element* text content — see <InTestSpecSource>{specSourceEscaped}</InTestSpecSource>
        //     above. Its XML layer escapes '&' and '<' but deliberately never the double-quote
        //     that delimits an *attribute* value (see its own remarks), because it was written for
        //     the element-text position. Version="..." below is an attribute value, a different
        //     grammatical slot; reusing an escaper shaped for the wrong slot would silently mis-
        //     escape a literal '"' if one ever reached it (it would not be turned into &quot;, so
        //     it would prematurely close the attribute), rather than actually being safe here.
        // Applying either would also be solving a problem CliVersion.Current cannot pose: unlike
        // --spec, this is not adopter input. It is CliVersion.Read()'s output — an
        // AssemblyInformationalVersionAttribute value stripped at the first '+' — constrained by
        // construction to SemVer 2.0's grammar (ASCII letters, digits, '.', '-'; semver.org §9),
        // which contains none of '&', '<', '"', '%', '$', '@', ';', '?', '*': every character
        // either escaping rule exists to guard against. A build that somehow produced an
        // informational version outside that grammar would be a build-system defect to fix at the
        // source, not a value either escaper could safely paper over here.
        Write(projectRoot, $"{projectName}.csproj", $"""
                                                     <Project Sdk="Microsoft.NET.Sdk">
                                                       <PropertyGroup>
                                                         <TargetFramework>net10.0</TargetFramework>
                                                         <Nullable>enable</Nullable>
                                                         <ImplicitUsings>enable</ImplicitUsings>
                                                         <IsPackable>false</IsPackable>
                                                         <RunSettingsFilePath>$(MSBuildProjectDirectory)/{projectName}.runsettings</RunSettingsFilePath>
                                                         <InTestSpecSource>{buildTimeSpecPath}</InTestSpecSource>
                                                       </PropertyGroup>
                                                       <ItemGroup>
                                                         <PackageReference Include="MSTest.TestFramework" Version="4.3.3" />
                                                         <PackageReference Include="MSTest.TestAdapter" Version="4.3.3" />
                                                         <PackageReference Include="MSTest.Analyzers" Version="4.3.3" />
                                                         <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.9.0" />
                                                         <PackageReference Include="Shouldly" Version="4.3.0" />
                                                         <!-- The MSTest adapter ProjectReferences the neutral InTest.Runtime package and
                                                              brings it in transitively, so only this one reference is scaffolded — both
                                                              packages declare types in namespace InTest.Runtime, so nothing downstream (the
                                                              template, testBaseClass) needs to know two packages are involved. -->
                                                         <PackageReference Include="InTest.Runtime.MSTest" Version="{CliVersion.Current}" />
                                                       </ItemGroup>
                                                       <ItemGroup>
                                                         <Content Include="Generated/spec-schemas.json" Link="spec-schemas.json" CopyToOutputDirectory="PreserveNewest" />
                                                         <Content Include="Generated/spec-paths.json" Link="spec-paths.json" CopyToOutputDirectory="PreserveNewest" />
                                                         <!-- TestHost resolves configuration from AppContext.BaseDirectory, so these must
                                                              travel to the output directory. Without them every generated project fails at
                                                              AssemblyInitialize with a FileNotFoundException for appsettings.json. -->
                                                         <Content Include="appsettings*.json" CopyToOutputDirectory="PreserveNewest" />
                                                         <!-- FixtureStore also loads from AppContext.BaseDirectory. Without this every
                                                              fixture is invisible at runtime — every operation that needs one 400s or sends
                                                              literal "TODO:..." sentinels, and nothing at compile time catches it. -->
                                                         <Content Include="fixtures/**/*.json" CopyToOutputDirectory="PreserveNewest" />
                                                       </ItemGroup>
                                                       <!-- Parallelization intent lives in AssemblyInfo.cs. The MSBuild properties below
                                                            generate a second assembly attribute, which fails as CS0579 inside obj/. -->
                                                       <Target Name="InTestGuardParallelizeProperties" BeforeTargets="BeforeBuild"
                                                               Condition="'$(MSTestParallelizeScope)' != '' or '$(MSTestParallelizeWorkers)' != ''">
                                                         <Error Code="INTEST0001"
                                                                Text="Parallelization intent is declared in AssemblyInfo.cs. Remove MSTestParallelizeScope/MSTestParallelizeWorkers from the project file and edit [assembly: Parallelize] or [assembly: DoNotParallelize] instead." />
                                                       </Target>
                                                     </Project>
                                                     """);

        Write(projectRoot, "AssemblyInfo.cs", """
                                              using Microsoft.VisualStudio.TestTools.UnitTesting;

                                              // The single authoritative declaration of parallelization intent.
                                              // Do NOT set MSTestParallelizeScope in the .csproj — it generates this attribute,
                                              // and two of them is a build error.
                                              [assembly: DoNotParallelize]
                                              """);

        Write(projectRoot, ".editorconfig", """
                                            root = true

                                            [*.cs]
                                            dotnet_diagnostic.CA1707.severity = none
                                            """);

        // Content and reasoning both live once, on GitattributesContent above — UpgradeCommand
        // writes the identical constant for a project `init` never got the chance to scaffold it
        // for, and a second hand-copied literal here would be exactly the kind of duplicate this
        // repository's "one canonical explanation" rule exists to prevent.
        Write(projectRoot, ".gitattributes", GitattributesContent);

        Write(projectRoot, "TestStartup.cs", $$"""
                                               using InTest.Runtime;
                                               using Microsoft.Extensions.Configuration;
                                               using Microsoft.Extensions.DependencyInjection;
                                               using Microsoft.VisualStudio.TestTools.UnitTesting;

                                               namespace {{projectName}};

                                               [TestClass]
                                               public static class TestStartup
                                               {
                                                   [AssemblyInitialize]
                                                   public static async Task AssemblyInit(TestContext context)
                                                   {
                                                       TestHost.ConfigureServices = Register;
                                                       await TestHost.InitializeAsync(context, context.CancellationToken);
                                                   }

                                                   /// <summary>Drains any fixture teardown registered during AssemblyInit — runs even
                                                   /// when AssemblyInit itself failed, and never fails the run: see
                                                   /// TestHost.CleanupAsync for why a drain failure is written to the test log instead
                                                   /// of thrown.</summary>
                                                   [AssemblyCleanup]
                                                   public static async Task AssemblyCleanup(TestContext context)
                                                   {
                                                       await TestHost.CleanupAsync(context);
                                                   }

                                                   /// <summary>Team-owned registrations. Add configuration providers here. AuthHandler
                                                   /// is already attached to InTestClients.Api; a secured API needs only an
                                                   /// ITestTokenProvider registered below — do not also append a DelegatingHandler of
                                                   /// your own, or two handlers will set Authorization and the last one registered
                                                   /// silently wins. See "Auth" in Phase 3 of getting-started.md for a worked
                                                   /// example.</summary>
                                                   private static void Register(IServiceCollection services, IConfiguration configuration)
                                                   {
                                                       // StaticTokenProvider ships as the one-identity, one-token implementation; write
                                                       // your own (like YourTokenProvider below) for more than one identity, which the
                                                       // wrong-scope 403 cases need — and declare each identity's Scopes, or a read-only
                                                       // identity's own read operations can never produce a provable 403. Catalog and
                                                       // Inventory declare no `security` and register nothing at all — they cannot,
                                                       // since StaticTokenProvider needs a real token neither has a source for — so this
                                                       // stays commented for the same reason the IAssemblyFixture example below does: a
                                                       // live registration here would reference a type that does not exist yet, breaking
                                                       // every fresh scaffold's build before a team has written one. See "Auth" in Phase
                                                       // 3 of getting-started.md for a worked example.
                                                       // services.AddSingleton<ITestTokenProvider, YourTokenProvider>();

                                                       // Per-request fixtures: path and query parameter values live in fixtures/, not
                                                       // here — each operation that needs one has a fixture file with a "TODO:"
                                                       // sentinel for every value it requires. Fill those in by hand, or run
                                                       // `intest fixtures repair` after a spec change to add sentinels for anything
                                                       // newly required.

                                                       // A different kind of fixture: assembly fixtures seed data once before any test
                                                       // runs, registered here rather than under fixtures/. Order is resolved
                                                       // automatically from DependsOn; profile-restrict with AppliesTo. See "fixtures"
                                                       // in Phase 5 of getting-started.md for a worked example.
                                                       // services.AddSingleton<IAssemblyFixture, YourFixture>();
                                                   }
                                               }
                                               """);

        Write(projectRoot, $"{baseClassName}.cs", $$"""
                                                    using InTest.Runtime;

                                                    namespace {{projectName}};

                                                    /// <summary>Your shared helpers. Generated classes derive from this.</summary>
                                                    public abstract class {{baseClassName}} : ApiTestBase
                                                    {
                                                    }
                                                    """);

        Write(projectRoot, "appsettings.json", """
                                               {
                                                 "InTest": {
                                                   "DefaultProfile": "local",
                                                   "Readiness": {
                                                     "Enabled": true,
                                                     "Path": "/health/ready",
                                                     "ExpectStatus": 200,
                                                     "ConsecutiveSuccesses": 2,
                                                     "TimeoutSeconds": 120,
                                                     "IntervalSeconds": 3
                                                   }
                                                 },
                                                 // BaseUrl substitutes for the spec's servers[0].url: the spec's paths are appended
                                                 // to it. If those paths already begin with a prefix such as /api, this value must
                                                 // NOT repeat it, or every request 404s against configuration that looks correct.
                                                 "Api": { "BaseUrl": "https://localhost:5001/" }
                                               }
                                               """);

        Write(projectRoot, "appsettings.staging.json", """
                                                       { "Api": { "BaseUrl": "https://REPLACE-ME.example.com/" } }
                                                       """);

        Write(projectRoot, $"{projectName}.runsettings", """
                                                         <?xml version="1.0" encoding="utf-8"?>
                                                         <RunSettings>
                                                           <TestRunParameters>
                                                             <!-- Uncommenting this PINS the profile and makes INTEST_PROFILE unreachable.
                                                                  Leave commented unless this file is environment-specific. -->
                                                             <!-- <Parameter name="profile" value="staging" /> -->
                                                           </TestRunParameters>
                                                           <MSTest>
                                                             <TestTimeout>60000</TestTimeout>
                                                           </MSTest>
                                                         </RunSettings>
                                                         """);

        Write(projectRoot, Path.Combine(".config", "dotnet-tools.json"), DotnetToolsJsonContent(CliVersion.Current));

        Console.WriteLine($"Initialised {projectName}. Next: `intest generate`.");
        return ExitCode.Ok;
    }

    /// <summary>
    /// Internal rather than private: <c>UpgradeCommand</c> reuses this exact normalization
    /// (<c>ReplaceLineEndings("\r\n") + "\r\n"</c>, matching every other file `init` scaffolds) to
    /// write <see cref="GitattributesContent"/> for a project `init` itself refuses to touch —
    /// see that field's doc comment for why the two commands must share the constant.
    /// [crlf-everywhere]: this normalizes every file `init` writes, not only the three paths
    /// `GitattributesContent` pins — intest.json, the .csproj, TestStartup.cs and the rest are
    /// scaffolded once, at write time, so this call site is their only source of line-ending
    /// truth; nothing in `.gitattributes` needs to pin them separately for the initial write to
    /// be CRLF (a subsequent checkout of the adopter's own repo is the adopting team's own
    /// `.gitattributes`/core.autocrlf concern from that point on, per CLAUDE.md's ownership
    /// table).
    /// </summary>
    internal static void Write(string root, string relativePath, string content)
        => File.WriteAllText(Path.Combine(root, relativePath), content.ReplaceLineEndings("\r\n") + "\r\n");

    /// <summary>
    /// Renders <paramref name="value"/> as a JSON string literal, quotes included, for splicing
    /// directly into the intest.json template in place of a hand-written <c>"..."</c> pair. Uses
    /// <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> rather than the default encoder:
    /// verified against both for the ordinary path
    /// <c>../Orders/bin/Debug/net10.0/orders.json</c>, the output is byte-identical either way, so
    /// the choice cannot be proven by round-tripping a hazardous value through
    /// <c>ConfigLoader.Load</c> — both encoders produce valid JSON encoding the same string. It
    /// buys readability instead: the default encoder is markedly more aggressive about escaping
    /// ordinary path and URL characters into <c>\uXXXX</c>, which serves no purpose in a file
    /// adopters read by hand, since JSON string escaping already makes every character losslessly
    /// representable without it. See
    /// <c>InitCommandTests.WritesAmpersandAndNonAsciiCharactersLiterallyIntoIntestJson</c> for a
    /// character each encoder renders differently, and for what reverting this encoder choice
    /// breaks.
    /// </summary>
    private static string JsonSpecSource(string value) => JsonSerializer.Serialize(value, SpecSourceJsonOptions);
}

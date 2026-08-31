using InTest.Cli.Commands;
using Shouldly;

namespace InTest.Golden.Tests;

/// <summary>
/// The real signal. A golden file proves output is stable; only a compiler proves it is valid.
/// </summary>
[TestClass]
public class CompileVerificationTests
{
    private string _root = null!;

    /// <summary>
    /// Parameterized on the spec file name (Task 10 item 7's shared-setup pattern extended
    /// here): every other project scaffold detail — namespace, base class, csproj, assembly
    /// info — is identical regardless of which spec is under test, so only the one thing that
    /// actually varies between <see cref="GeneratedProjectCompiles"/> and
    /// <see cref="GeneratedProjectWithHostileSpecTextCompiles"/> is a parameter rather than a
    /// second hand-copied method. Returns the project root rather than only setting
    /// <see cref="_root"/>, so a test that calls this must capture the result into a local and
    /// use that local from then on — a future test method that forgets to call
    /// <see cref="CreateProject"/> then fails at compile time (its local <c>root</c> simply
    /// doesn't exist) rather than at runtime against an obscure <c>_root == null!</c>.
    /// <see cref="_root"/> itself is still set here, purely so <see cref="RemoveProject"/> has
    /// something to clean up regardless of whether the caller kept its own reference. A test
    /// that needs a project root must obtain it from here, not from <see cref="_root"/> directly
    /// — <see cref="_root"/> exists only for <see cref="RemoveProject"/>. A test that never calls
    /// <see cref="CreateProject"/> at all skips past this guard entirely, which is exactly how
    /// this regression happened once already.
    /// <para>
    /// Task 8 Step 0: <paramref name="framework"/> parameterizes three things, not one — measured
    /// against a real xUnit compile attempt, not assumed from the mstest shape. The <c>.csproj</c>
    /// string (xunit.v3 + <c>&lt;OutputType&gt;Exe&lt;/OutputType&gt;</c> instead of the MSTest
    /// package trio), <c>AssemblyInfo.cs</c>'s content (<c>[assembly:
    /// Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]</c> instead of <c>[assembly:
    /// DoNotParallelize]</c> — the one rev 1 of the plan missed entirely: without it, a scaffolded
    /// xUnit project still carries an MSTest-only assembly attribute and CS0246s on
    /// <c>Microsoft.VisualStudio.TestTools.UnitTesting</c>, which this project's <c>.csproj</c>
    /// never references for that branch), and <c>intest.json</c>'s own <c>"framework"</c> value,
    /// which <c>ConfigLoader.RequireSupportedFramework</c> reads and <c>TemplateRenderer</c>
    /// selects a template on. Defaults to <c>"mstest"</c>, last of the parameter list, so every
    /// pre-existing call site above keeps compiling and keeps building an MSTest project exactly
    /// as before.
    /// </para>
    /// <para>
    /// Unlike the scaffold InitCommand writes (which references the adapter package by name, at
    /// <c>CliVersion.Current</c>), both branches here <c>ProjectReference</c> the adapter project
    /// directly by path — this test built against local source before Task 8 and continues to
    /// after it; only the <em>shape</em> of the reference differs by framework (an xUnit project
    /// also needs <c>xunit.v3</c> itself and <c>&lt;OutputType&gt;Exe&lt;/OutputType&gt;</c>,
    /// neither of which the MSTest branch carries — see <c>InTest.Runtime.xUnit.csproj</c>'s own
    /// comment for the measured "xUnit.net v3 test projects must be executable" error a library-
    /// shaped project gets without it).
    /// </para>
    /// </summary>
    private string CreateProject(string specFileName, string framework = "mstest")
    {
        _root = Path.Combine(Path.GetTempPath(), "intest-compile-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);

        File.Copy(Path.Combine(AppContext.BaseDirectory, "Specs", specFileName), Path.Combine(_root, specFileName));

        File.WriteAllText(Path.Combine(_root, "intest.json"), $$"""
                                                                { "schemaVersion": 1, "spec": { "source": "{{specFileName}}" },
                                                                  "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "InTest.Runtime.ApiTestBase",
                                                                               "framework": "{{framework}}" } }
                                                                """);

        // Task 6 of the NUnit framework pack plan extended this to a three-way switch — see that
        // plan's own [nunit-is-vstest] and [one-package]: NUnit joins MSTest's "ordinary class
        // library, no OutputType" shape rather than xUnit's executable one, but needs its own
        // adapter package pair (NUnit + NUnit3TestAdapter) and its own AssemblyInfo.cs attribute
        // ([assembly: NUnit.Framework.LevelOfParallelism(1)] — [nunit-is-sequential] — rather than
        // fixing a live parallelism hazard the way the other two frameworks' attributes do).
        switch (framework)
        {
            case "xunit":
            {
                var xunitRuntimeProject = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "src", "InTest.Runtime.xUnit", "InTest.Runtime.xUnit.csproj"));

                File.WriteAllText(Path.Combine(_root, "Orders.ApiTests.csproj"), $"""
                                                                                  <Project Sdk="Microsoft.NET.Sdk">
                                                                                    <PropertyGroup>
                                                                                      <TargetFramework>net10.0</TargetFramework>
                                                                                      <Nullable>enable</Nullable>
                                                                                      <ImplicitUsings>enable</ImplicitUsings>
                                                                                      <IsPackable>false</IsPackable>
                                                                                      <OutputType>Exe</OutputType>
                                                                                    </PropertyGroup>
                                                                                    <ItemGroup>
                                                                                      <PackageReference Include="xunit.v3" Version="4.0.0" />
                                                                                      <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.9.0" />
                                                                                      <ProjectReference Include="{xunitRuntimeProject}" />
                                                                                    </ItemGroup>
                                                                                  </Project>
                                                                                  """);

                File.WriteAllText(Path.Combine(_root, "AssemblyInfo.cs"), """
                                                                          [assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]
                                                                          """);
                break;
            }

            case "nunit":
            {
                var nunitRuntimeProject = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "src", "InTest.Runtime.NUnit", "InTest.Runtime.NUnit.csproj"));

                // No <OutputType>Exe</OutputType> — that was xunit.v3's requirement ([one-package]:
                // NUnit alone compiles an ordinary class library under classic VSTest).
                File.WriteAllText(Path.Combine(_root, "Orders.ApiTests.csproj"), $"""
                                                                                  <Project Sdk="Microsoft.NET.Sdk">
                                                                                    <PropertyGroup>
                                                                                      <TargetFramework>net10.0</TargetFramework>
                                                                                      <Nullable>enable</Nullable>
                                                                                      <ImplicitUsings>enable</ImplicitUsings>
                                                                                      <IsPackable>false</IsPackable>
                                                                                    </PropertyGroup>
                                                                                    <ItemGroup>
                                                                                      <PackageReference Include="NUnit" Version="4.6.1" />
                                                                                      <PackageReference Include="NUnit3TestAdapter" Version="6.3.0" />
                                                                                      <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.9.0" />
                                                                                      <ProjectReference Include="{nunitRuntimeProject}" />
                                                                                    </ItemGroup>
                                                                                  </Project>
                                                                                  """);

                File.WriteAllText(Path.Combine(_root, "AssemblyInfo.cs"), """
                                                                          [assembly: NUnit.Framework.LevelOfParallelism(1)]
                                                                          """);
                break;
            }

            default:
            {
                var runtimeProject = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "src", "InTest.Runtime.MSTest", "InTest.Runtime.MSTest.csproj"));

                File.WriteAllText(Path.Combine(_root, "Orders.ApiTests.csproj"), $"""
                                                                                  <Project Sdk="Microsoft.NET.Sdk">
                                                                                    <PropertyGroup>
                                                                                      <TargetFramework>net10.0</TargetFramework>
                                                                                      <Nullable>enable</Nullable>
                                                                                      <ImplicitUsings>enable</ImplicitUsings>
                                                                                      <IsPackable>false</IsPackable>
                                                                                    </PropertyGroup>
                                                                                    <ItemGroup>
                                                                                      <PackageReference Include="MSTest.TestFramework" Version="4.3.3" />
                                                                                      <PackageReference Include="MSTest.TestAdapter" Version="4.3.3" />
                                                                                      <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.9.0" />
                                                                                      <ProjectReference Include="{runtimeProject}" />
                                                                                    </ItemGroup>
                                                                                  </Project>
                                                                                  """);

                File.WriteAllText(Path.Combine(_root, "AssemblyInfo.cs"), """
                                                                          using Microsoft.VisualStudio.TestTools.UnitTesting;

                                                                          [assembly: DoNotParallelize]
                                                                          """);
                break;
            }
        }

        return _root;
    }

    [TestCleanup]
    public void RemoveProject()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [TestMethod]
    public async Task GeneratedProjectCompiles()
    {
        var root = CreateProject("orders.json");

        // Specs/orders.json's GET /orders/{id} has a required path parameter, so under decision
        // 1 that operation needs a fixture. This test never calls `init` — it hand-writes
        // intest.json above — but repair needs only intest.json plus the spec, so it works
        // directly here too. Without this, generate now reports drift instead of compiling
        // anything (Task 4).
        (await FixturesRepairCommand.RunAsync(root, CancellationToken.None)).ShouldBe(0);

        (await GenerateCommand.RunAsync(root, CancellationToken.None)).ShouldBe(0);

        // Pins that this test actually compiled orders.json, not some other spec — the same
        // premise its sibling GeneratedProjectWithHostileSpecTextCompiles pins for its own spec.
        // A future edit that accidentally swaps CreateProject's argument (exactly what happened
        // here once already) would otherwise still build a syntactically valid but wrong project
        // and this test would stay green having compiled the wrong thing. FixtureParameter and
        // the auth-guard calls only exist because orders.json declares a required path parameter
        // and a secured operation with scopes — hostile-text.json's four plain parameterless
        // GETs can never produce either.
        var generated = await File.ReadAllTextAsync(Path.Combine(root, "Generated", "OrdersTests.g.cs"));
        generated.ShouldContain("FixtureParameter(\"getOrderById\", \"id\")",
        customMessage: "orders.json's required path parameter is missing from the generated source — did CreateProject run against the wrong spec?");
        generated.ShouldContain("RequireMultipleIdentities();",
        customMessage: "orders.json's auth-guard case is missing from the generated source — did CreateProject run against the wrong spec?");

        var (exitCode, output) = await ProcessRunner.RunAsync("dotnet", $"build \"{root}\" --nologo -v q");

        exitCode.ShouldBe(0, $"Generated project failed to compile:{Environment.NewLine}{output}");
    }

    /// <summary>
    /// The real proof that spec-derived text is escaped before it lands in a generated C#
    /// string literal — a string assertion can only confirm a backslash appears somewhere;
    /// only the compiler can confirm the result is valid C#.
    /// <para>
    /// Every operation in Specs/hostile-text.json is deliberately parameterless with no JSON
    /// request body, so none of them ever trips TestPlanBuilder.cs's <c>needsFixture</c> gate —
    /// see that file's comment on the <c>if (needsFixture &amp;&amp; !FixtureDocument.TryValidateOperationKey(...))</c>
    /// check for the canonical explanation of why the gate stays that narrow and what it means
    /// for a hostile operationId to reach <see cref="Rendering.TemplateRenderer"/> unvalidated.
    /// This is the exact live path the reported defect travels: a fully valid OpenAPI document
    /// whose parameterless operation's operationId embeds a C#-literal-breaking character.
    /// </para>
    /// <para>
    /// The spec's four operations each isolate one site: the first's operationId contains both
    /// <c>"</c> and <c>\</c>; the second exercises a hostile path template (still parameterless,
    /// so it stays fixture-free too); the third exercises a hostile query parameter name — an
    /// optional query parameter with no example or default never gets a fixture-sentinelled
    /// value (see <c>FixtureComposer.ParameterValue</c>), so it appears in
    /// <c>TestCasePlan.QueryParameterNames</c> without ever making the operation need a fixture;
    /// the fourth carries an embedded LF and CR in its operationId, proving CSharpLiteral.Escape's
    /// full C# grammar set through a real compile rather than only CSharpLiteralTests' unit
    /// assertions — this is what actually confirms <c>OperationKey.Resolve</c>'s <c>.Trim()</c>
    /// does not strip an embedded (as opposed to leading/trailing) newline before it reaches the
    /// template. The three other New_Line_Characters the grammar also forbids — NEL (U+0085), LS
    /// (U+2028), PS (U+2029) — are exercised only at the unit level in CSharpLiteralTests; all
    /// three were confirmed (by direct experiment against csc, and by tracing them through
    /// SpecLoader into a parsed operationId) to survive JSON parsing and to break a real
    /// compile if left raw, so the gap is unproven-through-this-pipeline, not untested.
    /// A hostile path *parameter* name could not be added the same way: any path parameter is
    /// unconditionally sentinelled (decision 1), so it unconditionally sets NeedsFixture — that
    /// site's escaping is covered instead by TemplateRendererEscapingTests, which does not need
    /// a real spec to reach it.
    /// </para>
    /// <para>
    /// No <c>FixturesRepairCommand.RunAsync</c> call here, unlike <see cref="GeneratedProjectCompiles"/>:
    /// every operation in hostile-text.json is deliberately fixture-free (that's the whole
    /// premise), so <c>GenerateCommand</c>'s drift check — which only ever looks at cases where
    /// <c>NeedsFixture</c> is true — has nothing to compare and repair would report "Nothing to
    /// repair." Confirmed, not assumed: this test passes identically without the call.
    /// </para>
    /// <para>
    /// This test's real hazard, and the reason it asserts on the generated file's content before
    /// building it: it can pass while proving nothing. If a future change ever made a
    /// parameterless operation need a fixture, <c>TestPlanBuilder.cs</c>'s <c>needsFixture &amp;&amp;</c>
    /// gate would then skip all four hostile operations, <c>GenerateCommand</c> would report
    /// them skipped and still return 0, the generated class would be empty, <c>dotnet build</c>
    /// would succeed trivially, and this test would stay green having exercised none of the
    /// escaping it claims to. The <c>ShouldContain</c> calls below pin the premise directly —
    /// each hostile operation's escaped form must actually appear in the generated source —
    /// before the compile assertion is allowed to mean anything.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task GeneratedProjectWithHostileSpecTextCompiles()
    {
        var root = CreateProject("hostile-text.json");

        (await GenerateCommand.RunAsync(root, CancellationToken.None)).ShouldBe(0);

        var generated = await File.ReadAllTextAsync(Path.Combine(root, "Generated", "WidgetsTests.g.cs"));

        // One assertion per hostile operation in the spec, each pinned to the exact escaped
        // text TemplateRenderer must have produced — not merely "a backslash appears somewhere".
        // A vacuous pass (operations skipped, class empty) fails every one of these before the
        // build assertion below is even reached.
        generated.ShouldContain("""RequireFixture("list\"Widgets\\Escaped");""",
        customMessage: "the quote+backslash operationId case did not reach the renderer escaped");
        generated.ShouldContain("""InTestUrl.Build("/widgets/say\"hi\\there")""",
        customMessage: "the hostile path template case did not reach the renderer escaped");
        generated.ShouldContain("""FixtureQueryParameters("searchWidgets", "so\"rt\\key")""",
        customMessage: "the hostile query parameter name case did not reach the renderer escaped");
        generated.ShouldContain("""RequireFixture("list\nThings\rMore");""",
        customMessage: "the embedded LF/CR operationId case did not reach the renderer escaped");

        var (exitCode, output) = await ProcessRunner.RunAsync("dotnet", $"build \"{root}\" --nologo -v q");

        exitCode.ShouldBe(0, $"Generated project failed to compile:{Environment.NewLine}{output}");
    }

    /// <summary>
    /// Task 8 Step 1's fifth xUnit case, and the one whose own justification rev 1 of the plan got
    /// wrong — worth stating plainly rather than silently fixing. Rev 1 argued this case matters
    /// under xUnit because the hostile text reaches the trx <c>testName</c> attribute and the
    /// runner's console line. <b>That is false under this plan's own chosen Task 6 mapping</b>:
    /// <c>xunit-class.scriban</c> emits the display name via <c>[Trait("Description", …)]</c>
    /// (see that template, and <c>ApiTestBase.BeginTest</c>'s <c>TestContext.Current.Test?.TestDisplayName</c>),
    /// which keeps a case's description out of both the trx <c>testName</c> and the console
    /// output entirely — hostile spec text on this path never reaches either. Kept anyway, on its
    /// real merit: it is the only proof that <c>CSharpLiteral.Escape</c>'s output is valid C#
    /// inside the <i>second</i> template's own literal sites (<c>RequireFixture("…")</c>,
    /// <c>InTestUrl.Build("…")</c>, <c>FixtureQueryParameters("…", "…")</c>) — a compile-only
    /// concern, identical in kind to <see cref="GeneratedProjectWithHostileSpecTextCompiles"/>
    /// above, just proved against xunit-class.scriban's copy of those same call sites rather than
    /// mstest-class.scriban's. That is also why this lives here, in
    /// <c>CompileVerificationTests</c>, rather than beside a run-against-a-stub case in
    /// <c>GeneratedSuiteExecutionTests</c> — nothing about it needs a live request to go anywhere;
    /// the compiler is the only oracle this test needs. (If a future change moves the display-name
    /// mapping to <c>[Fact(DisplayName = …)]</c> instead, rev 1's original justification becomes
    /// true again and this case should move to a run-against-a-stub test that asserts on the trx
    /// <c>testName</c>/console line directly — the two decisions are coupled, per the plan.)
    /// </summary>
    [TestMethod]
    public async Task GeneratedProjectWithHostileSpecTextCompilesUnderXunit()
    {
        var root = CreateProject("hostile-text.json", framework: "xunit");

        (await GenerateCommand.RunAsync(root, CancellationToken.None)).ShouldBe(0);

        var generated = await File.ReadAllTextAsync(Path.Combine(root, "Generated", "WidgetsTests.g.cs"));

        // Identical needles to GeneratedProjectWithHostileSpecTextCompiles above: TestCasePlan
        // computes every *_literal field once, before TemplateRenderer picks a template — Task 7
        // already proved TemplateEscapingGuardTests holds both templates to the same *_literal
        // naming rule, so the escaped text itself must be byte-identical between the two
        // frameworks. What differs, and what only a real compile can prove, is whether
        // xunit-class.scriban's own copy of these call sites still compiles once this exact text
        // is spliced in.
        generated.ShouldContain("""RequireFixture("list\"Widgets\\Escaped");""",
        customMessage: "the quote+backslash operationId case did not reach the renderer escaped");
        generated.ShouldContain("""InTestUrl.Build("/widgets/say\"hi\\there")""",
        customMessage: "the hostile path template case did not reach the renderer escaped");
        generated.ShouldContain("""FixtureQueryParameters("searchWidgets", "so\"rt\\key")""",
        customMessage: "the hostile query parameter name case did not reach the renderer escaped");
        generated.ShouldContain("""RequireFixture("list\nThings\rMore");""",
        customMessage: "the embedded LF/CR operationId case did not reach the renderer escaped");

        var (exitCode, output) = await ProcessRunner.RunAsync("dotnet", $"build \"{root}\" --nologo -v q");

        exitCode.ShouldBe(0, $"Generated xUnit project failed to compile:{Environment.NewLine}{output}");
    }

    /// <summary>
    /// The reviewer-requested case <c>TemplateRendererClientTests</c>'s
    /// <c>AppendsTheCancellationTokenToABareCallChainButNotToASelfClosingOverride</c> already
    /// covers as a rendered-string assertion, but nothing compiled one — exactly the gap that hid
    /// the CS0149 defect <c>TemplateRenderer.BuildClientCallExpression</c>'s own doc comment
    /// names: before that fix, a self-closing <c>client-map.json</c> override (one that already
    /// spells its own argument list, the documented escape hatch getting-started.md's own worked
    /// example uses) got a second <c>(cancellationToken: …)</c> argument list appended
    /// unconditionally, producing
    /// <c>GetOrderByIdAsync(...)(cancellationToken: TestContext.CancellationToken)</c> — "method
    /// group cannot be invoked twice", a real compiler error a string-content assertion can never
    /// catch. This test builds a real client-routed project with a self-closing override and lets
    /// the compiler be the oracle, the same way <see cref="GeneratedProjectCompiles"/> already is
    /// for the raw-HTTP path.
    /// <para>
    /// <c>orders.json</c>'s <c>getOrderById</c> declares its <c>id</c> path parameter as a plain
    /// <c>type: string</c> (no <c>format: uuid</c>), so it resolves to
    /// <see cref="InTest.Cli.Planning.PathParameterKind.String"/> and the <c>{id}</c> placeholder substitutes
    /// to a bare <c>FixtureParameter(...)</c> call with no <c>.Parse(...)</c> wrapper — matching
    /// the fake client's own <c>string id</c> parameter below without needing a type-conversion
    /// wrapper to also compile correctly.
    /// </para>
    /// <para>
    /// The fake client type is written directly into the scaffolded project root (picked up by
    /// the SDK-style csproj's default <c>**/*.cs</c> glob, the same way
    /// <see cref="CreateProject"/>'s own <c>AssemblyInfo.cs</c> already is) rather than referenced
    /// from a real generated Kiota/NSwag package — this test only needs a type that compiles
    /// against the override's call shape, not a real generator's output; <c>client.kind</c> is set
    /// to <c>"kiota"</c> arbitrarily, since an override bypasses <c>ClientCallPlanner</c>'s
    /// per-kind gating entirely regardless of which kind is configured.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task GeneratedProjectWithASelfClosingClientMapOverrideCompiles()
    {
        var root = CreateProject("orders.json");

        File.WriteAllText(Path.Combine(root, "intest.json"), """
                                                              { "schemaVersion": 1, "spec": { "source": "orders.json" },
                                                                "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "InTest.Runtime.ApiTestBase",
                                                                             "framework": "mstest" },
                                                                "client": { "kind": "kiota", "typeName": "Orders.ApiClient.OrdersApiClient" } }
                                                              """);

        // orders.json's other operation, listOrders (GET /orders, no path parameter, no query
        // parameter, no request body), qualifies for the Kiota convention on its own — the fake
        // client below is not a real Kiota builder chain, so it needs an override too, or
        // ClientCallPlanner.Resolve would derive "Api.Orders.GetAsync" against a type that has no
        // such member. Overridden with the same self-closing shape as getOrderById below, rather
        // than only overriding the one operation this test cares about, so the whole class routes
        // through the fake client the same way a real adopter project fully configured for a
        // client section would — the point of this test is proving a self-closing override
        // compiles, not proving convention derivation withholds correctly (already covered by
        // ClientCallPlannerTests and TestPlanBuilderTests).
        File.WriteAllText(Path.Combine(root, "client-map.json"), """
                                                                  { "overrides": {
                                                                      "getOrderById": "GetOrderByIdAsync({id}, cancellationToken: TestContext.CancellationToken)",
                                                                      "listOrders": "ListOrdersAsync(cancellationToken: TestContext.CancellationToken)"
                                                                  } }
                                                                  """);

        File.WriteAllText(Path.Combine(root, "FakeOrdersApiClient.cs"), """
                                                                         namespace Orders.ApiClient;

                                                                         // Stands in for a real Kiota/NSwag-generated client — only the two call
                                                                         // shapes client-map.json's overrides above name need to exist for this
                                                                         // test's project to compile.
                                                                         public sealed class OrdersApiClient
                                                                         {
                                                                             public Task<object?> GetOrderByIdAsync(string id, CancellationToken cancellationToken = default)
                                                                                 => throw new NotImplementedException();

                                                                             public Task<object?> ListOrdersAsync(CancellationToken cancellationToken = default)
                                                                                 => throw new NotImplementedException();
                                                                         }
                                                                         """);

        (await FixturesRepairCommand.RunAsync(root, CancellationToken.None)).ShouldBe(0);

        (await GenerateCommand.RunAsync(root, CancellationToken.None)).ShouldBe(0);

        // Pins the premise before the compile assertion is allowed to mean anything (the same
        // discipline GeneratedProjectWithHostileSpecTextCompiles's own comment argues for): the
        // override must actually have reached the renderer, substituted, and closed its own
        // argument list — not merely "the project happened to build".
        var generated = await File.ReadAllTextAsync(Path.Combine(root, "Generated", "OrdersTests.g.cs"));
        generated.ShouldContain(
        "await ApiClient<Orders.ApiClient.OrdersApiClient>().GetOrderByIdAsync(FixtureParameter(\"getOrderById\", \"id\"), cancellationToken: TestContext.CancellationToken);",
        customMessage: "the self-closing override did not reach the renderer substituted and unmodified — did the cancellation-token append fire a second time?");

        var (exitCode, output) = await ProcessRunner.RunAsync("dotnet", $"build \"{root}\" --nologo -v q");

        exitCode.ShouldBe(0, $"Generated project with a self-closing client-map.json override failed to compile:{Environment.NewLine}{output}");
    }

    /// <summary>
    /// A second layer on the same recurring defect this file exists to close: a generated shape
    /// asserted only as text, with nothing compiling it. <c>ClientCallPlannerTests</c> pins
    /// <see cref="Planning.ClientCallPlanner.BuildNSwagConvention"/>'s argument order as a string
    /// assertion; before this test, nothing had ever built a project against it, which is exactly
    /// how a wrong argument order shipped and stayed green on this branch.
    /// <see cref="Planning.ClientCallPlanner.BuildNSwagConvention"/>'s own doc comment records the
    /// measured defect this test exists to catch a regression of: NSwag binds a generated method's
    /// positional path-parameter arguments in the spec's declared <c>parameters</c>-array order, not
    /// path-template order — the convention originally shipped assuming the opposite. The measured
    /// reproduction there (nswag 14.7.1) is a path <c>/customers/{customerId}/orders/{orderId}</c>
    /// whose <c>parameters</c> array declares <c>orderId</c> before <c>customerId</c>; the generated
    /// client emits <c>GetCustomerOrderAsync(System.Guid orderId, System.Guid customerId, ...)</c> —
    /// declared order and path-template order disagree, and because both parameters happened to
    /// share a type (<c>Guid</c>), the wrong-order call still compiled, silently asserting against
    /// the wrong resource instead of failing loudly. That is precisely the gap a compile-verification
    /// test cannot close by reproducing the bug verbatim: a same-typed reproduction compiles under
    /// either argument order, so only a string assertion — never the compiler — could ever have
    /// caught a regression back to path-template order.
    /// <para>
    /// This test therefore deliberately does not reuse the real bug's same-typed shape. It reuses the
    /// same operation and the same declared-order-inverts-path-order arrangement — <c>orderId</c>
    /// declared first, <c>customerId</c> second, while the path template itself orders
    /// <c>customerId</c> before <c>orderId</c> — but gives the two parameters two <i>different</i>
    /// <see cref="Planning.PathParameterKind"/> values instead of both being
    /// <see cref="Planning.PathParameterKind.Guid"/>: <c>orderId</c> is declared <c>type: string,
    /// format: uuid</c> (<see cref="Planning.PathParameterKind.Guid"/>), <c>customerId</c> is a bare
    /// <c>type: integer</c> with no <c>format</c> (<see cref="Planning.PathParameterKind.Integer"/> —
    /// absent or non-<c>int64</c> format still resolves to <c>Integer</c>, per
    /// <c>TestPlanBuilder.ResolvePathParameterKind</c>'s own doc comment, the same fact
    /// <see cref="GeneratedProjectWithIntegerAndLongPathParametersCompiles"/> relies on for its own
    /// bare-<c>integer</c> case). With two different types, a regression back to path-template order
    /// can no longer bind any overload of the fake client below at all — passing a
    /// <c>Guid.Parse(...)</c> result where the fake client declares <c>int customerId</c>, or vice
    /// versa, is a real CS1503 argument-type mismatch, not a silently-compiling swap. Only a type
    /// mismatch can turn an ordering regression into a build failure, which is the entire point of a
    /// compile-verification test — the real, same-typed bug could never have been caught this way.
    /// </para>
    /// <para>
    /// <c>listOrders</c> (no path parameter, unrelated to the ordering fix) stays in the spec for the
    /// same reason it was already here: it is the second operation that independently qualifies for
    /// the NSwag convention (no query parameter, no request body, an underscore-free declared
    /// <c>operationId</c>), preserving the "two qualifying operations both reach the renderer through
    /// <see cref="Planning.ClientCallPlanner.Resolve"/> with nothing to override" coverage without a
    /// second near-duplicate test. The fake client's token-carrying/non-token-carrying overload pair
    /// on <c>GetCustomerOrderAsync</c> preserves this file's other piece of NSwag coverage too:
    /// <c>cancellationToken:</c>-by-name still has to select the right overload out of two, now
    /// against a two-parameter signature instead of one.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task GeneratedProjectWithAnNSwagConventionCallCompiles()
    {
        var root = CreateProject("orders.json");

        // orders.json's own id parameter is a bare `type: string` on a single-path-parameter
        // operation, which can never distinguish declared order from path-template order — this
        // test needs two path parameters of two different PathParameterKind values, declared in an
        // order that inverts the path template's own order, so the copied spec is overwritten with
        // getCustomerOrder: orderId (format: uuid -> Guid) declared first, customerId (bare integer
        // -> Integer) declared second, while the path template itself orders customerId before
        // orderId. listOrders is kept as the second, no-path-parameter operation — see this
        // method's own doc comment for why.
        File.WriteAllText(Path.Combine(root, "orders.json"), """
                                                              {
                                                                "openapi": "3.0.3",
                                                                "info": { "title": "Orders", "version": "1.0" },
                                                                "paths": {
                                                                  "/orders": {
                                                                    "get": {
                                                                      "operationId": "listOrders",
                                                                      "tags": ["Orders"],
                                                                      "responses": { "200": { "description": "ok", "content": { "application/json": {
                                                                        "schema": { "type": "array", "items": { "$ref": "#/components/schemas/Order" } } } } } }
                                                                    }
                                                                  },
                                                                  "/customers/{customerId}/orders/{orderId}": {
                                                                    "get": {
                                                                      "operationId": "getCustomerOrder",
                                                                      "tags": ["Orders"],
                                                                      "parameters": [
                                                                        { "name": "orderId", "in": "path", "required": true, "schema": { "type": "string", "format": "uuid" } },
                                                                        { "name": "customerId", "in": "path", "required": true, "schema": { "type": "integer" } }
                                                                      ],
                                                                      "responses": {
                                                                        "200": { "description": "ok", "content": { "application/json": {
                                                                          "schema": { "$ref": "#/components/schemas/Order" } } } },
                                                                        "404": { "description": "not found" }
                                                                      }
                                                                    }
                                                                  }
                                                                },
                                                                "components": {
                                                                  "schemas": {
                                                                    "Order": {
                                                                      "type": "object",
                                                                      "required": ["id", "quantity"],
                                                                      "properties": {
                                                                        "id": { "type": "string" },
                                                                        "quantity": { "type": "integer", "minimum": 1 },
                                                                        "notes": { "type": "string", "nullable": true }
                                                                      }
                                                                    }
                                                                  }
                                                                }
                                                              }
                                                              """);

        File.WriteAllText(Path.Combine(root, "intest.json"), """
                                                              { "schemaVersion": 1, "spec": { "source": "orders.json" },
                                                                "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "InTest.Runtime.ApiTestBase",
                                                                             "framework": "mstest" },
                                                                "client": { "kind": "nswag", "typeName": "Orders.NSwagClient.OrdersClient" } }
                                                              """);

        // Written in real NSwag declared-order (measured, see BuildNSwagConvention's own doc
        // comment): orderId (Guid) before customerId (int), matching the spec's own parameters
        // array above, not the path template's customerId-before-orderId order. A regression in
        // ClientCallPlanner back to path-template order would try to pass a Guid.Parse(...) result
        // where this signature declares int customerId (and an int.Parse(...) result where it
        // declares System.Guid orderId) — neither overload below can bind that call, so the
        // regression fails the build (CS1503) rather than compiling with a silently swapped
        // argument order. The token-carrying/non-token-carrying overload pair on
        // GetCustomerOrderAsync preserves the cancellationToken-by-name overload-targeting coverage
        // the original single-parameter version of this test already had.
        File.WriteAllText(Path.Combine(root, "FakeNSwagOrdersClient.cs"), """
                                                                           namespace Orders.NSwagClient;

                                                                           public sealed class OrdersClient
                                                                           {
                                                                               public Task<object?> GetCustomerOrderAsync(System.Guid orderId, int customerId)
                                                                                   => throw new NotImplementedException();

                                                                               public Task<object?> GetCustomerOrderAsync(System.Guid orderId, int customerId, CancellationToken cancellationToken)
                                                                                   => throw new NotImplementedException();

                                                                               public Task<object?> ListOrdersAsync(CancellationToken cancellationToken = default)
                                                                                   => throw new NotImplementedException();
                                                                           }
                                                                           """);

        (await FixturesRepairCommand.RunAsync(root, CancellationToken.None)).ShouldBe(0);

        (await GenerateCommand.RunAsync(root, CancellationToken.None)).ShouldBe(0);

        // Pins the premise before the compile assertion is allowed to mean anything (the same
        // discipline every other test in this file follows): the NSwag convention must actually
        // have been derived in the spec's declared parameter order — orderId before customerId,
        // each with its own .Parse conversion — and reached the renderer, not merely "the project
        // happened to build".
        var generated = await File.ReadAllTextAsync(Path.Combine(root, "Generated", "OrdersTests.g.cs"));
        generated.ShouldContain(
        "await ApiClient<Orders.NSwagClient.OrdersClient>().GetCustomerOrderAsync(Guid.Parse(FixtureParameter(\"getCustomerOrder\", \"orderId\")), int.Parse(FixtureParameter(\"getCustomerOrder\", \"customerId\")), cancellationToken: TestContext.CancellationToken);",
        customMessage: "the NSwag convention-derived call did not reach the renderer in the spec's declared parameter order — a regression back to path-template order would either produce this wrong text or fail to compile against the fake client's (Guid, int) signature");

        var (exitCode, output) = await ProcessRunner.RunAsync("dotnet", $"build \"{root}\" --nologo -v q");

        exitCode.ShouldBe(0, $"Generated project with an NSwag convention-derived call failed to compile:{Environment.NewLine}{output}");
    }

    /// <summary>
    /// The audit <c>[nswag-compile-verification]</c>'s own review asked for: <c>Integer</c> and
    /// <c>Long</c> are the two <see cref="Planning.PathParameterKind"/> values
    /// <c>TemplateRendererClientTests.SplicesAnIntegerPathParameterThroughIntParse</c> and
    /// <c>SplicesALongPathParameterThroughLongParse</c> pin only as rendered strings —
    /// <c>int.Parse(FixtureParameter(...))</c> and <c>long.Parse(FixtureParameter(...))</c> spliced
    /// into a Kiota-shaped indexer. <see cref="SplicesAGuidPathParameterThroughGuidParse"/>'s
    /// production sibling has a compiled proof
    /// (<c>GeneratedSuiteExecutionTests.GeneratedClientRoutedSuccessCaseWithAUuidPathParameterCompilesAgainstTheTypedIndexer</c>,
    /// via <c>GoldenTypedClientSources.FakeOrdersApiClient</c>'s <c>this[Guid position]</c>
    /// overload); before this test, <c>Integer</c>/<c>Long</c> had none — nothing built a project
    /// where either conversion's result was passed to a signature that actually declares
    /// <c>int</c>/<c>long</c>, as opposed to merely rendering the expected substring.
    /// <para>
    /// One test covers both kinds rather than two near-duplicates, the same way
    /// <see cref="GeneratedProjectWithAnNSwagConventionCallCompiles"/> exercises two operations in
    /// one project: two GET-by-id operations sharing the <c>Orders</c> tag (so both land in the
    /// same generated <c>OrdersTests.g.cs</c>) — <c>getOrderById</c>'s <c>id</c> is <c>type:
    /// integer</c> with no <c>format</c> (bare <c>int32</c>, per
    /// <c>TestPlanBuilder.ResolvePathParameterKind</c>'s own doc comment: absent or non-<c>int64</c>
    /// format still resolves to <see cref="Planning.PathParameterKind.Integer"/>), and
    /// <c>getAccountById</c>'s <c>id</c> is <c>type: integer, format: int64</c>, resolving to
    /// <see cref="Planning.PathParameterKind.Long"/>. No <c>client-map.json</c> here, matching
    /// <see cref="GeneratedProjectWithAnNSwagConventionCallCompiles"/>'s reasoning: both operations
    /// have neither a query parameter nor a request body, so
    /// <see cref="Planning.ClientCallPlanner.Resolve"/> derives
    /// <see cref="Planning.ClientCallPlanner.BuildKiotaConvention"/>'s builder-chain expression for
    /// each on its own — <c>Orders[{id}].GetAsync</c> and <c>Accounts[{id}].GetAsync</c> (no
    /// leading <c>Api.</c> segment — unlike <see cref="GoldenTypedClientSources.FakeOrdersApiClient"/>'s
    /// own <c>GET /api/status</c>, neither path below has a literal <c>api</c> segment for
    /// <c>BuildKiotaConvention</c> to pascal-case into one; confirmed directly by generating against
    /// this exact spec before writing the fake client, not assumed from the NSwag test's shape) —
    /// with nothing to override.
    /// </para>
    /// <para>
    /// The fake client below is written as a builder-chain (<c>Api.Orders[int id]</c> /
    /// <c>Api.Accounts[long id]</c>, each returning an item builder with its own
    /// <c>GetAsync(CancellationToken)</c>), not a flat method the way
    /// <see cref="GeneratedProjectWithASelfClosingClientMapOverrideCompiles"/>'s override-only fake
    /// is — a flat method would compile against <c>int.Parse(...)</c>/<c>long.Parse(...)</c> too,
    /// but would not prove the conversion binds a real Kiota item-builder indexer the way
    /// <c>GoldenTypedClientSources.FakeStatusRequestBuilder</c>'s <c>this[Guid position]</c> already
    /// does for the Guid kind; each indexer here is declared with exactly the parameter type the
    /// conversion must produce (<c>int</c>, <c>long</c>), so a mismatched conversion — the wrong
    /// <c>.Parse</c>, or none at all — fails to compile rather than merely reading wrong in a string
    /// assertion.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task GeneratedProjectWithIntegerAndLongPathParametersCompiles()
    {
        var root = CreateProject("orders.json");

        // orders.json's own id parameter is a bare `type: string` (PathParameterKind.String, no
        // conversion) -- this test needs Integer and Long instead, so the copied spec is overwritten
        // with a two-operation variant: getOrderById's id has no format (bare int32) and
        // getAccountById's id is format: int64, same as this file's own Guid variant above
        // overwrites orders.json for its own kind.
        File.WriteAllText(Path.Combine(root, "orders.json"), """
                                                              {
                                                                "openapi": "3.0.3",
                                                                "info": { "title": "Orders", "version": "1.0" },
                                                                "paths": {
                                                                  "/orders/{id}": {
                                                                    "get": {
                                                                      "operationId": "getOrderById",
                                                                      "tags": ["Orders"],
                                                                      "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "integer" } }],
                                                                      "responses": {
                                                                        "200": { "description": "ok", "content": { "application/json": {
                                                                          "schema": { "$ref": "#/components/schemas/Order" } } } },
                                                                        "404": { "description": "not found" }
                                                                      }
                                                                    }
                                                                  },
                                                                  "/accounts/{id}": {
                                                                    "get": {
                                                                      "operationId": "getAccountById",
                                                                      "tags": ["Orders"],
                                                                      "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "integer", "format": "int64" } }],
                                                                      "responses": {
                                                                        "200": { "description": "ok", "content": { "application/json": {
                                                                          "schema": { "$ref": "#/components/schemas/Order" } } } },
                                                                        "404": { "description": "not found" }
                                                                      }
                                                                    }
                                                                  }
                                                                },
                                                                "components": {
                                                                  "schemas": {
                                                                    "Order": {
                                                                      "type": "object",
                                                                      "required": ["id", "quantity"],
                                                                      "properties": {
                                                                        "id": { "type": "string" },
                                                                        "quantity": { "type": "integer", "minimum": 1 },
                                                                        "notes": { "type": "string", "nullable": true }
                                                                      }
                                                                    }
                                                                  }
                                                                }
                                                              }
                                                              """);

        File.WriteAllText(Path.Combine(root, "intest.json"), """
                                                              { "schemaVersion": 1, "spec": { "source": "orders.json" },
                                                                "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "InTest.Runtime.ApiTestBase",
                                                                             "framework": "mstest" },
                                                                "client": { "kind": "kiota", "typeName": "Orders.ApiClient.OrdersApiClient" } }
                                                              """);

        // A builder-chain fake, not a flat method -- see this test's own doc comment for why. Only
        // the two indexer shapes ClientCallPlanner.BuildKiotaConvention derives for this spec's two
        // operations need to exist for this test's project to compile: Orders[int] and
        // Accounts[long] directly on the client type -- no Api property, since neither path below
        // has a literal "api" segment (this test's own doc comment).
        File.WriteAllText(Path.Combine(root, "FakeOrdersApiClient.cs"), """
                                                                         namespace Orders.ApiClient;

                                                                         public sealed class OrdersApiClient
                                                                         {
                                                                             public OrdersRequestBuilder Orders { get; } = new();
                                                                             public AccountsRequestBuilder Accounts { get; } = new();
                                                                         }

                                                                         public sealed class OrdersRequestBuilder
                                                                         {
                                                                             public OrdersItemRequestBuilder this[int id] => new();
                                                                         }

                                                                         public sealed class OrdersItemRequestBuilder
                                                                         {
                                                                             public Task<object?> GetAsync(CancellationToken cancellationToken = default)
                                                                                 => throw new NotImplementedException();
                                                                         }

                                                                         public sealed class AccountsRequestBuilder
                                                                         {
                                                                             public AccountsItemRequestBuilder this[long id] => new();
                                                                         }

                                                                         public sealed class AccountsItemRequestBuilder
                                                                         {
                                                                             public Task<object?> GetAsync(CancellationToken cancellationToken = default)
                                                                                 => throw new NotImplementedException();
                                                                         }
                                                                         """);

        (await FixturesRepairCommand.RunAsync(root, CancellationToken.None)).ShouldBe(0);

        (await GenerateCommand.RunAsync(root, CancellationToken.None)).ShouldBe(0);

        // Pins the premise before the compile assertion is allowed to mean anything (the same
        // discipline every other test in this file follows): both conversions must actually have
        // been derived, substituted, and reached the renderer -- not merely "the project happened
        // to build".
        var generated = await File.ReadAllTextAsync(Path.Combine(root, "Generated", "OrdersTests.g.cs"));
        generated.ShouldContain(
        "await ApiClient<Orders.ApiClient.OrdersApiClient>().Orders[int.Parse(FixtureParameter(\"getOrderById\", \"id\"))].GetAsync(cancellationToken: TestContext.CancellationToken);",
        customMessage: "the Kiota-derived call for the Integer-kind path parameter did not reach the renderer with its int.Parse conversion applied");
        generated.ShouldContain(
        "await ApiClient<Orders.ApiClient.OrdersApiClient>().Accounts[long.Parse(FixtureParameter(\"getAccountById\", \"id\"))].GetAsync(cancellationToken: TestContext.CancellationToken);",
        customMessage: "the Kiota-derived call for the Long-kind path parameter did not reach the renderer with its long.Parse conversion applied");

        var (exitCode, output) = await ProcessRunner.RunAsync("dotnet", $"build \"{root}\" --nologo -v q");

        exitCode.ShouldBe(0, $"Generated project with Integer- and Long-kind path parameters failed to compile:{Environment.NewLine}{output}");
    }

    [TestMethod]
    public async Task RefusesAnInjectionShapedRootNamespaceInsteadOfCompilingIt()
    {
        // Measured before this defect was fixed: this exact rootNamespace made `generate` exit 0
        // and the generated project compile CLEAN. mstest-class.scriban emits
        // "namespace {{ namespace }};" as declaration syntax, not inside quotes, so the trailing
        // "//" comments out the template's own ';' and everything between the semicolon it
        // supplies and that comment — including "public class Injected"'s static constructor —
        // is compiled straight into the test assembly. The assertion here is "generate refused",
        // not "the build failed": by the time a compiler could weigh in, adopter code already
        // shipped into the assembly, which is the regression this test pins.
        var root = CreateProject("orders.json");

        File.WriteAllText(Path.Combine(root, "intest.json"), """
                                                             { "schemaVersion": 1, "spec": { "source": "orders.json" },
                                                               "project": { "rootNamespace": "Orders.ApiTests; public class Injected { static Injected() { System.Console.WriteLine(\"x\"); } } //", "testBaseClass": "InTest.Runtime.ApiTestBase" } }
                                                             """);

        (await GenerateCommand.RunAsync(root, CancellationToken.None)).ShouldBe(2);
        Directory.Exists(Path.Combine(root, "Generated")).ShouldBeFalse();
    }
}

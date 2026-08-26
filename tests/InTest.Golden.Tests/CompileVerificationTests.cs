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
    /// </summary>
    private string CreateProject(string specFileName)
    {
        _root = Path.Combine(Path.GetTempPath(), "intest-compile-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);

        File.Copy(Path.Combine(AppContext.BaseDirectory, "Specs", specFileName), Path.Combine(_root, specFileName));

        File.WriteAllText(Path.Combine(_root, "intest.json"), $$"""
                                                                { "schemaVersion": 1, "spec": { "source": "{{specFileName}}" },
                                                                  "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "InTest.Runtime.ApiTestBase",
                                                                               "framework": "mstest" } }
                                                                """);

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

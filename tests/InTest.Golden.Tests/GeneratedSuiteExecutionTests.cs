using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using InTest.Cli;
using InTest.Cli.Commands;
using Shouldly;

namespace InTest.Golden.Tests;

/// <summary>
/// Scaffolds a project, generates into it, builds it, and <b>runs</b> it against a live stub
/// (<see cref="GoldenApiStub"/>).
/// <para>
/// This exists because of a defect the v0 acceptance run found: <c>init</c> scaffolded
/// appsettings.json but never copied it to the output directory, so every generated project
/// died at AssemblyInitialize. Compile verification passed throughout — it proves generated
/// code builds, never that it runs. Those are different gates and only the first was covered.
/// </para>
/// </summary>
[TestClass]
public class GeneratedSuiteExecutionTests
{
    private const string Spec = """
                                {
                                  "openapi": "3.0.3",
                                  "info": { "title": "Stub", "version": "1.0" },
                                  "paths": {
                                    "/api/status": {
                                      "get": {
                                        "operationId": "getStatus",
                                        "tags": ["Status"],
                                        "responses": {
                                          "200": {
                                            "description": "ok",
                                            "content": {
                                              "application/json": {
                                                "schema": { "$ref": "#/components/schemas/Status" }
                                              }
                                            }
                                          }
                                        }
                                      }
                                    }
                                  },
                                  "components": {
                                    "schemas": {
                                      "Status": {
                                        "type": "object",
                                        "required": ["state"],
                                        "properties": { "state": { "type": "string" } }
                                      }
                                    }
                                  }
                                }
                                """;

    /// <summary>
    /// <see cref="Spec"/> plus a path-parameter operation, used only by
    /// <see cref="FixtureParameterReachesALiveRequestEndToEnd"/>. This is the F1 live proof Task
    /// 4a deferred here (its report, lines 1176-1196): a bare GET with no parameters composes no
    /// fixture at all (decision 1), so it can never prove a fixture is loaded and consumed by a
    /// running test — only an operation with a required parameter can. Kept as a separate spec
    /// rather than folded into <see cref="Spec"/> so the two existing tests below, which build
    /// and run the suite without ever touching <c>fixtures/getStatusById.json</c>, are unaffected
    /// by this addition.
    /// <para>
    /// <c>[typed-path-parameters]</c>: <c>id</c> declares <c>format: uuid</c> — not a bare
    /// <c>type: string</c> — so that
    /// <see cref="GeneratedClientRoutedSuccessCaseWithAUuidPathParameterCompilesAgainstTheTypedIndexer"/>
    /// exercises <c>PathParameterKind.Guid</c> end to end. This is the same spec
    /// <see cref="FixtureParameterReachesALiveRequestEndToEnd"/> already builds and runs over raw
    /// HTTP with no <c>client</c> section configured, so the format change is covered by that
    /// test too: a uuid-formatted string path parameter must still round-trip as a plain fixture
    /// string on the raw-HTTP path, which never converts it.
    /// </para>
    /// </summary>
    private const string SpecWithPathParameter = """
                                                 {
                                                   "openapi": "3.0.3",
                                                   "info": { "title": "Stub", "version": "1.0" },
                                                   "paths": {
                                                     "/api/status": {
                                                       "get": {
                                                         "operationId": "getStatus",
                                                         "tags": ["Status"],
                                                         "responses": {
                                                           "200": {
                                                             "description": "ok",
                                                             "content": {
                                                               "application/json": {
                                                                 "schema": { "$ref": "#/components/schemas/Status" }
                                                               }
                                                             }
                                                           }
                                                         }
                                                       }
                                                     },
                                                     "/api/status/{id}": {
                                                       "get": {
                                                         "operationId": "getStatusById",
                                                         "tags": ["Status"],
                                                         "parameters": [
                                                           { "name": "id", "in": "path", "required": true, "schema": { "type": "string", "format": "uuid" } }
                                                         ],
                                                         "responses": {
                                                           "200": {
                                                             "description": "ok",
                                                             "content": {
                                                               "application/json": {
                                                                 "schema": { "$ref": "#/components/schemas/Status" }
                                                               }
                                                             }
                                                           }
                                                         }
                                                       }
                                                     }
                                                   },
                                                   "components": {
                                                     "schemas": {
                                                       "Status": {
                                                         "type": "object",
                                                         "required": ["state"],
                                                         "properties": { "state": { "type": "string" } }
                                                       }
                                                     }
                                                   }
                                                 }
                                                 """;

    /// <summary>
    /// A create-then-delete pair against <c>/api/items</c>, used only by
    /// <see cref="TheGeneratedSuitePassesTwiceAgainstTheSameStore"/> (Task 8a). Deliberately
    /// separate from <see cref="Spec"/> and <see cref="SpecWithPathParameter"/> for the same
    /// reason those two are separate from each other: this is the only test that needs
    /// <see cref="GoldenApiStub"/>'s stateful <c>POST /api/items</c> / <c>DELETE /api/items/{id}</c>
    /// pair, and keeping it on its own spec means nothing else in this file is affected by it.
    /// </summary>
    private const string SpecWithItemsLifecycle = """
                                                  {
                                                    "openapi": "3.0.3",
                                                    "info": { "title": "Stub", "version": "1.0" },
                                                    "paths": {
                                                      "/api/items": {
                                                        "post": {
                                                          "operationId": "createItem",
                                                          "tags": ["Items"],
                                                          "requestBody": {
                                                            "required": true,
                                                            "content": {
                                                              "application/json": {
                                                                "schema": { "$ref": "#/components/schemas/CreateItemRequest" }
                                                              }
                                                            }
                                                          },
                                                          "responses": {
                                                            "201": { "description": "Created" }
                                                          }
                                                        }
                                                      },
                                                      "/api/items/{id}": {
                                                        "delete": {
                                                          "operationId": "deleteItem",
                                                          "tags": ["Items"],
                                                          "parameters": [
                                                            { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }
                                                          ],
                                                          "responses": {
                                                            "204": { "description": "No Content" }
                                                          }
                                                        }
                                                      }
                                                    },
                                                    "components": {
                                                      "schemas": {
                                                        "CreateItemRequest": {
                                                          "type": "object",
                                                          "required": ["sku"],
                                                          "properties": { "sku": { "type": "string" } }
                                                        }
                                                      }
                                                    }
                                                  }
                                                  """;

    /// <summary>
    /// Plan Task 4, Step 2(b) — the F1 lesson repeated: <see cref="Specs/orders.json"/>'s golden
    /// regeneration proves a declared-error case's <i>text</i> is stable and compiles, never that
    /// it runs. <see cref="GoldenApiStub"/> is unreachable from that corpus at all —
    /// <c>GeneratedSuiteExecutionTests</c> writes its own inline specs to <c>spec.json</c>, so
    /// only one of those can prove a generated 404 test actually receives one over the wire.
    /// <para>
    /// Deliberately its own path, not <c>/api/status/{id}</c> from
    /// <see cref="SpecWithPathParameter"/>: <see cref="GoldenApiStub"/>'s <c>/api/status/</c>
    /// catch-all answers 200 for anything under that prefix, so an unmatchable id sent there would
    /// get a 200 — the opposite of what this test exists to prove. This path matches no arm of
    /// the stub's dispatch at all, so it falls through to the bare <c>_ => (404, ...)</c> default.
    /// </para>
    /// </summary>
    private const string SpecWithDeclaredNotFound = """
                                                    {
                                                      "openapi": "3.0.3",
                                                      "info": { "title": "Stub", "version": "1.0" },
                                                      "paths": {
                                                        "/api/widgets/{id}": {
                                                          "get": {
                                                            "operationId": "getWidgetById",
                                                            "tags": ["Widgets"],
                                                            "parameters": [
                                                              { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }
                                                            ],
                                                            "responses": {
                                                              "200": {
                                                                "description": "ok",
                                                                "content": {
                                                                  "application/json": {
                                                                    "schema": { "$ref": "#/components/schemas/Widget" }
                                                                  }
                                                                }
                                                              },
                                                              "404": { "description": "not found" }
                                                            }
                                                          }
                                                        }
                                                      },
                                                      "components": {
                                                        "schemas": {
                                                          "Widget": {
                                                            "type": "object",
                                                            "required": ["id"],
                                                            "properties": { "id": { "type": "string" } }
                                                          }
                                                        }
                                                      }
                                                    }
                                                    """;

    /// <summary>
    /// Task 5 Step 2's live wire proof: <see cref="Specs/orders.json"/>'s golden regeneration
    /// proves an auth case's <i>text</i> is stable and compiles, never that it runs — the same F1
    /// lesson <see cref="SpecWithDeclaredNotFound"/> exists to close for declared-error cases.
    /// <c>getSecureResource</c> deliberately takes no parameters and needs no request body, so
    /// decision 1 means it composes no fixture at all — nothing here needs
    /// <c>FixturesRepairCommand</c> to fill in a sentinel before the suite can run.
    /// <para>
    /// Its own path, <c>/api/secure</c>, rather than reusing <c>/api/status</c>: that path
    /// already answers 200 unconditionally for several other tests in this file, none of which
    /// register a token provider — folding security onto it would turn every one of those into
    /// an auth test by accident and break them the moment <see cref="GoldenApiStub"/> started
    /// inspecting <c>Authorization</c> there.
    /// </para>
    /// </summary>
    private const string SpecWithSecuredOperation = """
                                                    {
                                                      "openapi": "3.0.3",
                                                      "info": { "title": "Stub", "version": "1.0" },
                                                      "paths": {
                                                        "/api/secure": {
                                                          "get": {
                                                            "operationId": "getSecureResource",
                                                            "tags": ["Secure"],
                                                            "security": [{ "bearerAuth": [] }],
                                                            "responses": {
                                                              "200": {
                                                                "description": "ok",
                                                                "content": {
                                                                  "application/json": {
                                                                    "schema": { "$ref": "#/components/schemas/Status" }
                                                                  }
                                                                }
                                                              }
                                                            }
                                                          }
                                                        }
                                                      },
                                                      "components": {
                                                        "securitySchemes": { "bearerAuth": { "type": "http", "scheme": "bearer" } },
                                                        "schemas": {
                                                          "Status": {
                                                            "type": "object",
                                                            "required": ["state"],
                                                            "properties": { "state": { "type": "string" } }
                                                          }
                                                        }
                                                      }
                                                    }
                                                    """;

    /// <summary>
    /// Task 4 / F11's live wire proof. Unlike <see cref="SpecWithSecuredOperation"/> above
    /// (<c>bearerAuth: []</c>, scope-free), this operation declares a scope — so its generated
    /// wrong-scope 403 case carries a non-empty <c>RequiredScopes</c> and the template emits both
    /// guards, not just <c>RequireMultipleIdentities</c>. Paired with
    /// <see cref="GoldenTokenProviderSources.TwoIdentityTokenProvider"/>'s secondary identity,
    /// which now declares this exact scope: the secondary identity is authorized for this
    /// operation, so <c>RequireSecondaryIdentityLacks</c> must skip the case rather than let it run
    /// and fail against <see cref="GoldenApiStub.HandleScopedSecureResource"/>, which — unlike
    /// <c>HandleSecureResource</c> — answers 200 to any authenticated caller. Its own path, tag,
    /// and operationId, distinct from <see cref="SpecWithSecuredOperation"/>'s, so nothing here
    /// touches the class or test names that test already asserts on.
    /// <para>
    /// Task 4 / F11's other half — closing the gap the first pass left open: nothing above proves
    /// the guard does not skip a case it should not. <c>getScopedSecureResourceRequiringDelete</c>
    /// below requires both <c>"orders.write"</c> and <c>"orders.delete"</c> — deliberately more
    /// than one scope, and deliberately one the secondary identity holds and one it does not
    /// (<see cref="GoldenTokenProviderSources.TwoIdentityTokenProvider"/>'s secondary identity
    /// declares only <c>"orders.write"</c>). A single-scope requirement the secondary entirely
    /// lacks cannot tell <c>All</c> from <c>Any</c> apart — both evaluate to false over one
    /// element — so it would not catch the exact regression this test exists to catch
    /// (containment flipped from <c>All</c> to <c>Any</c>): partial overlap is what makes them
    /// diverge. <c>RequireSecondaryIdentityLacks</c>'s <c>requiredScopes.All(scopes.Contains)</c>
    /// is false (the secondary lacks <c>"orders.delete"</c>), so the case must run rather than
    /// skip; a mutated <c>Any</c> would see <c>"orders.write"</c> and skip it wrongly. Same tag as
    /// <c>getScopedSecureResource</c> above (<c>TestPlanBuilder</c> groups generated classes by an
    /// operation's first tag, <c>ClassName: g.Key + "Tests"</c>), so this lands in the same
    /// <c>ScopedSecureTests.g.cs</c> file and the same trx run the existing assertions already
    /// read — no new file, no new build/test invocation needed to prove it.
    /// </para>
    /// </summary>
    private const string SpecWithScopedSecuredOperation = """
                                                          {
                                                            "openapi": "3.0.3",
                                                            "info": { "title": "Stub", "version": "1.0" },
                                                            "paths": {
                                                              "/api/secure-scoped": {
                                                                "get": {
                                                                  "operationId": "getScopedSecureResource",
                                                                  "tags": ["ScopedSecure"],
                                                                  "security": [{ "bearerAuth": ["orders.write"] }],
                                                                  "responses": {
                                                                    "200": {
                                                                      "description": "ok",
                                                                      "content": {
                                                                        "application/json": {
                                                                          "schema": { "$ref": "#/components/schemas/Status" }
                                                                        }
                                                                      }
                                                                    }
                                                                  }
                                                                }
                                                              },
                                                              "/api/secure-scoped-delete": {
                                                                "get": {
                                                                  "operationId": "getScopedSecureResourceRequiringDelete",
                                                                  "tags": ["ScopedSecure"],
                                                                  "security": [{ "bearerAuth": ["orders.write", "orders.delete"] }],
                                                                  "responses": {
                                                                    "200": {
                                                                      "description": "ok",
                                                                      "content": {
                                                                        "application/json": {
                                                                          "schema": { "$ref": "#/components/schemas/Status" }
                                                                        }
                                                                      }
                                                                    }
                                                                  }
                                                                }
                                                              }
                                                            },
                                                            "components": {
                                                              "securitySchemes": { "bearerAuth": { "type": "http", "scheme": "bearer" } },
                                                              "schemas": {
                                                                "Status": {
                                                                  "type": "object",
                                                                  "required": ["state"],
                                                                  "properties": { "state": { "type": "string" } }
                                                                }
                                                              }
                                                            }
                                                          }
                                                          """;

    /// <summary>
    /// [stage-3b]'s golden proof: a client-routed Success case whose declared response carries no
    /// schema (bodiless 204 — the same shape <see cref="SpecWithItemsLifecycle"/>'s <c>deleteItem</c>
    /// uses) must still be routed through the client and assert only status
    /// (<c>ApiResponseAssertions.ShouldMatchCapturedStatusAsync</c>), never fall back to raw HTTP
    /// the way it silently did before that method existed (see that class's own doc comment and
    /// <c>TemplateRenderer.BuildClientCallExpression</c>'s doc for the full account of the gap this
    /// closes). Deliberately simpler than <see cref="SpecWithItemsLifecycle"/>: no path parameter
    /// and no request body, so decision 1 composes no fixture at all — this test needs none of that
    /// spec's seeding-fixture machinery, because the point here is which assertion the template
    /// emits, not another end-to-end proof that a fixture-backed delete works (already covered by
    /// <c>TheGeneratedSuitePassesTwiceAgainstTheSameStore</c>).
    /// </summary>
    private const string SpecWithBodilessClientRoutedOperation = """
                                                                  {
                                                                    "openapi": "3.0.3",
                                                                    "info": { "title": "Stub", "version": "1.0" },
                                                                    "paths": {
                                                                      "/api/ping": {
                                                                        "get": {
                                                                          "operationId": "ping",
                                                                          "tags": ["Status"],
                                                                          "responses": {
                                                                            "204": { "description": "No Content" }
                                                                          }
                                                                        }
                                                                      }
                                                                    }
                                                                  }
                                                                  """;

    private string _root = null!;
    private GoldenApiStub _stub = null!;

    [TestInitialize]
    public void StartStubAndScaffold()
    {
        _stub = new GoldenApiStub();

        _root = Path.Combine(Path.GetTempPath(), "intest-run-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "spec.json"), Spec);
    }

    [TestCleanup]
    public void StopStub()
    {
        _stub.Dispose();

        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, recursive: true); }
            catch (IOException) { }
        }
    }

    [TestMethod]
    public async Task GeneratedSuiteBuildsAndPassesAgainstALiveService()
    {
        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();

        // This spec's only operation is a bare GET with no body and no parameters, so today it
        // composes no fixture at all (decision 1) and this call is a no-op — but it mirrors what
        // an adopter actually runs, and it is what keeps this test realistic if the spec ever
        // grows an operation that does need one. The fixture pipeline itself — a required
        // parameter actually loaded from a fixture and sent on a live request — is proved by
        // FixtureParameterReachesALiveRequestEndToEnd below, against SpecWithPathParameter.
        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        var build = await ProcessRunner.RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");
        build.ExitCode.ShouldBe(0, $"generated project failed to build:{Environment.NewLine}{build.Output}");

        var test = await ProcessRunner.RunAsync("dotnet", $"test \"{_root}\" --no-build --nologo");

        // The assertion that matters: the suite ran and passed. A FileNotFoundException for
        // appsettings.json, an unresolvable schema bundle, or a broken base URL all fail here
        // and none of them fail a compile check.
        test.Output.ShouldContain("Passed!", customMessage: test.Output);
        test.ExitCode.ShouldBe(0, test.Output);
    }

    /// <summary>
    /// <c>[capture-not-deserialize]</c>'s decisive proof — stage 1 of
    /// <c>docs/superpowers/plans/2026-08-25-intest-typed-client-invocation.md</c>, and per that
    /// plan's own words "the feature's whole viability". <see cref="GoldenApiStub"/> answers
    /// <c>GET /api/status</c> with status 200 (so <see cref="GoldenTypedClientSources.FakeStatusClient"/>
    /// never throws — its own non-2xx check never fires) but a body missing the required
    /// <c>"state"</c> property entirely (<c>{}</c>). <c>FakeStatusClient</c> deserializes that
    /// successfully — <c>state</c> is a plain nullable property, not <c>required</c> — proving the
    /// stream really was read past <see cref="InTest.Runtime.ResponseCaptureHandler"/>'s
    /// <c>Content</c> replacement, so the generated case's own client call never throws either.
    /// <para>
    /// If raw-bytes validation did not survive the client's own deserialization, this test would
    /// pass — the client got a value, no exception anywhere, "Passed!" prints. It must instead
    /// <b>fail</b>, and fail specifically because
    /// <c>ApiResponseAssertions.ShouldMatchCapturedContractAsync</c> (called against
    /// <see cref="InTest.Runtime.ApiTestCore.LastCapturedResponse"/>'s raw, unparsed bytes, not
    /// anything <c>FakeStatusClient</c> itself produced) ran <c>SchemaBundle.Validate</c> against
    /// them and found the missing property. The assertions below distinguish that specific failure
    /// reason from any other kind — a compile error, a different exception, a wrong status — so
    /// this test cannot pass for the wrong reason: the trx message must name a schema violation
    /// (<c>PropertyRequired</c>, NJsonSchema's own kind name for a missing required property) and
    /// must not carry <see cref="GoldenTypedClientSources.FakeStatusClient"/>'s own exception text,
    /// which would only appear if this test somehow observed a client-thrown error instead of
    /// InTest's own verdict.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task ClientRoutedSuccessCaseCatchesASchemaViolationAfterTheClientDeserializes()
    {
        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();

        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        EnableClientCaptureInSpecPaths();
        RegisterFakeStatusClient();

        // Status 200 (so the client itself never throws) but a body missing the required "state"
        // property entirely — a schema violation the client's own lenient deserialization does not
        // notice, but SchemaBundle.Validate, run against the raw captured bytes, does.
        _stub.OverrideStatusResponse(200, "{}");

        var generatedFile = Directory.GetFiles(_root, "StatusTests.g.cs", SearchOption.AllDirectories)
            .ShouldHaveSingleItem("generate should have produced exactly one StatusTests.g.cs");
        File.ReadAllText(generatedFile).ShouldContain("public partial class StatusTests",
        customMessage: "GetStatus_ClientRouted's own partial-class extension needs a partial StatusTests.g.cs to extend");

        var build = await ProcessRunner.RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");
        build.ExitCode.ShouldBe(0, $"generated project failed to build:{Environment.NewLine}{build.Output}");

        var resultsDir = Path.Combine(_root, "TestResults");
        var test = await ProcessRunner.RunAsync("dotnet",
        $"test \"{_root}\" --no-build --nologo --filter \"FullyQualifiedName~GetStatus_ClientRouted\" " +
        $"--logger \"trx;LogFileName=results.trx\" --results-directory \"{resultsDir}\"");

        var trxPath = Directory.GetFiles(resultsDir, "results.trx", SearchOption.AllDirectories)
            .ShouldHaveSingleItem($"expected exactly one results.trx under {resultsDir}:{Environment.NewLine}{test.Output}");

        var trx = XDocument.Load(trxPath);
        var result = trx.Descendants()
            .Where(e => e.Name.LocalName == "UnitTestResult")
            .SingleOrDefault(e => (e.Attribute("testName")?.Value ?? "").Contains("GetStatus_ClientRouted", StringComparison.Ordinal));

        result.ShouldNotBeNull($"GetStatus_ClientRouted did not appear in the trx at all:{Environment.NewLine}{test.Output}");
        result!.Attribute("outcome")?.Value.ShouldBe("Failed",
        $"GetStatus_ClientRouted must fail on the schema violation — if this passed, raw-bytes " +
        $"validation did not survive the client's own deserialization, and the whole feature is " +
        $"worthless:{Environment.NewLine}{test.Output}");

        var failureText = result.Descendants().Where(e => e.Name.LocalName == "Message")
            .Select(e => e.Value).FirstOrDefault() ?? "";

        // The failure reason that matters: a schema violation, named as such, not merely "some"
        // failure. PropertyRequired is NJsonSchema's own ValidationErrorKind name for a missing
        // required property (SchemaViolation.Kind is e.Kind.ToString() verbatim — see
        // SchemaBundle.Validate).
        failureText.ShouldContain("Schema:",
        customMessage: $"GetStatus_ClientRouted failed, but not on a schema violation:{Environment.NewLine}{test.Output}");
        failureText.ShouldContain("PropertyRequired",
        customMessage: $"GetStatus_ClientRouted failed, but the violation was not the missing 'state' property:{Environment.NewLine}{test.Output}");

        // Rules out the failure being FakeStatusClient's own exception surfacing instead of
        // InTest's contract failure — it must not have thrown at all here (status 200, and its
        // deserialization tolerates the missing property), so this text can never legitimately
        // appear.
        failureText.ShouldNotContain("FakeStatusClient: request failed",
        customMessage: $"the failure came from FakeStatusClient's own exception, not InTest's captured-response verdict:{Environment.NewLine}{test.Output}");

        test.ExitCode.ShouldBe(1, test.Output);
    }

    /// <summary>
    /// The happy-path half of <c>[capture-not-deserialize]</c>'s proof, alongside
    /// <see cref="ClientRoutedSuccessCaseCatchesASchemaViolationAfterTheClientDeserializes"/>: a
    /// schema-conforming body must still pass, and — the part a bare "Passed!" would not prove —
    /// <see cref="GoldenTypedClientSources.FakeStatusClient"/> must have actually produced a real,
    /// usable deserialized result from the stream <see cref="InTest.Runtime.ResponseCaptureHandler"/>
    /// replaced, not merely completed without throwing. See
    /// <c>GoldenTypedClientSources.ClientRoutedStatusTests</c>'s own doc for why that second
    /// assertion is placed after <c>ShouldMatchCapturedContractAsync</c> rather than before it.
    /// </summary>
    [TestMethod]
    public async Task ClientRoutedSuccessCaseReceivesAUsableDeserializedResult()
    {
        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();

        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        EnableClientCaptureInSpecPaths();
        RegisterFakeStatusClient();

        // Explicit, even though it matches GoldenApiStub's own unconditional default — stated here
        // so this test's intent does not silently depend on that default never changing.
        _stub.OverrideStatusResponse(200, """{"state":"ok"}""");

        var build = await ProcessRunner.RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");
        build.ExitCode.ShouldBe(0, $"generated project failed to build:{Environment.NewLine}{build.Output}");

        var resultsDir = Path.Combine(_root, "TestResults");
        var test = await ProcessRunner.RunAsync("dotnet",
        $"test \"{_root}\" --no-build --nologo --filter \"FullyQualifiedName~GetStatus_ClientRouted\" " +
        $"--logger \"trx;LogFileName=results.trx\" --results-directory \"{resultsDir}\"");

        var trxPath = Directory.GetFiles(resultsDir, "results.trx", SearchOption.AllDirectories)
            .ShouldHaveSingleItem($"expected exactly one results.trx under {resultsDir}:{Environment.NewLine}{test.Output}");

        var trx = XDocument.Load(trxPath);
        var result = trx.Descendants()
            .Where(e => e.Name.LocalName == "UnitTestResult")
            .SingleOrDefault(e => (e.Attribute("testName")?.Value ?? "").Contains("GetStatus_ClientRouted", StringComparison.Ordinal));

        result.ShouldNotBeNull($"GetStatus_ClientRouted did not appear in the trx at all:{Environment.NewLine}{test.Output}");
        result!.Attribute("outcome")?.Value.ShouldBe("Passed",
        $"GetStatus_ClientRouted should pass against a schema-conforming body, and (per its own " +
        $"source) only reaches its ShouldNotBeNull/ShouldBe(\"ok\") result assertions once the " +
        $"contract assertion above them already passed — a failure here could be either " +
        $"half:{Environment.NewLine}{test.Output}");

        test.ExitCode.ShouldBe(0, test.Output);

        _stub.ReceivedPaths.ShouldContain("/api/status",
        $"the client-routed request never reached the stub over the wire. Paths served: {string.Join(", ", _stub.ReceivedPaths)}");
    }

    /// <summary>
    /// <c>[captured-response-is-the-verdict]</c>'s live proof: a Success case whose typed client
    /// call actually returns 500 must surface InTest's own contract failure — run id, expected vs
    /// actual status, elapsed, body excerpt — not
    /// <see cref="GoldenTypedClientSources.FakeStatusClient"/>'s own generator-specific
    /// <c>FakeApiException</c>, the way a bare NSwag <c>ApiException</c> or Kiota error mapping
    /// would otherwise reach the adopter with none of that context. <see cref="GoldenApiStub"/>
    /// answers 500 to <c>GET /api/status</c>, which makes
    /// <c>FakeStatusClient.GetStatusAsync</c> throw before it ever reaches its own
    /// deserialization — exactly the path <c>ClientRoutedStatusTests.GetStatus_ClientRouted</c>'s
    /// pinned <c>try</c>/exception-filter/<c>catch</c> shape exists to catch and convert into
    /// InTest's own verdict.
    /// </summary>
    [TestMethod]
    public async Task ClientRoutedSuccessCaseSurfacesInTestsOwnContractFailureNotTheClientsException()
    {
        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();

        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        EnableClientCaptureInSpecPaths();
        RegisterFakeStatusClient();

        _stub.OverrideStatusResponse(500, """{"error":"boom"}""");

        var build = await ProcessRunner.RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");
        build.ExitCode.ShouldBe(0, $"generated project failed to build:{Environment.NewLine}{build.Output}");

        var resultsDir = Path.Combine(_root, "TestResults");
        var test = await ProcessRunner.RunAsync("dotnet",
        $"test \"{_root}\" --no-build --nologo --filter \"FullyQualifiedName~GetStatus_ClientRouted\" " +
        $"--logger \"trx;LogFileName=results.trx\" --results-directory \"{resultsDir}\"");

        var trxPath = Directory.GetFiles(resultsDir, "results.trx", SearchOption.AllDirectories)
            .ShouldHaveSingleItem($"expected exactly one results.trx under {resultsDir}:{Environment.NewLine}{test.Output}");

        var trx = XDocument.Load(trxPath);
        var result = trx.Descendants()
            .Where(e => e.Name.LocalName == "UnitTestResult")
            .SingleOrDefault(e => (e.Attribute("testName")?.Value ?? "").Contains("GetStatus_ClientRouted", StringComparison.Ordinal));

        result.ShouldNotBeNull($"GetStatus_ClientRouted did not appear in the trx at all:{Environment.NewLine}{test.Output}");
        result!.Attribute("outcome")?.Value.ShouldBe("Failed",
        $"GetStatus_ClientRouted should fail against a 500 response:{Environment.NewLine}{test.Output}");

        var failureText = result.Descendants().Where(e => e.Name.LocalName == "Message")
            .Select(e => e.Value).FirstOrDefault() ?? "";

        // InTest's own verdict: expected 200 (the operation's declared Success status), got 500 —
        // ShouldMatchCapturedContractAsync's own Failure() message shape.
        failureText.ShouldContain("expected 200, got 500",
        customMessage: $"GetStatus_ClientRouted did not fail with InTest's own expected-vs-actual status message:{Environment.NewLine}{test.Output}");

        // The negative half that actually proves [captured-response-is-the-verdict]: the client's
        // own exception — swallowed by GetStatus_ClientRouted's second catch — must never reach
        // the trx at all.
        failureText.ShouldNotContain("FakeStatusClient: request failed",
        customMessage: $"the client's own FakeApiException leaked into the failure instead of being replaced by InTest's own verdict:{Environment.NewLine}{test.Output}");
        failureText.ShouldNotContain("FakeApiException",
        customMessage: $"the client's own exception type name leaked into the failure:{Environment.NewLine}{test.Output}");

        test.ExitCode.ShouldBe(1, test.Output);

        _stub.ReceivedPaths.ShouldContain("/api/status",
        $"the client-routed request never reached the stub over the wire. Paths served: {string.Join(", ", _stub.ReceivedPaths)}");
    }

    /// <summary>
    /// Stage 3's own golden proof, alongside the three <c>ClientRouted*</c> tests above: those
    /// prove the runtime mechanism works against a <b>hand-written</b> test class
    /// (<c>GoldenTypedClientSources.ClientRoutedStatusTests</c>), written before
    /// <c>ClientCallPlanner</c> or the template branch existed. This proves the same three
    /// verdicts — a schema violation caught after deserialization, a conforming body passing, a
    /// 500 surfacing InTest's own contract failure — against code <c>generate</c> itself emits,
    /// with <c>GenerateCommand</c> resolving the client config, writing
    /// <c>clientCaptureEnabled</c>, and <c>TemplateRenderer</c> rendering the pinned
    /// try/filter/catch shape, none of it hand-written here.
    /// <para>
    /// The scaffold's <c>getStatus</c> operation has no path parameter, so its single Success case
    /// (<c>GetStatus_Contract</c>) becomes the client-routed one directly — no separate
    /// <c>_ClientRouted</c> method name is needed the way the hand-written stage-1b class used
    /// one, because there is only ever one Success case per operation to collide with. This test's
    /// own decisive assertion mirrors <c>ClientRoutedSuccessCaseCatchesASchemaViolationAfterTheClientDeserializes</c>
    /// exactly, against <c>GetStatus_Contract</c> instead of <c>GetStatus_ClientRouted</c>.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task GeneratedClientRoutedSuccessCaseCatchesASchemaViolationAfterTheClientDeserializes()
    {
        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();
        AddClientConfig("Stub.ApiTests.FakeOrdersApiClient");
        RegisterFakeOrdersApiClient();

        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        // Decisive proof that GenerateCommand itself resolved the client call and wrote the
        // opt-in flag — no EnableClientCaptureInSpecPaths patch step exists in this test at all,
        // unlike the three ClientRouted* tests above.
        var specPathsPath = Path.Combine(_root, "Generated", "spec-paths.json");
        File.ReadAllText(specPathsPath).ShouldContain("\"clientCaptureEnabled\": true",
        customMessage: "generate should have written clientCaptureEnabled itself once a client " +
                       "config resolved a call for getStatus's Success case");

        var generatedFile = Directory.GetFiles(_root, "StatusTests.g.cs", SearchOption.AllDirectories)
            .ShouldHaveSingleItem("generate should have produced exactly one StatusTests.g.cs");
        var generatedText = File.ReadAllText(generatedFile);
        generatedText.ShouldContain("ApiClient<Stub.ApiTests.FakeOrdersApiClient>()",
        customMessage: "GetStatus_Contract itself should be the client-routed case — there is only one Success case for getStatus to collide with");
        generatedText.ShouldContain("Api.Status.GetAsync(cancellationToken: TestContext.CancellationToken)");

        // Status 200 (so the client itself never throws) but a body missing the required "state"
        // property entirely — a schema violation the client's own lenient deserialization does not
        // notice, but SchemaBundle.Validate, run against the raw captured bytes, does.
        _stub.OverrideStatusResponse(200, "{}");

        var build = await ProcessRunner.RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");
        build.ExitCode.ShouldBe(0, $"generated project failed to build:{Environment.NewLine}{build.Output}");

        var resultsDir = Path.Combine(_root, "TestResults");
        var test = await ProcessRunner.RunAsync("dotnet",
        $"test \"{_root}\" --no-build --nologo --filter \"FullyQualifiedName~GetStatus_Contract\" " +
        $"--logger \"trx;LogFileName=results.trx\" --results-directory \"{resultsDir}\"");

        var trxPath = Directory.GetFiles(resultsDir, "results.trx", SearchOption.AllDirectories)
            .ShouldHaveSingleItem($"expected exactly one results.trx under {resultsDir}:{Environment.NewLine}{test.Output}");

        var trx = XDocument.Load(trxPath);
        var result = trx.Descendants()
            .Where(e => e.Name.LocalName == "UnitTestResult")
            .SingleOrDefault(e => (e.Attribute("testName")?.Value ?? "").Contains("GetStatus_Contract", StringComparison.Ordinal));

        result.ShouldNotBeNull($"GetStatus_Contract did not appear in the trx at all:{Environment.NewLine}{test.Output}");
        result!.Attribute("outcome")?.Value.ShouldBe("Failed",
        $"GetStatus_Contract must fail on the schema violation — if this passed, the generated " +
        $"client-routed case did not preserve raw-bytes validation:{Environment.NewLine}{test.Output}");

        var failureText = result.Descendants().Where(e => e.Name.LocalName == "Message")
            .Select(e => e.Value).FirstOrDefault() ?? "";

        failureText.ShouldContain("Schema:",
        customMessage: $"GetStatus_Contract failed, but not on a schema violation:{Environment.NewLine}{test.Output}");
        failureText.ShouldContain("PropertyRequired",
        customMessage: $"GetStatus_Contract failed, but the violation was not the missing 'state' property:{Environment.NewLine}{test.Output}");

        // Rules out the failure being FakeOrdersApiClient's own exception surfacing instead of
        // InTest's contract failure — status 200 never throws from the fake client.
        failureText.ShouldNotContain("FakeOrdersApiClient: request failed",
        customMessage: $"the failure came from the fake client's own exception, not InTest's captured-response verdict:{Environment.NewLine}{test.Output}");

        test.ExitCode.ShouldBe(1, test.Output);
    }

    /// <summary>
    /// The happy-path half of the generated-code proof, alongside
    /// <see cref="GeneratedClientRoutedSuccessCaseCatchesASchemaViolationAfterTheClientDeserializes"/>:
    /// a schema-conforming body must still pass, over the wire, through the generated
    /// <c>GetStatus_Contract</c> case's client call. Unlike the hand-written
    /// <c>ClientRoutedStatusTests</c> (stage 1b), the generated case never keeps the deserialized
    /// result in a local variable to assert on — the pinned template shape has no reason to — so
    /// this test's proof is that the request reached the stub and the case passed, not a second
    /// assertion on a return value nothing generated code retains.
    /// <para>
    /// Also the third of <c>[warn-on-swallowed-exception]</c>'s three verification scenarios: a
    /// clean run — the typed client call throws nothing at all, second catch never entered — must
    /// warn nothing. <see cref="GeneratedClientRoutedCaseWarnsWhenAnExceptionIsSwallowedAfterACapture"/>
    /// is this test's positive counterpart (a swallowed exception with a capture present warns);
    /// <see cref="GeneratedClientRoutedCaseStillRethrowsWhenNothingWasCaptured"/> is the third
    /// (a swallowed exception with no capture still rethrows, unchanged).
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task GeneratedClientRoutedSuccessCaseReceivesAConformingBody()
    {
        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();
        AddClientConfig("Stub.ApiTests.FakeOrdersApiClient");
        RegisterFakeOrdersApiClient();

        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        _stub.OverrideStatusResponse(200, """{"state":"ok"}""");

        var build = await ProcessRunner.RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");
        build.ExitCode.ShouldBe(0, $"generated project failed to build:{Environment.NewLine}{build.Output}");

        var resultsDir = Path.Combine(_root, "TestResults");
        var test = await ProcessRunner.RunAsync("dotnet",
        $"test \"{_root}\" --no-build --nologo --filter \"FullyQualifiedName~GetStatus_Contract\" " +
        $"--logger \"trx;LogFileName=results.trx\" --results-directory \"{resultsDir}\"");

        var trxPath = Directory.GetFiles(resultsDir, "results.trx", SearchOption.AllDirectories)
            .ShouldHaveSingleItem($"expected exactly one results.trx under {resultsDir}:{Environment.NewLine}{test.Output}");

        var trx = XDocument.Load(trxPath);
        var result = trx.Descendants()
            .Where(e => e.Name.LocalName == "UnitTestResult")
            .SingleOrDefault(e => (e.Attribute("testName")?.Value ?? "").Contains("GetStatus_Contract", StringComparison.Ordinal));

        result.ShouldNotBeNull($"GetStatus_Contract did not appear in the trx at all:{Environment.NewLine}{test.Output}");
        result!.Attribute("outcome")?.Value.ShouldBe("Passed",
        $"GetStatus_Contract should pass against a schema-conforming body:{Environment.NewLine}{test.Output}");

        test.ExitCode.ShouldBe(0, test.Output);

        _stub.ReceivedPaths.ShouldContain("/api/status",
        $"the generated client-routed request never reached the stub over the wire. Paths served: {string.Join(", ", _stub.ReceivedPaths)}");

        // [warn-on-swallowed-exception], third verification scenario: a clean run — the typed
        // client call throws nothing, so the second catch is never entered at all — must warn
        // nothing. Nothing about WarnSwallowedClientException's own message text can appear here
        // by construction if it was never called.
        test.Output.ShouldNotContain("captured response is being used as the test's verdict",
        customMessage: $"a clean run warned about a swallowed exception that never happened:{Environment.NewLine}{test.Output}");
    }

    /// <summary>
    /// <c>[captured-response-is-the-verdict]</c>'s live proof against generated code: a Success
    /// case whose typed client call actually returns 500 must surface InTest's own contract
    /// failure — run id, expected vs actual status, elapsed, body excerpt — not the fake client's
    /// own <c>FakeApiException</c>. Mirrors
    /// <see cref="ClientRoutedSuccessCaseSurfacesInTestsOwnContractFailureNotTheClientsException"/>
    /// exactly, against generated rather than hand-written source.
    /// </summary>
    [TestMethod]
    public async Task GeneratedClientRoutedSuccessCaseSurfacesInTestsOwnContractFailureNotTheClientsException()
    {
        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();
        AddClientConfig("Stub.ApiTests.FakeOrdersApiClient");
        RegisterFakeOrdersApiClient();

        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        _stub.OverrideStatusResponse(500, """{"error":"boom"}""");

        var build = await ProcessRunner.RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");
        build.ExitCode.ShouldBe(0, $"generated project failed to build:{Environment.NewLine}{build.Output}");

        var resultsDir = Path.Combine(_root, "TestResults");
        var test = await ProcessRunner.RunAsync("dotnet",
        $"test \"{_root}\" --no-build --nologo --filter \"FullyQualifiedName~GetStatus_Contract\" " +
        $"--logger \"trx;LogFileName=results.trx\" --results-directory \"{resultsDir}\"");

        var trxPath = Directory.GetFiles(resultsDir, "results.trx", SearchOption.AllDirectories)
            .ShouldHaveSingleItem($"expected exactly one results.trx under {resultsDir}:{Environment.NewLine}{test.Output}");

        var trx = XDocument.Load(trxPath);
        var result = trx.Descendants()
            .Where(e => e.Name.LocalName == "UnitTestResult")
            .SingleOrDefault(e => (e.Attribute("testName")?.Value ?? "").Contains("GetStatus_Contract", StringComparison.Ordinal));

        result.ShouldNotBeNull($"GetStatus_Contract did not appear in the trx at all:{Environment.NewLine}{test.Output}");
        result!.Attribute("outcome")?.Value.ShouldBe("Failed",
        $"GetStatus_Contract should fail against a 500 response:{Environment.NewLine}{test.Output}");

        var failureText = result.Descendants().Where(e => e.Name.LocalName == "Message")
            .Select(e => e.Value).FirstOrDefault() ?? "";

        failureText.ShouldContain("expected 200, got 500",
        customMessage: $"GetStatus_Contract did not fail with InTest's own expected-vs-actual status message:{Environment.NewLine}{test.Output}");

        failureText.ShouldNotContain("FakeOrdersApiClient: request failed",
        customMessage: $"the fake client's own exception leaked into the failure instead of being replaced by InTest's own verdict:{Environment.NewLine}{test.Output}");
        failureText.ShouldNotContain("FakeApiException",
        customMessage: $"the fake client's own exception type name leaked into the failure:{Environment.NewLine}{test.Output}");

        test.ExitCode.ShouldBe(1, test.Output);

        _stub.ReceivedPaths.ShouldContain("/api/status",
        $"the generated client-routed request never reached the stub over the wire. Paths served: {string.Join(", ", _stub.ReceivedPaths)}");
    }

    /// <summary>
    /// <c>[warn-on-swallowed-exception]</c>'s decisive positive proof
    /// (docs/superpowers/plans/2026-08-25-intest-typed-client-invocation.md): the reviewer's exact
    /// failure mode, live — a <c>client-map.json</c> override routing <c>getStatus</c> through
    /// <see cref="GoldenTypedClientSources.FakeOrdersApiClient.GetStatusThenThrowAsync"/> instead of
    /// the plain convention-derived call, which makes one real request (captured normally by
    /// <c>ResponseCaptureHandler</c>) and then throws a synthetic <see cref="InvalidOperationException"/>
    /// that never reaches the wire at all. Before <c>[warn-on-swallowed-exception]</c>, the second
    /// catch's empty body discarded that exception outright — this test's negative half
    /// (nothing in the trx failure text, because the case still passes) is exactly what made that
    /// silent before. Its positive half is the actual proof: the exception's own type and message
    /// must still reach an operator, through <c>TestContext.DisplayMessage</c> at
    /// <see cref="MessageLevel.Warning"/>, on real process stdout — the same channel
    /// <see cref="SkippedFixtureIsNotRunByALiveGeneratedSuite"/> already confirms survives a
    /// <em>passing</em> run for the assembly-scoped diagnostics sink, now confirmed for the
    /// per-test one <c>ApiTestBase.ApiTestInitialize</c> builds.
    /// </summary>
    [TestMethod]
    public async Task GeneratedClientRoutedCaseWarnsWhenAnExceptionIsSwallowedAfterACapture()
    {
        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();
        AddClientConfig("Stub.ApiTests.FakeOrdersApiClient");

        // getStatus would otherwise qualify for the plain Kiota convention (Api.Status.GetAsync)
        // the way every other test in this file relies on — this override exists purely to route
        // it through GetStatusThenThrowAsync instead, the one call shape this test needs.
        File.WriteAllText(Path.Combine(_root, "client-map.json"), """
                                                                   { "overrides": {
                                                                       "getStatus": "GetStatusThenThrowAsync(cancellationToken: TestContext.CancellationToken)"
                                                                   } }
                                                                   """);

        RegisterFakeOrdersApiClient();

        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        // Pins the premise before anything downstream is allowed to mean something: the override
        // must have actually reached the renderer, not merely "the project happened to build".
        var generatedFile = Directory.GetFiles(_root, "StatusTests.g.cs", SearchOption.AllDirectories)
            .ShouldHaveSingleItem("generate should have produced exactly one StatusTests.g.cs");
        File.ReadAllText(generatedFile).ShouldContain(
        "await ApiClient<Stub.ApiTests.FakeOrdersApiClient>().GetStatusThenThrowAsync(cancellationToken: TestContext.CancellationToken);",
        customMessage: "the client-map.json override for getStatus did not reach the renderer");

        // A conforming body: the first, capturing call must succeed cleanly, so the only way this
        // case can fail is the synthetic exception GetStatusThenThrowAsync throws afterward — the
        // exact thing WarnSwallowedClientException must report without failing the test over it.
        _stub.OverrideStatusResponse(200, """{"state":"ok"}""");

        var build = await ProcessRunner.RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");
        build.ExitCode.ShouldBe(0, $"generated project failed to build:{Environment.NewLine}{build.Output}");

        var resultsDir = Path.Combine(_root, "TestResults");
        var test = await ProcessRunner.RunAsync("dotnet",
        $"test \"{_root}\" --no-build --nologo --filter \"FullyQualifiedName~GetStatus_Contract\" " +
        $"--logger \"trx;LogFileName=results.trx\" --results-directory \"{resultsDir}\"");

        var trxPath = Directory.GetFiles(resultsDir, "results.trx", SearchOption.AllDirectories)
            .ShouldHaveSingleItem($"expected exactly one results.trx under {resultsDir}:{Environment.NewLine}{test.Output}");

        var trx = XDocument.Load(trxPath);
        var result = trx.Descendants()
            .Where(e => e.Name.LocalName == "UnitTestResult")
            .SingleOrDefault(e => (e.Attribute("testName")?.Value ?? "").Contains("GetStatus_Contract", StringComparison.Ordinal));

        result.ShouldNotBeNull($"GetStatus_Contract did not appear in the trx at all:{Environment.NewLine}{test.Output}");
        result!.Attribute("outcome")?.Value.ShouldBe("Passed",
        $"GetStatus_Contract should still pass — the swallowed exception must be reported, not " +
        $"fail the test:{Environment.NewLine}{test.Output}");

        test.ExitCode.ShouldBe(0, test.Output);

        // The decisive proof: the swallowed exception's own type and message must reach real
        // process output, rather than vanishing the way [warn-on-swallowed-exception] exists to
        // prevent.
        test.Output.ShouldContain(nameof(InvalidOperationException),
        customMessage: $"the swallowed exception's type never reached process output:{Environment.NewLine}{test.Output}");
        test.Output.ShouldContain("simulated failure after the first call already captured a response",
        customMessage: $"the swallowed exception's message never reached process output:{Environment.NewLine}{test.Output}");

        _stub.ReceivedPaths.ShouldContain("/api/status",
        $"the first, capturing call never reached the stub over the wire. Paths served: {string.Join(", ", _stub.ReceivedPaths)}");
    }

    /// <summary>
    /// The negative half of <c>[warn-on-swallowed-exception]</c>'s live proof: when nothing was
    /// ever captured, the pinned shape's <em>first</em> catch — untouched by this task — must still
    /// rethrow exactly as it always has, rather than <see cref="ApiTestCore.WarnSwallowedClientException"/>
    /// ever being reached for it. Reuses <see cref="AttachThrowingHandlerToApiClient"/>, the same F10
    /// regression guard <see cref="ReadinessProbeSurvivesAThrowingApiHandler"/> already uses for the
    /// raw-HTTP path: <see cref="GoldenAuthHandlerSources.AlwaysThrowsHandler"/> throws on every
    /// request before it ever reaches the wire, so <c>ResponseCaptureHandler</c> never runs and
    /// <c>InTestAmbient.LastCapturedResponse.Value?.Value</c> stays null for the whole test — the
    /// exact condition the first catch's filter exists to detect.
    /// </summary>
    [TestMethod]
    public async Task GeneratedClientRoutedCaseStillRethrowsWhenNothingWasCaptured()
    {
        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();
        AddClientConfig("Stub.ApiTests.FakeOrdersApiClient");
        RegisterFakeOrdersApiClient();

        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        AttachThrowingHandlerToApiClient();

        var build = await ProcessRunner.RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");
        build.ExitCode.ShouldBe(0, $"generated project failed to build:{Environment.NewLine}{build.Output}");

        var resultsDir = Path.Combine(_root, "TestResults");
        var test = await ProcessRunner.RunAsync("dotnet",
        $"test \"{_root}\" --no-build --nologo --filter \"FullyQualifiedName~GetStatus_Contract\" " +
        $"--logger \"trx;LogFileName=results.trx\" --results-directory \"{resultsDir}\"");

        var trxPath = Directory.GetFiles(resultsDir, "results.trx", SearchOption.AllDirectories)
            .ShouldHaveSingleItem($"expected exactly one results.trx under {resultsDir}:{Environment.NewLine}{test.Output}");

        var trx = XDocument.Load(trxPath);
        var result = trx.Descendants()
            .Where(e => e.Name.LocalName == "UnitTestResult")
            .SingleOrDefault(e => (e.Attribute("testName")?.Value ?? "").Contains("GetStatus_Contract", StringComparison.Ordinal));

        result.ShouldNotBeNull($"GetStatus_Contract did not appear in the trx at all:{Environment.NewLine}{test.Output}");
        result!.Attribute("outcome")?.Value.ShouldBe("Failed",
        $"GetStatus_Contract should fail on the throwing handler's own exception, propagated " +
        $"unchanged, not pass or report a mere warning:{Environment.NewLine}{test.Output}");

        var failureText = result.Descendants().Where(e => e.Name.LocalName == "Message")
            .Select(e => e.Value).FirstOrDefault() ?? "";
        failureText.ShouldContain("identity provider unreachable",
        customMessage: $"GetStatus_Contract failed for an unexpected reason:{Environment.NewLine}{test.Output}");

        test.ExitCode.ShouldBe(1, test.Output);

        // The negative half: nothing was captured, so WarnSwallowedClientException must never run
        // for this case — the rethrown exception is the only thing that should reach the trx or
        // process output, not a warning about a discarded one.
        test.Output.ShouldNotContain("captured response is being used as the test's verdict",
        customMessage: $"WarnSwallowedClientException ran even though nothing was ever captured:{Environment.NewLine}{test.Output}");
    }

    /// <summary>
    /// [stage-3b]'s decisive positive proof, against generated code: <c>ping</c>'s only Success
    /// response is a bodiless 204 (<see cref="SpecWithBodilessClientRoutedOperation"/>), so its
    /// generated <c>Ping_Contract</c> case has a null <c>SchemaKey</c> — exactly the shape that used
    /// to fall back to raw HTTP before <c>ApiResponseAssertions.ShouldMatchCapturedStatusAsync</c>
    /// existed. Asserts on the generated source directly (client call present, status-only
    /// assertion present, no raw-HTTP shape at all) before ever building, then proves the routed
    /// call still passes over the wire against a genuine 204 from <see cref="GoldenApiStub"/>.
    /// </summary>
    [TestMethod]
    public async Task GeneratedClientRoutedBodilessSuccessCaseAssertsStatusOnlyAndPasses()
    {
        File.WriteAllText(Path.Combine(_root, "spec.json"), SpecWithBodilessClientRoutedOperation);

        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();
        AddClientConfig("Stub.ApiTests.FakeOrdersApiClient");
        RegisterFakeOrdersApiClient();

        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        var generatedFile = Directory.GetFiles(_root, "StatusTests.g.cs", SearchOption.AllDirectories)
            .ShouldHaveSingleItem("generate should have produced exactly one StatusTests.g.cs");
        var generatedText = File.ReadAllText(generatedFile);

        generatedText.ShouldContain("ApiClient<Stub.ApiTests.FakeOrdersApiClient>()",
        customMessage: "[stage-3b]: a schema-less client-routed case must still route through the " +
                       "client, never fall back to raw HTTP");
        generatedText.ShouldContain("Api.Ping.GetAsync(cancellationToken: TestContext.CancellationToken)");
        generatedText.ShouldContain("ShouldMatchCapturedStatusAsync(",
        customMessage: "a bodiless declared response has no schema to validate — the generated case " +
                       "must call the status-only captured assertion, not ShouldMatchCapturedContractAsync");
        generatedText.ShouldNotContain("ShouldMatchCapturedContractAsync(");
        generatedText.ShouldNotContain("new HttpRequestMessage(");

        var build = await ProcessRunner.RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");
        build.ExitCode.ShouldBe(0, $"generated project failed to build:{Environment.NewLine}{build.Output}");

        var resultsDir = Path.Combine(_root, "TestResults");
        var test = await ProcessRunner.RunAsync("dotnet",
        $"test \"{_root}\" --no-build --nologo --filter \"FullyQualifiedName~Ping_Contract\" " +
        $"--logger \"trx;LogFileName=results.trx\" --results-directory \"{resultsDir}\"");

        var trxPath = Directory.GetFiles(resultsDir, "results.trx", SearchOption.AllDirectories)
            .ShouldHaveSingleItem($"expected exactly one results.trx under {resultsDir}:{Environment.NewLine}{test.Output}");

        var trx = XDocument.Load(trxPath);
        var result = trx.Descendants()
            .Where(e => e.Name.LocalName == "UnitTestResult")
            .SingleOrDefault(e => (e.Attribute("testName")?.Value ?? "").Contains("Ping_Contract", StringComparison.Ordinal));

        result.ShouldNotBeNull($"Ping_Contract did not appear in the trx at all:{Environment.NewLine}{test.Output}");
        result!.Attribute("outcome")?.Value.ShouldBe("Passed",
        $"Ping_Contract should pass against a genuine 204 response, routed through the client:{Environment.NewLine}{test.Output}");

        test.ExitCode.ShouldBe(0, test.Output);

        _stub.ReceivedPaths.ShouldContain("/api/ping",
        $"the client-routed request never reached the stub over the wire. Paths served: {string.Join(", ", _stub.ReceivedPaths)}");
    }

    /// <summary>
    /// The negative half of [stage-3b]'s live proof, alongside
    /// <see cref="GeneratedClientRoutedBodilessSuccessCaseAssertsStatusOnlyAndPasses"/>: a
    /// generated status-only client-routed case must genuinely surface InTest's own status-mismatch
    /// verdict (<c>ContractAssertionException</c>, expected-vs-actual in the message) when the live
    /// response disagrees with the declared 204 — not merely compile and pass vacuously.
    /// <see cref="GoldenApiStub.OverridePingStatus"/> answers 200 instead, which does not throw from
    /// <see cref="GoldenTypedClientSources.FakeOrdersApiClient"/>'s own success-status check (200 is
    /// still a 2xx), so any failure here can only come from
    /// <c>ApiResponseAssertions.ShouldMatchCapturedStatusAsync</c> itself.
    /// </summary>
    [TestMethod]
    public async Task GeneratedClientRoutedBodilessSuccessCaseFailsOnAStatusMismatch()
    {
        File.WriteAllText(Path.Combine(_root, "spec.json"), SpecWithBodilessClientRoutedOperation);

        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();
        AddClientConfig("Stub.ApiTests.FakeOrdersApiClient");
        RegisterFakeOrdersApiClient();

        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        _stub.OverridePingStatus(200);

        var build = await ProcessRunner.RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");
        build.ExitCode.ShouldBe(0, $"generated project failed to build:{Environment.NewLine}{build.Output}");

        var resultsDir = Path.Combine(_root, "TestResults");
        var test = await ProcessRunner.RunAsync("dotnet",
        $"test \"{_root}\" --no-build --nologo --filter \"FullyQualifiedName~Ping_Contract\" " +
        $"--logger \"trx;LogFileName=results.trx\" --results-directory \"{resultsDir}\"");

        var trxPath = Directory.GetFiles(resultsDir, "results.trx", SearchOption.AllDirectories)
            .ShouldHaveSingleItem($"expected exactly one results.trx under {resultsDir}:{Environment.NewLine}{test.Output}");

        var trx = XDocument.Load(trxPath);
        var result = trx.Descendants()
            .Where(e => e.Name.LocalName == "UnitTestResult")
            .SingleOrDefault(e => (e.Attribute("testName")?.Value ?? "").Contains("Ping_Contract", StringComparison.Ordinal));

        result.ShouldNotBeNull($"Ping_Contract did not appear in the trx at all:{Environment.NewLine}{test.Output}");
        result!.Attribute("outcome")?.Value.ShouldBe("Failed",
        $"Ping_Contract should fail against a 200 response when 204 was declared:{Environment.NewLine}{test.Output}");

        var failureText = result.Descendants().Where(e => e.Name.LocalName == "Message")
            .Select(e => e.Value).FirstOrDefault() ?? "";

        failureText.ShouldContain("expected 204, got 200",
        customMessage: $"Ping_Contract did not fail with InTest's own expected-vs-actual status message:{Environment.NewLine}{test.Output}");

        test.ExitCode.ShouldBe(1, test.Output);

        _stub.ReceivedPaths.ShouldContain("/api/ping",
        $"the client-routed request never reached the stub over the wire. Paths served: {string.Join(", ", _stub.ReceivedPaths)}");
    }

    /// <summary>
    /// [finding-3]'s coverage-gap closure, then <c>[typed-path-parameters]</c>'s own fix on top of
    /// it. Before [finding-3], no golden test compiled a client-routed case with a path parameter
    /// at all — <see cref="GoldenTypedClientSources.FakeOrdersApiClient"/> had no indexer anywhere
    /// until that finding added one, so <c>ClientCallPlanner.BuildKiotaConvention</c>'s
    /// <c>Api.Status[{id}].GetAsync</c> shape went completely unexercised by anything that
    /// actually compiles. At that point the fix was a stopgap: <c>FixtureParameter</c> returns
    /// <see cref="string"/>, so the generated call bound the <c>[Obsolete]</c>-marked
    /// <c>this[string]</c> overload every time, wrapped in a
    /// <c>#pragma warning disable CS0618</c> that suppressed the warning without changing what the
    /// call actually bound.
    /// <para>
    /// <c>[typed-path-parameters]</c> is the real fix this test now proves: <c>id</c> declares
    /// <c>format: uuid</c> (<see cref="SpecWithPathParameter"/>), so
    /// <c>TestPlanBuilder.ResolvePathParameterKind</c> resolves it to <c>PathParameterKind.Guid</c>
    /// and <c>TemplateRenderer.WrapForClientCall</c> wraps the spliced fixture value in
    /// <c>Guid.Parse(...)</c> before it reaches the indexer — binding
    /// <c>FakeStatusRequestBuilder</c>'s <c>this[Guid position]</c> overload instead, the
    /// non-obsolete one real kiota 1.34.1 output carries alongside the deprecated
    /// <c>this[string]</c> (confirmed in this plan's own measurement table). The pragma is gone
    /// from <c>mstest-class.scriban</c> entirely now — there is nothing left for it to suppress.
    /// </para>
    /// <para>
    /// <c>-p:WarningsAsErrors=CS0618</c> — not a blanket <c>TreatWarningsAsErrors</c> — is what
    /// actually proves the fix rather than merely asserting it in a comment: the scaffold's own
    /// <c>.csproj</c> sets no <c>TreatWarningsAsErrors</c>, so promoting this one warning code to
    /// an error is what makes a regression back to the deprecated overload fail the build instead
    /// of merely warning. A blanket flip would risk failing on some unrelated warning this test has
    /// no business asserting about. The build succeeding here, with no pragma present anywhere in
    /// the generated source and no CS0618 in the build output, is the whole proof: the generated
    /// call binds the typed overload, not the deprecated one.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task GeneratedClientRoutedSuccessCaseWithAUuidPathParameterCompilesAgainstTheTypedIndexer()
    {
        File.WriteAllText(Path.Combine(_root, "spec.json"), SpecWithPathParameter);

        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();
        AddClientConfig("Stub.ApiTests.FakeOrdersApiClient");
        RegisterFakeOrdersApiClient();

        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        var generatedFile = Directory.GetFiles(_root, "StatusTests.g.cs", SearchOption.AllDirectories)
            .ShouldHaveSingleItem("generate should have produced exactly one StatusTests.g.cs");
        var generatedText = File.ReadAllText(generatedFile);

        generatedText.ShouldContain(
        "Api.Status[Guid.Parse(FixtureParameter(\"getStatusById\", \"id\"))].GetAsync(cancellationToken: TestContext.CancellationToken);",
        customMessage: "getStatusById's client-routed case should splice the indexer through " +
                       "Guid.Parse(...), binding the typed this[Guid] overload rather than the " +
                       "deprecated this[string] one");
        generatedText.ShouldNotContain("#pragma warning disable CS0618",
        customMessage: "[typed-path-parameters] removed the pragma from mstest-class.scriban -- " +
                       "nothing this template emits should need it any more");
        generatedText.ShouldNotContain("#pragma warning restore CS0618");

        var build = await ProcessRunner.RunAsync("dotnet",
        $"build \"{_root}\" --nologo -v q -p:WarningsAsErrors=CS0618");
        build.ExitCode.ShouldBe(0,
        $"generated project failed to build with CS0618 promoted to an error -- the generated " +
        $"call must bind the typed, non-obsolete this[Guid] overload with no pragma needed at " +
        $"all:{Environment.NewLine}{build.Output}");
        build.Output.ShouldNotContain("CS0618", customMessage: build.Output);
    }

    /// <summary>
    /// F10 inverted (Task 1, Step 3). Before the readiness client existed, this exact scenario —
    /// a throwing handler on <c>InTestClients.Api</c>, exactly where an adopter's own bearer
    /// handler attaches via <c>TestStartup.cs</c>'s <c>Register</c> hook — made
    /// <c>TestHost.InitializeAsync</c> burn the full readiness timeout and fail with
    /// <c>ReadinessTimeoutException</c>, misreporting an unreachable identity provider as a dead
    /// API. Now the probe runs on <c>InTestClients.Readiness</c>, which carries no such handler,
    /// so readiness succeeds and the throwing handler's own exception surfaces where it actually
    /// belongs: on the first generated test that sends a request through
    /// <c>InTestClients.Api</c>.
    /// <para>
    /// Both halves matter. Asserting only "the suite failed" would also be satisfied by a
    /// readiness timeout — exactly the bug this guards against — so this asserts readiness was
    /// never the failure (no <c>ReadinessTimeoutException</c> anywhere in the run) <em>and</em>
    /// that <c>GetStatus_Contract</c> specifically failed, carrying the throwing handler's own
    /// message, not merely that something, somewhere, went wrong.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task ReadinessProbeSurvivesAThrowingApiHandler()
    {
        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();

        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        AttachThrowingHandlerToApiClient();

        var build = await ProcessRunner.RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");
        build.ExitCode.ShouldBe(0, $"generated project failed to build:{Environment.NewLine}{build.Output}");

        var resultsDir = Path.Combine(_root, "TestResults");
        var test = await ProcessRunner.RunAsync("dotnet",
        $"test \"{_root}\" --no-build --nologo --logger \"trx;LogFileName=results.trx\" --results-directory \"{resultsDir}\"");

        // The misdiagnosis this task exists to close: readiness must never be what failed here.
        test.Output.ShouldNotContain("ReadinessTimeoutException",
        customMessage: $"the readiness probe ran on a client carrying the throwing handler — F10 regressed:{Environment.NewLine}{test.Output}");

        var trxPath = Directory.GetFiles(resultsDir, "results.trx", SearchOption.AllDirectories)
            .ShouldHaveSingleItem($"expected exactly one results.trx under {resultsDir}:{Environment.NewLine}{test.Output}");

        var trx = XDocument.Load(trxPath);
        var statusResult = trx.Descendants()
            .Where(e => e.Name.LocalName == "UnitTestResult")
            .SingleOrDefault(e => (e.Attribute("testName")?.Value ?? "").Contains("GetStatus_Contract", StringComparison.Ordinal));

        statusResult.ShouldNotBeNull($"GetStatus_Contract did not appear in the trx at all:{Environment.NewLine}{test.Output}");
        statusResult!.Attribute("outcome")?.Value.ShouldBe("Failed",
        $"GetStatus_Contract should fail on the throwing handler's own exception, not pass or be skipped:{Environment.NewLine}{test.Output}");

        // The actual failure, not just "some" failure: the throwing handler's own message must
        // reach the test's own failure output, proving the first request — not readiness — is
        // where this failed.
        var failureText = statusResult.Descendants().Where(e => e.Name.LocalName == "Message")
            .Select(e => e.Value).FirstOrDefault() ?? "";
        failureText.ShouldContain("identity provider unreachable",
        customMessage: $"GetStatus_Contract failed for an unexpected reason:{Environment.NewLine}{test.Output}");

        test.ExitCode.ShouldBe(1, test.Output);
    }

    [TestMethod]
    public async Task ScaffoldedConfigurationTravelsToTheOutputDirectory()
    {
        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();
        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        (await ProcessRunner.RunAsync("dotnet", $"build \"{_root}\" --nologo -v q")).ExitCode.ShouldBe(0);

        var output = Path.Combine(_root, "bin", "Debug", "net10.0");
        foreach (var required in new[] { "appsettings.json", "spec-schemas.json", "spec-paths.json" })
        {
            File.Exists(Path.Combine(output, required)).ShouldBeTrue($"{required} did not reach the output directory.");
        }
    }

    /// <summary>
    /// The F1 live proof (plan Task 8, Step 2a). Everything else in this file proves a generated
    /// suite builds and runs; nothing yet proves a fixture is actually <i>loaded and used</i> by
    /// a running test rather than merely declared for copying (Task 4a proved only the latter).
    /// Runs exactly the sequence an adopter does — generate, repair, hand-fill the sentinel,
    /// build, run — against an operation whose only way to succeed is a fixture value reaching a
    /// live HTTP request.
    /// </summary>
    [TestMethod]
    public async Task FixtureParameterReachesALiveRequestEndToEnd()
    {
        File.WriteAllText(Path.Combine(_root, "spec.json"), SpecWithPathParameter);

        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();

        // `generate` is read-only under fixtures/ and refuses to run at all while one is
        // missing (it exits with "no fixture found", the drift check working as intended) — so
        // `repair` must create the fixture first, exactly as it does in the two tests above.
        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        // Guard against the generated suite silently missing the operation entirely (the plan's
        // second failure mode) as early as possible: if getStatusById were never generated, the
        // fixture `repair` just created for it would be orphaned and this assertion would catch
        // it directly, rather than only inferring it later from a shorter trx.
        var generatedFile = Directory.GetFiles(_root, "StatusTests.g.cs", SearchOption.AllDirectories)
            .ShouldHaveSingleItem("generate should have produced exactly one StatusTests.g.cs");
        File.ReadAllText(generatedFile).ShouldContain("GetStatusById_Contract",
        customMessage: "the operation this test exists to prove must actually be generated");

        var fixturePath = Path.Combine(_root, "fixtures", "getStatusById.json");
        File.Exists(fixturePath).ShouldBeTrue("`fixtures repair` should have composed one fixture for the required path parameter");
        var beforeReplace = File.ReadAllText(fixturePath);
        beforeReplace.ShouldContain("\"TODO:id\"", customMessage: "a required path parameter always gets a sentinel (decision 1)");

        // The step a human adopter performs by hand: fill in the sentinel with a value the
        // service actually accepts.
        File.WriteAllText(fixturePath, beforeReplace.Replace("\"TODO:id\"", "\"42\"", StringComparison.Ordinal));

        // Guard against the first failure mode directly, rather than only inferring it from the
        // live request's outcome below: re-reads the file from disk (not the in-memory string
        // just written) so a no-op caused by the wrong path, the wrong key, or writing to the
        // wrong file is caught here rather than only by RequireFixture further down.
        File.ReadAllText(fixturePath).ShouldNotContain("TODO:id",
        customMessage: "the sentinel replacement must actually take effect on disk");

        var build = await ProcessRunner.RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");
        build.ExitCode.ShouldBe(0, $"generated project failed to build:{Environment.NewLine}{build.Output}");

        var resultsDir = Path.Combine(_root, "TestResults");
        var test = await ProcessRunner.RunAsync("dotnet",
        $"test \"{_root}\" --no-build --nologo --logger \"trx;LogFileName=results.trx\" --results-directory \"{resultsDir}\"");

        var trxPath = Directory.GetFiles(resultsDir, "results.trx", SearchOption.AllDirectories)
            .ShouldHaveSingleItem($"expected exactly one results.trx under {resultsDir}:{Environment.NewLine}{test.Output}");

        var trx = XDocument.Load(trxPath);
        var statusByIdResult = trx.Descendants()
            .Where(e => e.Name.LocalName == "UnitTestResult")
            .SingleOrDefault(e => (e.Attribute("testName")?.Value ?? "").Contains("GetStatusById_Contract", StringComparison.Ordinal));

        // The assertion that closes the F1 loop: the specific test this fixture exists for was
        // both present (guards the second failure mode — the suite cannot quietly pass one test
        // short with nothing noticing) and passed (guards the first — an unresolved sentinel
        // makes RequireFixture throw before any request is built, and the stub itself rejects
        // the literal sentinel too, so a no-op replace fails here even if the direct on-disk
        // check above were somehow fooled).
        statusByIdResult.ShouldNotBeNull(
        $"GetStatusById_Contract did not appear in the trx at all — the suite ran one test short and nothing noticed:{Environment.NewLine}{test.Output}");
        statusByIdResult!.Attribute("outcome")?.Value.ShouldBe("Passed",
        $"GetStatusById_Contract ran but did not pass — the fixture value likely never reached the live request:{Environment.NewLine}{test.Output}");

        test.ExitCode.ShouldBe(0, test.Output);
    }

    /// <summary>
    /// Plan Task 6, Step 1 — the crux of the v1-b fixture lifecycle. A first draft of this test
    /// claimed to discriminate three orderings ("services before seeding", "seeding before
    /// resolution", "resolution before validation") but only the last two actually failed under
    /// any wrong implementation, and both failed the <em>same</em> way (an unresolved
    /// <c>{{fixture:...}}</c> token) — "services before seeding" was true by construction the
    /// fixture could not even compile without it. This version tests two independently
    /// falsifiable orderings instead:
    /// <list type="bullet">
    /// <item><description>Seeding after readiness. <c>GoldenFixtureSources.SeedIdFixture</c>
    /// takes a real <c>IHttpClientFactory</c> constructor dependency (proving fixtures can
    /// consume anything <c>ConfigureServices</c> registered) and calls the stub's
    /// <c>/api/seed</c>, which only answers once <see cref="GoldenApiStub"/> has seen as many
    /// <c>/health/ready</c> probes as <c>Readiness.WaitAsync</c> requires to return
    /// (<see cref="GoldenApiStub.RequiredReadyProbes"/>). If seeding ran before readiness, this
    /// call gets a 503 and the fixture throws.</description></item>
    /// <item><description>Resolution after seeding, seeding after services. The fixture publishes
    /// the value <em>it received back from that live call</em>, and the fixture value under test
    /// points at <c>{{fixture:seededId}}</c>. Validation would flag that token as unresolved, and
    /// <c>RequireFixture</c> would throw before any request is built, unless
    /// <c>TokenResolver</c> was built with the published key already in hand.</description></item>
    /// </list>
    /// <para>
    /// Asserting <c>test.Output.ShouldContain("Passed!")</c> alone would be too weak — a suite
    /// that resolved the token to the wrong value, or somehow ran with the wrong order but still
    /// happened to satisfy the stub (which answers 200 for almost anything under
    /// <c>/api/status/</c>), could still print it. The assertion that actually closes the loop is
    /// on <see cref="GoldenApiStub.ReceivedPaths"/>: the stub, running in this process, records
    /// every path it served, so this test can confirm the exact value
    /// <c>GoldenFixtureSources.SeedIdFixture</c> published — not merely "some" value — reached
    /// the wire.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task APublishedFixtureKeyReachesALiveRequest()
    {
        File.WriteAllText(Path.Combine(_root, "spec.json"), SpecWithPathParameter);

        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();

        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        // Point the required path parameter at a fixture token instead of a literal value, so the
        // live request can only carry the right id if TokenResolver was built with the published
        // key already in hand.
        var fixturePath = Path.Combine(_root, "fixtures", "getStatusById.json");
        File.Exists(fixturePath).ShouldBeTrue("`fixtures repair` should have composed one fixture for the required path parameter");
        var beforeReplace = File.ReadAllText(fixturePath);
        beforeReplace.ShouldContain("\"TODO:id\"", customMessage: "a required path parameter always gets a sentinel (decision 1)");
        File.WriteAllText(fixturePath, beforeReplace.Replace("\"TODO:id\"", "\"{{fixture:seededId}}\"", StringComparison.Ordinal));

        // Register a fake assembly fixture the way an adopter would: a class implementing
        // IAssemblyFixture, added to the project, and wired into TestStartup.cs's Register hook.
        File.WriteAllText(Path.Combine(_root, "SeedIdFixture.cs"), GoldenFixtureSources.SeedIdFixture);
        RegisterFixture("SeedIdFixture");

        var build = await ProcessRunner.RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");
        build.ExitCode.ShouldBe(0, $"generated project failed to build:{Environment.NewLine}{build.Output}");

        var resultsDir = Path.Combine(_root, "TestResults");
        var test = await ProcessRunner.RunAsync("dotnet",
        $"test \"{_root}\" --no-build --nologo --logger \"trx;LogFileName=results.trx\" --results-directory \"{resultsDir}\"");

        var trxPath = Directory.GetFiles(resultsDir, "results.trx", SearchOption.AllDirectories)
            .ShouldHaveSingleItem($"expected exactly one results.trx under {resultsDir}:{Environment.NewLine}{test.Output}");

        var trx = XDocument.Load(trxPath);
        var statusByIdResult = trx.Descendants()
            .Where(e => e.Name.LocalName == "UnitTestResult")
            .SingleOrDefault(e => (e.Attribute("testName")?.Value ?? "").Contains("GetStatusById_Contract", StringComparison.Ordinal));

        statusByIdResult.ShouldNotBeNull(
        $"GetStatusById_Contract did not appear in the trx at all:{Environment.NewLine}{test.Output}");
        statusByIdResult!.Attribute("outcome")?.Value.ShouldBe("Passed",
        $"GetStatusById_Contract ran but did not pass — a published fixture key likely never reached " +
        $"TokenResolver:{Environment.NewLine}{test.Output}");

        test.ExitCode.ShouldBe(0, test.Output);

        // The assertion that actually proves the order, not just that the suite reported success:
        // the exact value SeedIdFixture published — "seeded-42", not "TODO:id" and not anything
        // else — must have reached the stub on the wire.
        _stub.ReceivedPaths.ShouldContain("/api/status/seeded-42",
        $"the published fixture value never reached the live request. Paths actually served: " +
        $"{string.Join(", ", _stub.ReceivedPaths)}");
    }

    /// <summary>
    /// Proves <c>AppliesTo</c>-based skipping — <c>FixtureRunner.RunAsync</c>'s own logic,
    /// already unit-tested against a bare <see cref="StringWriter"/> in
    /// <c>FixtureRunnerTests</c> — actually threads correctly through <c>TestHost</c>'s real
    /// profile resolution and real DI-resolved fixture list in a live, generated, built, and run
    /// suite. <c>GoldenFixtureSources.SkippedFixture</c>'s <c>AppliesTo</c> excludes the
    /// scaffold's default profile ("local"); if it ran anyway, it throws, which fails
    /// [AssemblyInitialize] and every test in the suite — the actual strong signal this test
    /// relies on (<c>test.ExitCode.ShouldBe(0)</c>). The marker file it also writes first is
    /// belt-and-braces only: absence of a file degrades to a vacuous pass if its path is ever
    /// wrong, and there is no positive control proving the mechanism itself works.
    /// <para>
    /// Also asserts the skip line itself reached real process output — see
    /// <c>TestContextDiagnostics</c>'s own doc for why that is
    /// <c>TestContext.DisplayMessage(Warning, ...)</c>, not <c>WriteLine</c>, and for the
    /// confirmed VSTest behaviour behind that choice.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task SkippedFixtureIsNotRunByALiveGeneratedSuite()
    {
        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();

        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        File.WriteAllText(Path.Combine(_root, "SkippedFixture.cs"), GoldenFixtureSources.SkippedFixture);
        RegisterFixture("SkippedFixture");

        var build = await ProcessRunner.RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");
        build.ExitCode.ShouldBe(0, $"generated project failed to build:{Environment.NewLine}{build.Output}");

        var test = await ProcessRunner.RunAsync("dotnet", $"test \"{_root}\" --no-build --nologo");

        // If SkippedFixture ran instead of being skipped, it throws and AssemblyInitialize fails
        // every test — the real signal. The suite must still pass.
        test.ExitCode.ShouldBe(0, test.Output);

        // Belt-and-braces: SkippedFixture writes this file to the output directory as the very
        // first thing it does if it ever runs at all, before it throws.
        var markerPath = Path.Combine(_root, "bin", "Debug", "net10.0", "skipped-fixture-ran.marker");
        File.Exists(markerPath).ShouldBeFalse(
        "SkippedFixture ran even though its AppliesTo excludes the active profile ('local') — " +
        "FixtureRunner's skip logic did not apply inside a live TestHost.InitializeAsync run.");

        // The seam DisplayMessage opened up: FixtureRunner's own skip line, verbatim, reaching
        // real process stdout on a passing run.
        test.Output.ShouldContain(
        "Skipping fixture 'Stub.ApiTests.SkippedFixture': its AppliesTo does not include profile 'local'.",
        customMessage: $"the skip line never reached process output:{Environment.NewLine}{test.Output}");
    }

    /// <summary>
    /// I1 (Task 6's third review round): the aggregated fixture-validation report must surface
    /// even when nothing fails — decision 2's whole point is that a non-blocking fixture problem
    /// stays visible while the run still succeeds. Uses <c>--filter</c> to run only
    /// <c>GetStatus_Contract</c>, the operation with no fixture, while
    /// <c>fixtures/getStatusById.json</c> keeps its unresolved <c>"TODO:id"</c> sentinel: nothing
    /// calls <c>RequireFixture("getStatusById")</c>, so nothing fails, and the run passes with a
    /// real problem sitting in the report. Before <c>TestHost</c> used
    /// <c>TestContext.DisplayMessage</c>, this report existed only as a <c>WriteLine</c> call
    /// that VSTest silently drops on exactly this kind of passing run (see
    /// <c>TestContextDiagnostics</c>'s doc for the confirmed mechanism) — so this test would
    /// have passed against that bug: nothing here checks that the report exists, only that it
    /// reached somewhere a human or CI system would actually see it.
    /// </summary>
    [TestMethod]
    public async Task ValidationReportWithAProblemSurfacesOnAPassingRun()
    {
        File.WriteAllText(Path.Combine(_root, "spec.json"), SpecWithPathParameter);

        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();

        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        var fixturePath = Path.Combine(_root, "fixtures", "getStatusById.json");
        File.ReadAllText(fixturePath).ShouldContain("\"TODO:id\"",
        customMessage: "left unresolved on purpose — this test needs a genuine, standing validation problem");

        var build = await ProcessRunner.RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");
        build.ExitCode.ShouldBe(0, $"generated project failed to build:{Environment.NewLine}{build.Output}");

        // "GetStatus_Contract" (no "By") does not match "GetStatusById_Contract" as a substring,
        // so this filter runs only the fixture-free operation and never touches the one with the
        // still-unresolved sentinel.
        var test = await ProcessRunner.RunAsync("dotnet",
        $"test \"{_root}\" --no-build --nologo --filter \"FullyQualifiedName~GetStatus_Contract\"");

        test.ExitCode.ShouldBe(0,
        $"the filtered run should pass — nothing calls RequireFixture for the one operation with a " +
        $"problem:{Environment.NewLine}{test.Output}");

        test.Output.ShouldContain("getStatusById:",
        customMessage: $"the aggregated report never reached process output on this passing run:{Environment.NewLine}{test.Output}");
        test.Output.ShouldContain("is still unfilled (TODO:id)",
        customMessage: $"the report reached output but not with the expected problem detail:{Environment.NewLine}{test.Output}");
    }

    /// <summary>
    /// Plan Task 4, Step 2(b)'s live proof. Unlike <see cref="FixtureParameterReachesALiveRequestEndToEnd"/>,
    /// this deliberately never fills in <c>fixtures/getWidgetById.json</c>'s sentinel — decision
    /// 6's whole point is that a declared-error case must not care whether that sibling fixture is
    /// resolved, so the filter below runs only <c>GetWidgetById_NotFound</c> and never touches
    /// <c>GetWidgetById_Contract</c>, which would otherwise fail on the sentinel it never received.
    /// </summary>
    [TestMethod]
    public async Task DeclaredErrorCaseReceivesARealNotFoundOverTheWire()
    {
        File.WriteAllText(Path.Combine(_root, "spec.json"), SpecWithDeclaredNotFound);

        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();

        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        // Guard against the generated suite silently missing the declared-error case entirely,
        // the same way FixtureParameterReachesALiveRequestEndToEnd guards its own operation.
        var generatedFile = Directory.GetFiles(_root, "WidgetsTests.g.cs", SearchOption.AllDirectories)
            .ShouldHaveSingleItem("generate should have produced exactly one WidgetsTests.g.cs");
        var generated = File.ReadAllText(generatedFile);
        generated.ShouldContain("GetWidgetById_NotFound",
        customMessage: "the declared-error case this test exists to prove must actually be generated");
        generated.ShouldContain("Guid.NewGuid().ToString()",
        customMessage: "decision 6: a declared-error case must send a generated, unmatchable id, never a fixture value");

        var build = await ProcessRunner.RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");
        build.ExitCode.ShouldBe(0, $"generated project failed to build:{Environment.NewLine}{build.Output}");

        var resultsDir = Path.Combine(_root, "TestResults");
        var test = await ProcessRunner.RunAsync("dotnet",
        $"test \"{_root}\" --no-build --nologo --filter \"FullyQualifiedName~GetWidgetById_NotFound\" " +
        $"--logger \"trx;LogFileName=results.trx\" --results-directory \"{resultsDir}\"");

        var trxPath = Directory.GetFiles(resultsDir, "results.trx", SearchOption.AllDirectories)
            .ShouldHaveSingleItem($"expected exactly one results.trx under {resultsDir}:{Environment.NewLine}{test.Output}");

        var trx = XDocument.Load(trxPath);
        var results = trx.Descendants().Where(e => e.Name.LocalName == "UnitTestResult").ToList();

        // The filter targets only the declared-error case: GetWidgetById_Contract's own fixture
        // was never filled in above, and decision 6 exists precisely so that never matters here —
        // if it did run and was blocked by RequireFixture, this count would catch it at 2.
        results.Count.ShouldBe(1,
        $"expected exactly 1 filtered test (GetWidgetById_NotFound) but the trx recorded {results.Count}:{Environment.NewLine}{test.Output}");

        var notFoundResult = results.Single();
        (notFoundResult.Attribute("testName")?.Value ?? "").ShouldContain("GetWidgetById_NotFound",
        customMessage: $"the filtered trx result was not the expected test:{Environment.NewLine}{test.Output}");
        notFoundResult.Attribute("outcome")?.Value.ShouldBe("Passed",
        $"GetWidgetById_NotFound ran but did not pass — the declared-error case likely never received a real 404 over the wire:{Environment.NewLine}{test.Output}");

        test.ExitCode.ShouldBe(0, test.Output);

        // Closes the loop from the outside, the same pattern APublishedFixtureKeyReachesALiveRequest
        // uses against ReceivedPaths: the generated request must have actually reached the stub
        // under /api/widgets/, not merely have been built and never sent.
        _stub.ReceivedPaths.Any(p => p.StartsWith("/api/widgets/", StringComparison.Ordinal))
            .ShouldBeTrue($"the generated declared-error case never reached the stub over the wire. " +
                          $"Paths served: {string.Join(", ", _stub.ReceivedPaths)}");
    }

    /// <summary>
    /// Task 5 Step 2's live wire proof — the auth half of the F1 lesson
    /// <see cref="DeclaredErrorCaseReceivesARealNotFoundOverTheWire"/> already closed for
    /// declared-error cases. Registers <see cref="GoldenTokenProviderSources.TwoIdentityTokenProvider"/>
    /// so the wrong-scope 403 case's <c>RequireMultipleIdentities</c> guard passes, and runs the
    /// whole generated suite — three tests, all for the one secured operation this spec declares.
    /// <para>
    /// The trap this test exists to catch, stated in the plan's own words: "a 401 test passes
    /// trivially when every request is anonymous — which is precisely the day-one state." Merely
    /// asserting <c>GetSecureResource_Unauthorized</c> passed would not distinguish "AuthHandler
    /// correctly sent no token" from "AuthHandler never sends tokens at all" — both look
    /// identical from that one test's own outcome. Asserting
    /// <c>GetSecureResource_Contract</c> (the success case, which needs the <c>default</c>
    /// token) also passed <em>in the same run</em> is what closes that gap:
    /// <see cref="GoldenApiStub.HandleSecureResource"/> only answers 200 to a request that
    /// actually carries <c>"Bearer token-for-default"</c>, so if <c>AuthHandler</c> ever
    /// regressed to sending no token for the Default slot too, the success case would flip to 401
    /// right alongside the 401 case and this assertion would catch it.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task AuthCasesReceiveRealStatusesOverTheWireAndSuccessCasesStillPass()
    {
        File.WriteAllText(Path.Combine(_root, "spec.json"), SpecWithSecuredOperation);

        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();
        RegisterTokenProvider();

        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        // Guard against the generated suite silently missing an auth case entirely, the same way
        // DeclaredErrorCaseReceivesARealNotFoundOverTheWire guards its own operation.
        var generatedFile = Directory.GetFiles(_root, "SecureTests.g.cs", SearchOption.AllDirectories)
            .ShouldHaveSingleItem("generate should have produced exactly one SecureTests.g.cs");
        var generated = File.ReadAllText(generatedFile);
        generated.ShouldContain("GetSecureResource_Unauthorized",
        customMessage: "the no-token 401 case this test exists to prove must actually be generated");
        generated.ShouldContain("GetSecureResource_Forbidden",
        customMessage: "the wrong-scope 403 case this test exists to prove must actually be generated");
        generated.ShouldContain("RequireMultipleIdentities();",
        customMessage: "decision 3: the 403 case must carry the runtime guard");

        var build = await ProcessRunner.RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");
        build.ExitCode.ShouldBe(0, $"generated project failed to build:{Environment.NewLine}{build.Output}");

        var resultsDir = Path.Combine(_root, "TestResults");
        var test = await ProcessRunner.RunAsync("dotnet",
        $"test \"{_root}\" --no-build --nologo --logger \"trx;LogFileName=results.trx\" --results-directory \"{resultsDir}\"");

        var trxPath = Directory.GetFiles(resultsDir, "results.trx", SearchOption.AllDirectories)
            .ShouldHaveSingleItem($"expected exactly one results.trx under {resultsDir}:{Environment.NewLine}{test.Output}");

        var trx = XDocument.Load(trxPath);
        var results = trx.Descendants().Where(e => e.Name.LocalName == "UnitTestResult").ToList();

        results.Count.ShouldBe(3,
        $"expected exactly 3 tests (Contract, Unauthorized, Forbidden) but the trx recorded {results.Count}:{Environment.NewLine}{test.Output}");

        foreach (var (name, expectedOutcome) in new[]
                 {
                     ("GetSecureResource_Contract", "Passed"),
                     ("GetSecureResource_Unauthorized", "Passed"),
                     ("GetSecureResource_Forbidden", "Passed")
                 })
        {
            var result = results.SingleOrDefault(e => (e.Attribute("testName")?.Value ?? "").Contains(name, StringComparison.Ordinal));
            result.ShouldNotBeNull($"{name} did not appear in the trx at all:{Environment.NewLine}{test.Output}");
            result!.Attribute("outcome")?.Value.ShouldBe(expectedOutcome,
            $"{name} did not receive its expected real status over the wire:{Environment.NewLine}{test.Output}");
        }

        test.ExitCode.ShouldBe(0, test.Output);

        // Closes the loop from the outside, the same pattern DeclaredErrorCaseReceivesARealNotFoundOverTheWire
        // uses against ReceivedPaths: every one of the three generated requests must have
        // actually reached the stub, not merely have been built and never sent.
        _stub.ReceivedPaths.Count(p => p == "/api/secure").ShouldBe(3,
        $"expected all 3 generated cases to reach the stub over the wire. Paths served: {string.Join(", ", _stub.ReceivedPaths)}");
    }

    /// <summary>
    /// <c>[mixed-idiom-execution]</c>: closes a coverage gap the plan's own <c>[success-only]</c>
    /// decision names directly. A generated class can contain both a client-routed Success case
    /// and raw-HTTP declared-error/auth siblings — <c>[success-only]</c>: <c>TestPlanBuilder.Build</c>
    /// calls <c>ClientCallPlanner</c> only when building the <c>Success</c> case; declared-error and
    /// auth cases are built afterward from separate helpers (<c>TryPlanDeclaredNotFound</c>,
    /// <c>PlanAuthCases</c>) that never touch it, regardless of whether <c>client</c> is configured
    /// — and the plan's own risk section calls this mixed shape accepted for v1, "reintroducing
    /// two ways of calling the same API inside a single file". An audit found that shape was
    /// <b>compiled</b> in three <c>CompileVerificationTests</c> cases but never <b>run</b> anywhere:
    /// every Golden test that runs an auth case (<see cref="AuthCasesReceiveRealStatusesOverTheWireAndSuccessCasesStillPass"/>
    /// above) configures no <c>client</c> section, and every Golden test that configures one
    /// (every <c>GeneratedClientRouted*</c> test in this file) exercises the unsecured <c>getStatus</c>
    /// operation, with no auth case in play. This is the single most likely real adopter shape — a
    /// class where some cases go through the typed client and others go straight over raw HTTP,
    /// sharing one <c>ApiTestBase</c>, one <c>HttpClient</c>, one identity pipeline and one
    /// captured-response slot — and it had never actually been run before this test.
    /// <para>
    /// Reuses <see cref="SpecWithSecuredOperation"/> exactly as
    /// <see cref="AuthCasesReceiveRealStatusesOverTheWireAndSuccessCasesStillPass"/> does — already
    /// proven, with no <c>client</c> section, to produce one <c>SecureTests.g.cs</c> class with
    /// three cases (<c>Contract</c>, <c>Unauthorized</c>, <c>Forbidden</c>) sharing exactly the
    /// pipeline this test cares about — and adds <see cref="AddClientConfig"/> plus
    /// <see cref="RegisterFakeOrdersApiClient"/> (over the new
    /// <c>GoldenTypedClientSources.FakeApiRequestBuilder.Secure</c> builder — see its own doc for
    /// why the extension lives there) before <c>generate</c> runs, so
    /// <c>GetSecureResource_Contract</c> becomes the client-routed case while its two auth siblings
    /// stay raw HTTP, unchanged from the no-client-config shape
    /// <see cref="AuthCasesReceiveRealStatusesOverTheWireAndSuccessCasesStillPass"/> already proves.
    /// </para>
    /// <para>
    /// Two things this test exists to observe, not merely assert, per the review that requested it:
    /// <b>the shared <c>InTestAmbient.LastCapturedResponse</c> slot</b> — a per-test
    /// <c>AsyncLocal&lt;CapturedResponseSlot?&gt;</c> that <c>ApiTestCore.BeginTest</c> reassigns to
    /// a <em>fresh</em> cell for every test method, so the two raw-HTTP auth cases can never observe
    /// a stale capture left behind by the client-routed <c>Contract</c> case, or vice versa, purely
    /// by construction — and <b><c>ResponseCaptureHandler</c> now running over the raw-HTTP cases'
    /// own requests too</b>, since <c>clientCaptureEnabled</c> gates attachment to
    /// <c>InTestClients.Api</c> for the whole run, not per case (<c>[capture-is-opt-in]</c>'s own
    /// doc calls this harmless because <c>Client</c> and every typed client resolve over that same
    /// pipeline) — so both auth cases' <c>Client.SendAsync</c> calls are captured into the ambient
    /// slot exactly like the client-routed call is, even though neither case's rendered body ever
    /// reads it. The negative assertions below on process output are what actually check that
    /// neither observation turns into a real defect (a spurious "[client-rides-the-api-pipeline]:
    /// no response has been captured" exception, or an unexpected swallowed-exception warning) — a
    /// bare "Passed!" would not distinguish "this never happened" from "this happened but the test
    /// still passed by accident".
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task GeneratedMixedIdiomClassRunsTheClientRoutedSuccessCaseAlongsideItsRawHttpAuthSiblings()
    {
        File.WriteAllText(Path.Combine(_root, "spec.json"), SpecWithSecuredOperation);

        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();
        RegisterTokenProvider();
        AddClientConfig("Stub.ApiTests.FakeOrdersApiClient");
        RegisterFakeOrdersApiClient();

        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        // Decisive proof of the mixed shape itself, before anything runs: one generated class, one
        // Success case routed through the typed client, its two auth siblings still raw HTTP.
        var generatedFile = Directory.GetFiles(_root, "SecureTests.g.cs", SearchOption.AllDirectories)
            .ShouldHaveSingleItem("generate should have produced exactly one SecureTests.g.cs");
        var generated = File.ReadAllText(generatedFile);

        generated.ShouldContain("ApiClient<Stub.ApiTests.FakeOrdersApiClient>()",
        customMessage: "GetSecureResource_Contract should be the client-routed case now that a client section is configured");
        generated.ShouldContain("Api.Secure.GetAsync(cancellationToken: TestContext.CancellationToken)",
        customMessage: "the Kiota convention over GET /api/secure should have resolved to Api.Secure.GetAsync");
        generated.ShouldContain("GetSecureResource_Unauthorized",
        customMessage: "the no-token 401 case must still be generated");
        generated.ShouldContain("GetSecureResource_Forbidden",
        customMessage: "the wrong-scope 403 case must still be generated");
        generated.ShouldContain("RequireMultipleIdentities();",
        customMessage: "decision 3: the 403 case must still carry the runtime guard");

        // [success-only]'s mechanical check: exactly one case in this class should be
        // client-routed. A regression that routed an auth case through the client too — defeating
        // the entire reasoning [success-only]'s own doc gives for why that never happens — would
        // show up here as a second "ApiClient<" occurrence, which neither auth case's own rendered
        // body (a bare HttpRequestMessage/Client.SendAsync pair, per mstest-class.scriban's
        // raw-HTTP branch) ever contains today.
        var apiClientOccurrences = generated.Split("ApiClient<", StringSplitOptions.None).Length - 1;
        apiClientOccurrences.ShouldBe(1,
        $"expected exactly one client-routed case (GetSecureResource_Contract) in this class, found " +
        $"{apiClientOccurrences} occurrences of \"ApiClient<\":{Environment.NewLine}{generated}");

        var build = await ProcessRunner.RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");
        build.ExitCode.ShouldBe(0, $"generated project failed to build:{Environment.NewLine}{build.Output}");

        var resultsDir = Path.Combine(_root, "TestResults");
        var test = await ProcessRunner.RunAsync("dotnet",
        $"test \"{_root}\" --no-build --nologo --logger \"trx;LogFileName=results.trx\" --results-directory \"{resultsDir}\"");

        var trxPath = Directory.GetFiles(resultsDir, "results.trx", SearchOption.AllDirectories)
            .ShouldHaveSingleItem($"expected exactly one results.trx under {resultsDir}:{Environment.NewLine}{test.Output}");

        var trx = XDocument.Load(trxPath);
        var results = trx.Descendants().Where(e => e.Name.LocalName == "UnitTestResult").ToList();

        results.Count.ShouldBe(3,
        $"expected exactly 3 tests (Contract, Unauthorized, Forbidden) but the trx recorded {results.Count}:{Environment.NewLine}{test.Output}");

        foreach (var name in new[]
                 {
                     "GetSecureResource_Contract",
                     "GetSecureResource_Unauthorized",
                     "GetSecureResource_Forbidden"
                 })
        {
            var result = results.SingleOrDefault(e => (e.Attribute("testName")?.Value ?? "").Contains(name, StringComparison.Ordinal));
            result.ShouldNotBeNull($"{name} did not appear in the trx at all:{Environment.NewLine}{test.Output}");
            result!.Attribute("outcome")?.Value.ShouldBe("Passed",
            $"{name} did not receive its expected real status over the wire:{Environment.NewLine}{test.Output}");
        }

        test.ExitCode.ShouldBe(0, test.Output);

        // Closes the loop from the outside, the same pattern every other live-wire test in this
        // file uses: all 3 requests — the client-routed Contract case included — must have
        // actually reached the stub, not merely have been built.
        _stub.ReceivedPaths.Count(p => p == "/api/secure").ShouldBe(3,
        $"expected all 3 cases (client-routed and raw-HTTP alike) to reach the stub over the wire. Paths served: {string.Join(", ", _stub.ReceivedPaths)}");

        // The two observations this test exists to make, turned into assertions: mixing the two
        // idioms in one class must never surface [client-rides-the-api-pipeline]'s own "nothing was
        // captured" exception against either auth case (it would, if BeginTest's fresh-slot-per-test
        // guarantee ever regressed and a stale capture — or the absence of one — leaked across
        // cases), and ResponseCaptureHandler now running over the auth cases' own raw-HTTP requests
        // must never produce a spurious [warn-on-swallowed-exception] warning (it would only ever
        // fire from inside a client-routed case's own catch block, which neither auth case's
        // rendered body contains at all).
        test.Output.ShouldNotContain("no response has been captured",
        customMessage: $"a case unexpectedly hit LastCapturedResponse's throwing guard:{Environment.NewLine}{test.Output}");
        test.Output.ShouldNotContain("captured response is being used as the test's verdict",
        customMessage: $"a case unexpectedly warned about a swallowed exception that never happened:{Environment.NewLine}{test.Output}");
    }

    /// <summary>
    /// Task 4 / F11's live wire proof — the whole of F11 in one assertion. Before this plan (the
    /// template never emitted <c>RequireSecondaryIdentityLacks</c>, whatever
    /// <c>TestCasePlan.RequiredScopes</c> carried), the generated suite fails here:
    /// <c>GetScopedSecureResource_Forbidden</c> would run, send a request carrying
    /// <c>token-for-secondary</c>, and get a real 200 back from
    /// <see cref="GoldenApiStub.HandleScopedSecureResource"/> — the secondary identity genuinely
    /// holds <c>"orders.write"</c> (<see cref="GoldenTokenProviderSources.TwoIdentityTokenProvider"/>),
    /// so a correct API authorizes it, and asserting 403 against that 200 fails.
    /// <para>
    /// Asserts both that the run has no failures, and that the specific case is
    /// <c>NotExecuted</c> in the .trx: "no failures" alone would also describe a suite that simply
    /// stopped generating the case at all, which is not what this test exists to prove.
    /// <c>NotExecuted</c>, not "Skipped" — confirmed on MSTest 4.3.3 / .NET 10 (v1-c) to be the
    /// .trx's own spelling for an <c>Assert.Inconclusive</c> outcome; "Skipped" is only the console
    /// summary's word for the same thing.
    /// </para>
    /// <para>
    /// The other half, closing a gap the first pass of this test left open: pinning only the
    /// skip branch means a guard that over-skips — <c>All</c> flipped to <c>Any</c>, or the
    /// empty-<c>requiredScopes</c> early return removed — would still leave the whole repo suite
    /// green, since every scoped 403 case would turn skip-green too. <c>SpecWithScopedSecuredOperation</c>
    /// now also declares <c>getScopedSecureResourceRequiringDelete</c>, requiring both
    /// <c>"orders.write"</c> (which the secondary identity holds) and <c>"orders.delete"</c>
    /// (which it does not) — partial overlap, deliberately, so <c>All</c> and <c>Any</c> actually
    /// disagree on it; a single scope the secondary entirely lacks would not distinguish them. So
    /// its <c>_Forbidden</c> case must run (<c>Passed</c>, not <c>NotExecuted</c>) and receive a
    /// real 403 from <see cref="GoldenApiStub.HandleScopedSecureResourceRequiringDelete"/>. Both
    /// operations share the <c>ScopedSecure</c> tag, so both land in the same generated
    /// <c>ScopedSecureTests.g.cs</c> file and the same trx this test already reads — one guard,
    /// both branches, proven in one run.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task AForbiddenCaseTheSecondaryIdentityIsAuthorizedForSkipsRatherThanFails()
    {
        File.WriteAllText(Path.Combine(_root, "spec.json"), SpecWithScopedSecuredOperation);

        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();
        RegisterTokenProvider();

        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        // Guard against the generated suite silently missing the case, or the guard call, entirely
        // — the same pattern every other live-wire test in this file uses.
        var generatedFile = Directory.GetFiles(_root, "ScopedSecureTests.g.cs", SearchOption.AllDirectories)
            .ShouldHaveSingleItem("generate should have produced exactly one ScopedSecureTests.g.cs");
        var generated = File.ReadAllText(generatedFile);
        generated.ShouldContain("GetScopedSecureResource_Forbidden",
        customMessage: "the wrong-scope 403 case this test exists to prove must actually be generated");
        generated.ShouldContain("RequireSecondaryIdentityLacks(\"orders.write\");",
        customMessage: "Task 4: the scoped 403 case must carry both guards, not just RequireMultipleIdentities");
        generated.ShouldContain("GetScopedSecureResourceRequiringDelete_Forbidden",
        customMessage: "the wrong-scope 403 case that must actually run — the guard's other half — must be generated");
        generated.ShouldContain("RequireSecondaryIdentityLacks(\"orders.delete\", \"orders.write\");",
        customMessage: "Task 4: the running scoped 403 case must carry both guards too, not just RequireMultipleIdentities, " +
                       "and must union both required scopes (ordinal-sorted) rather than just one");

        var build = await ProcessRunner.RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");
        build.ExitCode.ShouldBe(0, $"generated project failed to build:{Environment.NewLine}{build.Output}");

        var resultsDir = Path.Combine(_root, "TestResults");
        var test = await ProcessRunner.RunAsync("dotnet",
        $"test \"{_root}\" --no-build --nologo --logger \"trx;LogFileName=results.trx\" --results-directory \"{resultsDir}\"");

        var trxPath = Directory.GetFiles(resultsDir, "results.trx", SearchOption.AllDirectories)
            .ShouldHaveSingleItem($"expected exactly one results.trx under {resultsDir}:{Environment.NewLine}{test.Output}");

        var trx = XDocument.Load(trxPath);
        var results = trx.Descendants().Where(e => e.Name.LocalName == "UnitTestResult").ToList();

        results.Count.ShouldBe(6,
        $"expected exactly 6 tests (Contract, Unauthorized, Forbidden for each of the two scoped " +
        $"operations) but the trx recorded {results.Count}:{Environment.NewLine}{test.Output}");

        // "No failures" alone would also describe a suite that stopped generating the case at
        // all — the count assertion above already rules that out, but this states the "no
        // failures" half explicitly and by name, over every result in the run.
        var failed = results.Where(e => e.Attribute("outcome")?.Value == "Failed").ToList();
        failed.ShouldBeEmpty(
        $"expected no failures in the run, but {failed.Count} test(s) failed:{Environment.NewLine}{test.Output}");

        var forbiddenResult = results.SingleOrDefault(e =>
            (e.Attribute("testName")?.Value ?? "").Contains("GetScopedSecureResource_Forbidden", StringComparison.Ordinal));
        forbiddenResult.ShouldNotBeNull(
        $"GetScopedSecureResource_Forbidden did not appear in the trx at all:{Environment.NewLine}{test.Output}");
        forbiddenResult!.Attribute("outcome")?.Value.ShouldBe("NotExecuted",
        $"GetScopedSecureResource_Forbidden should have been skipped by RequireSecondaryIdentityLacks " +
        $"— the secondary identity holds the scope this operation requires, so it cannot produce a real " +
        $"403:{Environment.NewLine}{test.Output}");

        // The success and 401 cases must still genuinely pass — the guard must skip only the one
        // case it exists for, not the whole class.
        foreach (var name in new[] { "GetScopedSecureResource_Contract", "GetScopedSecureResource_Unauthorized" })
        {
            var result = results.SingleOrDefault(e => (e.Attribute("testName")?.Value ?? "").Contains(name, StringComparison.Ordinal));
            result.ShouldNotBeNull($"{name} did not appear in the trx at all:{Environment.NewLine}{test.Output}");
            result!.Attribute("outcome")?.Value.ShouldBe("Passed",
            $"{name} did not receive its expected real status over the wire:{Environment.NewLine}{test.Output}");
        }

        // The gap this test exists to close: nothing above proves the guard does not over-skip.
        // GetScopedSecureResourceRequiringDelete_Forbidden's secondary identity lacks
        // "orders.delete" entirely, so RequireSecondaryIdentityLacks must let this case run —
        // Passed, not NotExecuted — and it must receive a real 403 over the wire from
        // GoldenApiStub.HandleScopedSecureResourceRequiringDelete.
        var runningForbiddenResult = results.SingleOrDefault(e =>
            (e.Attribute("testName")?.Value ?? "").Contains("GetScopedSecureResourceRequiringDelete_Forbidden", StringComparison.Ordinal));
        runningForbiddenResult.ShouldNotBeNull(
        $"GetScopedSecureResourceRequiringDelete_Forbidden did not appear in the trx at all:{Environment.NewLine}{test.Output}");
        runningForbiddenResult!.Attribute("outcome")?.Value.ShouldBe("Passed",
        $"GetScopedSecureResourceRequiringDelete_Forbidden should have run — the secondary identity does " +
        $"not hold \"orders.delete\", so RequireSecondaryIdentityLacks must not skip it, and it must " +
        $"receive a real 403:{Environment.NewLine}{test.Output}");

        foreach (var name in new[] { "GetScopedSecureResourceRequiringDelete_Contract", "GetScopedSecureResourceRequiringDelete_Unauthorized" })
        {
            var result = results.SingleOrDefault(e => (e.Attribute("testName")?.Value ?? "").Contains(name, StringComparison.Ordinal));
            result.ShouldNotBeNull($"{name} did not appear in the trx at all:{Environment.NewLine}{test.Output}");
            result!.Attribute("outcome")?.Value.ShouldBe("Passed",
            $"{name} did not receive its expected real status over the wire:{Environment.NewLine}{test.Output}");
        }

        test.ExitCode.ShouldBe(0, test.Output);

        // outcome="NotExecuted" is also satisfied by a guard that runs after the request has
        // already gone out — a case that sends real traffic and then reports as skipped. For a
        // wrong-scope 403 on a mutating operation that is exactly the hazard the plan's
        // unmatchable-id rule exists to prevent. The .trx cannot distinguish the two; the stub
        // can.
        _stub.ReceivedPaths.Count(p => p == "/api/secure-scoped").ShouldBe(2,
        "Contract and Unauthorized reach the stub; the skipped Forbidden case must never build a request.");

        // The stub-hit assertion is what distinguishes "ran and got a real 403" from "skipped
        // quietly": all three cases for this operation — including Forbidden — must reach the
        // wire, unlike the skipped operation just above.
        _stub.ReceivedPaths.Count(p => p == "/api/secure-scoped-delete").ShouldBe(3,
        "Contract, Unauthorized, and the running Forbidden case must all reach the stub over the wire.");
    }

    /// <summary>
    /// Task 8's own guard: Task 8 is a transcript (the v1-b acceptance run against
    /// <c>samples/Catalog.Api</c>, recorded in <c>docs/v0-acceptance.md</c>) proving F7 closed by
    /// running a generated suite twice against the same store, by hand. A manual result regresses
    /// silently — nobody notices until the next acceptance run — so this reproduces that same
    /// shape automatically: <see cref="GoldenFixtureSources.RepeatableSeedFixture"/> is
    /// <c>CatalogSeedFixture</c>'s create-then-clean-up pair reduced to what it needs, run against
    /// <see cref="GoldenApiStub"/>'s stateful <c>/api/items</c> store, which 409s a duplicate
    /// <c>sku</c> and 404s a delete of a row it does not know about — the exact two failure modes
    /// F7 reproduced.
    /// <para>
    /// Strengthened past the plan's own snippet (<c>Output.ShouldContain("Passed!")</c> twice),
    /// which several earlier tasks' plan-supplied snippets already turned out to be vacuous
    /// against: a suite that ran zero tests, or one whose operations were all blocked by fixture
    /// validation before a single request went out, would still print "Passed!". Both runs are
    /// instead checked against their own trx — exact test count, and both operations individually
    /// present and Passed, the same pattern <see cref="FixtureParameterReachesALiveRequestEndToEnd"/>
    /// and <see cref="APublishedFixtureKeyReachesALiveRequest"/> already use above.
    /// </para>
    /// <para>
    /// That still leaves the plan's own stated worry in Task 8 Step 3 open: a second run could
    /// pass "for the wrong reason" — because nothing was ever created, rather than because
    /// creation and teardown both genuinely worked. A review round on this task found that the
    /// first draft here only closed the "created" half: <see cref="TestHost.CleanupAsync"/> swallows
    /// a <c>FixtureLifecycleException</c> by design (its own doc explains why — a teardown
    /// complaint must not bury a real test failure), so a cleanup delete that targets the wrong
    /// id neither fails a test nor fails the run; it only stops being observed. The reviewer
    /// proved this by sabotaging <c>RepeatableSeedFixture</c>'s own cleanup to a bogus id and
    /// watching the guard stay green. <see cref="GoldenFixtureSources.RepeatableSeedFixture"/> now
    /// seeds a second item nothing else ever references or deletes, so its cleanup is the only
    /// thing that can remove it — see that constant's own doc for the full reasoning. The three
    /// assertions on <see cref="_stub"/> after both runs close the gap from the outside, in the
    /// same spirit as <see cref="APublishedFixtureKeyReachesALiveRequest"/>'s check against
    /// <see cref="GoldenApiStub.ReceivedPaths"/>: <see cref="GoldenApiStub.ItemCount"/> proves both
    /// that a real, uncleaned-up row exists per run (the generated <c>CreateItem_Contract</c>
    /// test's own create, which nothing deletes — the same permanent-leak shape as
    /// <c>CatalogSeedFixture</c>'s product) <em>and</em> that the cleanup-only row from each run
    /// was genuinely removed, not merely requested; the create/delete call counts on
    /// <see cref="GoldenApiStub.ReceivedPaths"/> prove every one of those live calls — fixture and
    /// generated-test alike — actually happened, every run, not merely once.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task TheGeneratedSuitePassesTwiceAgainstTheSameStore()
    {
        await ScaffoldGenerateAndBuildWithSeedingFixture();

        await RunAndAssertBothOperationsPassAsync("run1");
        await RunAndAssertBothOperationsPassAsync("run2");

        // Distinguishes "passed because it worked" from "passed because nothing happened" (Task
        // 8 Step 3's own stated worry) — including the teardown half a review round found this
        // count alone did not originally cover (see this test's own doc). Per run: the seeded
        // item and the cleanup-only item are both created and both genuinely deleted (net zero
        // each), and CreateItem_Contract's own item is never cleaned up (mirrors
        // CatalogSeedFixture's permanently-leaked product). Two genuine runs therefore leave
        // exactly two rows behind — no more (a cleanup that no-ops, or deletes the wrong id,
        // leaves the cleanup-only item behind too and this comes out higher) and no fewer (a
        // create that silently did not happen brings it down).
        _stub.ItemCount.ShouldBe(2,
        $"expected exactly 2 leaked items after two runs (one per run's CreateItem_Contract, " +
        $"never cleaned up) but the store has {_stub.ItemCount} — a lower count means a create " +
        $"silently did not happen; a higher count means a delete or its cleanup did not remove " +
        $"the row it was supposed to.");

        // 3 POSTs per run: the seeding fixture's own seed item, its cleanup-only item, and the
        // generated CreateItem_Contract test's own create.
        var createCalls = _stub.ReceivedPaths.Count(p => p == "/api/items");
        createCalls.ShouldBe(6,
        $"expected 6 POST /api/items calls (3 per run: the seeding fixture's seed item, its " +
        $"cleanup-only item, and the generated CreateItem_Contract test) but saw {createCalls}. " +
        $"Paths served: {string.Join(", ", _stub.ReceivedPaths)}");

        // 3 DELETEs per run: the generated DeleteItem_Contract test (targets the seed item), the
        // seed item's own cleanup (tolerates the 404 from the line above), and the cleanup-only
        // item's cleanup (must be a genuine 204 — nothing else could have deleted it first).
        var deleteCalls = _stub.ReceivedPaths.Count(p => p.StartsWith("/api/items/", StringComparison.Ordinal));
        deleteCalls.ShouldBe(6,
        $"expected 6 DELETE /api/items/{{id}} calls (3 per run: DeleteItem_Contract, the seed " +
        $"item's cleanup, and the cleanup-only item's cleanup) but saw {deleteCalls}. Paths " +
        $"served: {string.Join(", ", _stub.ReceivedPaths)}");
    }

    /// <summary>
    /// Builds once — generate, fill <c>fixtures/createItem.json</c>'s <c>sku</c> and
    /// <c>fixtures/deleteItem.json</c>'s <c>id</c> with fixture tokens, register
    /// <see cref="GoldenFixtureSources.RepeatableSeedFixture"/>, then build — mirroring the v1-b
    /// acceptance run's own shape: one build, then two <c>dotnet test --no-build</c> invocations
    /// against the same running <see cref="_stub"/>, exactly as its two invocations ran against
    /// the same, never-restarted <c>samples/Catalog.Api</c> process and the same, never-reset
    /// database.
    /// </summary>
    private async Task ScaffoldGenerateAndBuildWithSeedingFixture()
    {
        File.WriteAllText(Path.Combine(_root, "spec.json"), SpecWithItemsLifecycle);

        InitCommand.Run(_root, "Stub.ApiTests", "spec.json").ShouldBe(0);
        UseProjectReferenceInsteadOfPackage();
        PointAtStub();

        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        var createFixturePath = Path.Combine(_root, "fixtures", "createItem.json");
        var createFixture = File.ReadAllText(createFixturePath);
        createFixture.ShouldContain("\"TODO:sku\"",
        customMessage: "a required body property always gets a sentinel (decision 1)");
        File.WriteAllText(createFixturePath,
        createFixture.Replace("\"TODO:sku\"", "\"{{fixture:newItem.sku}}\"", StringComparison.Ordinal));

        var deleteFixturePath = Path.Combine(_root, "fixtures", "deleteItem.json");
        var deleteFixture = File.ReadAllText(deleteFixturePath);
        deleteFixture.ShouldContain("\"TODO:id\"",
        customMessage: "a required path parameter always gets a sentinel (decision 1)");
        File.WriteAllText(deleteFixturePath,
        deleteFixture.Replace("\"TODO:id\"", "\"{{fixture:seededItem.id}}\"", StringComparison.Ordinal));

        File.WriteAllText(Path.Combine(_root, "RepeatableSeedFixture.cs"), GoldenFixtureSources.RepeatableSeedFixture);
        RegisterFixture("RepeatableSeedFixture");

        var build = await ProcessRunner.RunAsync("dotnet", $"build \"{_root}\" --nologo -v q");
        build.ExitCode.ShouldBe(0, $"generated project failed to build:{Environment.NewLine}{build.Output}");
    }

    /// <summary>
    /// Runs <c>dotnet test --no-build</c> once and asserts, from its trx rather than its console
    /// text, that exactly two tests ran and both — <c>CreateItem_Contract</c> and
    /// <c>DeleteItem_Contract</c> — passed. Checking the count is what closes the plan's own
    /// stated gap in its snippet: a suite that silently ran zero tests still prints "Passed!".
    /// </summary>
    private async Task RunAndAssertBothOperationsPassAsync(string label)
    {
        var resultsDir = Path.Combine(_root, "TestResults", label);
        var test = await ProcessRunner.RunAsync("dotnet",
        $"test \"{_root}\" --no-build --nologo --logger \"trx;LogFileName=results.trx\" --results-directory \"{resultsDir}\"");

        var trxPath = Directory.GetFiles(resultsDir, "results.trx", SearchOption.AllDirectories)
            .ShouldHaveSingleItem($"[{label}] expected exactly one results.trx under {resultsDir}:{Environment.NewLine}{test.Output}");

        var trx = XDocument.Load(trxPath);
        var results = trx.Descendants().Where(e => e.Name.LocalName == "UnitTestResult").ToList();

        results.Count.ShouldBe(2,
        $"[{label}] expected exactly 2 tests (CreateItem_Contract, DeleteItem_Contract) but " +
        $"the trx recorded {results.Count}:{Environment.NewLine}{test.Output}");

        foreach (var name in new[] { "CreateItem_Contract", "DeleteItem_Contract" })
        {
            var result = results.SingleOrDefault(e => (e.Attribute("testName")?.Value ?? "").Contains(name, StringComparison.Ordinal));
            result.ShouldNotBeNull($"[{label}] {name} did not appear in the trx at all:{Environment.NewLine}{test.Output}");
            result!.Attribute("outcome")?.Value.ShouldBe("Passed",
            $"[{label}] {name} ran but did not pass:{Environment.NewLine}{test.Output}");
        }

        test.ExitCode.ShouldBe(0, $"[{label}]{Environment.NewLine}{test.Output}");
    }

    private void PointAtStub()
    {
        var path = Path.Combine(_root, "appsettings.json");
        var original = File.ReadAllText(path);

        // Pinned, not merely replaced: if the scaffold's own default ever changes, this
        // assertion catches the drift here — loudly, at the one place that must stay in sync
        // with GoldenApiStub.RequiredReadyProbes — rather than a correct TestHost silently
        // failing a seeding-vs-readiness golden test with a bare, seemingly-unrelated 503 (M4).
        const string consecutiveSuccessesMarker = "\"ConsecutiveSuccesses\": 2";
        original.ShouldContain(consecutiveSuccessesMarker,
        customMessage: "the scaffold's default InTest:Readiness:ConsecutiveSuccesses changed — " +
                       "update GoldenApiStub.RequiredReadyProbes and this replacement together");

        var json = original
            .Replace("https://localhost:5001/", $"http://localhost:{_stub.Port}/", StringComparison.Ordinal)
            .Replace("\"TimeoutSeconds\": 120", "\"TimeoutSeconds\": 20", StringComparison.Ordinal)
            .Replace(consecutiveSuccessesMarker, $"\"ConsecutiveSuccesses\": {GoldenApiStub.RequiredReadyProbes}", StringComparison.Ordinal);

        File.WriteAllText(path, json);
    }

    /// <summary>
    /// The scaffold references InTest.Runtime from NuGet, which is not published. The needle
    /// tracks <see cref="CliVersion.Current"/> rather than a hardcoded "0.1.0" -- see
    /// ScaffoldCompileVerificationTests.UseProjectReferenceInsteadOfPackage's identical needle
    /// for the full account of why a hardcoded literal here silently stopped matching (a
    /// coincidence of InTest.Cli's own version happening to equal "0.1.0" is not the same as this
    /// needle being correct by construction) and asserted, not merely interpolated, so a future
    /// scaffold-format drift fails loudly here rather than several steps downstream as a
    /// confusing NU1101 against nuget.org.
    /// </summary>
    private void UseProjectReferenceInsteadOfPackage()
    {
        var runtimeProject = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "InTest.Runtime.MSTest", "InTest.Runtime.MSTest.csproj"));

        var path = Path.Combine(_root, "Stub.ApiTests.csproj");
        var csprojText = File.ReadAllText(path);

        var needle = $"""<PackageReference Include="InTest.Runtime.MSTest" Version="{CliVersion.Current}" />""";
        csprojText.ShouldContain(needle, Case.Sensitive,
        "InitCommand's scaffold no longer writes InTest.Runtime.MSTest's PackageReference in the " +
        "expected shape (Include=\"InTest.Runtime.MSTest\" Version=\"{CliVersion.Current}\") -- " +
        "update this needle alongside whatever changed.");

        var csproj = csprojText.Replace(
        needle,
        $"""<ProjectReference Include="{runtimeProject}" />""",
        StringComparison.Ordinal);

        File.WriteAllText(path, csproj);
    }

    /// <summary>
    /// Wires a fixture class already written into <c>_root</c> into <c>TestStartup.cs</c>'s
    /// <c>Register</c> hook, the way an adopter would — replacing the scaffold's own
    /// placeholder comment, which must still be present or this is silently a no-op.
    /// </summary>
    private void RegisterFixture(string typeName)
    {
        var testStartupPath = Path.Combine(_root, "TestStartup.cs");
        var testStartup = File.ReadAllText(testStartupPath);
        const string placeholder = "// services.AddSingleton<IAssemblyFixture, YourFixture>();";
        testStartup.ShouldContain(placeholder,
        customMessage: "the scaffolded registration placeholder must still be present to replace");

        File.WriteAllText(testStartupPath, testStartup.Replace(
        placeholder,
        $"services.AddSingleton<IAssemblyFixture, {typeName}>();",
        StringComparison.Ordinal));
    }

    /// <summary>
    /// Writes <see cref="GoldenTokenProviderSources.TwoIdentityTokenProvider"/> into the project
    /// and registers it in <c>TestStartup.cs</c>'s <c>Register</c> hook, anchored on the same
    /// comment <see cref="AttachThrowingHandlerToApiClient"/> uses — <c>AuthHandler</c> is
    /// already attached to <c>InTestClients.Api</c> (Task 2), so unlike that method this needs no
    /// separate <c>AddHttpClient</c> wiring, only the provider registration itself.
    /// </summary>
    private void RegisterTokenProvider()
    {
        File.WriteAllText(Path.Combine(_root, "TwoIdentityTokenProvider.cs"), GoldenTokenProviderSources.TwoIdentityTokenProvider);

        var testStartupPath = Path.Combine(_root, "TestStartup.cs");
        var testStartup = File.ReadAllText(testStartupPath);
        const string anchor = "// Per-request fixtures: path and query parameter values live in fixtures/, not";
        testStartup.ShouldContain(anchor,
        customMessage: "the scaffolded Register method's comment must still be present to anchor this edit");

        File.WriteAllText(testStartupPath, testStartup.Replace(
        anchor,
        "services.AddSingleton<ITestTokenProvider, TwoIdentityTokenProvider>();\n\n        " + anchor,
        StringComparison.Ordinal));
    }

    /// <summary>
    /// Writes <see cref="GoldenAuthHandlerSources.AlwaysThrowsHandler"/> into the project and
    /// wires it onto <c>InTestClients.Api</c> in <c>TestStartup.cs</c>'s <c>Register</c> hook.
    /// This is a deliberate simulation of the pre-<c>AuthHandler</c> adopter pattern the
    /// scaffold's own <c>Register</c> doc comment now explicitly forbids ("do not also append a
    /// DelegatingHandler of your own, or two handlers will set Authorization and the last one
    /// registered silently wins") — used anyway because F10's regression guard needs *some*
    /// handler on that client that fails, and this is the shape an adopter's own handler took
    /// before <c>AuthHandler</c> existed to attach one automatically. Never touches
    /// <c>InTestClients.Readiness</c>: that omission is the entire point of Task 1.
    /// </summary>
    private void AttachThrowingHandlerToApiClient()
    {
        File.WriteAllText(Path.Combine(_root, "AlwaysThrowsHandler.cs"), GoldenAuthHandlerSources.AlwaysThrowsHandler);

        var testStartupPath = Path.Combine(_root, "TestStartup.cs");
        var testStartup = File.ReadAllText(testStartupPath);
        const string anchor = "// Per-request fixtures: path and query parameter values live in fixtures/, not";
        testStartup.ShouldContain(anchor,
        customMessage: "the scaffolded Register method's comment must still be present to anchor this edit");

        File.WriteAllText(testStartupPath, testStartup.Replace(
        anchor,
        "services.AddTransient<AlwaysThrowsHandler>();\n        services.AddHttpClient(InTestClients.Api).AddHttpMessageHandler<AlwaysThrowsHandler>();\n\n        " + anchor,
        StringComparison.Ordinal));
    }

    /// <summary>
    /// Resolves the [capture-is-opt-in] registration circularity stage 1 of
    /// <c>docs/superpowers/plans/2026-08-25-intest-typed-client-invocation.md</c> names explicitly:
    /// <c>clientCaptureEnabled</c> in <c>Generated/spec-paths.json</c> is written by
    /// <c>GenerateCommand</c> from a resolved <c>TestCasePlan.ClientCallExpression</c> — both
    /// stage 2/3 work that does not exist yet — and this scaffold has no <c>client</c> config
    /// section to make <c>generate</c> write it. Without this patch,
    /// <c>InTest.Runtime.ResponseCaptureHandler</c> would never be attached to
    /// <c>InTestClients.Api</c> and every one of this file's client-routed golden tests would
    /// fail with <c>ApiTestCore.LastCapturedResponse</c>'s own "[client-rides-the-api-pipeline]:
    /// no response has been captured" exception, not the schema-violation or status-mismatch
    /// failures they actually exist to prove.
    /// <para>
    /// Runs <em>after</em> <c>GenerateCommand.RunAsync</c>, never before — <c>Generated/</c> is
    /// deleted and rewritten wholesale by every `generate` run (the ownership table in
    /// <c>CLAUDE.md</c>), so patching first would just be overwritten. Assert-first, the same
    /// discipline <c>RegisterFixture</c>/<c>RegisterTokenProvider</c>/<c>AttachThrowingHandlerToApiClient</c>
    /// already use for their own <c>TestStartup.cs</c> anchor edits: the file must exist and the
    /// key must be genuinely absent before this writes anything, so a future stage that starts
    /// writing <c>clientCaptureEnabled</c> itself (once <c>ClientCallPlanner</c> exists) fails this
    /// assertion loudly here rather than this patch silently no-opping or double-writing the key.
    /// </para>
    /// </summary>
    private void EnableClientCaptureInSpecPaths()
    {
        var specPathsPath = Path.Combine(_root, "Generated", "spec-paths.json");
        File.Exists(specPathsPath).ShouldBeTrue(
        "generate should have written Generated/spec-paths.json before this stage-1 patch runs");

        var original = File.ReadAllText(specPathsPath);
        original.ShouldNotContain("clientCaptureEnabled",
        customMessage: "generate itself must not write clientCaptureEnabled yet — ClientCallPlanner " +
                       "is stage 2/3 work that does not exist in this worktree. If this now fails, " +
                       "generate started writing the key itself and this stage-1 patch step should " +
                       "be removed (or updated to match whatever shape generate now emits) rather " +
                       "than silently double-writing it.");

        var node = JsonNode.Parse(original)!.AsObject();
        node["clientCaptureEnabled"] = true;

        // Same writer options InTest.Cli.Json.CommittedJsonOptions.Value uses for this exact
        // file — indented, CRLF interior line endings — so the patched file stays byte-shaped
        // like whatever `generate` itself would have written, even though this patch runs from
        // the golden test project rather than from InTest.Cli.Json's internal type directly.
        var patched = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\r\n" }) + "\r\n";
        File.WriteAllText(specPathsPath, patched);
    }

    /// <summary>
    /// Writes <see cref="GoldenTypedClientSources.FakeStatusClient"/> and
    /// <see cref="GoldenTypedClientSources.ClientRoutedStatusTests"/> into the project and
    /// registers <c>FakeStatusClient</c> in <c>TestStartup.cs</c>'s <c>Register</c> hook, over
    /// <c>IHttpClientFactory.CreateClient(InTestClients.Api)</c> per
    /// <c>[client-rides-the-api-pipeline]</c> — the same anchor
    /// <see cref="RegisterTokenProvider"/> and <see cref="AttachThrowingHandlerToApiClient"/> use,
    /// and the same reason: unlike a token provider (consumed by InTest itself), a typed client
    /// carries its own <c>HttpClient</c> unless deliberately built over InTest's, so this
    /// registration is what makes <see cref="InTest.Runtime.ResponseCaptureHandler"/>,
    /// <see cref="InTest.Runtime.AuthHandler"/> and <c>RunIdHandler</c> all reach requests
    /// <c>FakeStatusClient</c> sends.
    /// </summary>
    private void RegisterFakeStatusClient()
    {
        File.WriteAllText(Path.Combine(_root, "FakeStatusClient.cs"), GoldenTypedClientSources.FakeStatusClient);
        File.WriteAllText(Path.Combine(_root, "StatusTests.cs"), GoldenTypedClientSources.ClientRoutedStatusTests);

        var testStartupPath = Path.Combine(_root, "TestStartup.cs");
        var testStartup = File.ReadAllText(testStartupPath);
        const string anchor = "// Per-request fixtures: path and query parameter values live in fixtures/, not";
        testStartup.ShouldContain(anchor,
        customMessage: "the scaffolded Register method's comment must still be present to anchor this edit");

        File.WriteAllText(testStartupPath, testStartup.Replace(
        anchor,
        "services.AddTransient(sp => new FakeStatusClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(InTestClients.Api)));\n\n        " + anchor,
        StringComparison.Ordinal));
    }

    /// <summary>
    /// Adds intest.json's optional top-level <c>"client"</c> section — <c>{ "kind": "kiota",
    /// "typeName": ... }</c> — the way an adopter would hand-edit it after <c>init</c>, since
    /// <c>init</c> itself never scaffolds one (<c>--client-lockfile</c> is a later, separate
    /// stage). Assert-first, the same discipline every other <c>TestStartup.cs</c>/<c>intest.json</c>
    /// patch helper in this file uses: the section must be genuinely absent before this adds it,
    /// so a future <c>InitCommand</c> change that starts scaffolding one itself fails this
    /// assertion loudly rather than this helper silently double-writing the key.
    /// </summary>
    private void AddClientConfig(string typeName)
    {
        var path = Path.Combine(_root, "intest.json");
        var original = File.ReadAllText(path);
        original.ShouldNotContain("\"client\"",
        customMessage: "intest.json already declares a client section — InitCommand's scaffold changed");

        var node = JsonNode.Parse(original)!.AsObject();
        node["client"] = new JsonObject
        {
            ["kind"] = "kiota",
            ["typeName"] = typeName
        };

        // Same writer options EnableClientCaptureInSpecPaths uses for spec-paths.json — indented,
        // CRLF interior line endings — so this patched file stays byte-shaped like a hand-edited
        // intest.json would.
        var patched = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\r\n" }) + "\r\n";
        File.WriteAllText(path, patched);
    }

    /// <summary>
    /// Writes <see cref="GoldenTypedClientSources.FakeOrdersApiClient"/> into the project and
    /// registers it in <c>TestStartup.cs</c>'s <c>Register</c> hook, over
    /// <c>IHttpClientFactory.CreateClient(InTestClients.Api)</c> — same anchor and same reason
    /// <see cref="RegisterFakeStatusClient"/> uses. Unlike that helper, this writes no hand-written
    /// test class alongside the client: the whole point of stage 3's golden tests is that
    /// <c>generate</c> itself emits the client-routed case (<c>GetStatus_Contract</c>) once
    /// <see cref="AddClientConfig"/> has been called before it runs.
    /// </summary>
    private void RegisterFakeOrdersApiClient()
    {
        File.WriteAllText(Path.Combine(_root, "FakeOrdersApiClient.cs"), GoldenTypedClientSources.FakeOrdersApiClient);

        var testStartupPath = Path.Combine(_root, "TestStartup.cs");
        var testStartup = File.ReadAllText(testStartupPath);
        const string anchor = "// Per-request fixtures: path and query parameter values live in fixtures/, not";
        testStartup.ShouldContain(anchor,
        customMessage: "the scaffolded Register method's comment must still be present to anchor this edit");

        File.WriteAllText(testStartupPath, testStartup.Replace(
        anchor,
        "services.AddTransient(sp => new FakeOrdersApiClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(InTestClients.Api)));\n\n        " + anchor,
        StringComparison.Ordinal));
    }

    // RunAsync moved to ProcessRunner (Task 10 item 6) — shared with CompileVerificationTests
    // and ScaffoldCompileVerificationTests, which duplicated this exact block.
}

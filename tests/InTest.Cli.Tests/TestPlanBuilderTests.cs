using InTest.Cli.Planning;
using InTest.Cli.Spec;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class TestPlanBuilderTests
{
    private const string Spec = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": {
        "/orders/{id}": {
          "get": {
            "operationId": "getOrderById",
            "tags": ["Orders"],
            "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
            "responses": { "200": { "description": "ok", "content": { "application/json": {
              "schema": { "$ref": "#/components/schemas/Order" } } } } }
          }
        },
        "/health": { "get": { "responses": { "204": { "description": "no content" } } } },
        "/upload": { "post": { "tags": ["Files"],
          "requestBody": { "content": { "multipart/form-data": { "schema": { "type": "object" } } } },
          "responses": { "200": { "description": "ok" } } } }
      },
      "components": { "schemas": { "Order": { "type": "object" } } }
    }
    """;

    private static async Task<TestPlan> BuildAsync()
        => TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(Spec)).Document);

    [TestMethod]
    public async Task GroupsOperationsIntoClassesByFirstTag()
    {
        var plan = await BuildAsync();
        plan.Classes.Select(c => c.ClassName).ShouldContain("OrdersTests");
    }

    [TestMethod]
    public async Task PutsUntaggedOperationsInTheDefaultClass()
    {
        var plan = await BuildAsync();
        plan.Classes.Select(c => c.ClassName).ShouldContain("DefaultTests");
    }

    [TestMethod]
    public async Task NamesContractMethodsWithoutTheStatusCode()
    {
        var plan = await BuildAsync();
        var method = plan.Classes.SelectMany(c => c.Cases).Single(c => c.OperationKey == "getOrderById");
        method.MethodName.ShouldBe("GetOrderById_Contract");
    }

    [TestMethod]
    public async Task CarriesTheSchemaKeyForJsonResponses()
    {
        var plan = await BuildAsync();
        plan.Classes.SelectMany(c => c.Cases).Single(c => c.OperationKey == "getOrderById").SchemaKey.ShouldBe("Order");
    }

    [TestMethod]
    public async Task EmitsAStatusOnlyCaseForBodilessResponses()
    {
        var plan = await BuildAsync();
        var health = plan.Classes.SelectMany(c => c.Cases).Single(c => c.OperationKey == "get_health");
        health.ExpectedStatus.ShouldBe(204);
        health.SchemaKey.ShouldBeNull();
    }

    [TestMethod]
    public async Task SkipsUnsupportedContentTypesAndSaysWhy()
    {
        var plan = await BuildAsync();
        plan.Skipped.ShouldContain(s => s.OperationKey == "post_upload" && s.Reason.Contains("multipart/form-data"));
    }

    [TestMethod]
    public async Task RecordsPathParameterNamesInOrder()
    {
        var plan = await BuildAsync();
        plan.Classes.SelectMany(c => c.Cases).Single(c => c.OperationKey == "getOrderById")
            .PathParameterNames.ShouldBe(["id"]);
    }

    [TestMethod]
    public async Task IsDeterministic()
    {
        var first = System.Text.Json.JsonSerializer.Serialize(await BuildAsync());
        var second = System.Text.Json.JsonSerializer.Serialize(await BuildAsync());
        first.ShouldBe(second);
    }

    [TestMethod]
    public async Task SkipsAnOperationWhoseIdCannotBeAFixtureFileName()
    {
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{
            "/a":{"post":{"operationId":"Orders/Create",
              "requestBody":{"content":{"application/json":{"schema":{"type":"object"}}}},
              "responses":{"201":{"description":"ok"}}}},
            "/b":{"get":{"operationId":"listOrders","responses":{"200":{"description":"ok"}}}}}
        }
        """;

        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(spec)).Document);

        plan.Skipped.ShouldContain(sk => sk.OperationKey == "Orders/Create" && sk.Reason.Contains("'/'"));
        plan.Classes.SelectMany(c => c.Cases).ShouldContain(c => c.OperationKey == "listOrders",
            "one unusable operationId must not cost the rest of the document");
    }

    [TestMethod]
    public async Task DoesNotSkipAnUnusableIdWhenTheOperationNeedsNoFixture()
    {
        // No request body and no required parameter means no fixture is ever loaded, so the
        // filename is never needed. §12's rule is that skips remove tests and notes do not — this
        // operation is perfectly testable, so removing it would lose coverage for no reason.
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{"/a":{"get":{"operationId":"Orders/List","responses":{"200":{"description":"ok"}}}}}}
        """;

        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(spec)).Document);

        plan.Skipped.ShouldBeEmpty();
        plan.Classes.SelectMany(c => c.Cases).Count().ShouldBe(1);
    }

    [TestMethod]
    public async Task SkipsAnUnusableIdWhenAnOptionalQueryParameterCarriesAnExample()
    {
        // No body and no required parameter, but the composer still surfaces a real value for
        // this optional query parameter (tier 2) — so a fixture IS written, and the unusable
        // operationId must be caught before that write is attempted.
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{"/a":{"get":{"operationId":"Orders/List",
            "parameters":[{"name":"page","in":"query","required":false,"schema":{"type":"integer","example":2}}],
            "responses":{"200":{"description":"ok"}}}}}
        }
        """;

        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(spec)).Document);

        plan.Skipped.ShouldContain(sk => sk.OperationKey == "Orders/List" && sk.Reason.Contains("'/'"));
        plan.Classes.SelectMany(c => c.Cases).ShouldNotContain(c => c.OperationKey == "Orders/List");
    }

    [TestMethod]
    public async Task SkipsAnUnusableIdWhenAnOptionalQueryParameterCarriesADefault()
    {
        // Same shape as above but tier 3 (a declared default) rather than tier 2 (an example) —
        // the composer still emits a real value, so the same skip must fire.
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{"/a":{"get":{"operationId":"Orders/List",
            "parameters":[{"name":"page","in":"query","required":false,"schema":{"type":"integer","default":2}}],
            "responses":{"200":{"description":"ok"}}}}}
        }
        """;

        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(spec)).Document);

        plan.Skipped.ShouldContain(sk => sk.OperationKey == "Orders/List" && sk.Reason.Contains("'/'"));
        plan.Classes.SelectMany(c => c.Cases).ShouldNotContain(c => c.OperationKey == "Orders/List");
    }

    [TestMethod]
    public async Task DoesNotSkipAnUnusableIdWhenTheOptionalQueryParameterHasNeitherExampleNorDefault()
    {
        // Extends DoesNotSkipAnUnusableIdWhenTheOperationNeedsNoFixture (which covers the
        // no-parameters case) to an optional parameter that carries no example and no default:
        // the composer emits nothing for it either, so no fixture file is ever written and the
        // unusable operationId still never matters.
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{"/a":{"get":{"operationId":"Orders/List",
            "parameters":[{"name":"page","in":"query","required":false,"schema":{"type":"integer"}}],
            "responses":{"200":{"description":"ok"}}}}}
        }
        """;

        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(spec)).Document);

        plan.Skipped.ShouldBeEmpty();
        plan.Classes.SelectMany(c => c.Cases).Count().ShouldBe(1);
    }

    // --- Declared-error cases (decision 5): 404 only, and only with a path parameter. ---

    private static async Task<TestPlan> BuildAsync(string spec)
        => TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(spec)).Document);

    private const string SpecDeclaring404 = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": {
        "/orders/{id}": {
          "get": {
            "operationId": "getOrderById",
            "tags": ["Orders"],
            "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
            "responses": {
              "200": { "description": "ok", "content": { "application/json": {
                "schema": { "$ref": "#/components/schemas/Order" } } } },
              "404": { "description": "not found" }
            }
          }
        }
      },
      "components": { "schemas": { "Order": { "type": "object" } } }
    }
    """;

    [TestMethod]
    public async Task EmitsADeclaredErrorCaseFor404WhenTheOperationHasAPathParameter()
    {
        var plan = await BuildAsync(SpecDeclaring404);
        var cases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.OperationKey == "getOrderById").ToList();

        cases.ShouldContain(c => c.ExpectedStatus == 404 && c.Role == CaseRole.DeclaredError);
    }

    [TestMethod]
    public async Task NamesTheDeclaredErrorCaseByItsStatusRatherThanACollisionSuffix()
    {
        // "GetOrderById_NotFound", not "GetOrderById_Contract2" — decision 4's dedupe machinery
        // must never be what names a genuinely distinct case; only real name collisions get a
        // hash suffix.
        var plan = await BuildAsync(SpecDeclaring404);
        var notFound = plan.Classes.SelectMany(c => c.Cases).Single(c => c.ExpectedStatus == 404);

        notFound.MethodName.ShouldBe("GetOrderById_NotFound");
    }

    [TestMethod]
    public async Task TheDeclaredErrorMethodNameDoesNotMoveWhenAnUnrelatedOperationIsAdded()
    {
        // Decision 4 warns that keying the dedupe machinery on operation identity + role must
        // never let the *number* or *order* of other declared-error cases in the document shift
        // a name that has nothing to do with them — only a genuine name collision may add a
        // suffix. Rebuilding the same document twice (the previous shape of this test) cannot
        // exercise that: TestPlanBuilder.Build is a pure function, so identical input trivially
        // produces identical output regardless of how names are derived. This spec instead adds
        // an unrelated operation — also declaring 404, also with a path parameter, ordered before
        // "getOrderById" in the document — and checks that getOrderById's declared-error name is
        // unaffected. An implementation that assigns the "_NotFound" suffix by counting declared-
        // error cases in processing order, rather than keying strictly on operation identity,
        // fails this: getCustomerById's declared-error case is now first in doc order, so
        // getOrderById's would shift.
        const string specWithAPrecedingUnrelated404 = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Orders", "version": "1.0" },
          "paths": {
            "/customers/{id}": {
              "get": {
                "operationId": "getCustomerById",
                "tags": ["Customers"],
                "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
                "responses": {
                  "200": { "description": "ok", "content": { "application/json": {
                    "schema": { "$ref": "#/components/schemas/Order" } } } },
                  "404": { "description": "not found" }
                }
              }
            },
            "/orders/{id}": {
              "get": {
                "operationId": "getOrderById",
                "tags": ["Orders"],
                "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
                "responses": {
                  "200": { "description": "ok", "content": { "application/json": {
                    "schema": { "$ref": "#/components/schemas/Order" } } } },
                  "404": { "description": "not found" }
                }
              }
            }
          },
          "components": { "schemas": { "Order": { "type": "object" } } }
        }
        """;

        var plan = await BuildAsync(specWithAPrecedingUnrelated404);
        var notFound = plan.Classes.SelectMany(c => c.Cases)
            .Single(c => c.OperationKey == "getOrderById" && c.Role == CaseRole.DeclaredError);

        notFound.MethodName.ShouldBe("GetOrderById_NotFound");
    }

    [TestMethod]
    public async Task ANotFoundCaseUsesAnUnmatchableIdRatherThanAFixture()
    {
        var plan = await BuildAsync(SpecDeclaring404);
        var notFound = plan.Classes.SelectMany(c => c.Cases).Single(c => c.ExpectedStatus == 404);

        // A 404 test needs no data, so it must not be blocked by an unfilled fixture. Decision 6.
        notFound.NeedsFixture.ShouldBeFalse();
    }

    [TestMethod]
    public async Task ANotFoundCaseForAnIntegerPathParameterCarriesItsDeclaredKind()
    {
        // Review finding on Task 4: the renderer needs to know a path parameter is `type:
        // integer` to pick a well-typed unmatchable value instead of a GUID (see
        // TemplateRendererTests). TestPlanBuilder is the only place that ever reads the spec's
        // parameter schema, so it must carry the kind forward on the declared-error case.
        const string spec = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Orders", "version": "1.0" },
          "paths": {
            "/orders/{id}": {
              "get": {
                "operationId": "getOrderById",
                "tags": ["Orders"],
                "parameters": [
                  { "name": "id", "in": "path", "required": true, "schema": { "type": "integer", "format": "int32" } }
                ],
                "responses": {
                  "200": { "description": "ok", "content": { "application/json": {
                    "schema": { "$ref": "#/components/schemas/Order" } } } },
                  "404": { "description": "not found" }
                }
              }
            }
          },
          "components": { "schemas": { "Order": { "type": "object" } } }
        }
        """;

        var plan = await BuildAsync(spec);
        var notFound = plan.Classes.SelectMany(c => c.Cases).Single(c => c.Role == CaseRole.DeclaredError);

        notFound.PathParameterKinds.ShouldBe([PathParameterKind.Integer]);
    }

    // --- [typed-path-parameters]: ResolvePathParameterKinds is format-aware, not type-alone.
    // Each test below builds a plan from a real schema shape and reads the kind back off the
    // Success case rather than the declared-error one — Success is the new call site this task
    // added (TemplateRenderer.BuildClientCallExpression's per-kind conversion needs it there), so
    // asserting against it is the one that would actually catch a regression in that wiring,
    // not just in ResolvePathParameterKind's own mapping. ---

    private static async Task<PathParameterKind> ResolvedKindAsync(string schemaJson)
    {
        var spec = $$"""
        {
          "openapi": "3.0.3",
          "info": { "title": "Orders", "version": "1.0" },
          "paths": {
            "/orders/{id}": {
              "get": {
                "operationId": "getOrderById",
                "tags": ["Orders"],
                "parameters": [
                  { "name": "id", "in": "path", "required": true, "schema": {{schemaJson}} }
                ],
                "responses": {
                  "200": { "description": "ok", "content": { "application/json": {
                    "schema": { "$ref": "#/components/schemas/Order" } } } }
                }
              }
            }
          },
          "components": { "schemas": { "Order": { "type": "object" } } }
        }
        """;

        var plan = await BuildAsync(spec);
        var success = plan.Classes.SelectMany(c => c.Cases).Single(c => c.Role == CaseRole.Success);

        return success.PathParameterKinds.ShouldHaveSingleItem();
    }

    [TestMethod]
    public async Task APlainStringPathParameterResolvesToStringKind()
        => (await ResolvedKindAsync("""{ "type": "string" }""")).ShouldBe(PathParameterKind.String);

    [TestMethod]
    public async Task AUuidFormattedStringPathParameterResolvesToGuidKind()
        // The measured shape a real kiota 1.34.1 client's this[Guid] indexer overload expects
        // (typed-client-invocation plan's measurement table) — previously indistinguishable from
        // a plain string, since only "numeric or not" mattered before the client-routed branch's
        // per-kind conversion existed.
        => (await ResolvedKindAsync("""{ "type": "string", "format": "uuid" }""")).ShouldBe(PathParameterKind.Guid);

    [TestMethod]
    public async Task ANonUuidFormattedStringPathParameterStillResolvesToStringKind()
        // A declared format this resolver does not specifically recognize must fall through to
        // String, not be silently misclassified as Guid — ClientCallPlanner.Resolve's own doc
        // comment relies on ResolvePathParameterKind being exhaustive over every schema shape it
        // can see, with String as the catch-all.
        => (await ResolvedKindAsync("""{ "type": "string", "format": "date-time" }""")).ShouldBe(PathParameterKind.String);

    [TestMethod]
    public async Task ABareIntegerPathParameterWithNoFormatResolvesToIntegerKind()
        => (await ResolvedKindAsync("""{ "type": "integer" }""")).ShouldBe(PathParameterKind.Integer);

    [TestMethod]
    public async Task AnInt32FormattedIntegerPathParameterResolvesToIntegerKind()
        => (await ResolvedKindAsync("""{ "type": "integer", "format": "int32" }""")).ShouldBe(PathParameterKind.Integer);

    [TestMethod]
    public async Task AnInt64FormattedIntegerPathParameterResolvesToLongKind()
        => (await ResolvedKindAsync("""{ "type": "integer", "format": "int64" }""")).ShouldBe(PathParameterKind.Long);

    [TestMethod]
    public async Task TheSuccessCaseIsUnaffectedByTheDeclaredErrorCaseItGainsANeighbour()
    {
        var plan = await BuildAsync(SpecDeclaring404);
        var success = plan.Classes.SelectMany(c => c.Cases).Single(c => c.Role == CaseRole.Success);

        success.MethodName.ShouldBe("GetOrderById_Contract");
        success.ExpectedStatus.ShouldBe(200);
    }

    [TestMethod]
    public async Task DoesNotGenerateADeclaredErrorCaseFor400()
    {
        // No deterministic fixture-free trigger exists for 400 — sending the valid success
        // request would assert 400 against a 200 on every run.
        const string spec = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Orders", "version": "1.0" },
          "paths": {
            "/orders/{id}": {
              "get": {
                "operationId": "getOrderById",
                "tags": ["Orders"],
                "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
                "responses": {
                  "200": { "description": "ok", "content": { "application/json": {
                    "schema": { "$ref": "#/components/schemas/Order" } } } },
                  "400": { "description": "bad request" }
                }
              }
            }
          },
          "components": { "schemas": { "Order": { "type": "object" } } }
        }
        """;

        var plan = await BuildAsync(spec);
        var cases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.OperationKey == "getOrderById").ToList();

        cases.Count.ShouldBe(1);
        cases.ShouldNotContain(c => c.Role == CaseRole.DeclaredError);
    }

    [TestMethod]
    [DataRow("401")]
    [DataRow("403")]
    public async Task DoesNotGenerateADeclaredErrorCaseForAuthOwnedStatuses(string authStatus)
    {
        // The auth cases (Task 5) already own 401/403. A declared-error case here would send a
        // valid authenticated request and assert 401/403 against it — failing on every run.
        var spec = $$"""
        {
          "openapi": "3.0.3",
          "info": { "title": "Orders", "version": "1.0" },
          "paths": {
            "/orders/{id}": {
              "get": {
                "operationId": "getOrderById",
                "tags": ["Orders"],
                "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
                "responses": {
                  "200": { "description": "ok", "content": { "application/json": {
                    "schema": { "$ref": "#/components/schemas/Order" } } } },
                  "{{authStatus}}": { "description": "denied" }
                }
              }
            }
          },
          "components": { "schemas": { "Order": { "type": "object" } } }
        }
        """;

        var plan = await BuildAsync(spec);
        var cases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.OperationKey == "getOrderById").ToList();

        cases.Count.ShouldBe(1);
        cases.ShouldNotContain(c => c.Role == CaseRole.DeclaredError);
    }

    [TestMethod]
    public async Task SkipsAndNotesA404WithNoPathParameterRatherThanGuessingWhereToPutAnUnmatchableValue()
    {
        const string spec = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Orders", "version": "1.0" },
          "paths": {
            "/orders": {
              "get": {
                "operationId": "listOrders",
                "tags": ["Orders"],
                "responses": {
                  "200": { "description": "ok", "content": { "application/json": {
                    "schema": { "type": "array", "items": { "$ref": "#/components/schemas/Order" } } } } },
                  "404": { "description": "not found" }
                }
              }
            }
          },
          "components": { "schemas": { "Order": { "type": "object" } } }
        }
        """;

        var plan = await BuildAsync(spec);

        var cases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.OperationKey == "listOrders").ToList();
        cases.Count.ShouldBe(1, "the success case must still generate — only the declared-error case is affected");
        cases.ShouldNotContain(c => c.Role == CaseRole.DeclaredError);

        // §12: skips remove tests, notes do not. listOrders' success case is generated and runs,
        // so it must never appear in Skipped — GenerateCommand's "Skipped N operation(s)" line
        // and coverage-report.json's `skipped` array both read that list verbatim, and either
        // would misreport a live, passing operation as skipped.
        plan.Skipped.ShouldNotContain(s => s.OperationKey == "listOrders");

        plan.Notes.ShouldContain(n => n.OperationKey == "listOrders" && n.Reason.Contains("404"),
            "a silently dropped 404 case is indistinguishable from a bug");
    }

    [TestMethod]
    public async Task SkipsAndNotesA404WithARequiredQueryParameterRatherThanSendingAnIncompleteRequest()
    {
        // Decision 5's postscript: whether a missing *required* query parameter answers 400 or
        // 404 depends on binding and route configuration, so it is a measurement to take, not an
        // assumption to ship. A declared-error case that targets only the unmatchable path id and
        // omits the required "tenant" query parameter risks asserting 404 against what a
        // compliant, correctly-routed API actually answers with 400 — exactly the wall of wrong
        // failures decision 5 opens with. Treated the same as the no-path-parameter case: a note,
        // not a guess shipped as a test.
        const string spec = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Orders", "version": "1.0" },
          "paths": {
            "/orders/{id}": {
              "get": {
                "operationId": "getOrderById",
                "tags": ["Orders"],
                "parameters": [
                  { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } },
                  { "name": "tenant", "in": "query", "required": true, "schema": { "type": "string" } }
                ],
                "responses": {
                  "200": { "description": "ok", "content": { "application/json": {
                    "schema": { "$ref": "#/components/schemas/Order" } } } },
                  "404": { "description": "not found" }
                }
              }
            }
          },
          "components": { "schemas": { "Order": { "type": "object" } } }
        }
        """;

        var plan = await BuildAsync(spec);

        var cases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.OperationKey == "getOrderById").ToList();
        cases.Count.ShouldBe(1, "the success case must still generate — only the declared-error case is affected");
        cases.ShouldNotContain(c => c.Role == CaseRole.DeclaredError);

        plan.Skipped.ShouldNotContain(s => s.OperationKey == "getOrderById");
        plan.Notes.ShouldContain(n => n.OperationKey == "getOrderById" && n.Reason.Contains("tenant"),
            "a silently dropped 404 case is indistinguishable from a bug");
    }

    [TestMethod]
    public async Task SkipsAndNotesA404WithARequiredRequestBodyRatherThanSendingAnIncompleteRequest()
    {
        // The strictly stronger case of the required-query-parameter branch above: against an
        // ASP.NET Core [ApiController] with a non-nullable [FromBody] parameter, a bodyless
        // request (decision 6: send no body) is rejected by model binding with 400 before the
        // action's NotFound() path ever runs. Sending only the unmatchable path id and omitting
        // a required request body risks asserting 404 against what a compliant API answers with
        // 400 on every run — the exact wall of wrong failures decision 5 opens with. Treated the
        // same as the no-path-parameter and required-query-parameter cases: a note, not a guess
        // shipped as a test.
        const string spec = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Orders", "version": "1.0" },
          "paths": {
            "/orders/{id}": {
              "put": {
                "operationId": "updateOrder",
                "tags": ["Orders"],
                "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
                "requestBody": {
                  "required": true,
                  "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Order" } } }
                },
                "responses": {
                  "200": { "description": "ok", "content": { "application/json": {
                    "schema": { "$ref": "#/components/schemas/Order" } } } },
                  "404": { "description": "not found" }
                }
              }
            }
          },
          "components": { "schemas": { "Order": { "type": "object" } } }
        }
        """;

        var plan = await BuildAsync(spec);

        var cases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.OperationKey == "updateOrder").ToList();
        cases.Count.ShouldBe(1, "the success case must still generate — only the declared-error case is affected");
        cases.ShouldNotContain(c => c.Role == CaseRole.DeclaredError);

        plan.Skipped.ShouldNotContain(s => s.OperationKey == "updateOrder");
        plan.Notes.ShouldContain(n => n.OperationKey == "updateOrder" && n.Reason.Contains("request body"),
            "a silently dropped 404 case is indistinguishable from a bug");
    }

    [TestMethod]
    public async Task NeitherStatusDeclaredMeansOnlyTheSuccessCase()
    {
        var plan = await BuildAsync(Spec);
        var cases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.OperationKey == "getOrderById").ToList();

        cases.Count.ShouldBe(1);
        cases.Single().Role.ShouldBe(CaseRole.Success);
    }

    [TestMethod]
    public async Task SkipsTheDeclaredErrorCaseWhenTheSuccessCaseWasAlsoSkipped()
    {
        // An operation whose operationId cannot be a fixture filename is skipped entirely before
        // any case is built (SkipsAnOperationWhoseIdCannotBeAFixtureFileName above) — the
        // declared-error case must never appear on its own once the success case it would sit
        // beside was never generated, so the two can never disagree about the operation.
        const string spec = """
        {
          "openapi":"3.0.3","info":{"title":"T","version":"1"},
          "paths":{
            "/a/{id}":{"get":{"operationId":"Orders/Get",
              "parameters":[{"name":"id","in":"path","required":true,"schema":{"type":"string"}}],
              "responses":{
                "200":{"description":"ok"},
                "404":{"description":"not found"}
              }}}}
        }
        """;

        var plan = await BuildAsync(spec);

        plan.Skipped.ShouldContain(sk => sk.OperationKey == "Orders/Get" && sk.Reason.Contains("'/'"));
        plan.Classes.SelectMany(c => c.Cases).ShouldNotContain(c => c.OperationKey == "Orders/Get");

        // Exactly one skip reason for this operation, not two — the fixture-key skip alone
        // explains its absence; a second, 404-shaped skip reason would say the two disagreed.
        plan.Skipped.Count(sk => sk.OperationKey == "Orders/Get").ShouldBe(1);
    }

    // --- Auth cases (Task 5, decision 3 & 7): generated only for an operation declaring `security`. ---

    private const string SpecDeclaringSecurity = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": {
        "/orders/{id}": {
          "delete": {
            "operationId": "deleteOrder",
            "tags": ["Orders"],
            "security": [{ "bearerAuth": [] }],
            "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
            "responses": { "204": { "description": "no content" } }
          }
        }
      },
      "components": {
        "schemas": {},
        "securitySchemes": { "bearerAuth": { "type": "http", "scheme": "bearer" } }
      }
    }
    """;

    [TestMethod]
    public async Task EmitsANoTokenCaseFor401WhenTheOperationDeclaresSecurity()
    {
        var plan = await BuildAsync(SpecDeclaringSecurity);
        var cases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.OperationKey == "deleteOrder").ToList();

        cases.ShouldContain(c => c.ExpectedStatus == 401 && c.Role == CaseRole.Auth && c.Slot == IdentitySlot.None);
    }

    [TestMethod]
    public async Task EmitsAWrongScopeCaseFor403WhenTheOperationDeclaresSecurity()
    {
        var plan = await BuildAsync(SpecDeclaringSecurity);
        var cases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.OperationKey == "deleteOrder").ToList();

        cases.ShouldContain(c => c.ExpectedStatus == 403 && c.Role == CaseRole.Auth && c.Slot == IdentitySlot.Secondary);
    }

    [TestMethod]
    public async Task AnOperationDeclaringNoSecurityYieldsNeitherAuthCase()
    {
        // The plain Spec at the top of this file declares no `security` anywhere.
        var plan = await BuildAsync(Spec);
        var cases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.OperationKey == "getOrderById").ToList();

        cases.ShouldNotContain(c => c.Role == CaseRole.Auth);
    }

    [TestMethod]
    public async Task AuthCasesNeedNoFixture()
    {
        // Decision 6: an auth case must not be blocked by a sibling's unfilled fixture, and a
        // broken auth 403 must never be able to reach real data.
        var plan = await BuildAsync(SpecDeclaringSecurity);
        var authCases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.Role == CaseRole.Auth).ToList();

        authCases.ShouldNotBeEmpty();
        authCases.ShouldAllBe(c => !c.NeedsFixture);
    }

    [TestMethod]
    public async Task TheTwoAuthCasesGetDistinctStableMethodNames()
    {
        // Decision 4's extension: operation key + role alone is not unique for two auth cases on
        // the same operation (401 and 403 are both Role.Auth). Without expected status folded
        // into the dedupe identity, the second case's proposed name silently overwrites the
        // first's entry, and both cases end up assigned the very same MethodName — CS0111 the
        // moment the generated file is compiled.
        var plan = await BuildAsync(SpecDeclaringSecurity);
        var authCases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.Role == CaseRole.Auth).ToList();

        authCases.Count.ShouldBe(2);
        authCases.Select(c => c.MethodName).Distinct().Count().ShouldBe(2);
    }

    [TestMethod]
    public async Task AllFourRolesOnOneOperationStayDistinctWhenSecurityAndA404BothApply()
    {
        // The strongest form of the dedupe-identity fix: success, declared-404, auth-401 and
        // auth-403 all sharing one operation key must each keep their own MethodName. An
        // implementation that only fixed the two auth cases against each other, and not against
        // an operation that also has a DeclaredError case, could still collide here.
        const string spec = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Orders", "version": "1.0" },
          "paths": {
            "/orders/{id}": {
              "get": {
                "operationId": "getOrderById",
                "tags": ["Orders"],
                "security": [{ "bearerAuth": [] }],
                "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
                "responses": {
                  "200": { "description": "ok", "content": { "application/json": {
                    "schema": { "$ref": "#/components/schemas/Order" } } } },
                  "404": { "description": "not found" }
                }
              }
            }
          },
          "components": {
            "schemas": { "Order": { "type": "object" } },
            "securitySchemes": { "bearerAuth": { "type": "http", "scheme": "bearer" } }
          }
        }
        """;

        var plan = await BuildAsync(spec);
        var cases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.OperationKey == "getOrderById").ToList();

        cases.Count.ShouldBe(4);
        cases.Select(c => c.MethodName).Distinct().Count().ShouldBe(4);
    }

    [TestMethod]
    public async Task AuthCasesAreGeneratedEvenWithoutAPathParameter()
    {
        // Unlike the declared-error 404 case, an auth case has nowhere it must point an
        // unmatchable value at all — sending no token, or the wrong scope, needs no target
        // resource. Decision 5's path-parameter restriction is specific to 404; it must not leak
        // onto auth cases.
        const string spec = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Orders", "version": "1.0" },
          "paths": {
            "/orders": {
              "get": {
                "operationId": "listOrders",
                "tags": ["Orders"],
                "security": [{ "bearerAuth": [] }],
                "responses": { "200": { "description": "ok", "content": { "application/json": {
                  "schema": { "type": "array", "items": { "$ref": "#/components/schemas/Order" } } } } } }
              }
            }
          },
          "components": {
            "schemas": { "Order": { "type": "object" } },
            "securitySchemes": { "bearerAuth": { "type": "http", "scheme": "bearer" } }
          }
        }
        """;

        var plan = await BuildAsync(spec);
        var cases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.OperationKey == "listOrders").ToList();

        cases.ShouldContain(c => c.ExpectedStatus == 401 && c.Role == CaseRole.Auth);
        cases.ShouldContain(c => c.ExpectedStatus == 403 && c.Role == CaseRole.Auth);
    }

    [TestMethod]
    public async Task AuthCasesAreCategorizedContractLikeEveryOtherCase()
    {
        // Decision 8: §9's gate splits on TestCategory("Contract") vs ("Variation"). Auth cases
        // are deterministic, fixture-free and gate-safe, so they belong in Contract like
        // declared-error cases, never a separate category.
        var plan = await BuildAsync(SpecDeclaringSecurity);
        var authCases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.Role == CaseRole.Auth).ToList();

        authCases.ShouldAllBe(c => c.Category == "Contract");
    }

    private const string SpecWithDocumentLevelSecurityOnly = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "security": [{ "bearerAuth": [] }],
      "paths": {
        "/orders/{id}": {
          "get": {
            "operationId": "getOrderById",
            "tags": ["Orders"],
            "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
            "responses": { "200": { "description": "ok", "content": { "application/json": {
              "schema": { "$ref": "#/components/schemas/Order" } } } } }
          }
        }
      },
      "components": {
        "schemas": { "Order": { "type": "object" } },
        "securitySchemes": { "bearerAuth": { "type": "http", "scheme": "bearer" } }
      }
    }
    """;

    [TestMethod]
    public async Task AnOperationInheritingDocumentLevelSecurityGetsACoverageNoteInsteadOfSilentlyNoAuthCases()
    {
        // Review finding on Task 5: an operation that omits `security` entirely inherits the
        // document-level block per the OpenAPI spec, but v1-c's operation.Security-only check
        // (decision comment above) treats that the same as an operation with no auth at all —
        // and, unlike every other withheld case in this method (the three notes.Add calls above
        // the auth branch), it did so with no CoverageNote, so the gap was invisible in
        // coverage-report.json. This spec has no per-operation `security`, so the plain
        // AnOperationDeclaringNoSecurityYieldsNeitherAuthCase test above does not cover it.
        var plan = await BuildAsync(SpecWithDocumentLevelSecurityOnly);
        var cases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.OperationKey == "getOrderById").ToList();

        cases.ShouldNotContain(c => c.Role == CaseRole.Auth);
        plan.Notes.ShouldContain(n => n.OperationKey == "getOrderById" &&
            n.Reason.Contains("security", StringComparison.OrdinalIgnoreCase));
    }

    // The test above with a single line changed (Task 10 item 8(b)): the operation now declares
    // `"security": []` explicitly, instead of omitting the key. Only the absent-key arm — the
    // test above — was covered; this is the `{ Count: 0 }` arm operation.Security is null
    // deliberately excludes, per the decision comment on TestPlanBuilder's auth branch. Both
    // arms depend on Microsoft.OpenApi materializing an empty `"security": []` array as a
    // non-null, zero-count list rather than normalizing it to null — if it normalized to null
    // instead, this operation would be indistinguishable from the absent-key case above and
    // would wrongly get a note, which is exactly what this test would catch.
    private const string SpecWithAnExplicitEmptySecurityOverride = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "security": [{ "bearerAuth": [] }],
      "paths": {
        "/orders/{id}": {
          "get": {
            "operationId": "getOrderById",
            "tags": ["Orders"],
            "security": [],
            "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
            "responses": { "200": { "description": "ok", "content": { "application/json": {
              "schema": { "$ref": "#/components/schemas/Order" } } } } }
          }
        }
      },
      "components": {
        "schemas": { "Order": { "type": "object" } },
        "securitySchemes": { "bearerAuth": { "type": "http", "scheme": "bearer" } }
      }
    }
    """;

    [TestMethod]
    public async Task AnOperationExplicitlyOverridingSecurityToEmptyGetsNoNoteAndNoAuthCases()
    {
        // Decision comment on the auth branch: an explicit empty array is the spec's own way of
        // overriding the document default to "no auth" for this operation, which is not a gap to
        // report — unlike the absent-key case above, which silently inherited without being able
        // to say so.
        var plan = await BuildAsync(SpecWithAnExplicitEmptySecurityOverride);
        var cases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.OperationKey == "getOrderById").ToList();

        cases.ShouldNotContain(c => c.Role == CaseRole.Auth);
        plan.Notes.ShouldNotContain(n => n.OperationKey == "getOrderById");
    }

    private const string SpecDeclaringSecurityWithARequiredBody = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": {
        "/orders/{id}": {
          "put": {
            "operationId": "updateOrder",
            "tags": ["Orders"],
            "security": [{ "bearerAuth": [] }],
            "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
            "requestBody": {
              "required": true,
              "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Order" } } }
            },
            "responses": { "200": { "description": "ok", "content": { "application/json": {
              "schema": { "$ref": "#/components/schemas/Order" } } } } }
          }
        }
      },
      "components": {
        "schemas": { "Order": { "type": "object" } },
        "securitySchemes": { "bearerAuth": { "type": "http", "scheme": "bearer" } }
      }
    }
    """;

    [TestMethod]
    public async Task AuthCasesOnAnOperationWithARequiredBodyStillSendNoBody()
    {
        // Review finding on Task 5: the renderer-level test this replaced (AnAuthCaseSendsNoBody
        // in TemplateRendererTests) rendered a hand-built plan whose HasRequestBody defaulted to
        // false and could never fail — the template's body block is gated purely on
        // `tc.has_body`, with no role in the condition. This is the real guard: it fails if the
        // auth branch in TestPlanBuilder ever starts copying the success case's
        // FixtureComposer.HasJsonBodyToCompose(operation), which is the only way an auth case
        // could end up with a body at all.
        var plan = await BuildAsync(SpecDeclaringSecurityWithARequiredBody);
        var cases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.OperationKey == "updateOrder").ToList();

        var success = cases.Single(c => c.Role == CaseRole.Success);
        var authCases = cases.Where(c => c.Role == CaseRole.Auth).ToList();

        success.HasRequestBody.ShouldBeTrue();
        authCases.Count.ShouldBe(2);
        authCases.ShouldAllBe(c => !c.HasRequestBody);
    }

    // --- RequiredScopes (carrying declared OAuth scopes into the plan for a later runtime guard). ---

    private const string SpecDeclaringSecurityWithAScope = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": {
        "/orders/{id}": {
          "delete": {
            "operationId": "deleteOrder",
            "tags": ["Orders"],
            "security": [{ "oauth2Auth": ["orders.write"] }],
            "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
            "responses": { "204": { "description": "no content" } }
          }
        }
      },
      "components": {
        "schemas": {},
        "securitySchemes": {
          "oauth2Auth": {
            "type": "oauth2",
            "flows": { "clientCredentials": {
              "tokenUrl": "https://example.com/token",
              "scopes": { "orders.write": "Write orders" } } }
          }
        }
      }
    }
    """;

    [TestMethod]
    public async Task TheForbiddenCaseCarriesTheOperationsDeclaredScope()
    {
        var plan = await BuildAsync(SpecDeclaringSecurityWithAScope);
        var forbidden = plan.Classes.SelectMany(c => c.Cases)
            .Single(c => c.OperationKey == "deleteOrder" && c.ExpectedStatus == 403);

        forbidden.RequiredScopes.ShouldBe(["orders.write"]);
    }

    // Two separate security *requirements* (distinct entries in the `security` array), each
    // naming a scope on its own scheme — not two keys inside one requirement object. An
    // implementation that reads only operation.Security[0] and ignores the rest would see just
    // "orders.write" here and this assertion would fail on the missing "orders.read".
    private const string SpecDeclaringSecurityAcrossTwoRequirements = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": {
        "/orders/{id}": {
          "delete": {
            "operationId": "deleteOrder",
            "tags": ["Orders"],
            "security": [
              { "oauth2Write": ["orders.write"] },
              { "oauth2Read": ["orders.read"] }
            ],
            "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
            "responses": { "204": { "description": "no content" } }
          }
        }
      },
      "components": {
        "schemas": {},
        "securitySchemes": {
          "oauth2Write": {
            "type": "oauth2",
            "flows": { "clientCredentials": {
              "tokenUrl": "https://example.com/token",
              "scopes": { "orders.write": "Write orders" } } }
          },
          "oauth2Read": {
            "type": "oauth2",
            "flows": { "clientCredentials": {
              "tokenUrl": "https://example.com/token",
              "scopes": { "orders.read": "Read orders" } } }
          }
        }
      }
    }
    """;

    [TestMethod]
    public async Task TheForbiddenCaseUnionsScopesAcrossEverySecurityRequirement()
    {
        var plan = await BuildAsync(SpecDeclaringSecurityAcrossTwoRequirements);
        var forbidden = plan.Classes.SelectMany(c => c.Cases)
            .Single(c => c.OperationKey == "deleteOrder" && c.ExpectedStatus == 403);

        forbidden.RequiredScopes.Count.ShouldBe(2);
        forbidden.RequiredScopes.ShouldContain("orders.write");
        forbidden.RequiredScopes.ShouldContain("orders.read");
    }

    // Same shape as the union spec above, but both requirements name the *same* scope on
    // different schemes — the union must not carry the duplicate through.
    private const string SpecDeclaringSecurityWithADuplicateScopeAcrossSchemes = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": {
        "/orders/{id}": {
          "delete": {
            "operationId": "deleteOrder",
            "tags": ["Orders"],
            "security": [
              { "oauth2A": ["orders.write"] },
              { "oauth2B": ["orders.write"] }
            ],
            "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
            "responses": { "204": { "description": "no content" } }
          }
        }
      },
      "components": {
        "schemas": {},
        "securitySchemes": {
          "oauth2A": {
            "type": "oauth2",
            "flows": { "clientCredentials": {
              "tokenUrl": "https://example.com/token",
              "scopes": { "orders.write": "Write orders" } } }
          },
          "oauth2B": {
            "type": "oauth2",
            "flows": { "clientCredentials": {
              "tokenUrl": "https://example.com/token",
              "scopes": { "orders.write": "Write orders" } } }
          }
        }
      }
    }
    """;

    [TestMethod]
    public async Task TheForbiddenCasesUnionedScopesAreDistinct()
    {
        var plan = await BuildAsync(SpecDeclaringSecurityWithADuplicateScopeAcrossSchemes);
        var forbidden = plan.Classes.SelectMany(c => c.Cases)
            .Single(c => c.OperationKey == "deleteOrder" && c.ExpectedStatus == 403);

        forbidden.RequiredScopes.ShouldBe(["orders.write"]);
    }

    // A single security *requirement* naming two schemes (two keys in the same JSON object),
    // where one of those schemes itself names two scopes — the two axes the requirement-spanning
    // and duplicate-scope fixtures above never exercise together, since both of those put each
    // scheme in its own separate requirement. An implementation that only flattens across
    // requirements (SelectMany(requirement => requirement.Values)) but takes just the first
    // scheme's scopes within a requirement, or that flattens schemes but takes just the first
    // scope within a scheme (SelectMany(scopes => scopes)), would still pass every other fixture
    // in this file but would miss scopes here.
    private const string SpecDeclaringSecurityWithTwoSchemesInOneRequirement = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": {
        "/orders/{id}": {
          "delete": {
            "operationId": "deleteOrder",
            "tags": ["Orders"],
            "security": [
              { "oauth2A": ["orders.read", "orders.write"], "oauth2B": ["orders.admin"] }
            ],
            "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
            "responses": { "204": { "description": "no content" } }
          }
        }
      },
      "components": {
        "schemas": {},
        "securitySchemes": {
          "oauth2A": {
            "type": "oauth2",
            "flows": { "clientCredentials": {
              "tokenUrl": "https://example.com/token",
              "scopes": { "orders.read": "Read orders", "orders.write": "Write orders" } } }
          },
          "oauth2B": {
            "type": "oauth2",
            "flows": { "clientCredentials": {
              "tokenUrl": "https://example.com/token",
              "scopes": { "orders.admin": "Administer orders" } } }
          }
        }
      }
    }
    """;

    [TestMethod]
    public async Task TheForbiddenCaseUnionsScopesAcrossEverySchemeInASingleSecurityRequirement()
    {
        var plan = await BuildAsync(SpecDeclaringSecurityWithTwoSchemesInOneRequirement);
        var forbidden = plan.Classes.SelectMany(c => c.Cases)
            .Single(c => c.OperationKey == "deleteOrder" && c.ExpectedStatus == 403);

        // Order-sensitive on purpose: the scope union is sorted (StringComparer.Ordinal) as a
        // determinism guard, because OpenApiSecurityRequirement is a Dictionary whose enumeration
        // order is unspecified, and Task 4 renders this union into a golden file compared
        // byte-exact. A test that accepted any order would let that ordering regress silently.
        forbidden.RequiredScopes.ShouldBe(["orders.admin", "orders.read", "orders.write"]);
    }

    // Same shape as the requirement-spanning union spec above, but the two requirements name
    // scopes that differ only in case. RFC 6749 scope tokens are case-sensitive, so these are two
    // distinct scopes, not a duplicate — an implementation that unioned with an ignore-case
    // comparer would collapse them to one and silently drop a requirement the 403 guard should be
    // comparing against.
    private const string SpecDeclaringSecurityWithScopesDifferingOnlyByCase = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": {
        "/orders/{id}": {
          "delete": {
            "operationId": "deleteOrder",
            "tags": ["Orders"],
            "security": [
              { "oauth2Lower": ["orders.read"] },
              { "oauth2Upper": ["ORDERS.READ"] }
            ],
            "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
            "responses": { "204": { "description": "no content" } }
          }
        }
      },
      "components": {
        "schemas": {},
        "securitySchemes": {
          "oauth2Lower": {
            "type": "oauth2",
            "flows": { "clientCredentials": {
              "tokenUrl": "https://example.com/token",
              "scopes": { "orders.read": "Read orders" } } }
          },
          "oauth2Upper": {
            "type": "oauth2",
            "flows": { "clientCredentials": {
              "tokenUrl": "https://example.com/token",
              "scopes": { "ORDERS.READ": "Read orders (shouty)" } } }
          }
        }
      }
    }
    """;

    [TestMethod]
    public async Task TheForbiddenCasesUnionedScopesArePreservedCaseSensitively()
    {
        var plan = await BuildAsync(SpecDeclaringSecurityWithScopesDifferingOnlyByCase);
        var forbidden = plan.Classes.SelectMany(c => c.Cases)
            .Single(c => c.OperationKey == "deleteOrder" && c.ExpectedStatus == 403);

        forbidden.RequiredScopes.Count.ShouldBe(2);
        forbidden.RequiredScopes.ShouldContain("orders.read");
        forbidden.RequiredScopes.ShouldContain("ORDERS.READ");
    }

    [TestMethod]
    public async Task ASecuredButScopeFreeOperationStillGetsAForbiddenCaseWithNoRequiredScopes()
    {
        // SpecDeclaringSecurity declares `bearerAuth: []` — secured, but names no scope. This
        // must still emit the auth cases (unchanged from the existing coverage above); it must
        // not silently withhold them just because there is nothing to require.
        var plan = await BuildAsync(SpecDeclaringSecurity);
        var forbidden = plan.Classes.SelectMany(c => c.Cases)
            .Single(c => c.OperationKey == "deleteOrder" && c.ExpectedStatus == 403);

        forbidden.RequiredScopes.ShouldNotBeNull();
        forbidden.RequiredScopes.ShouldBeEmpty();
    }

    [TestMethod]
    public async Task TheUnauthorizedCaseNeverCarriesRequiredScopes()
    {
        // The 401 case sends no token at all, so a scope requirement is never meaningful there —
        // even though the operation itself declares a scope for the 403 case to carry.
        var plan = await BuildAsync(SpecDeclaringSecurityWithAScope);
        var unauthorized = plan.Classes.SelectMany(c => c.Cases)
            .Single(c => c.OperationKey == "deleteOrder" && c.ExpectedStatus == 401);

        unauthorized.RequiredScopes.ShouldNotBeNull();
        unauthorized.RequiredScopes.ShouldBeEmpty();
    }

    [TestMethod]
    public async Task EveryNonAuthCaseCarriesNoRequiredScopes()
    {
        // Success and declared-error cases are never auth cases and must never carry a scope
        // requirement — RequiredScopes must be an empty collection, never a null reference, for
        // every one of them.
        var plan = await BuildAsync(SpecDeclaringSecurityWithAScope);
        var nonAuthCases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.Role != CaseRole.Auth).ToList();

        nonAuthCases.ShouldNotBeEmpty();
        nonAuthCases.ShouldAllBe(c => c.RequiredScopes != null);
        nonAuthCases.ShouldAllBe(c => c.RequiredScopes.Count == 0);
    }

    [TestMethod]
    public void WithExpressionSettingRequiredScopesToNullNormalizesToEmpty()
    {
        // `with` expressions drive the init accessor directly (compiler-generated copy
        // constructor + init setters) rather than re-running the primary constructor's field
        // initializer, so the never-null guarantee has to be enforced in the init accessor
        // itself — a coalesce that only lived in the field initializer would guarantee non-null
        // for a freshly-constructed plan but not for one produced by `with`.
        var plan = new TestCasePlan("A_Contract", "d", "a", true, "GET", "/a", [], 200, "Order", "Contract");

        var mutated = plan with { RequiredScopes = null! };

        mutated.RequiredScopes.ShouldNotBeNull();
        mutated.RequiredScopes.ShouldBeEmpty();
    }

    // --- ClientCallExpression wiring (typed-client-invocation plan, [convention-and-config]). ---
    // TestPlanBuilder.Build's client parameter is optional and defaults to null, so every test
    // above this region already covers "absent client leaves everything unchanged" implicitly —
    // none of them pass one. These pass a ClientPlanningConfig explicitly.

    private static readonly IReadOnlyDictionary<string, string> NoOverrides =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly ClientPlanningConfig KiotaClient =
        new(ClientKind.Kiota, "Orders.ApiClient.OrdersApiClient", NoOverrides);

    private const string SpecWithAQueryParameter = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": {
        "/orders": {
          "get": {
            "operationId": "listOrders",
            "tags": ["Orders"],
            "parameters": [{ "name": "status", "in": "query", "required": false, "schema": { "type": "string" } }],
            "responses": { "200": { "description": "ok", "content": { "application/json": {
              "schema": { "type": "array", "items": { "$ref": "#/components/schemas/Order" } } } } } }
          }
        }
      },
      "components": { "schemas": { "Order": { "type": "object" } } }
    }
    """;

    private const string SpecWithARequestBody = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": {
        "/orders": {
          "post": {
            "operationId": "createOrder",
            "tags": ["Orders"],
            "requestBody": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Order" } } } },
            "responses": { "201": { "description": "created", "content": { "application/json": {
              "schema": { "$ref": "#/components/schemas/Order" } } } } }
          }
        }
      },
      "components": { "schemas": { "Order": { "type": "object" } } }
    }
    """;

    // [nswag-needs-operationid]: measured against a real nswag 14.7.1 client generated from an
    // operationId shaped exactly like this one — "Orders_GetById" produces a separate
    // "OrdersClient.GetByIdAsync" partial class, never a method on the single configured
    // client.typeName. No query parameter and no request body, so the only thing withholding
    // convention here is the underscore itself.
    private const string SpecWithUnderscoreOperationId = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": {
        "/orders/{id}": {
          "get": {
            "operationId": "Orders_GetById",
            "tags": ["Orders"],
            "parameters": [{ "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }],
            "responses": { "200": { "description": "ok", "content": { "application/json": {
              "schema": { "$ref": "#/components/schemas/Order" } } } } }
          }
        }
      },
      "components": { "schemas": { "Order": { "type": "object" } } }
    }
    """;

    [TestMethod]
    public async Task ClientCallExpressionIsNullWithNoClientConfigured()
    {
        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(Spec)).Document);
        var success = plan.Classes.SelectMany(c => c.Cases).Single(c => c.OperationKey == "getOrderById");

        success.ClientCallExpression.ShouldBeNull();
        plan.Notes.ShouldBeEmpty();
    }

    [TestMethod]
    public async Task ClientCallExpressionIsSetForAQualifyingSuccessCase()
    {
        // getOrderById: GET /orders/{id} — Success, no query parameters, no request body.
        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(Spec)).Document, KiotaClient);
        var success = plan.Classes.SelectMany(c => c.Cases).Single(c => c.OperationKey == "getOrderById");

        success.ClientCallExpression.ShouldBe("Orders[{id}].GetAsync");
        plan.Notes.ShouldBeEmpty();
    }

    [TestMethod]
    public async Task ClientCallExpressionIsNullForADeclaredErrorCaseRegardlessOfConfig()
    {
        // [success-only]: TryPlanDeclaredNotFound never even attempts a resolution — it must stay
        // null with a client configured, exactly as it already is with none.
        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(SpecDeclaring404)).Document, KiotaClient);
        var notFound = plan.Classes.SelectMany(c => c.Cases).Single(c => c.Role == CaseRole.DeclaredError);

        notFound.ClientCallExpression.ShouldBeNull();
    }

    [TestMethod]
    public async Task ClientCallExpressionIsNullForAuthCasesRegardlessOfConfig()
    {
        // [success-only]: PlanAuthCases never even attempts a resolution either.
        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(SpecDeclaringSecurity)).Document, KiotaClient);
        var authCases = plan.Classes.SelectMany(c => c.Cases).Where(c => c.Role == CaseRole.Auth).ToList();

        authCases.ShouldNotBeEmpty();
        authCases.ShouldAllBe(c => c.ClientCallExpression == null);
    }

    [TestMethod]
    public async Task ClientCallExpressionIsNullWithANoteForAQueryParameterSuccessCase()
    {
        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(SpecWithAQueryParameter)).Document, KiotaClient);
        var success = plan.Classes.SelectMany(c => c.Cases).Single(c => c.OperationKey == "listOrders");

        success.ClientCallExpression.ShouldBeNull();
        plan.Notes.ShouldContain(n => n.OperationKey == "listOrders" &&
            n.Reason.Contains("query parameters") && n.Reason.Contains("client-map.json", StringComparison.Ordinal),
            "a withheld convention must point the adopter at the override map, not report the operation as unsupported");
    }

    [TestMethod]
    public async Task ClientCallExpressionIsNullWithANoteForARequestBodySuccessCase()
    {
        // Measured finding: Kiota's PostAsync takes a typed model object, not a JSON string.
        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(SpecWithARequestBody)).Document, KiotaClient);
        var success = plan.Classes.SelectMany(c => c.Cases).Single(c => c.OperationKey == "createOrder");

        success.ClientCallExpression.ShouldBeNull();
        plan.Notes.ShouldContain(n => n.OperationKey == "createOrder" &&
            n.Reason.Contains("request body") && n.Reason.Contains("client-map.json", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ClientCallExpressionIsNullWithANoteForRefit()
    {
        // [refit-override-only]: permanent, unconditional — unlike NSwag below, nothing about
        // this operation's shape (it has an operationId, no query parameters, no request body)
        // could ever change the verdict for Refit.
        var client = new ClientPlanningConfig(ClientKind.Refit, "Orders.ApiClient.IOrdersApi", NoOverrides);

        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(Spec)).Document, client);
        var success = plan.Classes.SelectMany(c => c.Cases).Single(c => c.OperationKey == "getOrderById");

        success.ClientCallExpression.ShouldBeNull();
        plan.Notes.ShouldContain(n => n.OperationKey == "getOrderById" && n.Reason.Contains("Refit", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ClientCallExpressionAppliesTheNSwagConventionWhenTheOperationIdQualifies()
    {
        // [nswag-needs-operationid]: getOrderById has an operationId with no '_', no query
        // parameters and no request body, so NSwag's convention now applies — measured against a
        // real nswag 14.7.1 client (ClientCallPlanner's own doc comment carries the evidence).
        var client = new ClientPlanningConfig(ClientKind.NSwag, "Orders.ApiClient.OrdersApiClient", NoOverrides);

        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(Spec)).Document, client);
        var success = plan.Classes.SelectMany(c => c.Cases).Single(c => c.OperationKey == "getOrderById");

        success.ClientCallExpression.ShouldBe("GetOrderByIdAsync({id}, cancellationToken: TestContext.CancellationToken)");
        plan.Notes.ShouldNotContain(n => n.OperationKey == "getOrderById");
    }

    [TestMethod]
    public async Task ClientCallExpressionIsNullWithANoteForNSwagWhenNoOperationIdIsDeclared()
    {
        // "/health" declares no operationId, no query parameters and no request body — otherwise
        // exactly the shape that would qualify. OperationKey.Resolve synthesizes "get_health" for
        // it, which TestPlanBuilder must not mistake for a real, present operationId.
        var client = new ClientPlanningConfig(ClientKind.NSwag, "Orders.ApiClient.OrdersApiClient", NoOverrides);

        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(Spec)).Document, client);
        var success = plan.Classes.SelectMany(c => c.Cases).Single(c => c.OperationKey == "get_health");

        success.ClientCallExpression.ShouldBeNull();
        plan.Notes.ShouldContain(n => n.OperationKey == "get_health" && n.Reason.Contains("operationId", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ClientCallExpressionIsNullWithANoteForNSwagWhenTheOperationIdContainsAnUnderscore()
    {
        var client = new ClientPlanningConfig(ClientKind.NSwag, "Orders.ApiClient.OrdersClient", NoOverrides);

        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(SpecWithUnderscoreOperationId)).Document, client);
        var success = plan.Classes.SelectMany(c => c.Cases).Single(c => c.OperationKey == "Orders_GetById");

        success.ClientCallExpression.ShouldBeNull();
        plan.Notes.ShouldContain(n => n.OperationKey == "Orders_GetById" && n.Reason.Contains("'_'", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task AnOverrideBypassesTheQueryParameterGate()
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["listOrders"] = "Orders.GetAsync(rc => rc.QueryParameters.Status = status)"
        };
        var client = new ClientPlanningConfig(ClientKind.Kiota, "Orders.ApiClient.OrdersApiClient", overrides);

        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(SpecWithAQueryParameter)).Document, client);
        var success = plan.Classes.SelectMany(c => c.Cases).Single(c => c.OperationKey == "listOrders");

        success.ClientCallExpression.ShouldBe("Orders.GetAsync(rc => rc.QueryParameters.Status = status)");
        plan.Notes.ShouldNotContain(n => n.OperationKey == "listOrders");
    }

    [TestMethod]
    public async Task AnOverrideBypassesTheRequestBodyGate()
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["createOrder"] = "Orders.PostAsync(new CreateOrderRequest())"
        };
        var client = new ClientPlanningConfig(ClientKind.Kiota, "Orders.ApiClient.OrdersApiClient", overrides);

        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(SpecWithARequestBody)).Document, client);
        var success = plan.Classes.SelectMany(c => c.Cases).Single(c => c.OperationKey == "createOrder");

        success.ClientCallExpression.ShouldBe("Orders.PostAsync(new CreateOrderRequest())");
        plan.Notes.ShouldNotContain(n => n.OperationKey == "createOrder");
    }

    [TestMethod]
    public async Task AnOverrideBypassesTheNonKiotaKindGate()
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["getOrderById"] = "OrdersGETAsync(Guid.Parse(FixtureParameter(\"getOrderById\", \"id\")))"
        };
        var client = new ClientPlanningConfig(ClientKind.NSwag, "Orders.ApiClient.OrdersClient", overrides);

        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(Spec)).Document, client);
        var success = plan.Classes.SelectMany(c => c.Cases).Single(c => c.OperationKey == "getOrderById");

        success.ClientCallExpression.ShouldBe("OrdersGETAsync(Guid.Parse(FixtureParameter(\"getOrderById\", \"id\")))");
        plan.Notes.ShouldNotContain(n => n.OperationKey == "getOrderById");
    }

    [TestMethod]
    public async Task AStaleOverrideKeyYieldsANote()
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["noSuchOperation"] = "Foo.GetAsync()"
        };
        var client = new ClientPlanningConfig(ClientKind.Kiota, "Orders.ApiClient.OrdersApiClient", overrides);

        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(Spec)).Document, client);

        plan.Notes.ShouldContain(n => n.OperationKey == "noSuchOperation" && n.Reason.Contains("stale"));
    }

    [TestMethod]
    public async Task AnOverrideThatMatchesARealOperationIsNotReportedAsStale()
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["getOrderById"] = "Orders[{id}].GetAsync"
        };
        var client = new ClientPlanningConfig(ClientKind.Kiota, "Orders.ApiClient.OrdersApiClient", overrides);

        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(Spec)).Document, client);

        plan.Notes.ShouldNotContain(n => n.Reason.Contains("stale"));
    }

    // ---- Reproduced crash: a HEAD/OPTIONS/TRACE operation with a `client` section configured --

    private const string SpecWithAHeadOperation = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": {
        "/api/ping": {
          "head": {
            "operationId": "ping",
            "tags": ["Status"],
            "responses": { "200": { "description": "ok" } }
          }
        }
      }
    }
    """;

    /// <summary>
    /// The reproduced defect this task exists to fix, at the layer that actually crashed:
    /// <c>ClientCallPlanner.Resolve</c> used to call <c>BuildKiotaConvention</c> unconditionally
    /// once the query-parameter and request-body gates both passed — neither of which a bodiless
    /// HEAD operation ever trips — and that method throws for any verb outside
    /// GET/POST/PUT/PATCH/DELETE. Confirmed by direct reproduction before this fix: `generate`
    /// against a spec with <c>head: { responses: { "200": … } }</c> on <c>/api/ping</c> generated
    /// cleanly with no <c>client</c> section configured, then crashed with
    /// <c>intest: unexpected failure: ArgumentException: 'HEAD' has no known Kiota verb-method
    /// convention</c>, exit 2, the moment one was added — this test is the
    /// <see cref="TestPlanBuilder"/>-level half of that reproduction (<c>ClientCallPlannerTests</c>
    /// pins the same fix one layer down, directly against <c>Resolve</c>). Must not throw, and must
    /// still generate the HEAD operation's Success case over raw HTTP with a
    /// <see cref="CoverageNote"/> pointing at <c>client-map.json</c> — the same "note, not a crash"
    /// treatment every other withheld-convention reason on this plan already gets.
    /// </summary>
    [TestMethod]
    public async Task AHeadOperationWithAClientConfiguredGeneratesInsteadOfCrashing()
    {
        var plan = TestPlanBuilder.Build((await SpecLoader.LoadFromTextAsync(SpecWithAHeadOperation)).Document, KiotaClient);
        var success = plan.Classes.SelectMany(c => c.Cases).Single(c => c.OperationKey == "ping");

        success.ClientCallExpression.ShouldBeNull();
        plan.Notes.ShouldContain(n => n.OperationKey == "ping" &&
            n.Reason.Contains("HEAD", StringComparison.Ordinal) &&
            n.Reason.Contains("client-map.json", StringComparison.Ordinal));
    }
}

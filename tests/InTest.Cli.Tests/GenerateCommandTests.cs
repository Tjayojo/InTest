using InTest.Cli;
using InTest.Cli.Commands;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class GenerateCommandTests
{
    private const string Spec = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": { "/orders/{id}": { "get": { "operationId": "getOrderById", "tags": ["Orders"],
        "responses": { "200": { "description": "ok", "content": { "application/json": {
          "schema": { "$ref": "#/components/schemas/Order" } } } } } } } },
      "components": { "schemas": { "Order": { "type": "object" } } }
    }
    """;

    // listOrders declares 404 but has no path parameter to target with an unmatchable value
    // (decision 5's postscript), so TestPlanBuilder withholds its declared-error case as a
    // CoverageNote rather than a guess — exactly one noted operation.
    private const string SpecWithANotedOperation = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": { "/orders": { "get": { "operationId": "listOrders", "tags": ["Orders"],
        "responses": {
          "200": { "description": "ok", "content": { "application/json": {
            "schema": { "type": "array", "items": { "$ref": "#/components/schemas/Order" } } } } },
          "404": { "description": "not found" }
        } } } },
      "components": { "schemas": { "Order": { "type": "object" } } }
    }
    """;

    private string _root = null!;

    [TestInitialize]
    public void CreateProject()
    {
        _root = Path.Combine(Path.GetTempPath(), "intest-gen-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "orders.json"), Spec);
        File.WriteAllText(Path.Combine(_root, "intest.json"), """
        { "schemaVersion": 1, "spec": { "source": "orders.json" },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);
    }

    [TestCleanup]
    public void RemoveProject()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private async Task<int> RunAsync() => await GenerateCommand.RunAsync(_root, CancellationToken.None);

    /// <summary>Runs `generate` with stderr captured, so a test can assert what the adopter is told.</summary>
    private async Task<(int ExitCode, string Error)> RunCapturingErrorAsync(string? projectRoot = null)
    {
        var originalError = Console.Error;
        var capturedError = new StringWriter();
        Console.SetError(capturedError);
        try
        {
            return (await GenerateCommand.RunAsync(projectRoot ?? _root, CancellationToken.None),
                    capturedError.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    private void WriteConfig(string json) => File.WriteAllText(Path.Combine(_root, "intest.json"), json);

    /// <summary>
    /// The contract for every malformed-config case: exit 2 (§5 — "Nothing was written"), nothing
    /// under Generated/, and an explanation rather than a stack-trace-shaped line. The
    /// "unexpected failure" assertion is the load-bearing one — before ConfigLoader every case
    /// below already exited 2 through the catch-all, so an exit-code assertion alone would have
    /// passed against the defect.
    /// </summary>
    private async Task<string> ExpectExplainedConfigErrorAsync(string json)
    {
        WriteConfig(json);

        var (exitCode, error) = await RunCapturingErrorAsync();

        exitCode.ShouldBe(2);
        Directory.Exists(Path.Combine(_root, "Generated")).ShouldBeFalse();
        error.ShouldNotContain("unexpected failure");
        return error;
    }

    [TestMethod]
    public async Task WritesGeneratedClassesAndTheSchemaBundle()
    {
        (await RunAsync()).ShouldBe(0);
        File.Exists(Path.Combine(_root, "Generated", "OrdersTests.g.cs")).ShouldBeTrue();
        File.Exists(Path.Combine(_root, "Generated", "spec-schemas.json")).ShouldBeTrue();
        File.Exists(Path.Combine(_root, "coverage-report.json")).ShouldBeTrue();
    }

    [TestMethod]
    public async Task NeverWritesUnderFixtures()
    {
        await RunAsync();
        Directory.Exists(Path.Combine(_root, "fixtures")).ShouldBeFalse();
    }

    [TestMethod]
    public async Task IsDeterministic()
    {
        await RunAsync();
        var first = File.ReadAllText(Path.Combine(_root, "Generated", "OrdersTests.g.cs"));
        await RunAsync();
        File.ReadAllText(Path.Combine(_root, "Generated", "OrdersTests.g.cs")).ShouldBe(first);
    }

    [TestMethod]
    public async Task ReturnsToolErrorWhenTheSpecIsMissing()
    {
        File.Delete(Path.Combine(_root, "orders.json"));
        (await RunAsync()).ShouldBe(2);
    }

    [TestMethod]
    public async Task ReturnsToolErrorForAnInvalidRootNamespaceAndWritesNothing()
    {
        File.WriteAllText(Path.Combine(_root, "intest.json"), """
        { "schemaVersion": 1, "spec": { "source": "orders.json" },
          "project": { "rootNamespace": "My Project", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        var originalError = Console.Error;
        var capturedError = new StringWriter();
        Console.SetError(capturedError);
        int exitCode;
        try
        {
            exitCode = await RunAsync();
        }
        finally
        {
            Console.SetError(originalError);
        }

        exitCode.ShouldBe(2);
        Directory.Exists(Path.Combine(_root, "Generated")).ShouldBeFalse();
        capturedError.ToString().ShouldContain("rootNamespace", Case.Sensitive);
    }

    [TestMethod]
    public async Task ReturnsToolErrorForAnInvalidTestBaseClassAndWritesNothing()
    {
        File.WriteAllText(Path.Combine(_root, "intest.json"), """
        { "schemaVersion": 1, "spec": { "source": "orders.json" },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.class" } }
        """);

        var originalError = Console.Error;
        var capturedError = new StringWriter();
        Console.SetError(capturedError);
        int exitCode;
        try
        {
            exitCode = await RunAsync();
        }
        finally
        {
            Console.SetError(originalError);
        }

        exitCode.ShouldBe(2);
        Directory.Exists(Path.Combine(_root, "Generated")).ShouldBeFalse();
        capturedError.ToString().ShouldContain("testBaseClass", Case.Sensitive);
    }

    [TestMethod]
    public async Task ReturnsToolErrorWhenRootNamespaceIsJsonNull()
    {
        File.WriteAllText(Path.Combine(_root, "intest.json"), """
        { "schemaVersion": 1, "spec": { "source": "orders.json" },
          "project": { "rootNamespace": null, "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        (await RunAsync()).ShouldBe(2);
        Directory.Exists(Path.Combine(_root, "Generated")).ShouldBeFalse();
    }

    [TestMethod]
    public async Task PrintsHowManyOperationsWereNoted()
    {
        // Task 10 item 8(a): found by mutation — deleting the whole `if (plan.Notes.Count > 0)`
        // block in GenerateCommand passed the full Cli suite. coverage-report.json's own
        // `notes.withheld` array is already guarded by other tests, but this console line is the
        // only thing a developer sees without opening that artefact, and CoverageNote's entire
        // point is that a withheld case must not be a silent omission.
        File.WriteAllText(Path.Combine(_root, "orders.json"), SpecWithANotedOperation);

        var original = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            (await RunAsync()).ShouldBe(0);
        }
        finally
        {
            Console.SetOut(original);
        }

        captured.ToString().ShouldContain("Noted 1 operation(s)");
    }

    // ---- intest.json is adopter-editable, so every read of it is an untrusted input ----------
    // These assert the wiring: that `generate` routes ConfigLoadException to stderr and exit 2
    // without writing. ConfigLoaderTests covers the message text of each individual setting.

    /// <summary>
    /// The brief's second named defect. A missing `project` key reached
    /// `config.RootElement.GetProperty("project")` and surfaced through the catch-all as
    /// `intest: unexpected failure: KeyNotFoundException: …` — a stack-trace-shaped message for
    /// an ordinary hand-edit.
    /// </summary>
    [TestMethod]
    public async Task ExplainsAMissingProjectSectionInsteadOfReportingAnUnexpectedFailure()
    {
        var error = await ExpectExplainedConfigErrorAsync(
            """{ "schemaVersion": 1, "spec": { "source": "orders.json" } }""");

        error.ShouldContain("project", Case.Sensitive);
        error.ShouldNotContain("KeyNotFoundException");
    }

    /// <summary>
    /// The brief's first named defect: `spec.source` read with a bare `.GetString()!`. A number
    /// threw InvalidOperationException from inside System.Text.Json, naming neither the file nor
    /// the setting.
    /// </summary>
    [TestMethod]
    public async Task ExplainsASpecSourceThatIsNotAStringInsteadOfReportingAnUnexpectedFailure()
    {
        var error = await ExpectExplainedConfigErrorAsync("""
        { "schemaVersion": 1, "spec": { "source": 42 },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        error.ShouldContain("spec.source", Case.Sensitive);
        error.ShouldNotContain("InvalidOperationException");
    }

    /// <summary>
    /// The other half of that defect. `.GetString()!` returned null rather than throwing, so the
    /// failure surfaced further away still — `ArgumentNullException: Value cannot be null.
    /// (Parameter 'path2')` from Path.Combine, which names a parameter of a framework method
    /// rather than anything the adopter wrote.
    /// </summary>
    [TestMethod]
    public async Task ExplainsASpecSourceThatIsJsonNullInsteadOfReportingAnUnexpectedFailure()
    {
        var error = await ExpectExplainedConfigErrorAsync("""
        { "schemaVersion": 1, "spec": { "source": null },
          "project": { "rootNamespace": "Orders.ApiTests", "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        error.ShouldContain("spec.source", Case.Sensitive);
        error.ShouldNotContain("ArgumentNullException");
    }

    [TestMethod]
    public async Task ExplainsAnIntestJsonThatIsNotValidJsonInsteadOfReportingAnUnexpectedFailure()
    {
        var error = await ExpectExplainedConfigErrorAsync(
            """{ "schemaVersion": 1, "spec": { "source": "orders.json" } """);

        error.ShouldContain("intest.json", Case.Sensitive);
        error.ShouldContain("not valid JSON");
        error.ShouldNotContain("JsonReaderException");
    }

    /// <summary>
    /// The hole directly beneath 0f42984: TryValidateDottedName never saw a non-string, because
    /// `.GetString()` threw first. The injection guard and the type guard have to hold together.
    /// </summary>
    [TestMethod]
    public async Task ExplainsARootNamespaceThatIsNotAStringInsteadOfReportingAnUnexpectedFailure()
    {
        var error = await ExpectExplainedConfigErrorAsync("""
        { "schemaVersion": 1, "spec": { "source": "orders.json" },
          "project": { "rootNamespace": 7, "testBaseClass": "Orders.ApiTests.OrdersTestBase" } }
        """);

        error.ShouldContain("project.rootNamespace", Case.Sensitive);
    }

    /// <summary>
    /// `--project` is an argument the adopter typed, not a crash. It reached ConfigLoader.Load's
    /// ArgumentException.ThrowIfNullOrWhiteSpace and came back as
    /// "intest: unexpected failure: ArgumentException: ... (Parameter 'projectRoot')" — the right
    /// exit code attached to the wrong sentence, naming a C# parameter the adopter never wrote
    /// instead of the flag they did. `init` had the same rule stated a third way; there is one
    /// now, in CommandArguments, and this is generate's call site of it.
    /// </summary>
    [TestMethod]
    public async Task RefusesABlankProjectRatherThanReportingItAsACrash()
    {
        var (exitCode, error) = await RunCapturingErrorAsync(projectRoot: "");

        exitCode.ShouldBe(ExitCode.ToolError);
        error.ShouldNotContain("unexpected failure",
            customMessage: "an argument the adopter got wrong is refused, not reported as a crash");
        error.ShouldStartWith("--project", Case.Sensitive,
            customMessage: "a refusal names the flag the adopter typed, not the parameter it bound to");
        error.ShouldContain("is empty");
        error.ShouldContain("for example");
    }

}

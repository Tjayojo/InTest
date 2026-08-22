using InTest.Cli.Commands;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class GenerateDriftTests
{
    private const string Spec = """
    {
      "openapi":"3.0.3","info":{"title":"T","version":"1"},
      "paths":{"/api/products":{"post":{
        "operationId":"createProduct",
        "requestBody":{"content":{"application/json":{"schema":{"type":"object",
          "required":["sku"],"properties":{"sku":{"type":"string"}}}}}},
        "responses":{"201":{"description":"ok"}}}}}
    }
    """;

    private string _root = null!;

    [TestInitialize]
    public void CreateProject()
    {
        _root = Path.Combine(Path.GetTempPath(), "intest-gendrift-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "spec.json"), Spec);
        InitCommand.Run(_root, "T.ApiTests", "spec.json").ShouldBe(0);
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
    public async Task ReportsAMissingFixtureAsDriftAndWritesNothing()
    {
        var exitCode = await GenerateCommand.RunAsync(_root, CancellationToken.None);

        exitCode.ShouldBe(1);
        Directory.Exists(Path.Combine(_root, "fixtures")).ShouldBeFalse(
            "generate is read-only under fixtures/ — that is what keeps --check a pure comparison");
    }

    [TestMethod]
    public async Task DriftMessageNamesTheOperationAndPointsToRepair()
    {
        var report = new StringWriter();

        await GenerateCommand.RunAsync(_root, CancellationToken.None, report);

        report.ToString().ShouldContain("createProduct", Case.Sensitive,
            customMessage: "the drift report is useless if it doesn't say which operation is unresolved");
        report.ToString().ShouldContain("Run 'intest fixtures repair'", Case.Sensitive);
    }

    [TestMethod]
    public async Task NeverCreatesAnythingUnderFixturesEvenWhenReportingDrift()
    {
        var report = new StringWriter();

        await GenerateCommand.RunAsync(_root, CancellationToken.None, report);

        Directory.Exists(Path.Combine(_root, "fixtures")).ShouldBeFalse(
            "generate only reports drift — repair is the only writer under fixtures/");
    }

    [TestMethod]
    public async Task ReturnsOkOnceEveryFixtureIsResolved()
    {
        (await FixturesRepairCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);

        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(0);
    }
}

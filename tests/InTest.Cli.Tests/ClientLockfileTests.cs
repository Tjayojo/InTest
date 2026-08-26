using InTest.Cli.Spec;
using Shouldly;

namespace InTest.Cli.Tests;

/// <summary>
/// <see cref="ClientLockfile"/> follows the same parse/refusal idiom as
/// <see cref="Clients.ClientCallMap"/> and <c>Fixtures.FixtureDocument</c> (all three read a
/// committed, hand-edited or generator-written file where a malformed field is a realistic typo,
/// not adversarial input), so these tests follow <c>ClientCallMapTests</c>'s shape: a malformed
/// field throws <see cref="ClientLockfileException"/> naming the offending field, never a raw
/// framework exception.
/// <para>
/// The real-shaped fixture below is copied verbatim from a real <c>kiota generate</c> run (kiota
/// 1.34.1) against <c>samples/Orders.Api/Orders.Api.json</c> — see <c>ClientLockfile</c>'s own doc
/// comment for the exact command and why that measurement, not a guess, is what these tests are
/// built against.
/// </para>
/// </summary>
[TestClass]
public class ClientLockfileTests
{
    private string _root = null!;

    [TestInitialize]
    public void CreateDirectory()
    {
        _root = Path.Combine(Path.GetTempPath(), "intest-lockfile-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void RemoveDirectory()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string WriteLockfile(string json)
    {
        var path = Path.Combine(_root, "kiota-lock.json");
        File.WriteAllText(path, json);
        return path;
    }

    // ---- the real-shaped fixture, both descriptionLocation forms ------------------------------

    /// <summary>
    /// Every field present, values matching the real measurement — a local absolute path with
    /// forward slashes for descriptionLocation, exactly as kiota 1.34.1 wrote it.
    /// </summary>
    private const string RealKiotaLockfileLocalPath = """
        {
          "descriptionHash": "EF763FFCF3F41D04D8109657EC4DB02E68539996122549984A7E7280B7BB3B2DE8DE944ED89FFED7F7441D5D6DFB456A7AE626DDA724986243D5BEBBC354A662",
          "descriptionLocation": "D:/TestGen/samples/Orders.Api/Orders.Api.json",
          "lockFileVersion": "1.0.0",
          "kiotaVersion": "1.34.1",
          "clientClassName": "OrdersApiClient",
          "typeAccessModifier": "Public",
          "clientNamespaceName": "Orders.ApiClient",
          "language": "CSharp",
          "usesBackingStore": false
        }
        """;

    [TestMethod]
    public void RecoversTheSpecSourceAndClientIdentityFromALocalPathDescriptionLocation()
    {
        var path = WriteLockfile(RealKiotaLockfileLocalPath);

        var recovered = ClientLockfile.Recover(path);

        recovered.SpecSource.ShouldBe("D:/TestGen/samples/Orders.Api/Orders.Api.json");
        recovered.ClientKind.ShouldBe("kiota");
        // Dot-joined clientNamespaceName + clientClassName — exactly what intest.json's
        // client.typeName wants, and exactly the getting-started guide's own worked example.
        recovered.ClientTypeName.ShouldBe("Orders.ApiClient.OrdersApiClient");
    }

    [TestMethod]
    public void RecoversAUrlDescriptionLocationUnchanged()
    {
        // kiota's own documentation states descriptionLocation is equally a URL when the source
        // passed to `kiota generate --openapi` was one — this type treats it as a bare string
        // either way, doing no URL-specific parsing of its own.
        var json = RealKiotaLockfileLocalPath.Replace(
            "\"descriptionLocation\": \"D:/TestGen/samples/Orders.Api/Orders.Api.json\"",
            "\"descriptionLocation\": \"https://orders-staging.example.com/swagger/v1/swagger.json\"");
        var path = WriteLockfile(json);

        var recovered = ClientLockfile.Recover(path);

        recovered.SpecSource.ShouldBe("https://orders-staging.example.com/swagger/v1/swagger.json");
        recovered.ClientTypeName.ShouldBe("Orders.ApiClient.OrdersApiClient");
    }

    [TestMethod]
    public void RecoversTheSpecSourceAloneWhenNoClientIdentityFieldsArePresent()
    {
        var path = WriteLockfile("""{ "descriptionLocation": "orders.json" }""");

        var recovered = ClientLockfile.Recover(path);

        recovered.SpecSource.ShouldBe("orders.json");
        recovered.ClientKind.ShouldBeNull();
        recovered.ClientTypeName.ShouldBeNull();
    }

    // ---- missing file -------------------------------------------------------------------------

    [TestMethod]
    public void RefusesAMissingFile()
    {
        var path = Path.Combine(_root, "does-not-exist.json");

        var exception = Should.Throw<ClientLockfileException>(() => ClientLockfile.Recover(path));

        exception.Message.ShouldContain("--client-lockfile", Case.Sensitive);
        exception.Message.ShouldContain(path, Case.Sensitive);
    }

    // ---- missing / renamed required field ------------------------------------------------------

    [TestMethod]
    public void RefusesALockfileWithNoDescriptionLocation()
    {
        var path = WriteLockfile("""{ "kiotaVersion": "1.34.1" }""");

        var exception = Should.Throw<ClientLockfileException>(() => ClientLockfile.Recover(path));

        exception.Message.ShouldContain("descriptionLocation", Case.Sensitive);
    }

    [TestMethod]
    public void RefusesALockfileWhereDescriptionLocationWasRenamed()
    {
        // Simulates a future kiota renaming the field: this must fail loudly, naming the missing
        // field, rather than silently recovering an empty or wrong spec source.
        var path = WriteLockfile("""{ "description_location": "orders.json" }""");

        var exception = Should.Throw<ClientLockfileException>(() => ClientLockfile.Recover(path));

        exception.Message.ShouldContain("descriptionLocation", Case.Sensitive);
    }

    [TestMethod]
    public void RefusesABlankDescriptionLocation()
    {
        var path = WriteLockfile("""{ "descriptionLocation": "   " }""");

        var exception = Should.Throw<ClientLockfileException>(() => ClientLockfile.Recover(path));

        exception.Message.ShouldContain("descriptionLocation", Case.Sensitive);
    }

    [TestMethod]
    public void RefusesANonStringDescriptionLocation()
    {
        var path = WriteLockfile("""{ "descriptionLocation": 42 }""");

        var exception = Should.Throw<ClientLockfileException>(() => ClientLockfile.Recover(path));

        exception.Message.ShouldContain("descriptionLocation", Case.Sensitive);
    }

    // ---- partial client identity: refused, not silently dropped -------------------------------

    [TestMethod]
    public void RefusesAClientClassNameWithNoNamespaceName()
    {
        var path = WriteLockfile("""
            { "descriptionLocation": "orders.json", "clientClassName": "OrdersApiClient" }
            """);

        var exception = Should.Throw<ClientLockfileException>(() => ClientLockfile.Recover(path));

        exception.Message.ShouldContain("clientNamespaceName", Case.Sensitive);
    }

    [TestMethod]
    public void RefusesAClientNamespaceNameWithNoClassName()
    {
        var path = WriteLockfile("""
            { "descriptionLocation": "orders.json", "clientNamespaceName": "Orders.ApiClient" }
            """);

        var exception = Should.Throw<ClientLockfileException>(() => ClientLockfile.Recover(path));

        exception.Message.ShouldContain("clientClassName", Case.Sensitive);
    }

    // ---- malformed JSON -------------------------------------------------------------------------

    [TestMethod]
    public void RefusesMalformedJsonRatherThanThrowingJsonException()
    {
        var path = WriteLockfile("""{ "descriptionLocation": """);

        var exception = Should.Throw<ClientLockfileException>(() => ClientLockfile.Recover(path));

        exception.Message.ShouldContain(path, Case.Sensitive);
        exception.Message.ShouldContain("not valid JSON");
    }

    [TestMethod]
    public void RefusesARootThatIsNotAnObject()
    {
        var path = WriteLockfile("""["kiota-lock.json is not shaped like this"]""");

        var exception = Should.Throw<ClientLockfileException>(() => ClientLockfile.Recover(path));

        exception.Message.ShouldContain("object");
    }
}

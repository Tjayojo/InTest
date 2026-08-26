using Shouldly;

namespace InTest.Runtime.Tests;

/// <summary>
/// Finding D: exercises <see cref="InTestRun.ParseSpecPaths"/> — the parse itself, split out of
/// <see cref="InTestRun.ReadSpecPaths"/> precisely so these cases can be driven with literal JSON
/// text rather than a real file under <c>AppContext.BaseDirectory</c> — against every shape
/// <c>clientCaptureEnabled</c> can take in a generated project's <c>spec-paths.json</c>: absent,
/// <c>true</c>, <c>false</c>, and malformed (present but not a JSON boolean).
/// <para>
/// Absent and <c>false</c> must both resolve to <c>false</c> without throwing — a project with no
/// <c>client</c> section, and an older <c>spec-paths.json</c> predating this flag entirely, must
/// keep working exactly as before. A malformed value is different: <c>spec-paths.json</c> is
/// generator-owned (<c>Generated/</c> is never touched by humans per CLAUDE.md's ownership table),
/// so a present-and-non-boolean value can only mean the file was hand-edited or corrupted, and
/// silently reading it as <c>false</c> would send an adopter chasing the wrong cause — the
/// [client-rides-the-api-pipeline] "construct your client over
/// IHttpClientFactory.CreateClient(InTestClients.Api)" message, when that registration was never
/// the problem. So a malformed value is a hard, immediate error instead.
/// </para>
/// </summary>
[TestClass]
public class ReadSpecPathsTests
{
    private const string FakePath = "C:/fake/spec-paths.json";

    [TestMethod]
    public void AbsentClientCaptureEnabledReadsAsFalse()
    {
        var (_, clientCaptureEnabled) = InTestRun.ParseSpecPaths(FakePath, """{"operationPathPrefix":"/api"}""");

        clientCaptureEnabled.ShouldBeFalse();
    }

    [TestMethod]
    public void NoPropertiesAtAllReadsAsFalse()
    {
        var (operationPathPrefix, clientCaptureEnabled) = InTestRun.ParseSpecPaths(FakePath, "{}");

        operationPathPrefix.ShouldBeNull();
        clientCaptureEnabled.ShouldBeFalse();
    }

    [TestMethod]
    public void LiteralTrueReadsAsTrue()
    {
        var (_, clientCaptureEnabled) = InTestRun.ParseSpecPaths(FakePath, """{"clientCaptureEnabled":true}""");

        clientCaptureEnabled.ShouldBeTrue();
    }

    [TestMethod]
    public void LiteralFalseReadsAsFalse()
    {
        var (_, clientCaptureEnabled) = InTestRun.ParseSpecPaths(FakePath, """{"clientCaptureEnabled":false}""");

        clientCaptureEnabled.ShouldBeFalse();
    }

    /// <summary>
    /// A string "true" is the most plausible hand-edit mistake (JSON's <c>true</c> literal is
    /// unquoted; a human editing the file by hand is exactly the kind of adopter who might quote
    /// it) — must throw naming the file path, the offending raw value, and the fact the file is
    /// generator-owned, not read as false.
    /// </summary>
    [TestMethod]
    public void StringTrueIsAHardErrorNamingFileAndValue()
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            InTestRun.ParseSpecPaths(FakePath, """{"clientCaptureEnabled":"true"}"""));

        ex.Message.ShouldContain(FakePath);
        ex.Message.ShouldContain("\"true\"");
        ex.Message.ShouldContain("generator-owned");
    }

    [TestMethod]
    public void ANumberIsAHardError()
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            InTestRun.ParseSpecPaths(FakePath, """{"clientCaptureEnabled":1}"""));

        ex.Message.ShouldContain(FakePath);
        ex.Message.ShouldContain("1");
    }

    [TestMethod]
    public void NullIsAHardError()
    {
        Should.Throw<InvalidOperationException>(() =>
            InTestRun.ParseSpecPaths(FakePath, """{"clientCaptureEnabled":null}"""));
    }

    [TestMethod]
    public void AnObjectIsAHardError()
    {
        Should.Throw<InvalidOperationException>(() =>
            InTestRun.ParseSpecPaths(FakePath, """{"clientCaptureEnabled":{}}"""));
    }

    /// <summary>
    /// <see cref="InTestRun.OperationPathPrefix"/> parsing is untouched by this task and must keep
    /// working alongside a valid <c>clientCaptureEnabled</c> — proves the two reads from the same
    /// document did not become entangled by the refactor that split <c>ParseSpecPaths</c> out of
    /// <c>ReadSpecPaths</c>.
    /// </summary>
    [TestMethod]
    public void OperationPathPrefixStillParsesAlongsideClientCaptureEnabled()
    {
        var (operationPathPrefix, clientCaptureEnabled) = InTestRun.ParseSpecPaths(
            FakePath, """{"operationPathPrefix":"/api","clientCaptureEnabled":true}""");

        operationPathPrefix.ShouldBe("/api");
        clientCaptureEnabled.ShouldBeTrue();
    }
}

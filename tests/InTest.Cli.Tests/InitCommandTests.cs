using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using InTest.Cli;
using InTest.Cli.Commands;
using InTest.Cli.Configuration;
using Shouldly;

namespace InTest.Cli.Tests;

[TestClass]
public class InitCommandTests
{
    private string _root = null!;

    [TestInitialize]
    public void CreateDirectory()
    {
        _root = Path.Combine(Path.GetTempPath(), "intest-init-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void RemoveDirectory() => ForceDeleteDirectory(_root);

    /// <summary>
    /// Recursive delete that clears the read-only attribute first, rather than a plain
    /// <see cref="Directory.Delete(string, bool)"/>. This is not defensive gold-plating: v1-e's
    /// review originally asserted that <see cref="RemoveDirectory"/>'s plain delete already
    /// proved a force-unlock helper unnecessary, on the theory that _root — which
    /// GitattributesSurvivesAnAutocrlfInputCheckout leaves containing a real <c>.git</c>
    /// directory — already deletes cleanly today. Confirmed by direct experiment that this is
    /// false on this platform: `git commit` leaves every loose object under
    /// <c>.git/objects/**</c> mode <c>0444</c> (read-only, no write bit) on Windows, and a plain
    /// recursive delete throws <see cref="UnauthorizedAccessException"/> the moment it reaches
    /// one — reproduced by running <see cref="RemoveDirectory"/> unmodified against a scratch
    /// repo, which failed the same way. Both _root and the temporary clone
    /// GitattributesSurvivesAnAutocrlfInputCheckout makes go through a real `git init`/`clone` +
    /// commit, so both need this, not just the clone — hence one shared helper backing
    /// <see cref="RemoveDirectory"/> itself rather than a second, clone-only copy.
    /// </summary>
    private static void ForceDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }
        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(path, recursive: true);
    }

    [TestMethod]
    public void ScaffoldsEveryTeamOwnedFile()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "../Orders/bin/Debug/net10.0/orders.json").ShouldBe(0);

        foreach (var file in new[]
        {
            "intest.json", "Orders.ApiTests.csproj", ".editorconfig", ".gitattributes", "AssemblyInfo.cs",
            "TestStartup.cs", "OrdersTestBase.cs", "appsettings.json", "Orders.ApiTests.runsettings",
            ".config/dotnet-tools.json"
        })
        {
            File.Exists(Path.Combine(_root, file)).ShouldBeTrue($"{file} was not scaffolded.");
        }
    }

    // The spec used by GitattributesSurvivesAnAutocrlfInputCheckout: `getOrderById`'s path
    // parameter needs no fixture to generate successfully (mirrors GenerateCommandTests.Spec —
    // duplicated here rather than shared, matching how each test file in this project already
    // keeps its own local Spec constant), which keeps that test to one `generate` call with no
    // `fixtures repair` step in between.
    private const string SpecNeedingNoFixture = """
    {
      "openapi": "3.0.3",
      "info": { "title": "Orders", "version": "1.0" },
      "paths": { "/orders/{id}": { "get": { "operationId": "getOrderById", "tags": ["Orders"],
        "responses": { "200": { "description": "ok", "content": { "application/json": {
          "schema": { "$ref": "#/components/schemas/Order" } } } } } } } },
      "components": { "schemas": { "Order": { "type": "object" } } }
    }
    """;

    /// <summary>
    /// Proves the scaffolded .gitattributes actually does its job, rather than merely existing.
    /// "The file on disk contains CRLF" would pass even with .gitattributes missing entirely, or
    /// with its eol=crlf pins deleted, on a checkout whose own git config already defaults to
    /// CRLF — Git-for-Windows' core.autocrlf=true is exactly such a config: under
    /// [crlf-everywhere] its own checkout-time expansion already produces CRLF regardless of any
    /// pin, so forcing it (as an earlier version of this test did) proves nothing about whether
    /// the pin itself works. core.autocrlf=false is not hostile either, and for a more subtle
    /// reason confirmed by direct experiment (git plumbing: `git show HEAD:&lt;path&gt;` on a
    /// scratch repo) rather than assumed: this scaffold's .gitattributes deliberately carries no
    /// blanket `* text=auto` line (see GitattributesContent's own doc comment for why), so with no
    /// attribute at all matching a path and core.autocrlf=false, git performs *no* conversion in
    /// either direction — the object database stores the working-tree bytes verbatim and checkout
    /// returns them verbatim. Deleting a path's `eol=crlf` pin under core.autocrlf=false removes
    /// both the add-time LF-normalization and the checkout-time CRLF-re-expansion the pin was
    /// doing, and the two cancel out: the round trip stays byte-identical with or without the pin,
    /// so autocrlf=false cannot distinguish "pinned" from "unpinned" here (confirmed by direct
    /// experiment: deleting the fixtures/**/*.json line and re-running this test under
    /// autocrlf=false left it passing). core.autocrlf=input is the setting that actually
    /// reproduces the hazard [crlf-everywhere] exists to close: it normalizes CRLF to LF on add
    /// the same as core.autocrlf=true does, auto-detecting text files even with no attribute
    /// present, but — unlike core.autocrlf=true — does not re-expand LF back to CRLF on checkout.
    /// Confirmed by the same direct experiment: with no pin and core.autocrlf=input on both ends,
    /// `git show HEAD:&lt;path&gt;` after commit already reads LF, and the file checked out into a
    /// fresh clone reads LF too — a genuine, silent flattening of the scaffold's CRLF content, the
    /// exact defect an adopter with core.autocrlf=input (the common non-Windows default) would hit
    /// without this .gitattributes. With the pin restored, the same experiment's committed blob is
    /// still LF (eol=crlf implies text=true, which normalizes storage regardless of autocrlf) but
    /// checkout re-expands it to CRLF, matching the original bytes. This reproduces the v1-e
    /// line-endings task's manual measurement as an automated round trip, direction reversed for
    /// [crlf-everywhere]: commit a real `init` + `generate` scaffold with core.autocrlf=input set
    /// on the source, then materialize a second working copy with the same setting forced on the
    /// destination — and diff the bytes. Every one of InTest's own generated artefacts
    /// (Generated/**, coverage-report.json, fixtures/**/*.json — a base fixture and a profile
    /// overlay alike) must come back byte-identical; without .gitattributes pinning them to
    /// eol=crlf, this exact checkout would flatten them to LF, the same class of gap
    /// [crlf-everywhere] exists to close, direction reversed from what the v1-e manual experiment
    /// originally showed.
    /// </summary>
    [TestMethod]
    public async Task GitattributesSurvivesAnAutocrlfInputCheckout()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json").ShouldBe(ExitCode.Ok);
        File.WriteAllText(Path.Combine(_root, "orders.json"), SpecNeedingNoFixture);
        (await GenerateCommand.RunAsync(_root, CancellationToken.None)).ShouldBe(ExitCode.Ok);

        // `generate` alone never writes fixtures/ (only `fixtures repair` does), so write a base
        // fixture and a profile overlay by hand — pure CRLF, matching what FixtureDocument's
        // writer now produces. The overlay is the important half: fixtures/{profile}/*.json
        // (FixtureStore.Load's overlay directory) is purely adopter-authored and committed —
        // `fixtures repair` never writes it — which the v1-e review calls out as the strongest
        // case for pinning, not the weakest, so this round trip must exercise
        // fixtures/**/*.json's recursive match, not just the non-recursive fixtures/*.json case.
        Directory.CreateDirectory(Path.Combine(_root, "fixtures", "qa"));
        File.WriteAllText(Path.Combine(_root, "fixtures", "sample.json"), "{\r\n  \"sample\": true\r\n}\r\n");
        File.WriteAllText(Path.Combine(_root, "fixtures", "qa", "sample.json"), "{\r\n  \"sample\": false\r\n}\r\n");

        var tracked = new[]
        {
            Path.Combine("Generated", "OrdersTests.g.cs"),
            Path.Combine("Generated", "spec-schemas.json"),
            Path.Combine("Generated", "spec-paths.json"),
            "coverage-report.json",
            Path.Combine("fixtures", "sample.json"),
            Path.Combine("fixtures", "qa", "sample.json"),
        };
        var beforeCheckout = tracked.ToDictionary(f => f, f => File.ReadAllBytes(Path.Combine(_root, f)));

        RunGit(_root, "init -q");
        RunGit(_root, "config core.autocrlf input");
        RunGit(_root, "config user.email test@example.com");
        RunGit(_root, "config user.name Test");
        RunGit(_root, "add -A");
        RunGit(_root, "commit -q -m snapshot");

        var clone = Path.Combine(Path.GetTempPath(), "intest-clone-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            // --no-checkout, then set core.autocrlf, then checkout: a plain `clone` applies the
            // destination's config too late for a `-c` override to be trustworthy across git
            // versions (confirmed by direct experiment while measuring Step 1 — a `-c
            // core.autocrlf=input clone` converted the files correctly but did not persist the
            // setting into the clone's own .git/config, which this test does not want to depend
            // on). Splitting the two steps makes the setting unambiguously in effect for the
            // checkout that follows.
            RunGit(Path.GetTempPath(), $"clone -q --no-checkout \"{_root}\" \"{clone}\"");
            RunGit(clone, "config core.autocrlf input");
            RunGit(clone, "checkout -q HEAD -- .");

            foreach (var file in tracked)
            {
                AssertByteIdenticalAcrossCheckout(file, beforeCheckout[file], File.ReadAllBytes(Path.Combine(clone, file)));
            }
        }
        finally
        {
            // Same helper RemoveDirectory's [TestCleanup] uses for _root, not a second
            // clone-only copy or a plain Directory.Delete — see ForceDeleteDirectory's own doc
            // comment for why a plain recursive delete does not work against a directory that
            // contains a real `.git` (v1-e review, minor 7).
            ForceDeleteDirectory(clone);
        }
    }

    /// <summary>
    /// Fails with a message that names both things that can produce this exact symptom — bytes
    /// differing across a core.autocrlf=input checkout — rather than asserting only one. The naive
    /// message ("`.gitattributes` did not pin it to CRLF") is true when this test's own
    /// .gitattributes has a gap; it is false, and misleading, when the writer that produced
    /// <paramref name="before"/> already emitted LF before the file was ever committed (a
    /// <c>JsonSerializerOptions.NewLine</c> or template <c>Normalize</c> regression back toward
    /// the pre-[crlf-everywhere] direction) — in which case the checkout changed nothing and
    /// .gitattributes is not the bug. The two are distinguished by whether
    /// <paramref name="before"/> already contains a CRLF sequence: if it does not, the checkout
    /// did not remove one. A raw <c>byte[]</c> comparison (Shouldly's default <c>ShouldBe</c>)
    /// renders on the order of 10 KB of decimal byte codes for a file this size before reaching
    /// any custom message; hex is at least legible, and the CRLF counts alone usually say which
    /// half of the diagnosis applies without reading the dump at all.
    /// </summary>
    private static void AssertByteIdenticalAcrossCheckout(string file, byte[] before, byte[] after)
    {
        if (before.AsSpan().SequenceEqual(after))
        {
            return;
        }

        var crlfBefore = CountCrlf(before);
        var crlfAfter = CountCrlf(after);
        var likelyCause = crlfBefore == 0
            ? "the writer that produced this file already emitted LF before it was committed " +
              "(JsonSerializerOptions.NewLine, or a template's Normalize step, was not honored) " +
              "— .gitattributes is not at fault here"
            : ".gitattributes did not pin this file to CRLF, so the checkout stripped its CRLF " +
              "line endings down to LF";

        const int previewBytes = 256;
        Assert.Fail(
            $"{file} changed bytes across a core.autocrlf=input checkout: {before.Length} bytes " +
            $"before, {after.Length} after; {crlfBefore} CRLF sequence(s) before the checkout, " +
            $"{crlfAfter} after. Likely cause: {likelyCause}. First {previewBytes} bytes, hex — " +
            $"before: {Convert.ToHexString(before, 0, Math.Min(before.Length, previewBytes))}; " +
            $"after: {Convert.ToHexString(after, 0, Math.Min(after.Length, previewBytes))}.");
    }

    private static int CountCrlf(byte[] bytes)
    {
        var count = 0;
        for (var i = 0; i < bytes.Length - 1; i++)
        {
            if (bytes[i] == (byte)'\r' && bytes[i + 1] == (byte)'\n')
            {
                count++;
            }
        }
        return count;
    }

    // 60s: generous for `init`/`clone`/`config`/`add`/`commit`/`checkout` against a scratch repo
    // with a handful of files, but still short enough that a wedged git process fails this test
    // loudly instead of wedging the whole CI job the way an unbounded WaitForExit() previously
    // could (v1-e review, Important 6).
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// One empty file, shared by every <see cref="RunGit"/> call in this class and pointed at by
    /// <c>GIT_CONFIG_GLOBAL</c>/<c>GIT_CONFIG_SYSTEM</c> below, so a scratch repo never inherits
    /// the machine's own global or system git config. Without this a developer or CI box with
    /// <c>commit.gpgsign=true</c> makes `git commit` block on a passphrase prompt that never
    /// arrives under a non-interactive test host, and <c>core.hooksPath</c> or
    /// <c>init.templateDir</c> can inject third-party hooks into a repository this test creates
    /// and deletes within seconds. Lazy and written once — the file is always empty, so per-call
    /// I/O would be pure overhead.
    /// </summary>
    private static readonly Lazy<string> EmptyGitConfig = new(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), "intest-tests-empty-gitconfig-" + Guid.NewGuid().ToString("N")[..8]);
        File.WriteAllText(path, string.Empty);
        return path;
    });

    private static void RunGit(string workingDirectory, string arguments)
    {
        var startInfo = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = EmptyGitConfig.Value;
        startInfo.Environment["GIT_CONFIG_SYSTEM"] = EmptyGitConfig.Value;
        // This test never talks to a remote, so any credential/terminal prompt here can only
        // mean something is misconfigured — refuse it outright rather than block on it.
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        Process process;
        try
        {
            process = Process.Start(startInfo)!;
        }
        catch (Win32Exception ex)
        {
            // Process.Start's default failure here is a raw Win32Exception with no mention of
            // what was being started or why — indistinguishable, out of context, from any other
            // failure to launch a process. Name the actual cause: this class shells out to a
            // real `git` binary and has no fallback if one is not on PATH (v1-e review, minor 11).
            Assert.Fail(
                $"Could not start 'git {arguments}' in \"{workingDirectory}\": {ex.Message}. Is " +
                "git installed and on PATH? GitattributesSurvivesAnAutocrlfInputCheckout shells " +
                "out to a real git binary and cannot run without one.");
            throw; // Unreachable — Assert.Fail always throws — but keeps `process` definitely assigned.
        }

        using (process)
        {
            // Read both streams concurrently via the async event-based API, not
            // ReadToEnd() on stdout followed by ReadToEnd() on stderr: a child process's stderr
            // pipe is a fixed-size OS buffer (about 4 KB), and `git add -A` on a scaffolded
            // project under autocrlf=input alone emits over a thousand bytes of "CRLF will be
            // replaced by LF" warnings — measured at 1450 bytes (direction and figure both
            // reconfirmed for [crlf-everywhere]'s core.autocrlf=input, up from the
            // [lf-everywhere] predecessor's autocrlf=true "LF will be replaced by CRLF" wording),
            // 35% of the buffer. Reading stdout to completion first blocks forever the moment
            // stderr fills, because nothing is draining it and the child is blocked trying to
            // write to it: a deadlock, not a slow test (v1-e review, Important 6).
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit((int)GitTimeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* already exited */ }
                Assert.Fail(
                    $"git {arguments} in \"{workingDirectory}\" did not exit within " +
                    $"{GitTimeout.TotalSeconds}s — killed to fail this test loudly instead of " +
                    "wedging the run.");
            }

            // Per WaitForExit(int)'s own documentation: when reading redirected streams via the
            // asynchronous event-based API, call the parameterless WaitForExit() after receiving
            // true from the timed overload, to guarantee the async handlers above have finished
            // appending to stdout/stderr before they are read for the failure message below.
            process.WaitForExit();

            process.ExitCode.ShouldBe(0, $"git {arguments} failed: {stdout}{stderr}");
        }
    }

    [TestMethod]
    public void DeclaresParallelizationOnlyInAssemblyInfo()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");

        File.ReadAllText(Path.Combine(_root, "AssemblyInfo.cs")).ShouldContain("[assembly: DoNotParallelize]");
        // The element form, not the bare name: the INTEST0001 guard target must *name* both
        // properties in order to detect them, so what matters is that neither is ever *set*.
        var csproj = File.ReadAllText(Path.Combine(_root, "Orders.ApiTests.csproj"));
        csproj.ShouldNotContain("<MSTestParallelizeScope>");
        csproj.ShouldNotContain("<MSTestParallelizeWorkers>");
    }

    [TestMethod]
    public void GuardsAgainstTheDuplicateAttributeBuildBreak()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");
        File.ReadAllText(Path.Combine(_root, "Orders.ApiTests.csproj")).ShouldContain("INTEST0001");
    }

    [TestMethod]
    public void LeavesTheProfileParameterCommentedOut()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");
        var runsettings = File.ReadAllText(Path.Combine(_root, "Orders.ApiTests.runsettings"));
        runsettings.ShouldContain("<!-- <Parameter name=\"profile\"");
    }

    [TestMethod]
    public void RefusesToOverwriteAnExistingProject()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json").ShouldBe(0);
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json").ShouldBe(3);
    }

    [TestMethod]
    public void RefusesAnInvalidNameAndWritesNothing()
    {
        // --name seeds project.rootNamespace, project.testBaseClass, baseClassName, and the
        // `namespace` declaration of two scaffolded files — an invalid value here is invalid
        // regardless of what is (or is not) already on disk, so this must be checked before the
        // intest.json-already-exists check and before anything is written.
        var originalError = Console.Error;
        var capturedError = new StringWriter();
        Console.SetError(capturedError);
        int exitCode;
        try
        {
            exitCode = InitCommand.Run(_root, "My Project", "orders.json");
        }
        finally
        {
            Console.SetError(originalError);
        }

        exitCode.ShouldBe(2);
        Directory.GetFileSystemEntries(_root).ShouldBeEmpty();

        var message = capturedError.ToString();
        message.ShouldContain("--name", Case.Sensitive);
        message.ShouldContain("My Project");
    }

    [TestMethod]
    public void CsprojCopiesFixturesToTheOutputDirectory()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");

        File.ReadAllText(Path.Combine(_root, "Orders.ApiTests.csproj"))
            .ShouldContain("fixtures/**/*.json",
                customMessage: "FixtureStore loads from AppContext.BaseDirectory — this is the F1 defect repeating");
    }

    [TestMethod]
    public void TestStartupDoesNotReferenceTheDeletedTestDataType()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");

        File.ReadAllText(Path.Combine(_root, "TestStartup.cs"))
            .ShouldNotContain("TestData", customMessage: "Task 8 deletes it; a scaffold must not teach a dead API");
    }

    [TestMethod]
    public void RegisterCommentPointsAtImplementingITestTokenProviderNowThatAuthHandlerConsumesIt()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");

        // Task 2 question (e): AuthHandler now ships attached to InTestClients.Api, so telling
        // an adopter to append their own DelegatingHandler there produces two handlers both
        // setting Authorization, where the last one registered silently wins. The comment must
        // say AuthHandler is already attached and that only ITestTokenProvider needs
        // implementing — the instruction this same comment told people NOT to follow before
        // AuthHandler existed to consume it.
        var scaffold = File.ReadAllText(Path.Combine(_root, "TestStartup.cs"));

        scaffold.ShouldContain("AuthHandler",
            customMessage: "the scaffold must say AuthHandler is already attached, not send an adopter to write their own");
        scaffold.ShouldContain("ITestTokenProvider",
            customMessage: "the scaffold must point at the extension point that now actually works");
        scaffold.ShouldContain("InTestClients.Api",
            customMessage: "the scaffold must still name the client AuthHandler is attached to");
    }

    [TestMethod]
    public void ScaffoldedStartupDrainsFixtureCleanup()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");
        var startup = File.ReadAllText(Path.Combine(_root, "TestStartup.cs"));

        // Task 5: without an [AssemblyCleanup] calling TestHost.CleanupAsync, DrainAsync ships
        // with no caller and a fixture's teardown never runs in a generated project. One regex
        // ties the attribute, the method signature, and the call together as a single unit,
        // rather than two independent ShouldContain checks: independent checks would still pass
        // if the call were moved into AssemblyInit and AssemblyCleanup were left empty, which is
        // exactly the failure mode this test exists to catch. The call is pinned with its
        // parenthesised invocation, "TestHost.CleanupAsync(context)", not the bare
        // "TestHost.CleanupAsync" substring: that bare form also appears in the method's own doc
        // comment, so it would stay present even if the method body were gutted.
        Regex.IsMatch(
                startup,
                @"\[AssemblyCleanup\]\s+public\s+static\s+async\s+Task\s+AssemblyCleanup\(TestContext\s+context\)" +
                @"\s*\{\s*await\s+TestHost\.CleanupAsync\(context\);\s*\}",
                RegexOptions.Singleline)
            .ShouldBeTrue("expected [AssemblyCleanup] to directly wrap a call to TestHost.CleanupAsync(context)");
    }

    [TestMethod]
    public void RegisterMethodShowsACommentedFixtureRegistrationExample()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");
        var startup = File.ReadAllText(Path.Combine(_root, "TestStartup.cs"));

        // Commented, not live: `init` never discovers fixtures by reflection (v1-b decision 2), and a
        // live call here would reference a fixture type that does not exist yet, breaking every
        // fresh scaffold's build before a team has written one.
        startup.ShouldContain("// services.AddSingleton<IAssemblyFixture,");
    }

    [TestMethod]
    public void RegisterMethodShowsACommentedTokenProviderRegistrationExample()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders.json");
        var startup = File.ReadAllText(Path.Combine(_root, "TestStartup.cs"));

        // Task 6: same precedent as the IAssemblyFixture example above — commented, not live.
        // StaticTokenProvider needs a real token neither Catalog nor Inventory has a source for,
        // so a live registration here would either fail to construct or issue a token that
        // authenticates nothing. AuthHandler already no-ops when no provider is registered (Task
        // 2(b)), which is exactly the state this scaffold must ship in.
        startup.ShouldContain("// services.AddSingleton<ITestTokenProvider",
            customMessage: "the scaffold must show the registration, but only as a comment");
    }

    [TestMethod]
    public void EscapesAmpersandSoTheGeneratedCsprojActuallyParses()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "../R&D/orders.json").ShouldBe(0);

        var csprojText = File.ReadAllText(Path.Combine(_root, "Orders.ApiTests.csproj"));
        // The real parse, not a string check: an unescaped '&' is not well-formed XML and
        // XDocument.Parse throws on it rather than silently accepting it.
        var doc = XDocument.Parse(csprojText);

        doc.Descendants("InTestSpecSource").Single().Value.ShouldBe("../R&D/orders.json");
    }

    [TestMethod]
    public void EscapesDollarParenSoItSurvivesAsLiteralTextNotAnMSBuildExpansion()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders$(Configuration).json").ShouldBe(0);

        var csprojText = File.ReadAllText(Path.Combine(_root, "Orders.ApiTests.csproj"));
        var doc = XDocument.Parse(csprojText);

        // %24, not a bare $( — a bare $(Configuration) would expand as an MSBuild property
        // reference rather than surviving as the literal text the adopter typed.
        doc.Descendants("InTestSpecSource").Single().Value.ShouldBe("orders%24(Configuration).json");
    }

    [TestMethod]
    public void EscapesQuestionMarkSoTheIncludeGlobCannotResolveToADifferentFile()
    {
        // Confirmed by real `dotnet build` (see MSBuildPropertyValue's doc comment): with
        // specs/orders.json and specs/ordersX.json both on disk, an unescaped
        // Include="$(InTestSpecSource)" for "specs/orders?.json" silently resolved to
        // ordersX.json — the wrong file — instead of failing loudly.
        InitCommand.Run(_root, "Orders.ApiTests", "orders?.json").ShouldBe(0);

        var csprojText = File.ReadAllText(Path.Combine(_root, "Orders.ApiTests.csproj"));
        var doc = XDocument.Parse(csprojText);

        doc.Descendants("InTestSpecSource").Single().Value.ShouldBe("orders%3F.json");
    }

    [TestMethod]
    public void EscapesQuoteSoTheGeneratedIntestJsonActuallyParses()
    {
        InitCommand.Run(_root, "Orders.ApiTests", "orders\".json").ShouldBe(0);

        var jsonText = File.ReadAllText(Path.Combine(_root, "intest.json"));
        // The real parse: an unescaped '"' inside the JSON string value truncates it and leaves
        // the rest of the document malformed, which JsonDocument.Parse throws on.
        using var doc = JsonDocument.Parse(jsonText);

        doc.RootElement.GetProperty("spec").GetProperty("source").GetString().ShouldBe("orders\".json");
    }

    [TestMethod]
    public void WritesAmpersandAndNonAsciiCharactersLiterallyIntoIntestJson()
    {
        // Pins the choice of JavaScriptEncoder.UnsafeRelaxedJsonEscaping over the default
        // encoder — a choice round-tripping cannot prove, since both produce valid JSON encoding
        // the same string. The default encoder would render '&' as \u0026 and 'é' as \u00e9:
        // still correct JSON, but unreadable by an adopter who opens the file by hand.
        InitCommand.Run(_root, "Orders.ApiTests", "../R&D/café.json").ShouldBe(0);

        var jsonText = File.ReadAllText(Path.Combine(_root, "intest.json"));
        jsonText.ShouldContain("R&D");
        jsonText.ShouldContain("café");
    }

    [TestMethod]
    public void RoundTripsAHazardousSpecSourcePastConfigLoad()
    {
        // The strongest test on this surface: proves the value survives write (InitCommand) then
        // read (ConfigLoader) intact, through both escaping layers at once.
        var hazardous = "../R&D/orders?\"$(x).json";
        InitCommand.Run(_root, "Orders.ApiTests", hazardous).ShouldBe(0);

        ConfigLoader.Load(_root).SpecSource.ShouldBe(hazardous.Replace("\\", "/"));
    }

    [TestMethod]
    public void RefusesACharacterXmlCannotRepresentAndWritesNothing()
    {
        // U+0001 is a C0 control character XML 1.0's Char production excludes — no MSBuild or
        // XML escape sequence represents it, so this must refuse rather than escape.
        var originalError = Console.Error;
        var capturedError = new StringWriter();
        Console.SetError(capturedError);
        int exitCode;
        try
        {
            exitCode = InitCommand.Run(_root, "Orders.ApiTests", "orders\u0001.json");
        }
        finally
        {
            Console.SetError(originalError);
        }

        exitCode.ShouldBe(2);
        Directory.GetFileSystemEntries(_root).ShouldBeEmpty();

        var message = capturedError.ToString();
        message.ShouldContain("--spec", Case.Sensitive);
        // Pins that the diagnosis itself — not just the boilerplate sentence appended in
        // InitCommand — reached the message: MSBuildPropertyValue renders the offending
        // character as U+0001 rather than pasting the raw control character into the terminal.
        message.ShouldContain("U+0001");
    }

    // ---- One refusal surface -----------------------------------------------------------------
    // `init` rejects three arguments, and used to reject them two different ways. --name went
    // through CSharpIdentifier.TryValidateDottedName and came back as one sentence at exit 2;
    // --project and --spec went through ArgumentException.ThrowIfNullOrWhiteSpace and escaped
    // unhandled, which System.CommandLine turns into exit **1**. That is not a cosmetic
    // difference: §5 reserves 1 for "real work is outstanding that a human must do" — fixture
    // drift, validation failures — and separates it from 2 precisely so "CI can tell a crash from
    // fixture drift". A mistyped --spec therefore reported itself to a pipeline as fixture drift.
    // Two spellings of one mistake, `--name "My Project"` and `--name ""`, returned two different
    // exit codes. These tests pin the single surface that replaced it.

    /// <summary>Runs `init` with stderr captured, so a test can assert what the adopter is told.</summary>
    private static (int ExitCode, string Error) RunCapturingError(string projectRoot, string name, string spec)
    {
        var originalError = Console.Error;
        var capturedError = new StringWriter();
        Console.SetError(capturedError);
        try
        {
            return (InitCommand.Run(projectRoot, name, spec), capturedError.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    /// <summary>
    /// The shape, asserted on the assembled message rather than on the template that produces it.
    /// CSharpIdentifier.EmptyValueReason makes the middle of every refusal one object, but the
    /// setting that leads it and the example it carries are supplied per call site — so a
    /// fourth refusal written freehand would still share the template and still break the shape.
    /// This is the test that would catch that.
    /// </summary>
    [TestMethod]
    [DataRow("--project", "", "Orders.ApiTests", "orders.json", DisplayName = "--project empty")]
    [DataRow("--name", null, "", "orders.json", DisplayName = "--name empty")]
    [DataRow("--name", null, "   ", "orders.json", DisplayName = "--name whitespace")]
    [DataRow("--spec", null, "Orders.ApiTests", "", DisplayName = "--spec empty")]
    [DataRow("--spec", null, "Orders.ApiTests", "  \t ", DisplayName = "--spec whitespace")]
    public void RefusesEveryBlankArgumentInTheSameShape(
        string setting, string? projectRoot, string name, string spec)
    {
        var (exitCode, error) = RunCapturingError(projectRoot ?? _root, name, spec);

        exitCode.ShouldBe(ExitCode.ToolError,
            "§5 gives 2 for a tool error and 1 for outstanding work — an argument the adopter " +
            "mistyped is a tool error, and reporting it as 1 makes it indistinguishable from drift");
        error.ShouldStartWith(setting, Case.Sensitive,
            customMessage: "a refusal leads with the setting the adopter got wrong");
        error.ShouldContain("is empty",
            customMessage: "a refusal says what is wrong with the value, not just that something is");
        // Not ShouldContain("for example"): that phrase is discriminating only if an actual
        // quoted value follows it, and a rule that said "for example" and then trailed off would
        // have satisfied it. "Carries", not "ends with" — --project's example sits mid-sentence,
        // ahead of the sentence telling the adopter they can omit the flag entirely.
        Regex.IsMatch(error, "for example \"[^\"]+\"").ShouldBeTrue(
            "a refusal carries a value the adopter can copy");
    }

    /// <summary>
    /// Separate from the shape test because the shape test cannot prove this row: with --project
    /// blank, `_root` is not where a broken build would write. Path.Combine("", "intest.json") is
    /// "intest.json", so a blank --project does not fail — it silently retargets every write at
    /// the process's current directory. Refusing it is what stops `init` scaffolding nine files
    /// into whatever directory the adopter happened to be standing in.
    /// </summary>
    [TestMethod]
    public void RefusesABlankProjectRatherThanScaffoldingIntoTheCurrentDirectory()
    {
        var originalDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_root);
        try
        {
            var (exitCode, error) = RunCapturingError("", "Orders.ApiTests", "orders.json");

            exitCode.ShouldBe(ExitCode.ToolError);
            error.ShouldStartWith("--project", Case.Sensitive);
            Directory.GetFileSystemEntries(_root).ShouldBeEmpty(
                "a blank --project must be refused, not resolved to the current directory");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }
    }

    /// <summary>
    /// The same reason the blank <c>--spec</c> guard gives: `init` must never write a config it
    /// knows `generate` will reject. Measured before this guard existed —
    /// <c>init --spec https://example.com/openapi.json</c> printed
    /// "Initialised Orders.ApiTests. Next: `intest generate`." and exited <b>0</b>, writing the
    /// whole scaffold, and only then did `generate` fail with
    /// <c>Spec file not found: &lt;projectRoot&gt;\https://example.com/openapi.json</c> at exit 2.
    /// <para>
    /// So `init` did not merely fail to help: it actively confirmed the belief the help text had
    /// created ("Path or URL"), and displaced the contradiction onto a different command, one
    /// step later, phrased as a missing file. Refusing here is what makes the tool's own voice
    /// agree with itself.
    /// </para>
    /// </summary>
    [TestMethod]
    [DataRow("https://example.com/openapi.json", DisplayName = "https")]
    [DataRow("http://example.com/openapi.json", DisplayName = "http")]
    [DataRow("HTTPS://EXAMPLE.COM/openapi.json", DisplayName = "uppercase scheme")]
    public void RefusesAUrlSpecRatherThanScaffoldingAProjectGenerateWillReject(string spec)
    {
        var (exitCode, error) = RunCapturingError(_root, "Orders.ApiTests", spec);

        exitCode.ShouldBe(ExitCode.ToolError);
        Directory.GetFileSystemEntries(_root).ShouldBeEmpty(
            "§5's exit 2 is \"nothing was written\", and an argument is judged before the first write");
        error.ShouldStartWith("--spec", Case.Sensitive,
            customMessage: "a refusal leads with the setting the adopter got wrong");
        error.ShouldContain(spec,
            customMessage: "a refusal quotes what the adopter actually wrote");
        error.ShouldContain("URL",
            customMessage: "a refusal names the kind of value it is refusing");
        error.ShouldContain("for example \"",
            customMessage: "a refusal carries a value the adopter can copy");
    }

    /// <summary>
    /// The false positive the narrow predicate exists to avoid, pinned at `init` as well as at
    /// <see cref="Configuration.ConfigLoader"/> because the two refuse independently.
    /// <c>Uri.TryCreate</c> calls <c>C:/specs/orders.json</c> an <i>absolute</i> URI with scheme
    /// <c>file</c>, so a general absolute-URI check would refuse the most ordinary
    /// <c>--spec</c> value on Windows. The rule is an <c>http://</c>/<c>https://</c> prefix and
    /// nothing broader.
    /// </summary>
    [TestMethod]
    [DataRow("C:/specs/orders.json", DisplayName = "rooted Windows path — an absolute file: URI to Uri.TryCreate")]
    [DataRow("//fileserver/specs/orders.json", DisplayName = "UNC path")]
    [DataRow("specs/http/orders.json", DisplayName = "path with a url-ish segment")]
    public void ScaffoldsFromAPathThatOnlyLooksLikeAUrl(string spec)
    {
        var (exitCode, error) = RunCapturingError(_root, "Orders.ApiTests", spec);

        exitCode.ShouldBe(ExitCode.Ok, error);
        var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "intest.json")));
        config.RootElement.GetProperty("spec").GetProperty("source").GetString().ShouldBe(spec);
    }

    // ReportsAnUnanticipatedScaffoldFailureAsAToolErrorRatherThanAStackTrace moved to
    // InTest.Golden.Tests/CliExitCodeTests as CrashInACommandWithNoCatchOfItsOwnExitsToolError.
    // It asserted the catch-all inside InitCommand.Run, and that catch-all is now Program's, so a
    // test calling InitCommand.Run directly can no longer reach it — the exception escapes before
    // any exit code exists. Only a real process can observe the floor, which is the point of
    // moving it rather than deleting it.

    // ScaffoldStillBuildsWithNoTokenProviderRegistered moved to InTest.Golden.Tests, next to
    // CompileVerificationTests (Task 10 item 7): it was the only out-of-process *build* that
    // lived in this assembly, and under a solution-level `dotnet test` this assembly's ~6s run
    // fully overlaps InTest.Golden.Tests' ~1m40s one, so two independent MSBuild invocations
    // could build scaffolded projects that both ProjectReference the same InTest.Runtime.csproj
    // simultaneously — a known source of intermittent obj/ file-lock failures. (v1-e's
    // GitattributesSurvivesAnAutocrlfInputCheckout, added later, shells out to a real `git`
    // binary and so is also out-of-process, but it never invokes MSBuild, so the file-lock race
    // this comment describes still does not apply to it.) The assertion
    // itself is unchanged; see ScaffoldCompileVerificationTests there.
}

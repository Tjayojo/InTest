using Shouldly;

namespace InTest.Runtime.Tests;

/// <summary>
/// Guards the defect the v0 acceptance run found (F3): a base URL repeating a prefix the
/// spec's paths already carry sends every request to /api/api/... Every test returns 404
/// against configuration that looks entirely correct, so this must fail before the first
/// request rather than after nine of them.
/// </summary>
[TestClass]
public class PrefixDuplicationTests
{
    [TestMethod]
    public void ThrowsWhenTheBaseUrlRepeatsTheOperationPrefix()
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            InTestUrl.EnsureNoPrefixDuplication(new Uri("http://localhost:5081/api/"), "/api"));

        ex.Message.ShouldContain("/api/api/", Case.Sensitive);
        ex.Message.ShouldContain("servers[0].url", Case.Sensitive);
        ex.Message.ShouldContain("http://localhost:5081/", Case.Sensitive);
    }

    [TestMethod]
    public void ThrowsOnAMultiSegmentRepeatedPrefix()
    {
        Should.Throw<InvalidOperationException>(() =>
            InTestUrl.EnsureNoPrefixDuplication(new Uri("https://h/api/v2/"), "/api/v2"));
    }

    [TestMethod]
    [DataRow("http://localhost:5081/", "/api", DisplayName = "origin base, prefixed paths — the correct pairing")]
    [DataRow("https://h/api/", "", DisplayName = "no common prefix among operations")]
    [DataRow("https://h/", "", DisplayName = "neither side has a prefix")]
    [DataRow("https://h/gateway/", "/api", DisplayName = "different prefixes do not overlap")]
    public void AcceptsPairingsThatResolveCorrectly(string baseUrl, string prefix)
    {
        Should.NotThrow(() => InTestUrl.EnsureNoPrefixDuplication(new Uri(baseUrl), prefix));
    }

    [TestMethod]
    public void DoesNotFalsePositiveOnAPartialSegmentMatch()
    {
        // "/api" is a string prefix of "/apiary" but a different segment. Comparing text
        // rather than segments would reject a perfectly valid configuration.
        Should.NotThrow(() => InTestUrl.EnsureNoPrefixDuplication(new Uri("https://h/api/"), "/apiary"));
    }

    [TestMethod]
    public void TreatsAMissingPrefixManifestAsNothingToCheck()
    {
        Should.NotThrow(() => InTestUrl.EnsureNoPrefixDuplication(new Uri("https://h/api/"), null));
    }
}

using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class ResourceConflictAnalyzerTests
{
    [TestMethod]
    public void AnalyzeClassifiesMatchingPayloadsAsRedundant()
    {
        ResourceProvider[] providers =
        [
            new("Alpha.archive", 42, "base\\ui\\icon.xbm", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
            new("zeta.archive", 42, "base\\ui\\icon.xbm", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")
        ];

        ResourceConflict conflict = ResourceConflictAnalyzer.Analyze(providers, ["Alpha.archive", "zeta.archive"]).Single();

        Assert.AreEqual(ResourceConflictKind.Redundant, conflict.Kind);
        Assert.AreEqual("Alpha.archive", conflict.EngineWinnerArchive);
    }

    [TestMethod]
    public void AnalyzeClassifiesDifferentPayloadsAsDivergent()
    {
        ResourceProvider[] providers =
        [
            new("Alpha.archive", 42, "base\\ui\\icon.xbm", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
            new("zeta.archive", 42, "base\\ui\\icon.xbm", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")
        ];

        ResourceConflict conflict = ResourceConflictAnalyzer.Analyze(providers, ["Alpha.archive", "zeta.archive"]).Single();

        Assert.AreEqual(ResourceConflictKind.Divergent, conflict.Kind);
        Assert.AreEqual("Alpha.archive", conflict.EngineWinnerArchive);
    }

    [TestMethod]
    public void AnalyzeKeepsAProvenWinnerWhenPayloadComparisonIsUnavailable()
    {
        ResourceProvider[] providers =
        [
            new("Alpha.archive", 42, null, null),
            new("zeta.archive", 42, null, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")
        ];

        ResourceConflict conflict = ResourceConflictAnalyzer.Analyze(providers, ["Alpha.archive", "zeta.archive"]).Single();

        Assert.AreEqual(ResourceConflictKind.OrderedOverlap, conflict.Kind);
        Assert.AreEqual("Alpha.archive", conflict.EngineWinnerArchive);
        Assert.AreEqual("resource hash 42", conflict.DisplayName);
    }

    [TestMethod]
    public void AnalyzeDoesNotCompareCookedSha256ToRdarSha1()
    {
        ResourceProvider[] providers =
        [
            new("Alpha.archive", 42, "base\\ui\\icon.xbm", null, CookedPayloadSha256: new string('a', 64)),
            new("zeta.archive", 42, "base\\ui\\icon.xbm", new string('b', 40))
        ];

        ResourceConflict conflict = ResourceConflictAnalyzer.Analyze(providers, ["Alpha.archive", "zeta.archive"]).Single();

        Assert.AreEqual(ResourceConflictKind.OrderedOverlap, conflict.Kind);
        Assert.AreEqual("Alpha.archive", conflict.EngineWinnerArchive);
    }

    [TestMethod]
    public void AnalyzeUsesSharedRdarSha1WhenCookedEvidenceIsIncomplete()
    {
        ResourceProvider[] providers =
        [
            new("Alpha.archive", 42, "base\\ui\\icon.xbm", new string('a', 40), CookedPayloadSha256: new string('c', 64)),
            new("zeta.archive", 42, "base\\ui\\icon.xbm", new string('a', 40))
        ];

        ResourceConflict conflict = ResourceConflictAnalyzer.Analyze(providers, ["Alpha.archive", "zeta.archive"]).Single();

        Assert.AreEqual(ResourceConflictKind.Redundant, conflict.Kind);
    }

    [TestMethod]
    public void AnalyzeDoesNotNameAWinnerWhenAProviderIsAbsentFromProvenOrder()
    {
        ResourceProvider[] providers = [new("Alpha.archive", 42, null, new string('a', 40)), new("Unknown.archive", 42, null, new string('b', 40))];

        ResourceConflict conflict = ResourceConflictAnalyzer.Analyze(providers, ["Alpha.archive"]).Single();

        Assert.AreEqual(ResourceConflictKind.Unresolved, conflict.Kind);
        Assert.AreEqual("unresolved", conflict.EngineWinnerArchive);
    }

    [TestMethod]
    public void AnalyzeKeepsReadableWinnerWhenUnreadableArchiveIsBelowTheWholeChain()
    {
        ResourceProvider[] providers = [new("High.archive", 42, null, new string('a', 40)), new("Middle.archive", 42, null, new string('b', 40))];
        RdarArchiveFailure[] failures = [new("Low", "Low.archive", "Invalid RDAR index")];

        ResourceConflict conflict = ResourceConflictAnalyzer.Analyze(providers, ["High.archive", "Middle.archive", "Low.archive"], failures).Single();

        Assert.AreEqual(ResourceConflictKind.Divergent, conflict.Kind);
        Assert.AreEqual("High.archive", conflict.EngineWinnerArchive);
    }

    [TestMethod]
    public void AnalyzeLeavesChainUnresolvedWhenUnreadableArchiveCrossesKnownProviders()
    {
        ResourceProvider[] providers = [new("High.archive", 42, null, new string('a', 40)), new("Low.archive", 42, null, new string('b', 40))];
        RdarArchiveFailure[] failures = [new("Middle", "Middle.archive", "Invalid RDAR index")];

        ResourceConflict conflict = ResourceConflictAnalyzer.Analyze(providers, ["High.archive", "Middle.archive", "Low.archive"], failures).Single();

        Assert.AreEqual(ResourceConflictKind.Unresolved, conflict.Kind);
        Assert.AreEqual("unresolved", conflict.EngineWinnerArchive);
    }
}

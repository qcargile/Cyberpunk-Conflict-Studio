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
    public void AnalyzeDoesNotNameAWinnerWhenAProviderIsAbsentFromProvenOrder()
    {
        ResourceProvider[] providers = [new("Alpha.archive", 42, null, new string('a', 40)), new("Unknown.archive", 42, null, new string('b', 40))];

        ResourceConflict conflict = ResourceConflictAnalyzer.Analyze(providers, ["Alpha.archive"]).Single();

        Assert.AreEqual(ResourceConflictKind.Unresolved, conflict.Kind);
        Assert.AreEqual("unresolved", conflict.EngineWinnerArchive);
    }
}

using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class ArchiveOrderImpactAnalyzerTests
{
    [TestMethod]
    public void AnalyzeReportsResourcesWhoseWinnerChanges()
    {
        ResourceProvider[] providers = [new("Alpha.archive", 42, "base\\icon.xbm", new string('a', 40)), new("Beta.archive", 42, "base\\icon.xbm", new string('b', 40))];

        ArchiveWinnerDelta delta = ArchiveOrderImpactAnalyzer.Analyze(providers, ["Alpha.archive", "Beta.archive"], ["Beta.archive", "Alpha.archive"]).Single();

        Assert.AreEqual("Alpha.archive", delta.BeforeWinner);
        Assert.AreEqual("Beta.archive", delta.AfterWinner);
        Assert.AreEqual("base\\icon.xbm", delta.DisplayName);
    }
}

using ConflictStudio.App;
using ConflictStudio.Core;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class ArchiveConflictTreeViewModelTests
{
    [TestMethod]
    public void TreeBuildsInlineWinningLosingSameAndUniqueGroups()
    {
        ArchiveResourceOutcome winning = Outcome(1, "base\\winning.mesh", ArchiveResourceDisposition.Winning, ArchivePayloadRelation.Different, "High.archive", ["Low.archive"]);
        ArchiveResourceOutcome losing = Outcome(2, "base\\losing.mesh", ArchiveResourceDisposition.Losing, ArchivePayloadRelation.Different, "Patch.archive", ["Patch.archive"]);
        ArchiveResourceOutcome same = Outcome(3, "base\\same.mesh", ArchiveResourceDisposition.Winning, ArchivePayloadRelation.Identical, "High.archive", ["Low.archive"]);
        ArchiveResourceOutcome unique = Outcome(4, "base\\unique.mesh", ArchiveResourceDisposition.Unique, ArchivePayloadRelation.NotApplicable, "High.archive", []);
        ArchiveConflictSummary summary = new("High.archive", "High Mod", 0, [winning, same], [losing], [same], [], [unique]);
        ArchiveConflictTreeViewModel tree = new();

        tree.Load([summary]);
        tree.Filter(string.Empty, string.Empty, true);

        ArchiveConflictNode archive = tree.VisibleArchives.Single();
        string[] expected = ["Winning (1)", "Losing (1)", "Same content (1)", "No conflicts (1)"];
        CollectionAssert.AreEqual(expected, archive.Children.Select(value => value.Header).ToArray());
        Assert.AreEqual(1, archive.WinningCount);
        Assert.AreEqual(1, archive.LosingCount);
        Assert.AreEqual(1, archive.SameCount);
        Assert.IsTrue(archive.HasWinning);
        Assert.IsTrue(archive.HasLosing);
        ArchiveResourceNode resource = archive.Children[0].Children.Single();
        Assert.AreEqual("base\\winning.mesh", resource.Path);
        Assert.IsTrue(resource.ProviderContext.Contains("Low.archive", StringComparison.Ordinal));
    }

    [TestMethod]
    public void TreeFiltersArchivesAndFilesWithoutHidingTheProviderContext()
    {
        ArchiveResourceOutcome alpha = Outcome(1, "base\\alpha.mesh", ArchiveResourceDisposition.Winning, ArchivePayloadRelation.Different, "Cars.archive", ["Patch.archive"]);
        ArchiveResourceOutcome beta = Outcome(2, "base\\beta.mesh", ArchiveResourceDisposition.Losing, ArchivePayloadRelation.Different, "Other.archive", ["Other.archive"]);
        ArchiveConflictSummary cars = new("Cars.archive", "Vehicle Pack", 0, [alpha], [], [], [], []);
        ArchiveConflictSummary other = new("Other.archive", "Other Mod", 1, [], [beta], [], [], []);
        ArchiveConflictTreeViewModel tree = new();

        tree.Load([cars, other]);
        tree.Filter("vehicle", "alpha", false);

        Assert.AreEqual("Cars.archive", tree.VisibleArchives.Single().ArchiveName);
        Assert.AreEqual("base\\alpha.mesh", tree.VisibleArchives.Single().Children.Single().Children.Single().Path);
        Assert.IsTrue(tree.ResultSummary.Contains("1 archive", StringComparison.Ordinal));
    }

    [TestMethod]
    public void UnknownOnlyArchiveIsNeverCalledConflictFree()
    {
        ArchiveResourceOutcome unknown = Outcome(1, "base\\unknown.mesh", ArchiveResourceDisposition.Unresolved, ArchivePayloadRelation.Unknown, null, ["Other.archive"]);
        ArchiveConflictTreeViewModel tree = new();

        tree.Load([new ArchiveConflictSummary("Unknown.archive", "Unknown Mod", 8, [], [], [], [unknown], [])]);
        tree.Filter(string.Empty, string.Empty, false);

        ArchiveConflictNode archive = tree.VisibleArchives.Single();
        Assert.AreEqual(1, archive.UnknownCount);
        Assert.IsTrue(archive.HasUnknown);
        Assert.IsTrue(archive.CountSummary.Contains("can't determine", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void LoadDefersTreeMaterializationUntilOneFilterPass()
    {
        ArchiveConflictTreeViewModel tree = new();

        tree.Load([]);

        Assert.AreEqual(0, tree.VisibleArchives.Count);
        Assert.AreEqual("Run a profile scan to find archive conflicts.", tree.ResultSummary);
    }

    [TestMethod]
    public void TechnicalEvidenceLabelsCookedAndRdarHashesSeparately()
    {
        ArchiveResourceOutcome outcome = new(1, "base\\shared.mesh", ArchiveResourceDisposition.Winning, "Alpha.archive", new string('b', 64), "mesh", ResourcePathConfidence.ResolvedIndex, ["Beta.archive"], ArchivePayloadRelation.Unknown, new string('a', 40), new string('b', 64));
        ArchiveResourceNode node = new(outcome, ArchiveTreeTone.Winning);

        StringAssert.Contains(node.TechnicalEvidence, "Cooked SHA-256: " + new string('b', 64));
        StringAssert.Contains(node.TechnicalEvidence, "RDAR SHA-1: " + new string('a', 40));
    }

    private static ArchiveResourceOutcome Outcome(ulong hash, string path, ArchiveResourceDisposition disposition, ArchivePayloadRelation relation, string? winner, string[] others)
        => new(hash, path, disposition, winner, new string((char)('a' + (int)hash), 64), "mesh", ResourcePathConfidence.ResolvedIndex, others, relation);
}

using ConflictStudio.App;
using ConflictStudio.Core;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class ArchiveRelationshipPresentationTests
{
    [TestMethod]
    public void SameRelationshipStateColorsTheMatchingRowInBothArchivePanes()
    {
        ArchiveRailItem[] rail = [new("Alpha.archive"), new("Beta.archive")];
        ArchiveConflictNode[] tree = [Node("Alpha.archive"), Node("Beta.archive")];
        ArchiveOverviewEntry[] entries = [new("Alpha.archive", false, false, false, false, 0, 2), new("Beta.archive", true, false, false, false, 1, 2)];

        ArchiveRelationshipPresentation.Apply(rail, tree, entries);

        Assert.AreEqual(ArchiveRelationshipTone.None, rail[0].RelationshipTone);
        Assert.AreEqual(ArchiveRelationshipTone.Wins, rail[1].RelationshipTone);
        Assert.AreEqual(ArchiveRelationshipTone.None, tree[0].RelationshipTone);
        Assert.AreEqual(ArchiveRelationshipTone.Wins, tree[1].RelationshipTone);
    }

    [TestMethod]
    public void ClearingSelectionClearsRelationshipColorsInBothArchivePanes()
    {
        ArchiveRailItem rail = new("Beta.archive");
        ArchiveConflictNode tree = Node("Beta.archive");
        ArchiveOverviewEntry[] entries = [new("Beta.archive", false, true, false, false, 1, 2)];

        ArchiveRelationshipPresentation.Apply([rail], [tree], entries);
        ArchiveRelationshipPresentation.Apply([rail], [tree], []);

        Assert.AreEqual(ArchiveRelationshipTone.None, rail.RelationshipTone);
        Assert.AreEqual(ArchiveRelationshipTone.None, tree.RelationshipTone);
    }

    [TestMethod]
    [DataRow(true, true, false, false, ArchiveRelationshipTone.Mixed)]
    [DataRow(true, false, false, false, ArchiveRelationshipTone.Wins)]
    [DataRow(false, true, false, false, ArchiveRelationshipTone.Loses)]
    [DataRow(false, false, true, false, ArchiveRelationshipTone.Same)]
    [DataRow(true, false, false, true, ArchiveRelationshipTone.Unknown)]
    public void TonePreservesTheDirectionalMeaning(bool wins, bool loses, bool same, bool unknown, ArchiveRelationshipTone expected)
    {
        ArchiveOverviewEntry entry = new("Beta.archive", wins, loses, same, unknown, 1, 2);

        Assert.AreEqual(expected, ArchiveRelationshipPresentation.Tone(entry));
    }

    private static ArchiveConflictNode Node(string archiveName) => new(new ArchiveConflictSummary(archiveName, archiveName, 0, [], [], [], [], []), []);
}

using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class ProfileScanDriftAnalyzerTests
{
    [TestMethod]
    public void CompareReportsNewResourceAndSourceEvidence()
    {
        ProfileScanReceipt previous = EmptyReceipt();
        ProfileScanReceipt current = previous with { ResourceConflicts = [new ResourceConflict(42, "resource hash 42", ResourceConflictKind.Divergent, "Alpha.archive", [])], InteractionFindings = [new InteractionFinding("DamageSystem.ProcessHit()", InteractionFindingKind.Exclusive, "suppressed", ["Alpha", "Beta"])] };

        ProfileScanDrift drift = ProfileScanDriftAnalyzer.Compare(previous, current);

        Assert.AreEqual(1, drift.NewResourceConflicts.Length);
        Assert.AreEqual(1, drift.NewInteractionFindings.Length);
    }

    [TestMethod]
    public void CompareReportsChangedEvidenceWithoutDuplicatingItAsNewAndRemoved()
    {
        ResourceConflict before = new(42, "resource hash 42", ResourceConflictKind.Divergent, "Alpha.archive", [new ResourceProvider("Alpha.archive", 42, null, new string('a', 40))]);
        ResourceConflict after = before with { EngineWinnerArchive = "Beta.archive" };
        ProfileScanReceipt previous = EmptyReceipt() with { ResourceConflicts = [before] };
        ProfileScanReceipt current = EmptyReceipt() with { ResourceConflicts = [after] };

        ProfileScanDrift drift = ProfileScanDriftAnalyzer.Compare(previous, current);

        Assert.AreEqual(1, drift.ChangedResourceConflicts.Length);
        Assert.AreEqual(0, drift.NewResourceConflicts.Length);
        Assert.AreEqual(0, drift.RemovedResourceConflicts.Length);
    }

    [TestMethod]
    public void CompareReportsChangedDetailedTweakEvidenceThroughTheUnifiedQueue()
    {
        InteractionFinding finding = new("Items.Test.value", InteractionFindingKind.Review, "overlap", ["Alpha", "Beta"]);
        TweakOverlap beforeOverlap = new("Items.Test.value", TweakOverlapKind.ScalarOverwrite, [new TweakOperation("Alpha", "alpha.yaml", "Items.Test.value", "1", false), new TweakOperation("Beta", "beta.yaml", "Items.Test.value", "2", false)]);
        TweakOverlap afterOverlap = beforeOverlap with { Operations = [beforeOverlap.Operations[0], beforeOverlap.Operations[1] with { Value = "3" }] };
        ProfileScanReceipt previous = EmptyReceipt() with { InteractionFindings = [finding], TweakOverlaps = [beforeOverlap] };
        ProfileScanReceipt current = EmptyReceipt() with { InteractionFindings = [finding], TweakOverlaps = [afterOverlap] };

        ProfileScanDrift drift = ProfileScanDriftAnalyzer.Compare(previous, current);

        Assert.AreEqual(1, drift.ChangedWorkItems.Length);
        Assert.AreEqual("Items.Test.value", drift.ChangedWorkItems.Single().After.Target);
    }

    [TestMethod]
    public void CompareRejectsReceiptsFromDifferentInstallations()
    {
        ProfileScanReceipt previous = EmptyReceipt();
        ProfileScanReceipt current = previous with { InstallationId = new string('b', 64) };

        Assert.ThrowsExactly<ArgumentException>(() => ProfileScanDriftAnalyzer.Compare(previous, current));
    }

    private static ProfileScanReceipt EmptyReceipt() => new(1, "Standard", new DateTimeOffset(2026, 8, 25, 16, 0, 0, TimeSpan.Zero), [], [], [], [], [], [], [], [], [], [], [], [], Metrics: null, InstallationId: new string('a', 64));
}

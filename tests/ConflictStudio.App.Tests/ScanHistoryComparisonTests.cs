using ConflictStudio.App;
using ConflictStudio.Core;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class ScanHistoryComparisonTests
{
    [TestMethod]
    public void ComparisonKeepsBeforeAndAfterEvidenceForEveryChangeState()
    {
        ResourceConflict unchanged = Resource(1, "Unchanged", "Alpha.archive");
        ResourceConflict changedBefore = Resource(2, "Changed", "Alpha.archive");
        ResourceConflict changedAfter = Resource(2, "Changed", "Beta.archive");
        ProfileScanReceipt previous = Receipt([unchanged, changedBefore, Resource(3, "Removed", "Alpha.archive")]);
        ProfileScanReceipt current = Receipt([unchanged, changedAfter, Resource(4, "New", "Alpha.archive")]) with { ScannedAtUtc = previous.ScannedAtUtc.AddMinutes(1) };

        ScanHistoryComparison comparison = ScanHistoryComparison.Create(previous, current);

        ScanHistoryEntry newEntry = comparison.Entries.Single(value => value.Target == "New");
        ScanHistoryEntry changedEntry = comparison.Entries.Single(value => value.Target == "Changed");
        ScanHistoryEntry removedEntry = comparison.Entries.Single(value => value.Target == "Removed");
        ScanHistoryEntry unchangedEntry = comparison.Entries.Single(value => value.Target == "Unchanged");
        Assert.AreEqual(ScanHistoryChangeKind.New, newEntry.ChangeKind);
        Assert.IsNull(newEntry.Before);
        Assert.IsNotNull(newEntry.After);
        Assert.IsTrue(newEntry.CanOpenCurrent);
        Assert.AreEqual(ScanHistoryChangeKind.Changed, changedEntry.ChangeKind);
        Assert.AreNotEqual(changedEntry.Before?.EvidenceSha256, changedEntry.After?.EvidenceSha256);
        Assert.AreEqual(ScanHistoryChangeKind.NoLongerDetected, removedEntry.ChangeKind);
        Assert.AreEqual("No longer detected", removedEntry.Status);
        Assert.IsNotNull(removedEntry.Before);
        Assert.IsNull(removedEntry.After);
        Assert.IsFalse(removedEntry.CanOpenCurrent);
        Assert.AreEqual(ScanHistoryChangeKind.Unchanged, unchangedEntry.ChangeKind);
        Assert.AreEqual(unchangedEntry.Before?.EvidenceSha256, unchangedEntry.After?.EvidenceSha256);
    }

    [TestMethod]
    public void ComparisonWarnsWhenAnalyzerMetadataWasNotRecorded()
    {
        ProfileScanReceipt previous = Receipt([]);
        ProfileScanReceipt current = previous with { ScannedAtUtc = previous.ScannedAtUtc.AddMinutes(1), ToolVersion = "0.6.0", AnalysisVersion = "packed-1/code-9" };

        ScanHistoryComparison comparison = ScanHistoryComparison.Create(previous, current);

        StringAssert.Contains(comparison.ToolVersionNotice, "not recorded");
        StringAssert.Contains(comparison.AnalysisNotice, "not recorded");
        StringAssert.Contains(comparison.AnalysisNotice, "not attributed");
        Assert.IsTrue(comparison.AnalysisMayHaveChanged);
    }

    [TestMethod]
    public void ComparisonReportsChangedAnalyzerSeparatelyFromCoverage()
    {
        CodeCoverageReceipt beforeCoverage = new(1, [new CodeSourceCoverage("RedScript", 2)], 0, 0, 1, 0, []);
        CodeCoverageReceipt afterCoverage = new(1, [new CodeSourceCoverage("RedScript", 3)], 1, 1, 1, 2, []);
        ProfileScanReceipt previous = Receipt([]) with { ToolVersion = "0.5.0", AnalysisVersion = "packed-1/code-8", CodeCoverage = beforeCoverage };
        ProfileScanReceipt current = previous with { ScannedAtUtc = previous.ScannedAtUtc.AddMinutes(1), AnalysisVersion = "packed-1/code-9", CodeCoverage = afterCoverage };

        ScanHistoryComparison comparison = ScanHistoryComparison.Create(previous, current);

        StringAssert.Contains(comparison.AnalysisNotice, "packed-1/code-8");
        StringAssert.Contains(comparison.AnalysisNotice, "packed-1/code-9");
        StringAssert.Contains(comparison.AnalysisNotice, "not attributed");
        StringAssert.Contains(comparison.CoverageNotice, "changed");
        StringAssert.Contains(comparison.CoverageNotice, "unsupported=1");
        StringAssert.Contains(comparison.CoverageNotice, "unreadable=1");
        StringAssert.Contains(comparison.CoverageNotice, "dynamic=2");
        Assert.IsTrue(comparison.AnalysisMayHaveChanged);
    }

    [TestMethod]
    public void ComparisonDetectsDifferentReadIssuesWhenTheirCountsMatch()
    {
        CodeCoverageReceipt coverage = new(1, [new CodeSourceCoverage("RedScript", 2)], 0, 1, 1, 0, []);
        ProfileScanReceipt previous = Receipt([]) with { AnalysisVersion = "packed-1/code-8", CodeCoverage = coverage, SourceFailures = [new SourceAnalysisFailure("Alpha", "old.reds", "RedScript", "old failure")] };
        ProfileScanReceipt current = previous with { ScannedAtUtc = previous.ScannedAtUtc.AddMinutes(1), SourceFailures = [new SourceAnalysisFailure("Beta", "new.reds", "RedScript", "new failure")] };

        ScanHistoryComparison comparison = ScanHistoryComparison.Create(previous, current);

        StringAssert.Contains(comparison.CoverageNotice, "read issues changed");
        Assert.IsFalse(comparison.AnalysisMayHaveChanged);
    }

    [TestMethod]
    public void ComparisonRejectsAnotherProfile()
    {
        ProfileScanReceipt previous = Receipt([]);
        ProfileScanReceipt current = previous with { ProfileName = "Other", ScannedAtUtc = previous.ScannedAtUtc.AddMinutes(1) };

        Assert.ThrowsExactly<ArgumentException>(() => ScanHistoryComparison.Create(previous, current));
    }

    private static ResourceConflict Resource(ulong hash, string target, string winner) => new(hash, target, ResourceConflictKind.Divergent, winner, [new ResourceProvider("Alpha.archive", hash, target, new string('a', 40), ProviderName: "Alpha"), new ResourceProvider("Beta.archive", hash, target, new string('b', 40), ProviderName: "Beta")]);

    private static ProfileScanReceipt Receipt(ResourceConflict[] resources) => new(2, "Standard", new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero), ["Alpha", "Beta"], ["Alpha.archive", "Beta.archive"], [], resources, [], [], [], [], [], [], [], [], InstallationId: "installation");
}

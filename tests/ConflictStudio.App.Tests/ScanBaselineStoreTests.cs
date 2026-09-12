using ConflictStudio.App;
using ConflictStudio.Core;
using System.IO;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class ScanBaselineStoreTests
{
    [TestMethod]
    public void PinnedBaselineSurvivesRollingHistoryRetentionUntilCleared()
    {
        string root = TempDirectory();
        ScanBaselineStore store = new(root);
        ProfileScanReceipt pinned = Receipt();
        try
        {
            store.Pin(pinned);
            for (int index = 1; index <= 5; index++) ProfileScanReceiptPersistence.Save(root, pinned with { ScannedAtUtc = pinned.ScannedAtUtc.AddMinutes(index) });

            ScanBaselineLoadResult loaded = store.Load(pinned);

            Assert.AreEqual(ScanBaselineState.Available, loaded.State);
            Assert.AreEqual(pinned.ScannedAtUtc, loaded.Receipt?.ScannedAtUtc);
            Assert.AreEqual(2, Directory.EnumerateFiles(root, "scan-*.json").Count());
            store.Clear();
            Assert.AreEqual(ScanBaselineState.Missing, store.Load(pinned).State);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void CorruptedBaselineIsReportedAndPreserved()
    {
        string root = TempDirectory();
        string path = Path.Combine(root, "baseline.json");
        File.WriteAllText(path, "corrupted baseline");
        try
        {
            ScanBaselineLoadResult result = new ScanBaselineStore(root).Load(Receipt());

            Assert.AreEqual(ScanBaselineState.Unreadable, result.State);
            Assert.AreEqual("corrupted baseline", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ForeignBaselineIsReportedAndPreserved()
    {
        string root = TempDirectory();
        ScanBaselineStore store = new(root);
        ProfileScanReceipt foreign = Receipt() with { ProfileName = "Other" };
        store.Pin(foreign);
        byte[] before = File.ReadAllBytes(Path.Combine(root, "baseline.json"));
        try
        {
            ScanBaselineLoadResult result = store.Load(Receipt());

            Assert.AreEqual(ScanBaselineState.Foreign, result.State);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(Path.Combine(root, "baseline.json")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void InvalidReplacementDoesNotDamageExistingBaseline()
    {
        string root = TempDirectory();
        ScanBaselineStore store = new(root);
        ProfileScanReceipt receipt = Receipt();
        store.Pin(receipt);
        byte[] before = File.ReadAllBytes(Path.Combine(root, "baseline.json"));
        try
        {
            Assert.Throws<ProfileScanReceiptException>(() => store.Pin(receipt with { ResourceConflicts = null! }));

            CollectionAssert.AreEqual(before, File.ReadAllBytes(Path.Combine(root, "baseline.json")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string TempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "conflict-studio-baseline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static ProfileScanReceipt Receipt() => new(2, "Standard", new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero), [], [], [], [], [], [], [], [], [], [], [], [], InstallationId: "installation");
}

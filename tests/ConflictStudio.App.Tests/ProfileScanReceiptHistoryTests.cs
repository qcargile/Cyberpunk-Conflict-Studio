using ConflictStudio.App;
using ConflictStudio.Core;
using System.IO;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class ProfileScanReceiptHistoryTests
{
    [TestMethod]
    public void InvalidLatestReceiptIsPreservedWithoutBlockingTheFreshScan()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-receipt-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string latest = Path.Combine(root, "latest.json");
        string preserved = Path.Combine(root, "invalid-latest.json");
        File.WriteAllText(latest, "{\"schemaVersion\":2,\"staleField\":true}");

        try
        {
            ProfileScanReceiptHistory result = ProfileScanReceiptHistory.ReadOrPreserveInvalid(latest, preserved);

            Assert.IsNull(result.Receipt);
            Assert.IsTrue(result.InvalidHistory);
            Assert.IsTrue(result.CanReplaceLatest);
            Assert.AreEqual(preserved, result.PreservedInvalidPath);
            Assert.IsFalse(File.Exists(latest));
            Assert.IsTrue(File.Exists(preserved));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void WrongSchemaValueTypeIsPreservedWithoutBlockingTheFreshScan()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-receipt-schema-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string latest = Path.Combine(root, "latest.json");
        string preserved = Path.Combine(root, "invalid-latest.json");
        File.WriteAllText(latest, "{\"schemaVersion\":\"two\"}");
        try
        {
            ProfileScanReceiptHistory result = ProfileScanReceiptHistory.ReadOrPreserveInvalid(latest, preserved);

            Assert.IsTrue(result.InvalidHistory);
            Assert.AreEqual(preserved, result.PreservedInvalidPath);
            Assert.IsFalse(File.Exists(latest));
            Assert.IsTrue(File.Exists(preserved));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ExistingPreservedReceiptIsNeverOverwritten()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-receipt-collision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string latest = Path.Combine(root, "latest.json");
        string preserved = Path.Combine(root, "invalid-latest.json");
        File.WriteAllText(latest, "invalid new history");
        File.WriteAllText(preserved, "existing preserved history");

        try
        {
            ProfileScanReceiptHistory result = ProfileScanReceiptHistory.ReadOrPreserveInvalid(latest, preserved);

            Assert.AreEqual("existing preserved history", File.ReadAllText(preserved));
            Assert.IsNotNull(result.PreservedInvalidPath);
            Assert.AreNotEqual(preserved, result.PreservedInvalidPath);
            Assert.AreEqual("invalid new history", File.ReadAllText(result.PreservedInvalidPath!));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void PreservationFailureDoesNotThrowOrDeleteTheInvalidLatestReceipt()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-receipt-io-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string latest = Path.Combine(root, "latest.json");
        string blockedParent = Path.Combine(root, "blocked");
        File.WriteAllText(latest, "invalid history");
        File.WriteAllText(blockedParent, "not a directory");

        try
        {
            ProfileScanReceiptHistory result = ProfileScanReceiptHistory.ReadOrPreserveInvalid(latest, Path.Combine(blockedParent, "invalid-latest.json"));

            Assert.IsTrue(result.InvalidHistory);
            Assert.IsNull(result.PreservedInvalidPath);
            Assert.IsFalse(result.CanReplaceLatest);
            Assert.IsTrue(File.Exists(latest));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void PersistenceIoFailureIsReturnedWithoutInvalidatingTheFreshScan()
    {
        Exception? result = ProfileScanReceiptHistory.TryPersist(() => throw new IOException("locked"));

        Assert.IsInstanceOfType<IOException>(result);
    }

    [TestMethod]
    public void InvalidFreshReceiptIsNeverSuppressedAsAHistoryIoFailure()
    {
        Assert.Throws<ConflictStudio.Core.ProfileScanReceiptException>(() => ProfileScanReceiptHistory.TryPersist(() => throw new ConflictStudio.Core.ProfileScanReceiptException("invalid fresh receipt")));
    }

    [TestMethod]
    public void PersistenceWritesTimestampedScanWithoutReplacingUnpreservedInvalidLatest()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-receipt-transaction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string latest = Path.Combine(root, "latest.json");
        File.WriteAllText(latest, "unreadable historical receipt");
        ProfileScanReceipt receipt = Receipt();

        try
        {
            ProfileScanReceiptPersistenceResult result = ProfileScanReceiptPersistence.Save(root, receipt, (_, _) => new ProfileScanReceiptHistory(null, null, true));

            Assert.IsTrue(File.Exists(result.TimestampedScanPath));
            Assert.IsFalse(result.LatestReplaced);
            Assert.AreEqual("unreadable historical receipt", File.ReadAllText(latest));
            Assert.AreEqual(receipt.ScannedAtUtc, ProfileScanReceiptStore.Read(result.TimestampedScanPath).ScannedAtUtc);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void InvalidFreshReceiptFailsBeforeHistoricalStateIsReadOrMutated()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-invalid-fresh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string latest = Path.Combine(root, "latest.json");
        ProfileScanReceipt valid = Receipt();
        ProfileScanReceiptStore.Write(latest, valid);
        byte[] latestBefore = File.ReadAllBytes(latest);
        ProfileScanReceipt invalid = valid with { ResourceConflicts = null! };
        bool historyRead = false;

        try
        {
            Assert.Throws<ProfileScanReceiptException>(() => ProfileScanReceiptPersistence.Save(root, invalid, (_, _) =>
            {
                historyRead = true;
                return new ProfileScanReceiptHistory(valid, null, false);
            }));

            Assert.IsFalse(historyRead);
            CollectionAssert.AreEqual(latestBefore, File.ReadAllBytes(latest));
            Assert.AreEqual(1, Directory.EnumerateFiles(root).Count());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ReadableHistoryWithoutInstallationIdentityIsPreservedBeforeLatestIsReplaced()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-incompatible-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string latest = Path.Combine(root, "latest.json");
        ProfileScanReceipt previous = Receipt();
        ProfileScanReceipt current = Receipt() with { ScannedAtUtc = new DateTimeOffset(2026, 8, 28, 13, 0, 0, TimeSpan.Zero), InstallationId = "installation" };
        ProfileScanReceiptStore.Write(latest, previous);

        try
        {
            ProfileScanReceiptPersistenceResult result = ProfileScanReceiptPersistence.Save(root, current);

            Assert.IsTrue(result.InvalidHistory);
            Assert.IsTrue(result.IncompatibleHistory);
            Assert.IsNotNull(result.PreservedInvalidPath);
            Assert.IsTrue(result.LatestReplaced);
            Assert.AreEqual(previous.ScannedAtUtc, ProfileScanReceiptStore.Read(result.PreservedInvalidPath!).ScannedAtUtc);
            Assert.AreEqual(current.ScannedAtUtc, ProfileScanReceiptStore.Read(latest).ScannedAtUtc);
            Assert.IsNull(result.Drift);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void PersistenceRetainsOnlyTheTwoNewestScansAndDrifts()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-retention-" + Guid.NewGuid().ToString("N"));
        try
        {
            for (int index = 0; index < 5; index++)
            {
                ProfileScanReceipt receipt = Receipt() with { ScannedAtUtc = new DateTimeOffset(2026, 8, 28, 12, index, 0, TimeSpan.Zero), InstallationId = "installation" };
                ProfileScanReceiptPersistence.Save(root, receipt);
            }

            Assert.AreEqual(2, Directory.EnumerateFiles(root, "scan-*.json").Count());
            Assert.AreEqual(2, Directory.EnumerateFiles(root, "drift-*.json").Count());
            Assert.IsTrue(File.Exists(Path.Combine(root, "latest.json")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static ProfileScanReceipt Receipt() => new(2, "Profile", new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero), [], [], [], [], [], [], [], [], [], [], [], [], []);
}

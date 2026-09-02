using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class ProfileInputGuardTests
{
    [TestMethod]
    public void RequireUnchangedRejectsAProfileChangedDuringScan()
    {
        string path = Path.Combine(Path.GetTempPath(), "conflict-studio-profile-input-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(path, "+Alpha\n");
            ProfileInputSnapshot snapshot = ProfileInputGuard.Capture(path);
            File.WriteAllText(path, "+Beta\n");

            Assert.ThrowsExactly<ProfileInputChangedException>(() => ProfileInputGuard.RequireUnchanged(snapshot));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void RequireStillAbsentRejectsAnOrderSourceCreatedDuringScan()
    {
        string path = Path.Combine(Path.GetTempPath(), "conflict-studio-order-appeared-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            Assert.IsFalse(File.Exists(path));
            File.WriteAllText(path, "Alpha.archive\n");

            Assert.ThrowsExactly<ProfileInputChangedException>(() => ProfileInputGuard.RequireStillAbsent(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void CachedHashMismatchIsNotReportedAsAnInScanChange()
    {
        string path = Path.Combine(Path.GetTempPath(), "conflict-studio-cached-input-" + Guid.NewGuid().ToString("N") + ".archive");
        try
        {
            File.WriteAllText(path, "current");
            ProfileFileSnapshot snapshot = ProfileInputGuard.CaptureFile(path, new string('a', 64));

            CachedFingerprintMismatchException exception = Assert.ThrowsExactly<CachedFingerprintMismatchException>(() => ProfileInputGuard.RequireUnchanged(snapshot));

            StringAssert.Contains(exception.Message, "cached fingerprint", StringComparison.OrdinalIgnoreCase);
            StringAssert.Contains(exception.Message, path, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void FreshHashMismatchNamesBothFingerprints()
    {
        string path = Path.Combine(Path.GetTempPath(), "conflict-studio-fresh-input-" + Guid.NewGuid().ToString("N") + ".archive");
        try
        {
            File.WriteAllText(path, "alpha");
            ProfileFileSnapshot snapshot = ProfileInputGuard.CaptureFile(path, true);
            File.WriteAllText(path, "omega-longer");

            ProfileInputChangedException exception = Assert.ThrowsExactly<ProfileInputChangedException>(() => ProfileInputGuard.RequireUnchanged(snapshot));

            StringAssert.Contains(exception.Message, "fresh fingerprint", StringComparison.OrdinalIgnoreCase);
            StringAssert.Contains(exception.Message, "expected", StringComparison.OrdinalIgnoreCase);
            StringAssert.Contains(exception.Message, "actual", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void FreshHashOfAStableFileValidatesWithoutConversion()
    {
        string path = Path.Combine(Path.GetTempPath(), "conflict-studio-stable-input-" + Guid.NewGuid().ToString("N") + ".archive");
        try
        {
            byte[] content = Enumerable.Range(0, 4096).Select(value => (byte)(value % 251)).ToArray();
            File.WriteAllBytes(path, content);
            ProfileFileSnapshot snapshot = ProfileInputGuard.CaptureFile(path, true);
            string directHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content));

            ProfileInputGuard.RequireUnchanged(snapshot);

            Assert.AreEqual(directHash, snapshot.Sha256);
            Assert.AreEqual(64, snapshot.Sha256!.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void FreshFileWithOnlyAMetadataChangeRemainsValid()
    {
        string path = Path.Combine(Path.GetTempPath(), "conflict-studio-metadata-only-input-" + Guid.NewGuid().ToString("N") + ".archive");
        try
        {
            File.WriteAllText(path, "same bytes");
            ProfileFileSnapshot snapshot = ProfileInputGuard.CaptureFile(path, true);
            File.SetLastWriteTimeUtc(path, snapshot.LastWriteTimeUtc.AddSeconds(2));

            ProfileInputGuard.RequireUnchanged(snapshot);

            Assert.AreEqual(snapshot.Sha256, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

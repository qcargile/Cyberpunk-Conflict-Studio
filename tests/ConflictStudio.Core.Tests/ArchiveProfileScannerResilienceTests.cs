using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class ArchiveProfileScannerResilienceTests
{
    [TestMethod]
    public void Mo2ScanRetainsReadableArchivesAndNamesAnUnreadableArchive()
    {
        string root = TemporaryRoot("mo2-archive-failure");
        try
        {
            string good = WriteArchive(root, "Good", "Good.archive", "good");
            string broken = WriteArchive(root, "Broken", "Broken.archive", "broken");
            string profile = Path.Combine(root, "modlist.txt");
            File.WriteAllText(profile, "+Good\n+Broken\n");
            using FileStream locked = File.Open(broken, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            Mo2ArchiveProfile result = Mo2ArchiveProfileScanner.Scan(root, profile);

            Assert.AreEqual(good, result.Archives.Single().PhysicalPath);
            Assert.AreEqual("Broken.archive", Path.GetFileName(result.Failures.Single().FilePath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void Mo2ScanSurvivesFingerprintCacheSaveFailureAndReportsIt()
    {
        string root = TemporaryRoot("mo2-cache-failure");
        try
        {
            WriteArchive(root, "Good", "Good.archive", "good");
            string profile = Path.Combine(root, "modlist.txt");
            File.WriteAllText(profile, "+Good\n");
            string cachePath = Path.Combine(root, "cache-target");
            Directory.CreateDirectory(cachePath);

            Mo2ArchiveProfile result = Mo2ArchiveProfileScanner.Scan(root, profile, cachePath, true);

            Assert.AreEqual("Good.archive", result.Archives.Single().ArchiveName);
            Assert.AreEqual("Fingerprint cache", result.Failures.Single().Surface);
            Assert.AreEqual(cachePath, result.Failures.Single().FilePath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ManualScanRetainsReadableArchivesAndNamesAnUnreadableArchive()
    {
        string root = TemporaryRoot("manual-archive-failure");
        try
        {
            string good = WriteGameArchive(root, "Good.archive", "good");
            string broken = WriteGameArchive(root, "Broken.archive", "broken");
            using FileStream locked = File.Open(broken, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            Mo2ArchiveProfile result = ManualArchiveProfileScanner.Scan(root);

            Assert.AreEqual(good, result.Archives.Single().PhysicalPath);
            Assert.AreEqual("Broken.archive", Path.GetFileName(result.Failures.Single().FilePath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ManualScanTreatsAMissingLegacyArchiveFolderAsEmpty()
    {
        string root = TemporaryRoot("manual-empty-archive-folder");
        try
        {
            Mo2ArchiveProfile result = ManualArchiveProfileScanner.Scan(root);

            Assert.HasCount(0, result.Archives);
            Assert.HasCount(0, result.EffectiveOrder);
            Assert.AreEqual(ArchiveOrderEvidenceKind.FilenameFallback, result.OrderEvidence!.Kind);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void VortexScanRetainsReadableArchivesAndNamesAnUnreadableArchive()
    {
        string root = TemporaryRoot("vortex-archive-failure");
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            string provider = Path.Combine(staging, "Provider");
            string good = WriteGameArchive(provider, "Good.archive", "good");
            string broken = WriteGameArchive(provider, "Broken.archive", "broken");
            Dictionary<string, string> winners = new(StringComparer.OrdinalIgnoreCase)
            {
                ["archive\\pc\\mod\\Good.archive"] = "provider",
                ["archive\\pc\\mod\\Broken.archive"] = "provider"
            };
            VortexManagerContext context = new(1, new string('a', 64), DateTimeOffset.UtcNow, "profile", "Standard", game, staging, true, [new("provider", "Provider", provider, 0)], winners, [], null);
            using FileStream locked = File.Open(broken, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            Mo2ArchiveProfile result = VortexArchiveProfileScanner.Scan(context);

            Assert.AreEqual(good, result.Archives.Single().PhysicalPath);
            Assert.AreEqual("Broken.archive", Path.GetFileName(result.Failures.Single().FilePath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void Mo2ScanContainsAnUnreadableArchiveOrderFile()
    {
        string root = TemporaryRoot("mo2-order-failure");
        try
        {
            WriteArchive(root, "Good", "Good.archive", "good");
            string order = Path.Combine(root, "Good", "archive", "pc", "mod", "modlist.txt");
            File.WriteAllText(order, "Good.archive\n");
            string profile = Path.Combine(root, "modlist.txt");
            File.WriteAllText(profile, "+Good\n");
            using FileStream locked = File.Open(order, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            Mo2ArchiveProfile result = Mo2ArchiveProfileScanner.Scan(root, profile);

            Assert.AreEqual(ArchiveOrderEvidenceKind.Unresolved, result.OrderEvidence!.Kind);
            Assert.AreEqual("Archive order", result.Failures.Single().Surface);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ManualScanContainsAnUnreadableArchiveOrderFile()
    {
        string root = TemporaryRoot("manual-order-failure");
        try
        {
            WriteGameArchive(root, "Good.archive", "good");
            string order = Path.Combine(root, "archive", "pc", "mod", "modlist.txt");
            File.WriteAllText(order, "Good.archive\n");
            using FileStream locked = File.Open(order, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            Mo2ArchiveProfile result = ManualArchiveProfileScanner.Scan(root);

            Assert.AreEqual(ArchiveOrderEvidenceKind.Unresolved, result.OrderEvidence!.Kind);
            Assert.AreEqual("Archive order", result.Failures.Single().Surface);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void VortexScanContainsAnUnreadableArchiveOrderFile()
    {
        string root = TemporaryRoot("vortex-order-failure");
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            string provider = Path.Combine(staging, "Provider");
            WriteGameArchive(provider, "Good.archive", "good");
            string order = Path.Combine(game, "archive", "pc", "mod", "modlist.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(order)!);
            File.WriteAllText(order, "Good.archive\n");
            VortexManagerContext context = new(1, new string('a', 64), DateTimeOffset.UtcNow, "profile", "Standard", game, staging, true, [new("provider", "Provider", provider, 0)], [], ["Good.archive"], null);
            using FileStream locked = File.Open(order, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            Mo2ArchiveProfile result = VortexArchiveProfileScanner.Scan(context);

            Assert.AreEqual(ArchiveOrderEvidenceKind.Unresolved, result.OrderEvidence!.Kind);
            Assert.AreEqual("Archive order", result.Failures.Single().Surface);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string TemporaryRoot(string name)
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-" + name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string WriteArchive(string root, string provider, string name, string content)
        => WriteGameArchive(Path.Combine(root, provider), name, content);

    private static string WriteGameArchive(string root, string name, string content)
    {
        string directory = Path.Combine(root, "archive", "pc", "mod");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, content);
        return path;
    }
}

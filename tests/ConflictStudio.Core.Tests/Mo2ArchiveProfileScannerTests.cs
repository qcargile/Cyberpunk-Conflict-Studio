using ConflictStudio.Core;
using System.IO;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class Mo2ArchiveProfileScannerTests
{
    private static readonly string[] ExpectedOrder = ["Alpha.archive", "Beta.archive"];
    private static readonly string[] ExpectedTopologyOrder = ["Alpha.archive", "Manual.archive", "Overwrite.archive"];
    private static readonly string[] ManagedOrder = ["Beta.archive", "Alpha.archive"];

    [TestMethod]
    public void ScanUsesFilenameOrderWhenNoArchiveModlistExists()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-profile-archives-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            WriteArchive(root, "Alpha", "Alpha.archive", "alpha");
            WriteArchive(root, "Beta", "Beta.archive", "beta");
            string profile = Path.Combine(root, "modlist.txt");
            File.WriteAllText(profile, "+Beta\n-Disabled\n+Alpha\n");

            Mo2ArchiveProfile archiveProfile = Mo2ArchiveProfileScanner.Scan(root, profile);

            CollectionAssert.AreEqual(ExpectedOrder, archiveProfile.EffectiveOrder);
            Assert.AreEqual(ArchiveOrderEvidenceKind.FilenameFallback, archiveProfile.OrderEvidence!.Kind);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanIncludesOverwriteAndManualArchivesInFilenameOrder()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-profile-topology-" + Guid.NewGuid().ToString("N"));
        string modsRoot = Path.Combine(root, "mods");
        string gameRoot = Path.Combine(root, "game");
        try
        {
            WriteArchive(modsRoot, "Alpha", "Alpha.archive", "alpha");
            string overwrite = Path.Combine(root, "overwrite", "archive", "pc", "mod");
            Directory.CreateDirectory(overwrite);
            File.WriteAllText(Path.Combine(overwrite, "Overwrite.archive"), "overwrite");
            string manual = Path.Combine(gameRoot, "archive", "pc", "mod");
            Directory.CreateDirectory(manual);
            File.WriteAllText(Path.Combine(manual, "Manual.archive"), "manual");
            File.WriteAllText(Path.Combine(root, "ModOrganizer.ini"), $"[General]\ngamePath=@ByteArray({gameRoot.Replace("\\", "\\\\", StringComparison.Ordinal)})\n[Plugins]\nCyberpunk%202077%20Support%20Plugin\\reverse_archive_load_order=true\n");
            string profile = Path.Combine(root, "profiles", "Standard", "modlist.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
            File.WriteAllText(profile, "+Alpha\n");

            Mo2ArchiveProfile result = Mo2ArchiveProfileScanner.Scan(modsRoot, profile);

            CollectionAssert.AreEqual(ExpectedTopologyOrder, result.EffectiveOrder);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanUsesTheHighestPriorityVirtualArchiveModlist()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-profile-managed-order-" + Guid.NewGuid().ToString("N"));
        string modsRoot = Path.Combine(root, "mods");
        try
        {
            WriteArchive(modsRoot, "High", "Alpha.archive", "alpha");
            WriteArchive(modsRoot, "Low", "Beta.archive", "beta");
            string orderRoot = Path.Combine(modsRoot, "High", "archive", "pc", "mod");
            File.WriteAllLines(Path.Combine(orderRoot, "modlist.txt"), ManagedOrder);
            string profile = Path.Combine(root, "profiles", "Standard", "modlist.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
            File.WriteAllText(profile, "+High\n+Low\n");

            Mo2ArchiveProfile result = Mo2ArchiveProfileScanner.ScanInstance(root, profile);

            CollectionAssert.AreEqual(ManagedOrder, result.EffectiveOrder);
            Assert.AreEqual(ArchiveOrderEvidenceKind.ManagedModlist, result.OrderEvidence!.Kind);
            Assert.AreEqual("High", result.OrderEvidence.Provider);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanIgnoresInactiveEntriesInAnOtherwiseCompleteManagedOrder()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-profile-inactive-order-" + Guid.NewGuid().ToString("N"));
        string modsRoot = Path.Combine(root, "mods");
        try
        {
            WriteArchive(modsRoot, "High", "Alpha.archive", "alpha");
            WriteArchive(modsRoot, "Low", "Beta.archive", "beta");
            string orderRoot = Path.Combine(modsRoot, "High", "archive", "pc", "mod");
            File.WriteAllLines(Path.Combine(orderRoot, "modlist.txt"), ["Beta.archive", "Inactive.archive", "Alpha.archive"]);
            string profile = Path.Combine(root, "profiles", "Standard", "modlist.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
            File.WriteAllText(profile, "+High\n+Low\n");

            Mo2ArchiveProfile result = Mo2ArchiveProfileScanner.ScanInstance(root, profile);

            CollectionAssert.AreEqual(ManagedOrder, result.EffectiveOrder);
            Assert.AreEqual(ArchiveOrderEvidenceKind.ManagedModlist, result.OrderEvidence!.Kind);
            string[] ignored = ["Inactive.archive"];
            CollectionAssert.AreEqual(ignored, result.OrderEvidence.IgnoredEntries);
            Assert.IsTrue(result.OrderEvidence.Message.Contains("inactive", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanNamesActiveArchivesMissingFromManagedOrder()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-profile-missing-order-" + Guid.NewGuid().ToString("N"));
        string modsRoot = Path.Combine(root, "mods");
        try
        {
            WriteArchive(modsRoot, "High", "Alpha.archive", "alpha");
            WriteArchive(modsRoot, "Low", "Beta.archive", "beta");
            string orderRoot = Path.Combine(modsRoot, "High", "archive", "pc", "mod");
            File.WriteAllLines(Path.Combine(orderRoot, "modlist.txt"), ["Alpha.archive"]);
            string profile = Path.Combine(root, "profiles", "Standard", "modlist.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
            File.WriteAllText(profile, "+High\n+Low\n");

            Mo2ArchiveProfile result = Mo2ArchiveProfileScanner.ScanInstance(root, profile);

            Assert.AreEqual(ArchiveOrderEvidenceKind.Unresolved, result.OrderEvidence!.Kind);
            string[] missing = ["Beta.archive"];
            CollectionAssert.AreEqual(missing, result.OrderEvidence.MissingEntries);
            Assert.IsTrue(result.OrderEvidence.Message.Contains("Beta.archive", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanUsesTheFirstProviderForSameNameArchiveVfsCollisions()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-profile-vfs-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteArchive(root, "High", "Shared.archive", "high");
            WriteArchive(root, "Low", "Shared.archive", "low!");
            string profile = Path.Combine(root, "modlist.txt");
            File.WriteAllText(profile, "+High\n+Low\n");

            Mo2ArchiveProfile result = Mo2ArchiveProfileScanner.Scan(root, profile);

            Assert.AreEqual("High", result.Archives.Single().Provider);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanPersistsAndReusesUnchangedArchiveFingerprints()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-profile-cache-" + Guid.NewGuid().ToString("N"));
        string cache = Path.Combine(root, "cache", "fingerprints.json");
        Directory.CreateDirectory(root);
        try
        {
            WriteArchive(root, "Alpha", "Alpha.archive", "alpha");
            string profile = Path.Combine(root, "modlist.txt");
            File.WriteAllText(profile, "+Alpha\n");

            Mo2ArchiveProfile first = Mo2ArchiveProfileScanner.Scan(root, profile, cache);
            Mo2ArchiveProfile second = Mo2ArchiveProfileScanner.Scan(root, profile, cache);

            Assert.IsTrue(File.Exists(cache));
            Assert.AreEqual(first.Archives.Single().Sha256, second.Archives.Single().Sha256);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ForcedScanIgnoresAnOtherwiseValidCacheEntry()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-profile-fresh-" + Guid.NewGuid().ToString("N"));
        string cache = Path.Combine(root, "cache", "fingerprints.json");
        Directory.CreateDirectory(root);
        try
        {
            WriteArchive(root, "Alpha", "Alpha.archive", "alpha");
            string profile = Path.Combine(root, "modlist.txt");
            File.WriteAllText(profile, "+Alpha\n");
            Mo2ArchiveProfileScanner.Scan(root, profile, cache);
            string archive = Path.Combine(root, "Alpha", "archive", "pc", "mod", "Alpha.archive");
            DateTime originalWriteTime = File.GetLastWriteTimeUtc(archive);
            File.WriteAllText(archive, "omega");
            File.SetLastWriteTimeUtc(archive, originalWriteTime);

            Mo2ArchiveProfile cached = Mo2ArchiveProfileScanner.Scan(root, profile, cache);
            Mo2ArchiveProfile fresh = Mo2ArchiveProfileScanner.Scan(root, profile, cache, true);

            Assert.AreNotEqual(cached.Archives.Single().Sha256, fresh.Archives.Single().Sha256);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void WriteArchive(string root, string provider, string name, string text)
    {
        string directory = Path.Combine(root, provider, "archive", "pc", "mod");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), text);
    }
}

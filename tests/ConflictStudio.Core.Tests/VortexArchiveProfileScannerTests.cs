using ConflictStudio.Core;
using System.IO;
using System.Security.Cryptography;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class VortexArchiveProfileScannerTests
{
    private static readonly string[] ExpectedOrder = ["Beta.archive", "Shared.archive", "Alpha.archive", "Manual.archive"];

    [TestMethod]
    public void ScanUsesStagingProvidersDeploymentWinnersAndGameOrderFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-archives-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            string alpha = Path.Combine(staging, "Alpha");
            string beta = Path.Combine(staging, "Beta");
            WriteArchive(alpha, "Alpha.archive", "alpha");
            WriteArchive(alpha, "Shared.archive", "alpha shared");
            WriteArchive(beta, "Beta.archive", "beta");
            WriteArchive(beta, "Shared.archive", "beta shared");
            WriteArchive(game, "Manual.archive", "manual");
            string orderPath = Path.Combine(game, "archive", "pc", "mod", "modlist.txt");
            File.WriteAllText(orderPath, $"{ExpectedOrder[0]}\nhelper.archive.xl\n{string.Join('\n', ExpectedOrder[1..])}\n");
            string orderHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(orderPath)));
            Dictionary<string, string> winners = new(StringComparer.OrdinalIgnoreCase) { ["archive\\pc\\mod\\Shared.archive"] = "beta" };
            VortexManagerContext context = new(1, new string('a', 64), DateTimeOffset.UtcNow, "profile", "Standard", game, staging, true, [new("alpha", "Alpha", alpha, 0), new("beta", "Beta", beta, 1)], winners, ExpectedOrder, orderHash);

            Mo2ArchiveProfile profile = VortexArchiveProfileScanner.Scan(context);

            CollectionAssert.AreEqual(ExpectedOrder, profile.EffectiveOrder);
            Assert.AreEqual("Beta", profile.Archives.Single(value => value.ArchiveName == "Shared.archive").Provider);
            Assert.AreEqual(ArchiveOrderEvidenceKind.ManagedModlist, profile.OrderEvidence!.Kind);
            Assert.AreEqual("Vortex", profile.OrderEvidence.Provider);
            Assert.AreEqual(Path.GetFullPath(orderPath), profile.OrderEvidence.SourcePath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanMarksAStaleBridgeOrderUnresolved()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-stale-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            string alpha = Path.Combine(staging, "Alpha");
            WriteArchive(alpha, "Alpha.archive", "alpha");
            string orderPath = Path.Combine(game, "archive", "pc", "mod", "modlist.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(orderPath)!);
            File.WriteAllText(orderPath, "Alpha.archive\n");
            VortexManagerContext context = new(1, new string('a', 64), DateTimeOffset.UtcNow, "profile", "Standard", game, staging, true, [new("alpha", "Alpha", alpha, 0)], [], ["Alpha.archive"], new string('b', 64));

            Mo2ArchiveProfile profile = VortexArchiveProfileScanner.Scan(context);

            Assert.AreEqual(ArchiveOrderEvidenceKind.Unresolved, profile.OrderEvidence!.Kind);
            StringAssert.Contains(profile.OrderEvidence.Message, "changed after Vortex exported");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void WriteArchive(string root, string name, string text)
    {
        string directory = Path.Combine(root, "archive", "pc", "mod");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), text);
    }
}

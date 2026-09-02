using ConflictStudio.Core;
using System.Text;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class RdarResourceScannerTests
{
    private static readonly string[] ArchiveNames = ["Alpha.archive", "zeta.archive"];

    [TestMethod]
    public void ScanReadsEveryArchiveInTheSelectedDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-rdar-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            WriteArchive(Path.Combine(root, "Alpha.archive"), 42, 'a');
            WriteArchive(Path.Combine(root, "zeta.archive"), 42, 'b');

            ResourceProvider[] resources = RdarResourceScanner.Scan(root);

            Assert.AreEqual(2, resources.Length);
            CollectionAssert.AreEquivalent(ArchiveNames, resources.Select(value => value.ArchiveName).ToArray());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanResilientKeepsGoodResourcesAndReportsBadArchives()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-rdar-resilient-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            WriteArchive(Path.Combine(root, "Good.archive"), 42, 'a');
            File.WriteAllText(Path.Combine(root, "Broken.archive"), "not an archive");

            RdarResourceScanResult result = RdarResourceScanner.ScanResilient(root);

            Assert.AreEqual(1, result.Resources.Length);
            Assert.AreEqual("Broken.archive", result.Failures.Single().ArchiveName);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanResilientKeepsProviderIdentityAndUsesResolvedPathIndex()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-rdar-providers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string goodArchive = Path.Combine(root, "Good.archive");
            string badArchive = Path.Combine(root, "Broken.archive");
            WriteArchive(goodArchive, 42, 'a');
            File.WriteAllText(badArchive, "not an archive");

            RdarResourceScanResult result = RdarResourceScanner.ScanResilient(
                [new RdarArchiveInput("Good Mod", goodArchive), new RdarArchiveInput("Broken Mod", badArchive)],
                new Dictionary<ulong, string> { [42] = "base\\gameplay\\effect.mesh" });

            ResourceProvider resource = result.Resources.Single();
            Assert.AreEqual("Good Mod", resource.Provider);
            Assert.AreEqual("base\\gameplay\\effect.mesh", resource.ResourcePath);
            Assert.AreEqual("mesh", resource.ResourceType);
            Assert.AreEqual(ResourcePathConfidence.ResolvedIndex, resource.PathConfidence);
            Assert.AreEqual("Broken Mod", result.Failures.Single().Provider);
            Assert.AreEqual("Broken.archive", result.Failures.Single().ArchiveName);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanResilientSeparatesPhysicalArchiveOwnerFromLogicalRedmodIdentity()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-rdar-logical-provider-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string archive = Path.Combine(root, "Shared.archive");
            WriteArchive(archive, 42, 'a');

            RdarResourceScanResult result = RdarResourceScanner.ScanResilient([new RdarArchiveInput("Physical Mod", archive, "REDmod/Shared/Shared.archive", "REDmod: Shared")]);

            Assert.AreEqual("REDmod: Shared", result.Resources.Single().Provider);
            Assert.AreEqual("REDmod/Shared/Shared.archive", result.Resources.Single().ArchiveName);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void WriteArchive(string path, ulong hash, char payload)
    {
        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream, Encoding.ASCII, true);
        writer.Write(Encoding.ASCII.GetBytes("RDAR"));
        writer.Write(12U);
        writer.Write(44UL);
        writer.Write(84U);
        writer.Write(0UL);
        writer.Write(0U);
        writer.Write(128UL);
        writer.Write(0U);
        writer.Write(8U);
        writer.Write(56U);
        writer.Write(0UL);
        writer.Write(1U);
        writer.Write(0U);
        writer.Write(0U);
        writer.Write(hash);
        writer.Write(0L);
        writer.Write(0U);
        writer.Write(0U);
        writer.Write(0U);
        writer.Write(0U);
        writer.Write(0U);
        writer.Write(Enumerable.Repeat((byte)payload, 20).ToArray());
    }
}

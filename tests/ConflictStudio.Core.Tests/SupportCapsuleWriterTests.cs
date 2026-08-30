using ConflictStudio.Core;
using System.IO;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class SupportCapsuleWriterTests
{
    [TestMethod]
    public void WriteCreatesJsonAndHtmlArtifacts()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-capsule-" + Guid.NewGuid().ToString("N"));
        try
        {
            ConflictCasefile casefile = new(1, "Standard", new DateTimeOffset(2026, 8, 25, 16, 0, 0, TimeSpan.Zero), [], [], [], []);
            SupportEvidence evidence = new([], [], [], [], [], [], [], [], [], null);
            SupportCapsule capsule = new(3, casefile, [], evidence, [], new RuntimeProbeManifest(1, "Standard", DateTimeOffset.UtcNow, []), new SupportCapsuleSummary(0, 0, 0, 0, 0, 0, 0, 0));

            SupportCapsuleWriter.Write(root, capsule);

            Assert.IsTrue(File.Exists(Path.Combine(root, "conflict-casefile.json")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "conflict-casefile.html")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "runtime-probe", "probe-manifest.json")));
            string html = File.ReadAllText(Path.Combine(root, "conflict-casefile.html"));
            Assert.IsTrue(html.Contains("Archive order", StringComparison.Ordinal));
            Assert.IsTrue(html.Contains("Reviewed decisions", StringComparison.Ordinal));
            Assert.IsTrue(html.Contains("ArchiveXL evidence", StringComparison.Ordinal));
            Assert.IsTrue(html.Contains("Archive overview", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

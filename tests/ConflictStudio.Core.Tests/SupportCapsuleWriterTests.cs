using ConflictStudio.Core;
using System.IO;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class SupportCapsuleWriterTests
{
    [TestMethod]
    public void FixRoundFieldDeclarationsSurviveSerializedFindingsAndSupportExport()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-fields-" + Guid.NewGuid().ToString("N"));
        try
        {
            ModSourceInventory inventory = new(
                [new("Alpha", "first.reds", "@addField(PlayerPuppet)\nlet sharedState: Bool;"),
                 new("Alpha", "second.reds", "\n@addField(PlayerPuppet)\nlet sharedState: Int32;")], [], [], []);
            InteractionFinding finding = InteractionReportBuilder.Build(inventory).Single();
            string serialized = System.Text.Json.JsonSerializer.Serialize(finding);
            using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(serialized);
            Assert.IsTrue(json.RootElement.TryGetProperty("DeclarationEvidence", out System.Text.Json.JsonElement declarations), serialized);
            Assert.AreEqual(2, declarations.GetArrayLength());
            Assert.AreEqual("Alpha", declarations[0].GetProperty("Provider").GetString());
            Assert.AreEqual("first.reds", declarations[0].GetProperty("FilePath").GetString());
            Assert.AreEqual(2, declarations[0].GetProperty("Line").GetInt32());
            Assert.AreEqual("Bool", declarations[0].GetProperty("Type").GetString());
            Assert.AreEqual("second.reds", declarations[1].GetProperty("FilePath").GetString());
            Assert.AreEqual(3, declarations[1].GetProperty("Line").GetInt32());
            Assert.AreEqual("Int32", declarations[1].GetProperty("Type").GetString());
            InteractionFinding restored = System.Text.Json.JsonSerializer.Deserialize<InteractionFinding>(serialized)!;
            ProfileScanReceipt receipt = new(1, "Standard", DateTimeOffset.UtcNow, ["Alpha"], [], [], [], [], [restored], [], [], [], [], [], []);
            SupportCapsule capsule = SupportCapsuleBuilder.Build(receipt, []);

            SupportCapsuleWriter.Write(root, capsule);

            string html = File.ReadAllText(Path.Combine(root, "conflict-casefile.html"));
            StringAssert.Contains(html, "first.reds:2");
            StringAssert.Contains(html, "second.reds:3");
            StringAssert.Contains(html, "Bool");
            StringAssert.Contains(html, "Int32");
            string roundTrip = System.Text.Json.JsonSerializer.Serialize(SupportCapsuleWriter.Read(Path.Combine(root, "conflict-casefile.json")).Casefile.Findings.Single());
            Assert.AreEqual(serialized, roundTrip);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

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

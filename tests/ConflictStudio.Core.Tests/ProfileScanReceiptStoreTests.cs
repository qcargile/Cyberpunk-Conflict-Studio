using ConflictStudio.Core;
using System.IO;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class ProfileScanReceiptStoreTests
{
    [TestMethod]
    public void WriteAndReadRoundTripsTheUnifiedReceipt()
    {
        string path = Path.Combine(Path.GetTempPath(), "conflict-studio-receipt-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            ProfileScanReceipt receipt = new(2, "Standard", new DateTimeOffset(2026, 8, 25, 16, 0, 0, TimeSpan.Zero), ["Alpha"], ["Alpha.archive"], [], [], [], [], [], [], [], [], [], []);

            ProfileScanReceiptStore.Write(path, receipt);
            ProfileScanReceipt loaded = ProfileScanReceiptStore.Read(path);

            Assert.AreEqual("Standard", loaded.ProfileName);
            Assert.AreEqual("Alpha", loaded.ActiveProviders.Single());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ReceiptMigratesTheLegacyPayloadFieldToSchemaTwo()
    {
        string path = Path.Combine(Path.GetTempPath(), "conflict-studio-receipt-payload-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            ArchiveResourceOutcome outcome = new(1, "base\\shared.mesh", ArchiveResourceDisposition.Winning, "Alpha.archive", new string('a', 40), "mesh", ResourcePathConfidence.ResolvedIndex, ["Beta.archive"], ArchivePayloadRelation.Different);
            ProfileScanReceipt receipt = new ProfileScanReceipt(2, "Standard", new DateTimeOffset(2026, 8, 25, 16, 0, 0, TimeSpan.Zero), ["Alpha"], ["Alpha.archive"], [], [], [], [], [], [], [], [], [], []) with { ArchiveSummaries = [new ArchiveConflictSummary("Alpha.archive", "Alpha", 0, [outcome], [], [], [], [])] };

            ProfileScanReceiptStore.Write(path, receipt);
            string legacy = File.ReadAllText(path).Replace("\"schemaVersion\": 2", "\"schemaVersion\": 1", StringComparison.Ordinal).Replace("\"payloadFingerprint\"", "\"payloadSha1\"", StringComparison.Ordinal);
            File.WriteAllText(path, legacy);
            ProfileScanReceipt loaded = ProfileScanReceiptStore.Read(path);

            Assert.AreEqual(2, loaded.SchemaVersion);
            Assert.AreEqual(new string('a', 40), loaded.ArchiveSummaries!.Single().Winning.Single().PayloadFingerprint);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void WrongSchemaValueTypeIsReportedAsAnInvalidReceipt()
    {
        string path = Path.Combine(Path.GetTempPath(), "conflict-studio-receipt-schema-type-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{\"schemaVersion\":\"two\"}");

            Assert.ThrowsExactly<ProfileScanReceiptException>(() => ProfileScanReceiptStore.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void SchemaTwoReceiptInfersLegacyArchiveXlFailureKind()
    {
        string path = Path.Combine(Path.GetTempPath(), "conflict-studio-receipt-archivexl-kind-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            ProfileScanReceipt receipt = new(2, "Standard", new DateTimeOffset(2026, 8, 30, 16, 0, 0, TimeSpan.Zero), ["Alpha"], [], [], [], [], [], [], [], [], [], [], [new ArchiveXlSourceFailure("Alpha", "alpha.xl", "Unsupported ArchiveXL root operation 'merge'.", ArchiveXlFailureKind.Coverage)]);
            ProfileScanReceiptStore.Write(path, receipt);
            System.Text.Json.Nodes.JsonObject document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            document["archiveXlFailures"]!.AsArray()[0]!.AsObject().Remove("kind");
            File.WriteAllText(path, document.ToJsonString());

            ProfileScanReceipt loaded = ProfileScanReceiptStore.Read(path);

            Assert.AreEqual(ArchiveXlFailureKind.Coverage, loaded.ArchiveXlFailures.Single().Kind);
            Assert.IsFalse(ConflictWorkQueueBuilder.Build(loaded, []).Any(value => value.Target == "alpha.xl"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

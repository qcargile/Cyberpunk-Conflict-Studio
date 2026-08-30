using ConflictStudio.Core;
using System.Text;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class RdarResourceIndexReaderTests
{
    [TestMethod]
    public void ReadExtractsResourceHashesAndPayloadSha1FromAnRdarIndex()
    {
        string path = Path.Combine(Path.GetTempPath(), "conflict-studio-rdar-" + Guid.NewGuid().ToString("N") + ".archive");
        try
        {
            WriteArchive(path);

            ResourceProvider[] resources = RdarResourceIndexReader.Read(path);

            Assert.AreEqual(2, resources.Length);
            Assert.AreEqual((ulong)42, resources[0].ResourceHash);
            Assert.AreEqual(string.Concat(Enumerable.Repeat("61", 20)), resources[0].PayloadSha1);
            Assert.AreEqual(Path.GetFileName(path), resources[0].ArchiveName);
            Assert.AreEqual(2U, resources[0].SegmentMetadata?.InlineBufferSegmentCount);
            Assert.AreEqual(3U, resources[0].SegmentMetadata?.Start);
            Assert.AreEqual(5U, resources[0].SegmentMetadata?.End);
            Assert.AreEqual(ResourcePathConfidence.Unresolved, resources[0].PathConfidence);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ReadResolvesResourceTypeFromPlainArchiveCustomData()
    {
        string path = Path.Combine(Path.GetTempPath(), "conflict-studio-rdar-custom-" + Guid.NewGuid().ToString("N") + ".archive");
        try
        {
            WriteArchiveWithCustomPath(path);

            ResourceProvider resource = RdarResourceIndexReader.Read(path).Single();

            Assert.AreEqual("base\\ui\\icon.xbm", resource.ResourcePath);
            Assert.AreEqual("xbm", resource.ResourceType);
            Assert.AreEqual(ResourcePathConfidence.ArchiveCustomData, resource.PathConfidence);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ReadTreatsSha1OfEmptyAsUnavailablePayloadEvidence()
    {
        string path = Path.Combine(Path.GetTempPath(), "conflict-studio-rdar-empty-sha-" + Guid.NewGuid().ToString("N") + ".archive");
        try
        {
            WriteArchive(path);
            using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                stream.Position = 236;
                stream.Write(Convert.FromHexString("da39a3ee5e6b4b0d3255bfef95601890afd80709"));
            }

            ResourceProvider resource = RdarResourceIndexReader.Read(path)[0];

            Assert.IsNull(resource.PayloadSha1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void WriteArchive(string path)
    {
        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream, Encoding.ASCII, true);
        writer.Write(Encoding.ASCII.GetBytes("RDAR"));
        writer.Write(12U);
        writer.Write(172UL);
        writer.Write(140U);
        writer.Write(0UL);
        writer.Write(0U);
        writer.Write(312UL);
        writer.Write(0U);
        writer.Write(new byte[128]);
        writer.Write(8U);
        writer.Write(112U);
        writer.Write(0UL);
        writer.Write(2U);
        writer.Write(0U);
        writer.Write(0U);
        WriteRecord(writer, 42, 'a', 2, 3, 5);
        WriteRecord(writer, 84, 'b', 0, 5, 6);
    }

    private static void WriteArchiveWithCustomPath(string path)
    {
        byte[] customPath = Encoding.UTF8.GetBytes("base\\ui\\icon.xbm\0");
        uint customDataLength = checked((uint)(20 + customPath.Length));
        ulong indexPosition = 172 + customDataLength;
        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream, Encoding.ASCII, true);
        writer.Write(Encoding.ASCII.GetBytes("RDAR"));
        writer.Write(12U);
        writer.Write(indexPosition);
        writer.Write(84U);
        writer.Write(0UL);
        writer.Write(0U);
        writer.Write(indexPosition + 84);
        writer.Write(customDataLength);
        writer.Write(new byte[128]);
        writer.Write(0x4C585253U);
        writer.Write(1U);
        writer.Write((uint)customPath.Length);
        writer.Write((uint)customPath.Length);
        writer.Write(1U);
        writer.Write(customPath);
        writer.Write(8U);
        writer.Write(56U);
        writer.Write(0UL);
        writer.Write(1U);
        writer.Write(0U);
        writer.Write(0U);
        WriteRecord(writer, 5530321894750706376, 'a', 0, 0, 1);
    }

    private static void WriteRecord(BinaryWriter writer, ulong hash, char payload, uint inlineBufferSegmentCount, uint segmentStart, uint segmentEnd)
    {
        writer.Write(hash);
        writer.Write(0L);
        writer.Write(inlineBufferSegmentCount);
        writer.Write(segmentStart);
        writer.Write(segmentEnd);
        writer.Write(0U);
        writer.Write(0U);
        writer.Write(Enumerable.Repeat((byte)payload, 20).ToArray());
    }
}

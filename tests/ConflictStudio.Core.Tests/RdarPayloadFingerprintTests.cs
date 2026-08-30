using ConflictStudio.Core;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class RdarPayloadFingerprintTests
{
    [TestMethod]
    public void ApplyHashesTheReconstructedCookedResourceWithoutExtraction()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-payload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string alphaPath = Path.Combine(root, "Alpha.archive");
            string betaPath = Path.Combine(root, "Beta.archive");
            File.WriteAllBytes(alphaPath, [1, 2, 3, 4, 5]);
            File.WriteAllBytes(betaPath, [1, 2, 9, 4, 5]);
            RdarArchivePayloadIndex alpha = new("Alpha.archive", alphaPath, new Dictionary<ulong, RdarResourceStorage> { [42] = new(0, 2) }, [new(0, 3, 3), new(3, 2, 2)]);
            RdarArchivePayloadIndex beta = new("Beta.archive", betaPath, new Dictionary<ulong, RdarResourceStorage> { [42] = new(0, 2) }, [new(0, 3, 3), new(3, 2, 2)]);
            ResourceProvider[] providers = [new("Alpha.archive", 42, "base\\shared.mesh", null), new("Beta.archive", 42, "base\\shared.mesh", null)];

            ResourceProvider[] fingerprinted = RdarPayloadFingerprint.Apply(providers, [alpha, beta], null);

            Assert.IsNotNull(fingerprinted[0].CookedPayloadSha256);
            Assert.AreEqual(64, fingerprinted[0].CookedPayloadSha256!.Length);
            Assert.AreNotEqual(fingerprinted[0].CookedPayloadSha256, fingerprinted[1].CookedPayloadSha256);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ApplyDecodesOnlyTheFirstKarkSegment()
    {
        string path = Path.Combine(Path.GetTempPath(), "conflict-studio-payload-kark-" + Guid.NewGuid().ToString("N") + ".archive");
        try
        {
            byte[] file = new byte[19];
            BinaryPrimitives.WriteUInt32LittleEndian(file, 1263681867);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), 3);
            file[8] = 9;
            file[9] = 9;
            file[10] = 4;
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(11), 1263681867);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(15), 99);
            File.WriteAllBytes(path, file);
            RdarArchivePayloadIndex index = new("Alpha.archive", path, new Dictionary<ulong, RdarResourceStorage> { [42] = new(0, 2) }, [new(0, 10, 3), new(10, 9, 99)]);
            ResourceProvider[] providers = [new("Alpha.archive", 42, "base\\shared.mesh", null), new("Beta.archive", 42, "base\\shared.mesh", null)];

            ResourceProvider[] result = RdarPayloadFingerprint.ApplyWithDecoder(providers, [index], (compressed, size) => size == 3 && compressed.SequenceEqual(new byte[] { 9, 9 }) ? [1, 2, 3] : throw new AssertFailedException("Unexpected decode request."));

            Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(new byte[] { 1, 2, 3, 4, 75, 65, 82, 75, 99, 0, 0, 0 })), result[0].CookedPayloadSha256);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ApplyLeavesPayloadUnavailableWithoutAFirstSegmentDecoder()
    {
        string path = Path.Combine(Path.GetTempPath(), "conflict-studio-payload-no-oodle-" + Guid.NewGuid().ToString("N") + ".archive");
        try
        {
            byte[] file = new byte[9];
            BinaryPrimitives.WriteUInt32LittleEndian(file, 1263681867);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), 3);
            file[8] = 9;
            File.WriteAllBytes(path, file);
            RdarArchivePayloadIndex index = new("Alpha.archive", path, new Dictionary<ulong, RdarResourceStorage> { [42] = new(0, 1) }, [new(0, 9, 3)]);
            ResourceProvider[] providers = [new("Alpha.archive", 42, "base\\shared.mesh", null), new("Beta.archive", 42, "base\\shared.mesh", null)];

            ResourceProvider[] result = RdarPayloadFingerprint.ApplyWithDecoder(providers, [index], null);

            Assert.IsNull(result[0].CookedPayloadSha256);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ApplyRejectsOverflowingAndTruncatedSegments()
    {
        string path = Path.Combine(Path.GetTempPath(), "conflict-studio-payload-invalid-" + Guid.NewGuid().ToString("N") + ".archive");
        try
        {
            byte[] truncatedKark = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(truncatedKark, 1263681867);
            File.WriteAllBytes(path, truncatedKark);
            ResourceProvider[] providers = [new("Overflow.archive", 42, "base\\overflow.mesh", null), new("Truncated.archive", 84, "base\\truncated.mesh", null), new("Other.archive", 42, "base\\overflow.mesh", null), new("OtherTwo.archive", 84, "base\\truncated.mesh", null)];
            RdarArchivePayloadIndex overflow = new("Overflow.archive", path, new Dictionary<ulong, RdarResourceStorage> { [42] = new(0, 1) }, [new(ulong.MaxValue, 4, 4)]);
            RdarArchivePayloadIndex truncated = new("Truncated.archive", path, new Dictionary<ulong, RdarResourceStorage> { [84] = new(0, 1) }, [new(0, 4, 10)]);

            ResourceProvider[] result = RdarPayloadFingerprint.ApplyWithDecoder(providers, [overflow, truncated], (_, _) => throw new AssertFailedException("Malformed KARK must not be decoded."));

            Assert.IsNull(result.Single(value => value.ArchiveName == "Overflow.archive").CookedPayloadSha256);
            Assert.IsNull(result.Single(value => value.ArchiveName == "Truncated.archive").CookedPayloadSha256);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

using ConflictStudio.Core;
using System.Text;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class ResourcePathIndexTests
{
    [TestMethod]
    public void HashUsesFNV1a64()
    {
        Assert.AreEqual(0xa430d84680aabd0bUL, ResourcePathHash.Compute("hello"));
    }

    [TestMethod]
    public void ResolveLinesReturnsOnlyRequestedResourcePaths()
    {
        string wanted = "base\\gameplay\\wanted.mesh";
        string ignored = "base\\gameplay\\ignored.ent";
        byte[] lines = Encoding.UTF8.GetBytes(wanted + "\n" + ignored + "\n");

        Dictionary<ulong, string> resolved = ResourcePathIndex.ResolveLines(lines, new HashSet<ulong> { ResourcePathHash.Compute(wanted) });

        Assert.AreEqual(wanted, resolved.Single().Value);
    }

    [TestMethod]
    public void ResolveUsesTheAuthoritativeVortexWinnerForUsedHashes()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-usedhashes-winner-" + Guid.NewGuid().ToString("N"));
        try
        {
            string alpha = WriteIndex(root, "Alpha");
            string beta = WriteIndex(root, "Beta");
            Mo2InstancePaths paths = new(root, root, string.Empty, string.Empty, null, "Vortex");
            DeploymentProvider[] providers = [new("Alpha", Path.Combine(root, "Alpha"), null, "alpha"), new("Beta", Path.Combine(root, "Beta"), null, "beta")];
            Dictionary<string, string> winners = new(StringComparer.OrdinalIgnoreCase) { ["bin\\x64\\plugins\\cyber_engine_tweaks\\tweakdb\\usedhashes.kark"] = "beta" };

            ResourcePathIndexResult result = ResourcePathIndex.Resolve(paths, providers, new HashSet<ulong> { 1 }, winners);

            Assert.AreEqual("Beta", result.Evidence.Provider);
            Assert.AreEqual(beta, result.Evidence.SourcePath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ResolveDoesNotFallBackWhenTheAuthoritativeIndexIsExcluded()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-usedhashes-excluded-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteIndex(root, "Alpha");
            string beta = WriteIndex(root, "Beta");
            Mo2InstancePaths paths = new(root, root, string.Empty, string.Empty, null, "Vortex");
            DeploymentProvider[] providers = [new("Alpha", Path.Combine(root, "Alpha"), null, "alpha"), new("Beta", Path.Combine(root, "Beta"), null, "beta")];
            Dictionary<string, string> winners = new(StringComparer.OrdinalIgnoreCase) { ["bin\\x64\\plugins\\cyber_engine_tweaks\\tweakdb\\usedhashes.kark"] = "beta" };

            ResourcePathIndexResult result = ResourcePathIndex.Resolve(paths, providers, new HashSet<ulong> { 1 }, winners, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { beta });

            Assert.AreEqual(ResourcePathIndexState.Unavailable, result.Evidence.State);
            Assert.AreEqual("Beta", result.Evidence.Provider);
            StringAssert.Contains(result.Evidence.Message, "excluded");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ResolveReservesTheIdentityOfADisappearedHighPriorityIndex()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-missing-usedhashes-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteIndex(root, "Middle");
            WriteIndex(root, "Low");
            string disappeared = Path.Combine(root, "High", "bin", "x64", "plugins", "cyber_engine_tweaks", "tweakdb", "usedhashes.kark");
            Mo2InstancePaths paths = new(root, root, string.Empty, string.Empty, null, "MO2");
            DeploymentProvider[] providers = [new("High", Path.Combine(root, "High")), new("Middle", Path.Combine(root, "Middle")), new("Low", Path.Combine(root, "Low"))];

            ResourcePathIndexResult result = ResourcePathIndex.Resolve(paths, providers, new HashSet<ulong> { 1 }, null, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { disappeared });

            Assert.AreEqual("High", result.Evidence.Provider);
            Assert.AreEqual(disappeared, result.Evidence.SourcePath);
            StringAssert.Contains(result.Evidence.Message, "excluded");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ResolveDoesNotPromoteTwoLosersWhenTheDeployedWinnerIsLocked()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-locked-usedhashes-winner-" + Guid.NewGuid().ToString("N"));
        try
        {
            string high = WriteIndex(root, "High");
            WriteIndex(root, "Middle");
            WriteIndex(root, "Low");
            string oodle = Path.Combine(root, "bin", "x64", "oo2ext_7_win64.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(oodle)!);
            File.WriteAllText(oodle, "unused");
            Mo2InstancePaths paths = new(root, root, string.Empty, string.Empty, root, "Vortex");
            DeploymentProvider[] providers = [new("High", Path.Combine(root, "High"), null, "high"), new("Middle", Path.Combine(root, "Middle"), null, "middle"), new("Low", Path.Combine(root, "Low"), null, "low")];
            Dictionary<string, string> winners = new(StringComparer.OrdinalIgnoreCase) { ["bin\\x64\\plugins\\cyber_engine_tweaks\\tweakdb\\usedhashes.kark"] = "high" };
            using FileStream locked = File.Open(high, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            ResourcePathIndexResult result = ResourcePathIndex.Resolve(paths, providers, new HashSet<ulong> { 1 }, winners);

            Assert.AreEqual(ResourcePathIndexState.Failed, result.Evidence.State);
            Assert.AreEqual("High", result.Evidence.Provider);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string WriteIndex(string root, string provider)
    {
        string path = Path.Combine(root, provider, "bin", "x64", "plugins", "cyber_engine_tweaks", "tweakdb", "usedhashes.kark");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, provider);
        return path;
    }
}

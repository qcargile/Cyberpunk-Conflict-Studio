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
}

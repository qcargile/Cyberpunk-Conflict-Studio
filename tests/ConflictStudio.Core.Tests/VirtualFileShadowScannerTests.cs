using ConflictStudio.Core;
using System.IO;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class VirtualFileShadowScannerTests
{
    [TestMethod]
    public void ScanReportsTheFirstHighPriorityProviderAsWinner()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-shadows-" + Guid.NewGuid().ToString("N"));
        try
        {
            Write(root, "Alpha", "r6\\scripts\\shared.reds", "alpha");
            Write(root, "Beta", "r6\\scripts\\shared.reds", "beta");

            VirtualFileShadow shadow = VirtualFileShadowScanner.Scan(root, ["Alpha", "Beta"]).Single();

            Assert.AreEqual("Alpha", shadow.WinnerProvider);
            Assert.AreEqual(VirtualFileRelation.Different, shadow.Relation);
            Assert.AreEqual("r6\\scripts\\shared.reds", shadow.RelativePath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanUsesTheAuthoritativeVortexDeploymentWinner()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-shadows-" + Guid.NewGuid().ToString("N"));
        string alpha = Path.Combine(root, "Alpha");
        string beta = Path.Combine(root, "Beta");
        try
        {
            Write(alpha, string.Empty, "r6\\scripts\\shared.reds", "alpha");
            Write(beta, string.Empty, "r6\\scripts\\shared.reds", "beta");
            Dictionary<string, string> winners = new(StringComparer.OrdinalIgnoreCase) { ["r6\\scripts\\shared.reds"] = "beta" };

            VirtualFileShadow shadow = VirtualFileShadowScanner.ScanProviders([new DeploymentProvider("Alpha", alpha, null, "alpha"), new DeploymentProvider("Beta", beta, null, "beta")], winners).Single();

            Assert.AreEqual("Beta", shadow.WinnerProvider);
            Assert.AreEqual("beta", shadow.Providers[0].Provider, ignoreCase: true);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void Write(string root, string provider, string relative, string text)
    {
        string path = Path.Combine(root, provider, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}

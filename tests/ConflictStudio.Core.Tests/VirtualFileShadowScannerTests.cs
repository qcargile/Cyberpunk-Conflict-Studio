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

    [TestMethod]
    public void ScanIgnoresMutableRuntimeOutputs()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-mutable-shadows-" + Guid.NewGuid().ToString("N"));
        try
        {
            Write(root, "Alpha", "red4ext\\plugins\\Tool\\runtime.log", "alpha");
            Write(root, "Beta", "red4ext\\plugins\\Tool\\runtime.log", "beta");
            Write(root, "Alpha", "bin\\x64\\plugins\\Tool\\state.sqlite3", "alpha");
            Write(root, "Beta", "bin\\x64\\plugins\\Tool\\state.sqlite3", "beta");

            VirtualFileShadow[] shadows = VirtualFileShadowScanner.Scan(root, ["Alpha", "Beta"]);

            Assert.HasCount(0, shadows);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanResilientRetainsOtherShadowsAndNamesAnUnreadableFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-shadow-failure-" + Guid.NewGuid().ToString("N"));
        try
        {
            Write(root, "Alpha", "r6\\scripts\\broken.reds", "alpha");
            Write(root, "Beta", "r6\\scripts\\broken.reds", "beta");
            Write(root, "Alpha", "r6\\scripts\\good.reds", "same");
            Write(root, "Beta", "r6\\scripts\\good.reds", "same");
            string broken = Path.Combine(root, "Alpha", "r6", "scripts", "broken.reds");
            using FileStream locked = File.Open(broken, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            VirtualFileShadowScanResult result = VirtualFileShadowScanner.ScanProvidersResilient([new DeploymentProvider("Alpha", Path.Combine(root, "Alpha")), new DeploymentProvider("Beta", Path.Combine(root, "Beta"))]);

            Assert.AreEqual("r6\\scripts\\good.reds", result.Shadows.Single().RelativePath);
            Assert.AreEqual("Alpha", result.Failures.Single().Provider);
            Assert.AreEqual(broken, result.Failures.Single().FilePath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanResilientExcludesAnUnboundShadowWithoutUsingTheRemainingProvider()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-shadow-excluded-" + Guid.NewGuid().ToString("N"));
        try
        {
            Write(root, "Alpha", "r6\\scripts\\excluded.reds", "alpha");
            Write(root, "Beta", "r6\\scripts\\excluded.reds", "beta");
            Write(root, "Alpha", "r6\\scripts\\other.reds", "alpha other");
            Write(root, "Beta", "r6\\scripts\\other.reds", "beta other");
            string excluded = Path.Combine(root, "Alpha", "r6", "scripts", "excluded.reds");

            VirtualFileShadowScanResult result = VirtualFileShadowScanner.ScanProvidersResilient([new DeploymentProvider("Alpha", Path.Combine(root, "Alpha")), new DeploymentProvider("Beta", Path.Combine(root, "Beta"))], null, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { excluded });

            Assert.AreEqual("r6\\scripts\\other.reds", result.Shadows.Single().RelativePath);
            Assert.IsFalse(result.Shadows.Any(value => value.RelativePath == "r6\\scripts\\excluded.reds"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanResilientReservesTheIdentityOfADisappearedHighPriorityFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-missing-shadow-" + Guid.NewGuid().ToString("N"));
        try
        {
            Write(root, "Middle", "r6\\scripts\\shared.reds", "middle");
            Write(root, "Low", "r6\\scripts\\shared.reds", "low");
            Write(root, "Middle", "r6\\scripts\\other.reds", "middle other");
            Write(root, "Low", "r6\\scripts\\other.reds", "low other");
            string disappeared = Path.Combine(root, "High", "r6", "scripts", "shared.reds");

            VirtualFileShadowScanResult result = VirtualFileShadowScanner.ScanProvidersResilient([new DeploymentProvider("High", Path.Combine(root, "High")), new DeploymentProvider("Middle", Path.Combine(root, "Middle")), new DeploymentProvider("Low", Path.Combine(root, "Low"))], null, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { disappeared });

            Assert.AreEqual("r6\\scripts\\other.reds", result.Shadows.Single().RelativePath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanResilientDoesNotPromoteTwoLosersWhenTheDeployedWinnerIsLocked()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-locked-shadow-winner-" + Guid.NewGuid().ToString("N"));
        try
        {
            Write(root, "High", "r6\\scripts\\shared.reds", "high");
            Write(root, "Middle", "r6\\scripts\\shared.reds", "middle");
            Write(root, "Low", "r6\\scripts\\shared.reds", "low");
            Write(root, "Middle", "r6\\scripts\\other.reds", "middle other");
            Write(root, "Low", "r6\\scripts\\other.reds", "low other");
            string lockedPath = Path.Combine(root, "High", "r6", "scripts", "shared.reds");
            Dictionary<string, string> winners = new(StringComparer.OrdinalIgnoreCase) { ["r6\\scripts\\shared.reds"] = "high", ["r6\\scripts\\other.reds"] = "middle" };
            using FileStream locked = File.Open(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            VirtualFileShadowScanResult result = VirtualFileShadowScanner.ScanProvidersResilient([new DeploymentProvider("High", Path.Combine(root, "High"), null, "high"), new DeploymentProvider("Middle", Path.Combine(root, "Middle"), null, "middle"), new DeploymentProvider("Low", Path.Combine(root, "Low"), null, "low")], winners);

            Assert.AreEqual("r6\\scripts\\other.reds", result.Shadows.Single().RelativePath);
            Assert.AreEqual(lockedPath, result.Failures.Single().FilePath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanResilientDoesNotPromoteALoserWhenTheAuthoritativeWinnerIsMissing()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-shadow-missing-winner-" + Guid.NewGuid().ToString("N"));
        try
        {
            Write(root, "Alpha", "r6\\scripts\\shared.reds", "alpha");
            Write(root, "Beta", "r6\\scripts\\shared.reds", "beta");
            Dictionary<string, string> winners = new(StringComparer.OrdinalIgnoreCase) { ["r6\\scripts\\shared.reds"] = "missing" };

            VirtualFileShadowScanResult result = VirtualFileShadowScanner.ScanProvidersResilient([new DeploymentProvider("Alpha", Path.Combine(root, "Alpha"), null, "alpha"), new DeploymentProvider("Beta", Path.Combine(root, "Beta"), null, "beta")], winners);

            Assert.HasCount(0, result.Shadows);
            Assert.IsTrue(result.Failures.Any(value => value.Surface == "Virtual file winner" && value.FilePath == "r6\\scripts\\shared.reds"));
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

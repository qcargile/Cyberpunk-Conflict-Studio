using ConflictStudio.Core;
using System.IO;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class Mo2ProfileDiscoveryTests
{
    [TestMethod]
    public void ProfileDisplaysItsNameInsteadOfRecordDebugText()
    {
        Mo2Profile profile = new("Chrome & Blood - Standard Profile", "modlist.txt");

        Assert.AreEqual("Chrome & Blood - Standard Profile", profile.ToString());
    }
    [TestMethod]
    public void DiscoverReturnsOnlyProfileDirectoriesWithAModlist()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-mo2-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "profiles", "Standard"));
            Directory.CreateDirectory(Path.Combine(root, "profiles", "Empty"));
            File.WriteAllText(Path.Combine(root, "profiles", "Standard", "modlist.txt"), "+Alpha");

            Mo2Profile[] profiles = Mo2ProfileDiscovery.Discover(root);

            Assert.AreEqual(1, profiles.Length);
            Assert.AreEqual("Standard", profiles[0].Name);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

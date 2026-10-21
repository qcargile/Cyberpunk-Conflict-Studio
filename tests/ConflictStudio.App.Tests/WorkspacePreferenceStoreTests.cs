using ConflictStudio.App;
using ConflictStudio.Core;
using System.IO;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class WorkspacePreferenceStoreTests
{
    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"profileName\":\"Old\"}")]
    [DataRow("{\"mo2Root\":\"D:\\\\MO2\"}")]
    [DataRow("{\"mo2Root\":\" \",\"profileName\":\"Old\"}")]
    [DataRow("{\"mo2Root\":\"D:\\\\MO2\",\"profileName\":\"Old\",\"managerKind\":99}")]
    public void IncompletePreferencesAreIgnored(string json)
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-invalid-preferences-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "workspace.json"), json);

            Assert.IsNull(new WorkspacePreferenceStore(root).Load());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    [DataRow(ModManagerKind.Mo2)]
    [DataRow(ModManagerKind.Vortex)]
    [DataRow(ModManagerKind.Manual)]
    public void StoreRoundTripsLastInstallationAndProfile(ModManagerKind manager)
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-preferences-" + Guid.NewGuid().ToString("N"));
        try
        {
            WorkspacePreferenceStore store = new(root);
            WorkspacePreference expected = new("D:\\MO2", "Standard", manager);

            store.Save(expected);

            Assert.AreEqual(expected, store.Load());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ConstructorAndLoadTolerateAnUnavailableRoot()
    {
        WorkspacePreferenceStore store = new("Z:\\missing-drive\\conflict-studio");

        Assert.IsNull(store.Load());
        Assert.IsFalse(store.TrySave(new WorkspacePreference("D:\\MO2", "Standard")));
    }
}

using ConflictStudio.App;
using System.IO;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class WorkspacePreferenceStoreTests
{
    [TestMethod]
    public void StoreRoundTripsLastInstallationAndProfile()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-preferences-" + Guid.NewGuid().ToString("N"));
        try
        {
            WorkspacePreferenceStore store = new(root);
            WorkspacePreference expected = new("D:\\MO2", "Standard");

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

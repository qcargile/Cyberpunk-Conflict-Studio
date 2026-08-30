using ConflictStudio.App;
using System.IO;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class Mo2ProfileWorkspaceViewModelTests
{
    [TestMethod]
    public void DiscoverProfilesExposesTheirModlistPaths()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-mo2-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "profiles", "Standard"));
            File.WriteAllText(Path.Combine(root, "profiles", "Standard", "modlist.txt"), "+Alpha");
            Mo2ProfileWorkspaceViewModel viewModel = new();

            viewModel.Discover(root);

            Assert.AreEqual(1, viewModel.Profiles.Count);
            Assert.AreEqual("Standard", viewModel.Profiles[0].Name);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void DiscoverSelectsTheProfileConfiguredByMo2()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-mo2-active-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "profiles", "Immersive"));
            Directory.CreateDirectory(Path.Combine(root, "profiles", "Standard"));
            File.WriteAllText(Path.Combine(root, "profiles", "Immersive", "modlist.txt"), string.Empty);
            File.WriteAllText(Path.Combine(root, "profiles", "Standard", "modlist.txt"), string.Empty);
            File.WriteAllText(Path.Combine(root, "ModOrganizer.ini"), "selected_profile=@ByteArray(Standard)\n");
            Mo2ProfileWorkspaceViewModel viewModel = new();

            viewModel.Discover(root);

            Assert.AreEqual("Standard", viewModel.SelectedProfile?.Name);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

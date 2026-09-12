using ConflictStudio.App;
using ConflictStudio.Core;
using System.IO;
using System.Text.Json;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class SourceFileLocationsTests
{
    [TestMethod]
    public void SelectedFilesFollowTheCaseAndClearWithoutStaleActions()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                MainWindow window = new();
                try
                {
                    ProfileScanReceipt receipt = Receipt() with { SourceProviders = [new("Alpha", @"D:\Mods\Alpha")] };
                    typeof(MainWindow).GetField("_receipt", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, receipt);
                    typeof(MainWindow).GetField("_sourceFileLocations", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, new SourceFileLocations(receipt));
                    DataGrid table = (DataGrid)window.FindName("WorkQueueDataGrid");
                    ComboBox files = (ComboBox)window.FindName("SelectedFileComboBox");
                    Button open = (Button)window.FindName("OpenSourceFileButton");
                    TextBox location = (TextBox)window.FindName("SelectedFilePathTextBox");
                    ConflictWorkItem first = Item("Alpha", "r6\\scripts\\combat.reds");
                    ConflictWorkItem missing = Item("Missing", "init.lua");
                    table.ItemsSource = new[] { first, missing };
                    table.SelectedItem = first;
                    Assert.AreEqual("combat.reds (Alpha)", files.SelectedItem.ToString());
                    Assert.AreEqual(@"D:\Mods\Alpha\r6\scripts\combat.reds", location.Text);
                    Assert.IsTrue(open.IsEnabled);
                    table.SelectedItem = missing;
                    Assert.IsFalse(open.IsEnabled);
                    StringAssert.Contains(location.Text, "Scan the profile again");
                    table.SelectedItems.Add(first);
                    Assert.AreEqual(2, files.Items.Count);
                    table.SelectedItems.Clear();
                    Assert.AreEqual(0, files.Items.Count);
                    Assert.IsFalse(open.IsEnabled);
                    Assert.AreEqual(Visibility.Collapsed, ((StackPanel)window.FindName("SelectedFilesPanel")).Visibility);
                }
                finally { window.Close(); }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null) throw failure;
    }

    [TestMethod]
    public void LocationsUseScannedProviderRootsForEveryManager()
    {
        DeploymentProvider[] providers = [new("Alpha", @"D:\MO2\mods\Alpha"), new("Overwrite", @"E:\MO2 overwrite"), new("Game directory", @"D:\Games\Cyberpunk"), new("REDmod: Example", @"D:\Games\Cyberpunk\mods\Example"), new("Vortex provider", @"E:\Vortex staging\mod-123")];
        SourceFileLocations locations = new(Receipt() with { SourceProviders = providers });
        ConflictWorkItem[] items = providers.Select(value => Item(value.Name, "r6/scripts/combat.reds")).ToArray();

        SourceFileLocation[] files = locations.ForItems(items);

        Assert.AreEqual(5, files.Length);
        foreach (DeploymentProvider provider in providers) Assert.AreEqual(Path.Combine(provider.RootPath, "r6\\scripts\\combat.reds"), files.Single(value => value.Provider == provider.Name).PhysicalPath);
        Assert.AreEqual(5, locations.ForItems(items.Concat(items)).Length);
        Assert.IsEmpty(locations.ForItems([]));
    }

    [TestMethod]
    public void RecordedVirtualPathsTakePriorityWithoutUsingAnotherProvidersCopy()
    {
        ProfileScanReceipt receipt = Receipt() with
        {
            SourceProviders = [new("Alpha", @"D:\wrong root")],
            VirtualFileShadows = [new("r6\\scripts\\combat.reds", "Alpha", VirtualFileRelation.Different, [new("Alpha", @"E:\captured copy\combat.reds", 1, new string('a', 64), 0)])]
        };
        SourceFileLocations locations = new(receipt);

        Assert.AreEqual(@"E:\captured copy\combat.reds", locations.ForItems([Item("Alpha", "r6/scripts/combat.reds")]).Single().PhysicalPath);
        Assert.IsNull(locations.ForItems([Item("Beta", "r6/scripts/combat.reds")]).Single().PhysicalPath);
    }

    [TestMethod]
    [DataRow("../outside.reds")]
    [DataRow("../Alpha-other/outside.reds")]
    [DataRow("C:/outside.reds")]
    public void SourcePathsCannotEscapeTheirProvider(string path)
    {
        SourceFileLocations locations = new(Receipt() with { SourceProviders = [new("Alpha", @"D:\mods\Alpha")] });

        Assert.IsNull(locations.ForItems([Item("Alpha", path)]).Single().PhysicalPath);
    }

    [TestMethod]
    public void MissingFilesRequireRescanAndSavedReceiptsDoNotLeakProviderRoots()
    {
        string directory = Path.Combine(Path.GetTempPath(), "conflict-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "combat.reds");
            File.WriteAllText(path, "source");
            ProfileScanReceipt receipt = Receipt() with { SourceProviders = [new("Alpha", directory)] };
            SourceFileLocation file = new SourceFileLocations(receipt).ForItems([Item("Alpha", "combat.reds")]).Single();
            Assert.AreEqual(path, SourceFileLocations.ExistingPath(file));
            File.Delete(path);
            Assert.Throws<FileNotFoundException>(() => SourceFileLocations.ExistingPath(file));
            string json = JsonSerializer.Serialize(receipt);
            Assert.IsFalse(json.Contains(directory, StringComparison.Ordinal));
            ProfileScanReceipt restored = JsonSerializer.Deserialize<ProfileScanReceipt>(json)!;
            Assert.IsNull(new SourceFileLocations(restored).ForItems([Item("Alpha", "combat.reds")]).Single().PhysicalPath);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static ConflictWorkItem Item(string provider, string path)
        => new(ConflictSurface.ScriptAndTweak, "Target", EvidenceClassification.Exclusive, ConflictWorkState.NeedsAttention, "", "", null, [provider], new string('a', 64)) { SourceFiles = [new(provider, path)] };

    private static ProfileScanReceipt Receipt()
        => new(2, "Standard", DateTimeOffset.UtcNow, [], [], [], [], [], [], [], [], [], [], [], []);
}

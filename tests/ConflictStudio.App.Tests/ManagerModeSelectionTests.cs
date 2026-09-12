using ConflictStudio.App;
using System.IO;
using System.Reflection;
using System.Windows.Controls;

namespace ConflictStudio.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ManagerModeSelectionTests
{
    [TestMethod]
    [DataRow("", true)]
    [DataRow("", false)]
    [DataRow("   ", true)]
    [DataRow("missing", true)]
    public void ManualSelectionWithoutAnEnteredGameRootPromptsInsteadOfUsingWorkingDirectory(string path, bool archiveMarker)
    {
        WithGameDirectory(archiveMarker, (window, _) =>
        {
            Get<TextBox>(window, "Mo2RootTextBox").Text = path;
            Select(window, "Manual");
            Assert.AreEqual("CYBERPUNK INSTALLATION", Get<TextBlock>(window, "ManagerLocationLabel").Text);
            Assert.IsNull(Get<ComboBox>(window, "ProfileComboBox").SelectedItem);
            StringAssert.Contains(Get<TextBlock>(window, "WorkspaceStatusTextBlock").Text, "Choose the Cyberpunk installation folder");
            Select(window, "Mo2");
            Select(window, "Manual");
            Assert.IsNull(Get<ComboBox>(window, "ProfileComboBox").SelectedItem);
        });
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ExplicitGameRootStillSelectsTheManualProfile(bool relative)
    {
        WithGameDirectory(true, (window, root) =>
        {
            Get<TextBox>(window, "Mo2RootTextBox").Text = relative ? "." : root;
            Select(window, "Manual");
            ManualProfileOption option = (ManualProfileOption)Get<ComboBox>(window, "ProfileComboBox").SelectedItem;
            Assert.AreEqual(root, option.GameRoot);
        });
    }

    private static T Get<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);

    private static void Select(MainWindow window, string kind)
    {
        ComboBox manager = Get<ComboBox>(window, "ManagerModeComboBox");
        manager.SelectedItem = manager.Items.OfType<ComboBoxItem>().Single(item => string.Equals(item.Tag?.ToString(), kind, StringComparison.OrdinalIgnoreCase));
    }

    private static void WithGameDirectory(bool archiveMarker, Action<MainWindow, string> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-manual-selection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        if (archiveMarker) Directory.CreateDirectory(Path.Combine(root, "archive", "pc", "content"));
        else
        {
            Directory.CreateDirectory(Path.Combine(root, "bin", "x64"));
            File.WriteAllText(Path.Combine(root, "bin", "x64", "Cyberpunk2077.exe"), string.Empty);
        }
        string previousDirectory = Environment.CurrentDirectory;
        Exception? failure = null;
        Thread thread = new(() =>
        {
            MainWindow? window = null;
            try
            {
                Environment.CurrentDirectory = root;
                window = new MainWindow(Path.Combine(root, "state"));
                typeof(MainWindow).GetField("_restoringWorkspace", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                action(window, root);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); Environment.CurrentDirectory = previousDirectory; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool completed = thread.Join(TimeSpan.FromSeconds(20));
        if (completed) Directory.Delete(root, true);
        Assert.IsTrue(completed);
        if (failure is not null) throw new AssertFailedException(failure.ToString(), failure);
    }
}

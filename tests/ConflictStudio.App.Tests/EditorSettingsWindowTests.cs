using ConflictStudio.App;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class SourceEditorSettingsWindowTests
{
    [TestMethod]
    public void DialogLoadsTheSavedChoiceAndDisablesExecutableForNormalOpen()
    {
        Run(() =>
        {
            string root = Path.Combine(Path.GetTempPath(), "conflict-studio-editor-window-" + Guid.NewGuid().ToString("N"));
            try
            {
                SourceEditorPreferenceStore store = new(root);
                store.TrySave(new SourceEditorPreference(SourceEditorKind.NotepadPlusPlus, @"C:\Tools\notepad++.exe"));
                SourceEditorSettingsWindow window = new(store);
                try
                {
                    Assert.AreEqual(SourceEditorKind.NotepadPlusPlus, ((ComboBoxItem)((ComboBox)window.FindName("EditorKindComboBox")).SelectedItem).Tag);
                    Assert.AreEqual(@"C:\Tools\notepad++.exe", ((TextBox)window.FindName("EditorExecutableTextBox")).Text);
                    ((ComboBox)window.FindName("EditorKindComboBox")).SelectedIndex = 0;
                    Assert.IsFalse(((TextBox)window.FindName("EditorExecutableTextBox")).IsEnabled);
                    Assert.IsFalse(((Button)window.FindName("BrowseEditorButton")).IsEnabled);
                }
                finally { window.Close(); }
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
    }

    private static void Run(Action work)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(() =>
            {
                try { work(); }
                catch (Exception exception) { failure = exception; }
                finally { dispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(15)), "Editor settings test timed out.");
        if (failure is not null) throw failure;
    }
}

using ConflictStudio.App;
using ConflictStudio.Core;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ConflictStudio.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class VortexSetupTests
{
    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void ExplicitMo2SelectorsOverrideSavedVortex(bool explicitRoot, bool explicitProfile)
    {
        WithWindow((window, root) =>
        {
            string mo2 = Path.Combine(root, "mo2");
            Directory.CreateDirectory(Path.Combine(mo2, "profiles", "Current"));
            File.WriteAllText(Path.Combine(mo2, "profiles", "Current", "modlist.txt"), string.Empty);
            File.WriteAllText(Path.Combine(mo2, "ModOrganizer.ini"), "selected_profile=@ByteArray(Current)\n");
            string contextPath = Path.Combine(root, "bridge", "context.json");
            new WorkspacePreferenceStore(Path.Combine(root, "state")).Save(new WorkspacePreference(root, "Old", ModManagerKind.Vortex, contextPath));
            ApplicationLaunchOptions options = new(null, explicitRoot ? mo2 : null, explicitProfile ? "Current" : null, null);
            string previousDirectory = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = explicitRoot ? root : mo2;
                Task restore = (Task)typeof(MainWindow).GetMethod("RestoreWorkspaceAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [options])!;
                PumpUntil(() => restore.IsCompleted && HistoryIdle(window));
                restore.GetAwaiter().GetResult();

                Assert.IsInstanceOfType<Mo2Profile>(Get<ComboBox>(window, "ProfileComboBox").SelectedItem);
                Assert.AreEqual(mo2, Get<TextBox>(window, "Mo2RootTextBox").Text);
                Assert.IsFalse(File.Exists(Path.Combine(root, "bridge", "context-request.json")));
            }
            finally { Environment.CurrentDirectory = previousDirectory; }
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SelectingVortexRefreshesAMissingOrInvalidSavedContext(bool invalidContext)
    {
        WithWindow((window, root) =>
        {
            string contextPath = invalidContext ? Path.Combine(root, "bridge", "context.json") : Path.Combine(root, "state", "vortex", "context.json");
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(game);
            Directory.CreateDirectory(staging);
            if (invalidContext)
            {
                new WorkspacePreferenceStore(Path.Combine(root, "state")).Save(new WorkspacePreference(game, "Old", ModManagerKind.Vortex, contextPath));
                Directory.CreateDirectory(Path.GetDirectoryName(contextPath)!);
                File.WriteAllText(contextPath, "not valid json");
            }
            VortexContextBridgeStore bridge = new(Path.GetDirectoryName(contextPath)!);
            VortexContextRefreshRequest? request = null;
            DispatcherTimer responder = new() { Interval = TimeSpan.FromMilliseconds(20) };
            responder.Tick += (_, _) =>
            {
                request = bridge.ReadRequest();
                if (request is null) return;
                responder.Stop();
                VortexManagerContext pending = new(1, string.Empty, DateTimeOffset.UtcNow, "profile", "Current", game, staging, true, [], [], [], null, DeploymentInventoryComplete: true);
                VortexManagerContext context = pending with { ContextId = VortexManagerContextStore.ComputeContextId(pending) };
                File.WriteAllText(contextPath, JsonSerializer.Serialize(context));
                bridge.WriteResponse(new VortexContextRefreshResponse(1, request.RequestId, true, string.Empty, context.ContextId, DateTimeOffset.UtcNow));
            };
            responder.Start();
            if (invalidContext) typeof(MainWindow).GetMethod("RestoreWorkspacePreference", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [window, new RoutedEventArgs()]);
            else SelectVortex(window);
            PumpUntil(() => Get<ComboBox>(window, "ProfileComboBox").SelectedItem is VortexProfileOption
                && Get<Button>(window, "CancelScanButton").IsEnabled == false && HistoryIdle(window));
            responder.Stop();

            Assert.IsNotNull(request);
            Assert.AreEqual("Current", ((VortexProfileOption)Get<ComboBox>(window, "ProfileComboBox").SelectedItem).Name);
            Assert.AreEqual("Connect", Get<Button>(window, "FindProfilesButton").Content);
            Assert.AreEqual(contextPath, new WorkspacePreferenceStore(Path.Combine(root, "state")).Load()!.ContextPath);
            Assert.AreEqual(Visibility.Collapsed, Get<TextBlock>(window, "ErrorTextBlock").Visibility);
        });
    }

    [TestMethod]
    public void CancelledVortexConnectionCanBeRetried()
    {
        WithWindow((window, root) =>
        {
            string contextPath = Path.Combine(root, "bridge", "context.json");
            new WorkspacePreferenceStore(Path.Combine(root, "state")).Save(new WorkspacePreference(root, "Old", ModManagerKind.Vortex, contextPath));
            SelectVortex(window);
            PumpUntil(() => File.Exists(Path.Combine(root, "bridge", "context-request.json")));
            Get<Button>(window, "CancelScanButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => !Get<Button>(window, "CancelScanButton").IsEnabled);
            Assert.AreEqual("Scan cancelled. No incomplete scan results were saved.", Get<TextBlock>(window, "WorkspaceStatusTextBlock").Text);
            Assert.IsNull(Get<ComboBox>(window, "ProfileComboBox").SelectedItem);
            Assert.IsFalse(File.Exists(Path.Combine(root, "bridge", "context-request.json")));

            Get<Button>(window, "FindProfilesButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => Get<Button>(window, "CancelScanButton").IsEnabled);
            Get<Button>(window, "CancelScanButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => !Get<Button>(window, "CancelScanButton").IsEnabled);
            Assert.AreEqual(Visibility.Collapsed, Get<TextBlock>(window, "ErrorTextBlock").Visibility);
        });
    }

    [TestMethod]
    public void FailedVortexRefreshCanConnectAgain()
    {
        WithWindow((window, root) =>
        {
            string contextPath = Path.Combine(root, "bridge", "context.json");
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(game);
            Directory.CreateDirectory(staging);
            new WorkspacePreferenceStore(Path.Combine(root, "state")).Save(new WorkspacePreference(game, "Old", ModManagerKind.Vortex, contextPath));
            VortexContextBridgeStore bridge = new(Path.GetDirectoryName(contextPath)!);
            HashSet<string> handled = [];
            DispatcherTimer responder = new() { Interval = TimeSpan.FromMilliseconds(20) };
            responder.Tick += (_, _) =>
            {
                VortexContextRefreshRequest? request = bridge.ReadRequest();
                if (request is null || !handled.Add(request.RequestId)) return;
                if (handled.Count == 1)
                {
                    bridge.WriteResponse(new VortexContextRefreshResponse(1, request.RequestId, false, "Vortex could not read its profile.", null, DateTimeOffset.UtcNow));
                    return;
                }
                VortexManagerContext pending = new(1, string.Empty, DateTimeOffset.UtcNow, "profile", "Current", game, staging, true, [], [], [], null, DeploymentInventoryComplete: true);
                VortexManagerContext context = pending with { ContextId = VortexManagerContextStore.ComputeContextId(pending) };
                File.WriteAllText(contextPath, JsonSerializer.Serialize(context));
                bridge.WriteResponse(new VortexContextRefreshResponse(1, request.RequestId, true, string.Empty, context.ContextId, DateTimeOffset.UtcNow));
            };
            responder.Start();
            SelectVortex(window);
            PumpUntil(() => Get<TextBlock>(window, "ErrorTextBlock").Visibility == Visibility.Visible && !Get<Button>(window, "CancelScanButton").IsEnabled);
            StringAssert.Contains(Get<TextBlock>(window, "ErrorTextBlock").Text, "Vortex could not read its profile.");
            Assert.IsNull(Get<ComboBox>(window, "ProfileComboBox").SelectedItem);
            Get<Button>(window, "FindProfilesButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => Get<ComboBox>(window, "ProfileComboBox").SelectedItem is VortexProfileOption
                && !Get<Button>(window, "CancelScanButton").IsEnabled && HistoryIdle(window));
            responder.Stop();
            Assert.HasCount(2, handled);
            Assert.AreEqual(Visibility.Collapsed, Get<TextBlock>(window, "ErrorTextBlock").Visibility);
        });
    }

    private static T Get<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);

    private static bool HistoryIdle(MainWindow window) => !(bool)typeof(MainWindow).GetField("_historyBusy", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    private static void SelectVortex(MainWindow window)
    {
        ComboBox manager = Get<ComboBox>(window, "ManagerModeComboBox");
        manager.SelectedItem = manager.Items.OfType<ComboBoxItem>().Single(item => string.Equals(item.Tag?.ToString(), "Vortex", StringComparison.OrdinalIgnoreCase));
    }

    private static void PumpUntil(Func<bool> complete)
    {
        DispatcherFrame frame = new();
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            if (complete() || DateTimeOffset.UtcNow >= deadline) frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        Assert.IsTrue(complete(), "The Vortex setup did not reach the expected state.");
    }

    private static void WithWindow(Action<MainWindow, string> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Exception? failure = null;
        Thread thread = new(() =>
        {
            MainWindow? window = null;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                window = new MainWindow(Path.Combine(root, "state"));
                typeof(MainWindow).GetField("_restoringWorkspace", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                action(window, root);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        bool completed = thread.Join(TimeSpan.FromSeconds(25));
        if (completed) Directory.Delete(root, true);
        Assert.IsTrue(completed);
        if (failure is not null) throw new AssertFailedException(failure.ToString(), failure);
    }
}

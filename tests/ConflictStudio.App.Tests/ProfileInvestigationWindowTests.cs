using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using System.ComponentModel;
using ConflictStudio.App;
using ConflictStudio.Core;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class ProfileInvestigationWindowTests
{
    [TestMethod]
    public void InvestigationControlsAreAvailableWithoutClaimingAScan()
    {
        Run(() =>
        {
            MainWindow window = new();
            try
            {
                Assert.IsNotNull(window.FindName("ScanCoverageExpander"));
                Assert.IsNotNull(window.FindName("HistoryTab"));
                Assert.IsFalse(((Button)window.FindName("PinBaselineButton")).IsEnabled);
                Assert.IsFalse(((Button)window.FindName("OpenHistoryFindingButton")).IsEnabled);
                Assert.IsFalse(((Button)window.FindName("SaveOpenNoteButton")).IsEnabled);
                StringAssert.Contains(((TextBlock)window.FindName("CoverageSummaryTextBlock")).Text, "Run a scan");
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void ExpiredReviewAndOpenNoteRemainVisibleWithoutAcceptingChangedEvidence()
    {
        RunAsync(async () =>
        {
            using Fixture fixture = new();
            ProfileScanReceipt previous = fixture.Scan(2, 0);
            ConflictWorkItem oldItem = Item(previous);
            EvidenceDecisionStore decisions = new(Path.Combine(fixture.State, "decisions"));
            decisions.Review(previous.InstallationId!, "Test", oldItem, "Works as intended: original decision", DateTimeOffset.UtcNow);
            EvidenceNoteStore notes = new(Path.Combine(fixture.State, "decisions"));
            notes.SaveMany(previous.InstallationId!, "Test", [oldItem], "Check the new value", DateTimeOffset.UtcNow);
            ProfileScanReceipt current = fixture.Scan(3, 1);
            MainWindow window = new(fixture.State);
            try
            {
                await Load(window, fixture, current);
                DataGrid grid = Get<DataGrid>(window, "WorkQueueDataGrid");
                ConflictWorkItem item = grid.Items.Cast<ConflictWorkItem>().Single();
                Assert.AreEqual(ConflictWorkState.ReviewWhenRelevant, item.State);
                StringAssert.Contains(Get<TextBlock>(window, "ReviewContextTextBlock").Text, "original decision");
                StringAssert.Contains(Get<TextBlock>(window, "SelectedClassificationTextBlock").Text, "values");
                Assert.AreEqual("Check the new value", Get<TextBox>(window, "ReviewRationaleTextBox").Text);
                Get<TextBox>(window, "ReviewRationaleTextBox").Text = "Still investigating";
                Get<Button>(window, "SaveOpenNoteButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.PendingUserStateWrites;
                Assert.AreEqual(ConflictWorkState.ReviewWhenRelevant, grid.Items.Cast<ConflictWorkItem>().Single().State);
                Assert.AreEqual("Still investigating", notes.Load().Single().Text);
                Assert.AreEqual(current.InstallationId, notes.Load().Single().InstallationId);
                Assert.AreEqual(oldItem.EvidenceSha256, decisions.Load().Single().EvidenceSha256);
            }
            finally { await Finish(window); }
        });
    }

    [TestMethod]
    public void HistoryShowsChangedEvidenceAndNavigatesOnlyCurrentFindings()
    {
        RunAsync(async () =>
        {
            using Fixture fixture = new();
            ProfileScanReceipt previous = fixture.Scan(2, 0);
            ProfileScanReceiptPersistence.Save(fixture.History(previous), previous);
            ProfileScanReceipt current = fixture.Scan(3, 1);
            MainWindow window = new(fixture.State);
            try
            {
                await Load(window, fixture, current);
                DataGrid grid = Get<DataGrid>(window, "HistoryDataGrid");
                ScanHistoryEntry entry = grid.Items.Cast<ScanHistoryEntry>().Single();
                Assert.AreEqual(ScanHistoryChangeKind.Changed, entry.ChangeKind);
                StringAssert.Contains(Get<TextBox>(window, "HistoryBeforeTextBox").Text, entry.Before!.Summary);
                StringAssert.Contains(Get<TextBox>(window, "HistoryAfterTextBox").Text, entry.After!.Summary);
                Get<TabControl>(window, "MainTabControl").SelectedIndex = 3;
                Get<Button>(window, "OpenHistoryFindingButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.AreEqual(1, Get<TabControl>(window, "MainTabControl").SelectedIndex);
                Assert.AreEqual(entry.After.EvidenceSha256, ((ConflictWorkItem)Get<DataGrid>(window, "WorkQueueDataGrid").SelectedItem).EvidenceSha256);
            }
            finally { await Finish(window); }
        });
    }

    [TestMethod]
    public void RemovedFindingCannotOpenCurrentSourceOrClaimAFix()
    {
        RunAsync(async () =>
        {
            using Fixture fixture = new();
            ProfileScanReceipt previous = fixture.Scan(2, 0);
            ProfileScanReceiptPersistence.Save(fixture.History(previous), previous);
            ProfileScanReceipt current = previous with { ScannedAtUtc = previous.ScannedAtUtc.AddSeconds(1), InteractionFindings = [], TweakOverlaps = [], CodeEvidence = [] };
            MainWindow window = new(fixture.State);
            try
            {
                await Load(window, fixture, current);
                ScanHistoryEntry entry = Get<DataGrid>(window, "HistoryDataGrid").Items.Cast<ScanHistoryEntry>().Single();
                Assert.AreEqual(ScanHistoryChangeKind.NoLongerDetected, entry.ChangeKind);
                Assert.IsFalse(Get<Button>(window, "OpenHistoryFindingButton").IsEnabled);
                StringAssert.Contains(Get<TextBox>(window, "HistoryAfterTextBox").Text, "does not prove it was fixed");
                Get<TabControl>(window, "MainTabControl").SelectedIndex = 3;
                Get<Button>(window, "OpenHistoryFindingButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.AreEqual(3, Get<TabControl>(window, "MainTabControl").SelectedIndex);
            }
            finally { await Finish(window); }
        });
    }

    [TestMethod]
    public void PinAndClearButtonsRetainAnExplicitBaseline()
    {
        RunAsync(async () =>
        {
            using Fixture fixture = new();
            ProfileScanReceipt previous = fixture.Scan(2, 0);
            MainWindow window = new(fixture.State);
            try
            {
                await Load(window, fixture, previous);
                Get<Button>(window, "PinBaselineButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.PendingUserStateWrites;
                Assert.AreEqual(previous.ScannedAtUtc, new ScanBaselineStore(fixture.History(previous)).Load(previous).Receipt!.ScannedAtUtc);
                ProfileScanReceipt current = fixture.Scan(3, 1);
                await Load(window, fixture, current);
                Assert.AreEqual("baseline", ((ComboBoxItem)Get<ComboBox>(window, "HistoryReferenceComboBox").SelectedItem).Tag);
                Assert.AreEqual(ScanHistoryChangeKind.Changed, Get<DataGrid>(window, "HistoryDataGrid").Items.Cast<ScanHistoryEntry>().Single().ChangeKind);
                Get<Button>(window, "ClearBaselineButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.PendingUserStateWrites;
                Assert.AreEqual(ScanBaselineState.Missing, new ScanBaselineStore(fixture.History(current)).Load(current).State);
                Assert.AreEqual("previous", ((ComboBoxItem)Get<ComboBox>(window, "HistoryReferenceComboBox").SelectedItem).Tag);
            }
            finally { await Finish(window); }
        });
    }

    [TestMethod]
    public void ViewRestoresColumnsFiltersAndSortThenResets()
    {
        RunAsync(async () =>
        {
            using Fixture fixture = new();
            ProfileScanReceipt receipt = fixture.Scan(2, 0);
            MainWindow first = new(fixture.State);
            try
            {
                await Load(first, fixture, receipt);
                Get<TextBox>(first, "QueueSearchTextBox").Text = "Items.Test";
                DataGrid grid = Get<DataGrid>(first, "WorkQueueDataGrid");
                grid.Columns[0].Width = 333;
                grid.Columns[4].Visibility = Visibility.Collapsed;
                ICollectionView view = CollectionViewSource.GetDefaultView(grid.ItemsSource);
                view.SortDescriptions.Add(new SortDescription("Target", ListSortDirection.Descending));
                view.SortDescriptions.Add(new SortDescription("ProviderSummary", ListSortDirection.Ascending));
                Invoke(first, "SaveCurrentView");
                await first.PendingUserStateWrites;
            }
            finally { await Finish(first); }
            MainWindow second = new(fixture.State);
            try
            {
                await Load(second, fixture, receipt);
                DataGrid grid = Get<DataGrid>(second, "WorkQueueDataGrid");
                Assert.AreEqual("Items.Test", Get<TextBox>(second, "QueueSearchTextBox").Text);
                Assert.AreEqual(333d, grid.Columns[0].Width.Value);
                Assert.AreEqual(Visibility.Collapsed, grid.Columns[4].Visibility);
                SortDescription[] sort = CollectionViewSource.GetDefaultView(grid.ItemsSource).SortDescriptions.ToArray();
                Assert.AreEqual("Target", sort[0].PropertyName);
                Assert.AreEqual(ListSortDirection.Descending, sort[0].Direction);
                Assert.AreEqual("ProviderSummary", sort[1].PropertyName);
                Invoke(second, "ResetViewClicked", new object(), new RoutedEventArgs());
                await second.PendingUserStateWrites;
                Assert.AreEqual(string.Empty, Get<TextBox>(second, "QueueSearchTextBox").Text);
                Assert.IsTrue(grid.Columns.All(column => column.Visibility == Visibility.Visible));
                Assert.IsEmpty(new ProfileViewStateStore(fixture.State).Load(receipt.ManagerKind, receipt.InstallationId!, receipt.ProfileName).Columns);
            }
            finally { await Finish(second); }
        });
    }

    [TestMethod]
    public void CoverageShowsUnsupportedInputsAndLimitedManagerEvidence()
    {
        RunAsync(async () =>
        {
            using Fixture fixture = new();
            ProfileScanReceipt receipt = fixture.Scan(2, 0) with { DeploymentFresh = false, CodeCoverage = new(1, [new("CET Lua", 2)], 31, 0, 2, 86, ["A coverage limitation"]) };
            MainWindow window = new(fixture.State);
            try
            {
                await Load(window, fixture, receipt);
                string summary = Get<TextBlock>(window, "CoverageSummaryTextBlock").Text;
                StringAssert.Contains(summary, "31 unsupported");
                StringAssert.Contains(summary, "86 dynamic");
                StringAssert.Contains(summary, "limited manager evidence");
                StringAssert.Contains(Get<TextBlock>(window, "CoverageDetailsTextBlock").Text, "A coverage limitation");
            }
            finally { await Finish(window); }
        });
    }

    [TestMethod]
    public void CoverageFitsItsToolbarWhenCollapsedAndExpanded()
    {
        RunAsync(async () =>
        {
            using Fixture fixture = new();
            MainWindow window = new(fixture.State);
            try
            {
                await Load(window, fixture, fixture.Scan(2, 0));
                Expander coverage = Get<Expander>(window, "ScanCoverageExpander");
                Border toolbar = (Border)((FrameworkElement)coverage.Parent).Parent;
                FrameworkElement content = (FrameworkElement)window.Content;
                for (int expanded = 0; expanded < 2; expanded++)
                {
                    coverage.IsExpanded = expanded == 1;
                    content.Measure(new Size(1180, 720));
                    content.Arrange(new Rect(0, 0, 1180, 720));
                    content.UpdateLayout();
                    Point bottom = coverage.TranslatePoint(new Point(0, coverage.ActualHeight), toolbar);
                    Assert.IsTrue(bottom.Y <= toolbar.ActualHeight, $"Coverage bottom {bottom.Y} exceeds toolbar {toolbar.ActualHeight}.");
                }
            }
            finally { await Finish(window); }
        });
    }

    [TestMethod]
    public void NotesLoadEvenWhenTheInitialFilterHasNoRows()
    {
        RunAsync(async () =>
        {
            using Fixture fixture = new();
            ProfileScanReceipt receipt = fixture.Scan(2, 0);
            new EvidenceNoteStore(Path.Combine(fixture.State, "decisions")).SaveMany(receipt.InstallationId!, receipt.ProfileName, [Item(receipt)], "Retain this investigation", DateTimeOffset.UtcNow);
            MainWindow window = new(fixture.State);
            try
            {
                Get<TextBox>(window, "QueueSearchTextBox").Text = "no matching finding";
                await Load(window, fixture, receipt);
                Get<TextBox>(window, "QueueSearchTextBox").Text = string.Empty;
                Assert.AreEqual("Retain this investigation", Get<TextBox>(window, "ReviewRationaleTextBox").Text);
            }
            finally { await Finish(window); }
        });
    }

    [TestMethod]
    public void ClosingFlushesTheLastViewChange()
    {
        RunAsync(async () =>
        {
            using Fixture fixture = new();
            ProfileScanReceipt receipt = fixture.Scan(2, 0);
            MainWindow window = new(fixture.State);
            await Load(window, fixture, receipt);
            Get<TextBox>(window, "QueueSearchTextBox").Text = "Items.Test";
            window.Close();
            await window.PendingUserStateWrites;
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.AreEqual("Items.Test", new ProfileViewStateStore(fixture.State).Load(receipt.ManagerKind, receipt.InstallationId!, receipt.ProfileName).CodeSearch);
            window.Dispose();
        });
    }

    [TestMethod]
    [DataRow("_noteSaveTask")]
    [DataRow("_baselineWriteTask")]
    public void RescanWaitsForPendingUserWritesBeforeReadingState(string pendingField)
    {
        RunAsync(async () =>
        {
            using Fixture fixture = new();
            ProfileScanReceipt before = fixture.Scan(2, 0);
            MainWindow window = new(fixture.State);
            TaskCompletionSource pendingWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                await Load(window, fixture, before);
                typeof(MainWindow).GetField(pendingField, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, pendingWrite.Task);
                ProfileScanReceipt current = fixture.Scan(3, 1);
                Task loading = Load(window, fixture, current);
                Task first = await Task.WhenAny(loading, Task.Delay(300));
                Assert.AreNotSame(loading, first, "The rescan read persisted state before the pending user write finished.");
                if (pendingField == "_noteSaveTask")
                    new EvidenceNoteStore(Path.Combine(fixture.State, "decisions")).SaveMany(before.InstallationId!, before.ProfileName, [Item(before)], "Written during rescan", DateTimeOffset.UtcNow);
                else new ScanBaselineStore(fixture.History(before)).Pin(before);
                pendingWrite.SetResult();
                await loading;
                if (pendingField == "_noteSaveTask") Assert.AreEqual("Written during rescan", Get<TextBox>(window, "ReviewRationaleTextBox").Text);
                else StringAssert.Contains(Get<TextBlock>(window, "BaselineStatusTextBlock").Text, "Pinned scan");
            }
            finally { pendingWrite.TrySetResult(); await Finish(window); }
        });
    }

    [TestMethod]
    public void ExportWaitsUntilSavedNotesHaveLoaded()
    {
        RunAsync(async () =>
        {
            using Fixture fixture = new();
            MainWindow window = new(fixture.State);
            TaskCompletionSource pendingView = new(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                typeof(MainWindow).GetField("_viewSaveTask", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, pendingView.Task);
                Task loading = Load(window, fixture, fixture.Scan(2, 0));
                Assert.IsFalse(Get<Button>(window, "ExportButton").IsEnabled);
                pendingView.SetResult();
                await loading;
                Assert.IsTrue(Get<Button>(window, "ExportButton").IsEnabled);
            }
            finally { pendingView.TrySetResult(); await Finish(window); }
        });
    }

    [TestMethod]
    public void ArchiveXlFailuresRemainExplicitInCoverage()
    {
        using Fixture fixture = new();
        ArchiveXlSourceFailure[] unavailable = ArchiveXlSourceScanner.Scan([new("Unavailable", Path.Combine(fixture.Mo2, "missing"))]).Failures;
        ArchiveXlSourceFailure[] analyzed = ArchiveXlManifestAnalyzer.AnalyzeDetailed([
            new("Malformed", "broken.xl", "resource:\n  patch: [\n"),
            new("Malformed", "also-broken.xl", "resource:\n  patch: [\n"),
            new("Unsupported", "future.xl", "resource:\n  transform:\n    base\\shared.mesh: future\\shared.mesh\n"),
            new("Unsupported", "future-two.xl", "resource:\n  transform:\n    base\\shared.mesh: future\\shared.mesh\n"),
            new("Unsupported", "future-three.xl", "resource:\n  transform:\n    base\\shared.mesh: future\\shared.mesh\n")]).Failures;
        ProfileScanReceipt receipt = fixture.Scan(2, 0) with { ArchiveXlFailures = [.. unavailable, .. analyzed] };
        Assert.AreEqual(1, receipt.ArchiveXlFailures.Count(value => value.Kind == ArchiveXlFailureKind.Operational));
        Assert.AreEqual(2, receipt.ArchiveXlFailures.Count(value => value.Kind == ArchiveXlFailureKind.Malformed));
        Assert.AreEqual(3, receipt.ArchiveXlFailures.Count(value => value.Kind == ArchiveXlFailureKind.Coverage));
        ScanCoveragePresentation coverage = ScanCoveragePresentation.Create(receipt);
        StringAssert.Contains(coverage.Summary, "6 ArchiveXL issues");
        StringAssert.Contains(coverage.Details, "ArchiveXL unavailable inputs: 1");
        StringAssert.Contains(coverage.Details, "ArchiveXL malformed data: 2");
        StringAssert.Contains(coverage.Details, "ArchiveXL unsupported operations: 3");
    }

    [TestMethod]
    public void PairViewKeepsTheThirdProviderAndFindingNavigationUsesVisibleRows()
    {
        RunAsync(async () =>
        {
            using Fixture fixture = new();
            fixture.Scan(2, 0);
            File.AppendAllText(fixture.Profile.ModlistPath, "+Gamma\n");
            string gamma = Path.Combine(fixture.Mo2, "mods", "Gamma", "r6", "tweaks");
            Directory.CreateDirectory(gamma);
            File.WriteAllText(Path.Combine(gamma, "Gamma.yaml"), "Items.Test.value: 3\nItems.Second.value: 3\n");
            File.AppendAllText(Path.Combine(fixture.Mo2, "mods", "Alpha", "r6", "tweaks", "Alpha.yaml"), "\nItems.Second.value: 1\n");
            File.AppendAllText(Path.Combine(fixture.Mo2, "mods", "Beta", "r6", "tweaks", "Beta.yaml"), "\nItems.Second.value: 2\n");
            ProfileScanReceipt receipt = ProfileScanCoordinator.Scan(fixture.Mo2, fixture.Profile, DateTimeOffset.UtcNow);
            MainWindow window = new(fixture.State);
            try
            {
                await Load(window, fixture, receipt);
                Get<ComboBox>(window, "QueueProviderComboBox").SelectedItem = "Alpha";
                Get<ComboBox>(window, "QueueOtherProviderComboBox").SelectedItem = "Beta";
                DataGrid grid = Get<DataGrid>(window, "WorkQueueDataGrid");
                Assert.AreEqual(2, grid.Items.Count);
                Assert.IsTrue(grid.Items.Cast<ConflictWorkItem>().All(item => item.Providers.Contains("Gamma", StringComparer.Ordinal)));
                string first = ((ConflictWorkItem)grid.SelectedItem).Target;
                Get<Button>(window, "NextFindingButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.AreNotEqual(first, ((ConflictWorkItem)grid.SelectedItem).Target);
                Assert.IsFalse(Get<Button>(window, "NextFindingButton").IsEnabled);
                Get<Button>(window, "PreviousFindingButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.AreEqual(first, ((ConflictWorkItem)grid.SelectedItem).Target);
                Invoke(window, "SaveCurrentView");
                await window.PendingUserStateWrites;
                Assert.AreEqual("Beta", new ProfileViewStateStore(fixture.State).Load(receipt.ManagerKind, receipt.InstallationId!, receipt.ProfileName).CodeOtherProvider);
                await Load(window, fixture, receipt with { ScannedAtUtc = receipt.ScannedAtUtc.AddSeconds(1) });
                Assert.AreEqual("Alpha", Get<ComboBox>(window, "QueueProviderComboBox").SelectedItem);
                Assert.AreEqual("Beta", Get<ComboBox>(window, "QueueOtherProviderComboBox").SelectedItem);
            }
            finally { await Finish(window); }
        });
    }

    private static ConflictWorkItem Item(ProfileScanReceipt receipt) => ConflictWorkQueueBuilder.Build(receipt, []).Single(item => item.Target == "Items.Test.value");
    private static T Get<T>(MainWindow window, string name) where T : FrameworkElement => (T)window.FindName(name);
    private static void Invoke(MainWindow window, string method, params object[] args) => typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);

    private static async Task Load(MainWindow window, Fixture fixture, ProfileScanReceipt receipt)
    {
        typeof(MainWindow).GetField("_restoringWorkspace", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
        Get<TextBox>(window, "Mo2RootTextBox").Text = fixture.Mo2;
        Get<ComboBox>(window, "ProfileComboBox").ItemsSource = new[] { fixture.Profile };
        Get<ComboBox>(window, "ProfileComboBox").SelectedIndex = 0;
        Invoke(window, "LoadReceipt", receipt, fixture.Mo2, fixture.Profile, false);
        await window.InvestigationReady;
    }

    private static async Task Finish(MainWindow window)
    {
        Invoke(window, "SaveCurrentView");
        await window.PendingUserStateWrites;
        await window.InvestigationReady;
        window.Close();
    }

    private static void RunAsync(Func<Task> action)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await action(); }
                catch (Exception exception) { failure = exception; }
                finally { dispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)));
        if (failure is not null) throw new AssertFailedException(failure.ToString(), failure);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "conflict-studio-investigation-" + Guid.NewGuid().ToString("N"));
        public string Mo2 => Path.Combine(_root, "mo2");
        public string State => Path.Combine(_root, "state");
        public Mo2Profile Profile => new("Test", Path.Combine(Mo2, "profiles", "Test", "modlist.txt"));
        public string History(ProfileScanReceipt receipt) => Path.Combine(State, "receipts", receipt.InstallationId!, "Test");

        public ProfileScanReceipt Scan(int value, int sequence)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Profile.ModlistPath)!);
            File.WriteAllText(Profile.ModlistPath, "+Alpha\n+Beta\n");
            foreach (string provider in new[] { "Alpha", "Beta" })
            {
                string directory = Path.Combine(Mo2, "mods", provider, "r6", "tweaks");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, provider + ".yaml"), "Items.Test.value: " + (provider == "Alpha" ? 1 : value));
            }
            return ProfileScanCoordinator.Scan(Mo2, Profile, new DateTimeOffset(2026, 9, 12, 12, 0, sequence, TimeSpan.Zero));
        }

        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }

    private static void Run(Action action)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure is not null) throw new AssertFailedException(failure.Message, failure);
    }
}

using ConflictStudio.App;
using ConflictStudio.Core;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class MainWindowArchiveInteractionTests
{
    private static readonly string[] SelectedRange = ["A.archive", "B.archive", "C.archive"];
    private static readonly string[] SelectedPair = ["A.archive", "B.archive"];

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void LoadOrderSelectionSurvivesMissingOrVisibleConflictResults(bool showUnique)
    {
        RunArchiveSelectionScenario((window, rail, order) =>
        {
            if (showUnique)
            {
                ((CheckBox)window.FindName("ShowNonConflictingFilesCheckBox")).IsChecked = true;
                typeof(MainWindow).GetMethod("ApplyArchiveTreeFilter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, []);
            }
            rail.SelectedItem = rail.Items.Cast<ArchiveRailItem>().First();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            rail.Focus();
            rail.SelectedItem = rail.Items.Cast<ArchiveRailItem>().Last();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            Assert.AreEqual(order[2], ((ArchiveRailItem)rail.SelectedItem).ArchiveName);
            string[] selected = (string[])typeof(MainWindow).GetField("_selectedArchiveNames", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            Assert.AreEqual(order[2], selected.Single());
            object? node = typeof(MainWindow).GetField("_selectedArchiveNode", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
            if (showUnique) Assert.AreEqual(order[2], ((ArchiveConflictNode)node!).ArchiveName);
            else Assert.IsNull(node);
            Assert.IsTrue(rail.IsKeyboardFocusWithin);
            CollectionAssert.AreEqual(order, ((ArchiveOrderWorkspaceViewModel)window.DataContext).ProposedOrder.ToArray());
        });
    }

    [TestMethod]
    public void DeferredFilterSelectionCannotReplaceANewerLoadOrderChoice()
    {
        RunArchiveSelectionScenario((window, rail, order) =>
        {
            rail.SelectedItem = rail.Items.Cast<ArchiveRailItem>().First();
            typeof(MainWindow).GetMethod("ApplyArchiveTreeFilter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, []);

            rail.SelectedItem = rail.Items.Cast<ArchiveRailItem>().ElementAt(1);
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            Assert.AreEqual(order[1], ((ArchiveRailItem)rail.SelectedItem).ArchiveName);
            string[] selected = (string[])typeof(MainWindow).GetField("_selectedArchiveNames", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            Assert.AreEqual(order[1], selected.Single());
            Assert.AreEqual(order[1], ((ArchiveConflictNode)((TreeView)window.FindName("ArchiveConflictTreeView")).SelectedItem).ArchiveName);
        });
    }

    [TestMethod]
    public void NewArchiveGuidanceLocatesAdditionsWithoutMovingOrSavingThem()
    {
        Run(() =>
        {
            string applicationData = Path.Combine(Path.GetTempPath(), "conflict-studio-new-archives-" + Guid.NewGuid().ToString("N"));
            MainWindow window = new(applicationData);
            try
            {
                window.Width = window.MinWidth;
                window.Height = window.MinHeight;
                window.Show();
                string[] order = ["ZExisting.archive", "AExisting.archive", "PeachuNew.archive"];
                string details = "PeachuNew.archive is unlisted. Old.archive is inactive. " + new string('x', 4000);
                ArchiveOrderEvidence evidence = new(ArchiveOrderEvidenceKind.ManagedModlist, "Profile", "archive-order.txt", details)
                {
                    UnlistedArchives = ["PeachuNew.archive"], IgnoredEntries = ["Old.archive"], PreserveInactiveArchives = true
                };
                ProfileScanReceipt receipt = new(2, "Profile", DateTimeOffset.UtcNow, [], order, [], [], [], [], [], [], [], [], [], [], ArchiveOrderEvidence: evidence);
                typeof(MainWindow).GetMethod("SetScanLocked", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [false]);
                typeof(MainWindow).GetField("_receipt", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, receipt);
                ArchiveOrderWorkspaceViewModel workspace = (ArchiveOrderWorkspaceViewModel)window.DataContext;
                workspace.SetProposedOrder(order);
                typeof(MainWindow).GetMethod("RefreshArchiveRail", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, []);
                typeof(MainWindow).GetMethod("PresentArchiveOrderEvidence", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [evidence]);
                Button find = (Button)window.FindName("FindUnlistedArchivesButton");
                Assert.IsTrue(find.IsEnabled && find.IsVisible);
                Assert.IsFalse(((Expander)window.FindName("ArchiveOrderDetailsExpander")).IsExpanded);
                Assert.IsFalse(((TextBlock)window.FindName("ArchiveOrderEvidenceTextBlock")).Text.Contains("PeachuNew.archive", StringComparison.Ordinal));
                Assert.AreEqual(evidence.Message, ((TextBlock)window.FindName("ArchiveOrderDetailsTextBlock")).Text);
                window.UpdateLayout();
                Assert.IsLessThan(180d, ((Border)window.FindName("ArchiveOrderEvidenceBorder")).ActualHeight);

                find.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                ListBox rail = (ListBox)window.FindName("ArchiveOrderListBox");
                Assert.AreEqual("PeachuNew.archive", ((ArchiveRailItem)rail.SelectedItems.Cast<object>().Single()).ArchiveName);
                Assert.AreEqual("PeachuNew.archive", rail.Items.Cast<ArchiveRailItem>().Single(value => value.IsUnlisted).ArchiveName);
                CollectionAssert.AreEqual(order, workspace.ProposedOrder.ToArray());
                Assert.IsFalse(workspace.CanApply);
                typeof(MainWindow).GetMethod("SetScanLocked", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [true]);
                Assert.IsFalse(find.IsEnabled);
                typeof(MainWindow).GetMethod("InvalidateReceipt", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, []);
                Assert.AreEqual(Visibility.Collapsed, find.Visibility);
            }
            finally
            {
                window.Close();
                if (Directory.Exists(applicationData)) Directory.Delete(applicationData, true);
            }
        });
    }

    [TestMethod]
    public void MinimumEditorWindowKeepsStatusAndSaveVisible()
    {
        Run(() =>
        {
            string applicationData = Path.Combine(Path.GetTempPath(), "conflict-studio-editor-layout-" + Guid.NewGuid().ToString("N"));
            MainWindow owner = new(applicationData);
            SourceEditorSettingsWindow? editor = null;
            try
            {
                owner.Show();
                editor = new(owner, new SourceEditorPreferenceStore(applicationData));
                editor.Width = editor.MinWidth;
                editor.Height = editor.MinHeight;
                editor.Show();
                editor.UpdateLayout();
                FrameworkElement root = (FrameworkElement)editor.Content;
                TextBlock status = (TextBlock)editor.FindName("EditorSettingsStatusTextBlock");
                Button save = (Button)editor.FindName("SaveEditorButton");
                Rect statusBounds = status.TransformToAncestor(root).TransformBounds(new Rect(status.RenderSize));
                Rect saveBounds = save.TransformToAncestor(root).TransformBounds(new Rect(save.RenderSize));
                Assert.IsTrue(statusBounds.Height >= 28 && statusBounds.Bottom <= saveBounds.Top);
                Assert.IsTrue(saveBounds.Bottom <= root.ActualHeight);
            }
            finally
            {
                editor?.Close();
                owner.Close();
                if (Directory.Exists(applicationData)) Directory.Delete(applicationData, true);
            }
        });
    }

    [TestMethod]
    public void MinimumWindowKeepsSetupAndFindingFiltersVisible()
    {
        Run(() =>
        {
            string applicationData = Path.Combine(Path.GetTempPath(), "conflict-studio-layout-" + Guid.NewGuid().ToString("N"));
            MainWindow window = new(applicationData);
            try
            {
                window.Width = window.MinWidth;
                window.Height = window.MinHeight;
                window.Show();
                ((TabControl)window.FindName("MainTabControl")).SelectedIndex = 1;
                window.UpdateLayout();
                FrameworkElement root = (FrameworkElement)window.Content;
                string[] names = ["ManagerModeComboBox", "Mo2RootTextBox", "ProfileComboBox", "ScanProfileButton", "QueueSearchTextBox", "QueueViewComboBox", "QueueSurfaceComboBox", "QueueProviderComboBox", "QueueOtherProviderComboBox"];
                foreach (string name in names)
                {
                    FrameworkElement control = (FrameworkElement)window.FindName(name);
                    Rect bounds = control.TransformToAncestor(root).TransformBounds(new Rect(control.RenderSize));
                    Assert.IsTrue(control.IsVisible && bounds.Width >= 90 && bounds.Height >= 30, name);
                    Assert.IsTrue(bounds.Left >= 0 && bounds.Right <= root.ActualWidth && bounds.Top >= 0 && bounds.Bottom <= root.ActualHeight, name);
                }
            }
            finally
            {
                window.Close();
                if (Directory.Exists(applicationData)) Directory.Delete(applicationData, true);
            }
        });
    }

    [TestMethod]
    public void DragNearTheTopScrollsUp()
    {
        Assert.IsLessThan(0, ArchiveDragAutoScroll.LinesAt(1, 500));
        Assert.AreEqual(0, ArchiveDragAutoScroll.LinesAt(250, 500));
        Assert.IsGreaterThan(0, ArchiveDragAutoScroll.LinesAt(499, 500));
    }

    [TestMethod]
    public void WheelRemainderPreservesPartialNotches()
    {
        int remainder = 0;

        Assert.AreEqual(0, ArchiveDragAutoScroll.ConsumeWheelDelta(ref remainder, 60));
        Assert.AreEqual(60, remainder);
        Assert.AreEqual(-6, ArchiveDragAutoScroll.ConsumeWheelDelta(ref remainder, 60));
        Assert.AreEqual(0, remainder);
    }

    [TestMethod]
    public void DragCueMatchesTheBlockInsertionSide()
    {
        string[] order = ["A.archive", "B.archive", "C.archive", "D.archive"];

        Assert.AreEqual(ArchiveDropCue.After, ArchiveDropCueProjection.For(order, ["A.archive"], "C.archive"));
        Assert.AreEqual(ArchiveDropCue.Before, ArchiveDropCueProjection.For(order, ["D.archive"], "B.archive"));
        Assert.AreEqual(ArchiveDropCue.None, ArchiveDropCueProjection.For(order, ["B.archive", "D.archive"], "C.archive"));
    }

    [TestMethod]
    public void PendingCloseMapsDiscardAndApplyToTheirProductionOutcomes()
    {
        Assert.AreEqual(ArchivePendingCloseDisposition.ApplyThenClose, ArchivePendingClosePolicy.Resolve(ArchiveOrderCloseAction.ApplyAndClose));
        Assert.AreEqual(ArchivePendingCloseDisposition.CloseNow, ArchivePendingClosePolicy.Resolve(ArchiveOrderCloseAction.DiscardAndClose));
        Assert.AreEqual(ArchivePendingCloseDisposition.KeepOpen, ArchivePendingClosePolicy.Resolve(ArchiveOrderCloseAction.Cancel));
    }

    [TestMethod]
    public void ModifierSelectionIsCompleteBeforeDraggingBegins()
    {
        string[] archives = ArchiveDragSelection.Resolve("C.archive", false, ["A.archive"], ["A.archive", "B.archive", "C.archive"], true);

        CollectionAssert.AreEqual(SelectedRange, archives);
    }

    [TestMethod]
    public void PlainDragKeepsTheSelectionCapturedAtMouseDown()
    {
        string[] archives = ArchiveDragSelection.Resolve("B.archive", true, ["A.archive", "B.archive"], ["B.archive"], false);

        CollectionAssert.AreEqual(SelectedPair, archives);
    }

    [TestMethod]
    [DataRow(ArchiveOrderProblemLane.Legacy)]
    [DataRow(ArchiveOrderProblemLane.Redmod)]
    [DataRow(ArchiveOrderProblemLane.Combined)]
    public void IncompleteArchiveActionOpensTheSupportReportWithoutReplacingEvidence(ArchiveOrderProblemLane lane)
    {
        Run(() =>
        {
            string applicationData = Path.Combine(Path.GetTempPath(), "conflict-studio-archive-guidance-" + Guid.NewGuid().ToString("N"));
            MainWindow window = new(applicationData);
            try
            {
                window.Show();
                const string message = "Broken.archive could not be read.";
                ArchiveOrderEvidence evidence = new(ArchiveOrderEvidenceKind.Unresolved, "Vortex", null, message)
                {
                    ProblemLane = lane,
                    IncompleteArchiveLane = lane
                };
                ProfileScanReceipt receipt = new(2, "Profile", DateTimeOffset.UtcNow, [], [], [new("Provider", "Broken.archive", message)], [], [], [], [], [], [], [], [], [], ArchiveOrderEvidence: evidence, ManagerKind: ModManagerKind.Vortex);
                typeof(MainWindow).GetMethod("SetScanLocked", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [false]);
                typeof(MainWindow).GetField("_receipt", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, receipt);
                typeof(MainWindow).GetMethod("PresentArchiveOrderEvidence", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [evidence]);

                TextBlock evidenceText = (TextBlock)window.FindName("ArchiveOrderEvidenceTextBlock");
                TabControl tabs = (TabControl)window.FindName("MainTabControl");
                TabItem support = (TabItem)window.FindName("SupportTab");
                TextBox report = (TextBox)window.FindName("DiagnosticsTextBox");
                Assert.AreEqual(lane, ((ProfileScanReceipt)typeof(MainWindow).GetField("_receipt", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!).ArchiveOrderEvidence!.IncompleteArchiveLane);
                typeof(MainWindow).GetMethod("FocusArchiveLoadOrderClicked", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [window.FindName("ArchiveOrderActionButton"), new RoutedEventArgs(Button.ClickEvent)]);
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                Assert.AreSame(support, tabs.SelectedItem);
                Assert.AreEqual(ArchiveOrderGuidance.Instruction(evidence), evidenceText.Text);
                Assert.AreEqual(message, ((TextBlock)window.FindName("ArchiveOrderDetailsTextBlock")).Text);
                StringAssert.Contains(report.Text, "Broken.archive");
                Assert.IsTrue(report.IsKeyboardFocusWithin);
            }
            finally
            {
                window.Close();
                if (Directory.Exists(applicationData)) Directory.Delete(applicationData, true);
            }
        });
    }

    [TestMethod]
    public void ArchivePanesHaveADraggableDivider()
    {
        Exception? failure = null;
        bool splitterFound = false;
        GridResizeDirection direction = default;
        GridResizeBehavior behavior = default;
        Thread thread = new(() =>
        {
            try
            {
                MainWindow window = new();
                GridSplitter? splitter = window.FindName("ArchivePaneSplitter") as GridSplitter;
                splitterFound = splitter is not null;
                if (splitter is not null)
                {
                    direction = splitter.ResizeDirection;
                    behavior = splitter.ResizeBehavior;
                }
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null) throw failure;
        Assert.IsTrue(splitterFound);
        Assert.AreEqual(GridResizeDirection.Columns, direction);
        Assert.AreEqual(GridResizeBehavior.PreviousAndNext, behavior);
    }

    [TestMethod]
    public void ConflictingArchiveOptionFollowsUniqueFilesAndClearsWithFilters()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                MainWindow window = new();
                try
                {
                    CheckBox unique = (CheckBox)window.FindName("ShowNonConflictingFilesCheckBox");
                    CheckBox conflicting = (CheckBox)window.FindName("OnlyConflictingArchivesCheckBox");
                    Assert.IsFalse(conflicting.IsEnabled);
                    unique.IsChecked = true;
                    Assert.IsTrue(conflicting.IsEnabled);
                    conflicting.IsChecked = true;
                    unique.IsChecked = false;
                    Assert.IsFalse(conflicting.IsEnabled);
                    unique.IsChecked = true;
                    Assert.IsTrue(conflicting.IsChecked);
                    ((Button)window.FindName("ClearArchiveFiltersButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.IsFalse(unique.IsChecked);
                    Assert.IsFalse(conflicting.IsChecked);
                    Assert.IsFalse(conflicting.IsEnabled);
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

    private static void RunArchiveSelectionScenario(Action<MainWindow, ListBox, string[]> scenario)
    {
        Run(() =>
        {
            string applicationData = Path.Combine(Path.GetTempPath(), "conflict-studio-archive-selection-" + Guid.NewGuid().ToString("N"));
            MainWindow window = new(applicationData);
            try
            {
                window.Show();
                string[] order = ["NovaScanner.archive", "Other.archive", "NewUnique.archive"];
                ResourceProvider[] resources = [new(order[0], 1, "shared.mesh", new string('a', 40)), new(order[1], 1, "shared.mesh", new string('b', 40)), new(order[2], 2, "unique.mesh", new string('c', 40))];
                Mo2Archive[] archives = order.Select(name => new Mo2Archive(name, name, name, 1, new string('a', 64))).ToArray();
                ArchiveConflictSummary[] summaries = ArchiveResourceIndexBuilder.Build(resources, archives, order);
                ProfileScanReceipt receipt = new(2, "Profile", DateTimeOffset.UtcNow, [], order, [], [], [], [], [], [], [], [], [], [], ArchiveSummaries: summaries);
                typeof(MainWindow).GetMethod("SetScanLocked", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [false]);
                typeof(MainWindow).GetField("_receipt", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, receipt);
                ((ArchiveOrderWorkspaceViewModel)window.DataContext).SetProposedOrder(order);
                typeof(MainWindow).GetMethod("RefreshArchiveRail", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, []);
                ArchiveConflictTreeViewModel tree = (ArchiveConflictTreeViewModel)typeof(MainWindow).GetField("_archiveTree", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                tree.Load(summaries);
                typeof(MainWindow).GetMethod("ApplyArchiveTreeFilter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, []);
                window.UpdateLayout();
                scenario(window, (ListBox)window.FindName("ArchiveOrderListBox"), order);
            }
            finally
            {
                window.Close();
                if (Directory.Exists(applicationData)) Directory.Delete(applicationData, true);
            }
        });
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
        thread.Join();
        if (failure is not null) throw failure;
    }
}

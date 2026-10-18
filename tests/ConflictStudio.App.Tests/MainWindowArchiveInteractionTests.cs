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
                Assert.AreEqual(message, evidenceText.Text);
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

using ConflictStudio.App;
using ConflictStudio.Core;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class ArchiveOrderInteractionTests
{
    private static readonly string[] ApplicableCloseActions = ["Apply and exit", "Discard", "Keep editing"];
    private static readonly string[] BlockedCloseActions = ["Discard", "Keep editing"];

    [TestMethod]
    public void DragAutoScrollAcceleratesTowardTheTopAndBottomEdges()
    {
        Assert.AreEqual(-1, ArchiveDragAutoScroll.LinesAt(63, 240));
        Assert.IsLessThan(-10, ArchiveDragAutoScroll.LinesAt(4, 240));
        Assert.AreEqual(0, ArchiveDragAutoScroll.LinesAt(120, 240));
        Assert.AreEqual(1, ArchiveDragAutoScroll.LinesAt(177, 240));
        Assert.IsGreaterThan(10, ArchiveDragAutoScroll.LinesAt(236, 240));
        int remainder = 0;
        Assert.AreEqual(0, ArchiveDragAutoScroll.ConsumeWheelDelta(ref remainder, 30));
        Assert.AreEqual(0, ArchiveDragAutoScroll.ConsumeWheelDelta(ref remainder, 30));
        Assert.AreEqual(0, ArchiveDragAutoScroll.ConsumeWheelDelta(ref remainder, 30));
        Assert.AreEqual(-6, ArchiveDragAutoScroll.ConsumeWheelDelta(ref remainder, 30));
        Assert.AreEqual(0, remainder);
        Assert.AreEqual(0, ArchiveDragAutoScroll.ConsumeWheelDelta(ref remainder, 60));
        Assert.AreEqual(0, ArchiveDragAutoScroll.ConsumeWheelDelta(ref remainder, -60));
        Assert.AreEqual(0, remainder);
        Assert.AreEqual(6, ArchiveDragAutoScroll.ConsumeWheelDelta(ref remainder, -120));
    }

    [TestMethod]
    public void ProposedOrderRebuildsTheConflictPaneBeforeApply()
    {
        Mo2Archive[] archives = [new("Alpha", "Alpha.archive", "Alpha.archive", 1, "a"), new("Beta", "Beta.archive", "Beta.archive", 1, "b")];
        ResourceProvider[] resources = [new("Alpha.archive", 7, "base\\shared.mesh", new string('a', 64), ProviderName: "Alpha"), new("Beta.archive", 7, "base\\shared.mesh", new string('b', 64), ProviderName: "Beta")];

        ArchiveConflictSummary[] preview = ArchiveConflictPreview.Build(resources, archives, ["Beta.archive", "Alpha.archive"], []);

        Assert.HasCount(1, preview.Single(value => value.ArchiveName == "Beta.archive").Winning);
        Assert.HasCount(1, preview.Single(value => value.ArchiveName == "Alpha.archive").Losing);
        Assert.AreEqual(0, preview.Single(value => value.ArchiveName == "Beta.archive").OrderPosition);

        ArchiveResourceOutcome unique = new(8, "base\\unique.mesh", ArchiveResourceDisposition.Unique, "Alpha.archive", new string('c', 64), "mesh", ResourcePathConfidence.ResolvedIndex, [], ArchivePayloadRelation.NotApplicable);
        ArchiveResourceOutcome failure = new(0, "Archive unreadable: invalid index", ArchiveResourceDisposition.Unresolved, null, null, null, ResourcePathConfidence.Unresolved, [], ArchivePayloadRelation.Unknown);
        ResourceProvider[] rehydrated = ArchiveConflictPreview.RehydrateResources([preview.Single(value => value.ArchiveName == "Alpha.archive") with { Unique = [unique], Unresolved = [failure] }, preview.Single(value => value.ArchiveName == "Beta.archive")]);

        Assert.IsTrue(rehydrated.Any(value => value.ArchiveName == "Alpha.archive" && value.ResourceHash == 8));
        Assert.IsFalse(rehydrated.Any(value => value.ResourceHash == 0));
    }

    [TestMethod]
    public void PendingOrderCloseDialogUsesExplicitActionLabels()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                ArchiveOrderCloseDialog applicable = new(true);
                ArchiveOrderCloseDialog blocked = new(false);
                CollectionAssert.AreEqual(ApplicableCloseActions, applicable.ActionLabelsForTesting.ToArray());
                CollectionAssert.AreEqual(BlockedCloseActions, blocked.ActionLabelsForTesting.ToArray());
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void WindowPreviewsAndResetsMovedArchiveWinnersAndGuardsClose()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-live-preview-" + Guid.NewGuid().ToString("N"));
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                string archiveRoot = Path.Combine(root, "archive", "pc", "mod");
                Directory.CreateDirectory(archiveRoot);
                string alphaPath = Path.Combine(archiveRoot, "Alpha.archive");
                string betaPath = Path.Combine(archiveRoot, "Beta.archive");
                File.WriteAllText(alphaPath, "alpha");
                File.WriteAllText(betaPath, "beta");
                Mo2Archive[] archives = [new("Alpha", "Alpha.archive", alphaPath, 5, Hash(alphaPath)), new("Beta", "Beta.archive", betaPath, 4, Hash(betaPath))];
                ResourceProvider[] resources = [new("Alpha.archive", 7, "base\\shared.mesh", new string('a', 64), ProviderName: "Alpha"), new("Beta.archive", 7, "base\\shared.mesh", new string('b', 64), ProviderName: "Beta")];
                string[] order = ["Alpha.archive", "Beta.archive"];
                ResourceConflict[] conflicts = ResourceConflictAnalyzer.Analyze(resources, order);
                ArchiveConflictSummary[] summaries = ArchiveResourceIndexBuilder.Build(resources, archives, order);
                ArchiveOrderEvidence evidence = new(ArchiveOrderEvidenceKind.ManagedModlist, "Game directory", Path.Combine(archiveRoot, "modlist.txt"), "managed");
                ProfileScanReceipt receipt = new ProfileScanReceipt(2, "Deployed game", DateTimeOffset.UtcNow, ["Game directory"], order, [], conflicts, [], [], [], [], [], [], [], []) with { InstallationId = ProfileInstallationIdentity.Create("Manual", root), ArchiveSummaries = summaries, ArchiveOrderEvidence = evidence, ArchiveInventory = archives, EditableArchiveInventory = archives, EditableArchiveOrder = order, EditableArchiveOrderEvidence = evidence, ManagerKind = ModManagerKind.Manual };
                MainWindow window = new(false);
                window.LoadReceiptForTesting(receipt, root);
                window.Show();
                window.UpdateLayout();

                window.MoveArchiveOrderForTesting(["Beta.archive"], "Alpha.archive");

                Assert.IsTrue(window.IsPreviewingArchiveOrderForTesting);
                Assert.HasCount(1, window.ArchiveTreeForTesting.Find("Beta.archive")!.Summary.Winning);
                StringAssert.Contains(window.ArchiveOrderEvidenceTitleTextBlock.Text, "unapplied");

                window.ResetArchiveOrderForTesting();

                Assert.IsFalse(window.IsPreviewingArchiveOrderForTesting);
                Assert.HasCount(1, window.ArchiveTreeForTesting.Find("Alpha.archive")!.Summary.Winning);

                window.MoveArchiveOrderForTesting(["Beta.archive"], "Alpha.archive");
                window.PendingArchiveClosePromptForTesting = _ => ArchiveOrderCloseAction.Cancel;
                window.Close();
                Assert.IsTrue(window.IsVisible);
                window.PendingArchiveClosePromptForTesting = _ => ArchiveOrderCloseAction.DiscardAndClose;
                window.Close();
                Assert.IsFalse(window.IsVisible);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (Directory.Exists(root)) Directory.Delete(root, true);
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void DragAutoScrollMovesARealScrollViewer()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                StackPanel content = new();
                for (int index = 0; index < 80; index++) content.Children.Add(new TextBlock { Text = index.ToString(CultureInfo.InvariantCulture), Height = 20 });
                ScrollViewer viewer = new() { Height = 100, Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
                Window window = new() { Content = viewer, Width = 200, Height = 140 };
                window.Show();
                window.UpdateLayout();

                ArchiveDragAutoScroll.Scroll(viewer, 1);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                Assert.IsGreaterThan(0, viewer.VerticalOffset);
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void MainWindowDragTimerScrollsAndStops()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-window-auto-scroll-" + Guid.NewGuid().ToString("N"));
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                MainWindow window = CreatePreviewWindow(root);
                window.ArchiveOrderListBox.ItemsSource = Enumerable.Range(0, 80).Select(value => new ArchiveRailItem($"Filler{value:D2}.archive")).ToArray();
                window.UpdateLayout();
                window.LoadOrderScrollViewerForTesting!.ScrollToVerticalOffset(20);
                window.SetArchiveDragCandidateForTesting("Alpha.archive");
                MouseWheelEventArgs ordinaryWheel = new(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
                window.ArchiveOrderListBox.RaiseEvent(ordinaryWheel);
                Assert.IsFalse(ordinaryWheel.Handled);
                window.StartArchiveDragScrollForTesting("Alpha.archive", 1);

                PumpUntil(() => window.LoadOrderScrollViewerForTesting!.VerticalOffset > 0);

                Assert.IsTrue(window.IsArchiveDragScrollActiveForTesting);
                Assert.IsGreaterThan(0, window.LoadOrderScrollViewerForTesting!.VerticalOffset);
                window.StopArchiveDragScrollForTesting();
                double stoppedOffset = window.LoadOrderScrollViewerForTesting.VerticalOffset;
                Thread.Sleep(120);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.IsFalse(window.IsArchiveDragScrollActiveForTesting);
                Assert.AreEqual(stoppedOffset, window.LoadOrderScrollViewerForTesting.VerticalOffset, 0.01);

                window.LoadOrderScrollViewerForTesting.ScrollToVerticalOffset(20);
                window.UpdateLayout();
                double beforeWheel = window.LoadOrderScrollViewerForTesting.VerticalOffset;
                MouseWheelEventArgs wheel = new(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
                window.ArchiveOrderListBox.RaiseEvent(wheel);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.IsGreaterThan(beforeWheel, window.LoadOrderScrollViewerForTesting.VerticalOffset);
                Assert.IsTrue(wheel.Handled);
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (Directory.Exists(root)) Directory.Delete(root, true);
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void CloseAppliesAndVerifiesPendingOrderBeforeExit()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-close-apply-" + Guid.NewGuid().ToString("N"));
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                MainWindow window = CreatePreviewWindow(root);
                window.MoveArchiveOrderForTesting(["Beta.archive"], "Alpha.archive");
                window.PendingArchiveClosePromptForTesting = _ => ArchiveOrderCloseAction.ApplyAndClose;

                window.Close();
                PumpUntil(() => !window.IsVisible);

                Assert.IsFalse(window.IsVisible);
                Assert.AreEqual("Beta.archive\r\nAlpha.archive\r\n", File.ReadAllText(Path.Combine(root, "archive", "pc", "mod", "modlist.txt")));
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (Directory.Exists(root)) Directory.Delete(root, true);
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void CloseApplyFailureAndBusyStateKeepTheWindowOpen()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-close-failure-" + Guid.NewGuid().ToString("N"));
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                MainWindow window = CreatePreviewWindow(root);
                window.MoveArchiveOrderForTesting(["Beta.archive"], "Alpha.archive");
                int promptCount = 0;
                window.PendingArchiveClosePromptForTesting = _ => { promptCount++; return ArchiveOrderCloseAction.ApplyAndClose; };
                window.SetScanLockedForTesting(true);

                window.Close();

                Assert.IsTrue(window.IsVisible);
                Assert.AreEqual(0, promptCount);
                window.SetScanLockedForTesting(false);
                File.Delete(Path.Combine(root, "archive", "pc", "mod", "Beta.archive"));
                window.Close();
                PumpUntil(() => !window.IsApplyingPendingOrderForCloseForTesting);

                Assert.IsTrue(window.IsVisible);
                Assert.AreEqual(Visibility.Visible, window.ErrorTextBlock.Visibility);
                window.PendingArchiveClosePromptForTesting = _ => ArchiveOrderCloseAction.DiscardAndClose;
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (Directory.Exists(root)) Directory.Delete(root, true);
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void UnreadableArchiveKeepsTheAppliedPaneAndNamesTheUnavailablePreview()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-preview-unavailable-" + Guid.NewGuid().ToString("N"));
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                MainWindow window = CreatePreviewWindow(root, [new RdarArchiveFailure("Beta", "Beta.archive", "Invalid RDAR index")]);
                window.MoveArchiveOrderForTesting(["Beta.archive"], "Alpha.archive");

                StringAssert.Contains(window.ArchiveOrderEvidenceTitleTextBlock.Text, "Preview unavailable");
                StringAssert.Contains(window.ArchiveConflictCountTextBlock.Text, "applied results shown");
                Assert.IsFalse(window.IsPreviewingArchiveOrderForTesting);
                Assert.IsFalse(window.CanApplyArchiveOrderForTesting);
                bool? closeCanApply = null;
                window.PendingArchiveClosePromptForTesting = canApply => { closeCanApply = canApply; return ArchiveOrderCloseAction.DiscardAndClose; };
                window.Close();
                Assert.AreEqual(false, closeCanApply);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (Directory.Exists(root)) Directory.Delete(root, true);
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void IncompleteArchiveOrderLoadsReadOnlyInsteadOfAbortingTheWorkspace()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-incomplete-order-window-" + Guid.NewGuid().ToString("N"));
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                MainWindow window = CreatePreviewWindow(root, incompleteOrder: true);

                Assert.HasCount(2, window.ProposedArchiveOrderForTesting);
                Assert.HasCount(2, window.ArchiveTreeForTesting.VisibleArchives);
                Assert.IsFalse(window.CanApplyArchiveOrderForTesting);
                StringAssert.Contains(window.ArchiveOrderEvidenceTitleTextBlock.Text, "blocked");
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (Directory.Exists(root)) Directory.Delete(root, true);
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static MainWindow CreatePreviewWindow(string root, RdarArchiveFailure[]? failures = null, bool incompleteOrder = false)
    {
        string archiveRoot = Path.Combine(root, "archive", "pc", "mod");
        Directory.CreateDirectory(archiveRoot);
        string alphaPath = Path.Combine(archiveRoot, "Alpha.archive");
        string betaPath = Path.Combine(archiveRoot, "Beta.archive");
        File.WriteAllText(alphaPath, "alpha");
        File.WriteAllText(betaPath, "beta");
        Mo2Archive[] archives = [new("Alpha", "Alpha.archive", alphaPath, 5, Hash(alphaPath)), new("Beta", "Beta.archive", betaPath, 4, Hash(betaPath))];
        ResourceProvider[] resources = [new("Alpha.archive", 7, "base\\shared.mesh", new string('a', 64), ProviderName: "Alpha"), new("Beta.archive", 7, "base\\shared.mesh", new string('b', 64), ProviderName: "Beta")];
        string[] order = ["Alpha.archive", "Beta.archive"];
        RdarArchiveFailure[] archiveFailures = failures ?? [];
        ResourceConflict[] conflicts = ResourceConflictAnalyzer.Analyze(resources, order);
        ArchiveConflictSummary[] summaries = ArchiveResourceIndexBuilder.Build(resources, archives, archiveFailures.Length == 0 && !incompleteOrder ? order : [], archiveFailures);
        string orderPath = Path.Combine(archiveRoot, "modlist.txt");
        if (incompleteOrder) File.WriteAllText(orderPath, "Alpha.archive\r\n");
        ArchiveOrderEvidence evidence = incompleteOrder
            ? new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.Unresolved, "Game directory", orderPath, "Archive order is missing Beta.archive.") { MissingEntries = ["Beta.archive"], ProblemLane = ArchiveOrderProblemLane.Legacy }
            : new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.ManagedModlist, "Game directory", orderPath, "managed");
        ProfileScanReceipt receipt = new ProfileScanReceipt(2, "Deployed game", DateTimeOffset.UtcNow, ["Game directory"], order, archiveFailures, conflicts, [], [], [], [], [], [], [], []) with { InstallationId = ProfileInstallationIdentity.Create("Manual", root), ArchiveSummaries = summaries, ArchiveOrderEvidence = evidence, ArchiveInventory = archives, EditableArchiveInventory = archives, EditableArchiveOrder = order, EditableArchiveOrderEvidence = evidence, ManagerKind = ModManagerKind.Manual };
        MainWindow window = new(false);
        window.LoadReceiptForTesting(receipt, root);
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static void PumpUntil(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Thread.Sleep(10);
        }
        Assert.IsTrue(condition());
    }

    private static string Hash(string path) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
}

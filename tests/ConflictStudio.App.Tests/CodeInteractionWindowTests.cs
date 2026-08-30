using ConflictStudio.App;
using ConflictStudio.Core;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class CodeInteractionWindowTests
{
    [TestMethod]
    public void WindowLoadsVortexBridgeContextAsOneManagerProfile()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-window-" + Guid.NewGuid().ToString("N"));
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                string game = Path.Combine(root, "game");
                string staging = Path.Combine(root, "staging");
                Directory.CreateDirectory(game);
                Directory.CreateDirectory(staging);
                string contextPath = Path.Combine(root, "context.json");
                VortexManagerContext context = new(1, new string('a', 64), DateTimeOffset.UtcNow, "profile", "Vortex Profile", game, staging, true, [], [], [], null);
                File.WriteAllText(contextPath, System.Text.Json.JsonSerializer.Serialize(context));
                MainWindow window = new(false);

                window.LoadVortexContextForTesting(contextPath);

                Assert.AreEqual("VORTEX BRIDGE CONTEXT", window.ManagerLocationLabel.Text);
                Assert.AreEqual(Path.GetFullPath(contextPath), window.Mo2RootTextBox.Text);
                Assert.AreEqual("Vortex Profile", window.ProfileComboBox.SelectedItem?.ToString());
                Assert.AreEqual("Vortex", (window.ManagerModeComboBox.SelectedItem as ComboBoxItem)?.Tag);
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (Directory.Exists(root)) Directory.Delete(root, true);
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void TestWindowDoesNotRecordProductionStartupActivity()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                MainWindow window = new(false);

                Assert.AreEqual(0, window.SessionActionsForTesting.Count);
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void MaterialActionFailureIsVisibleAndRecorded()
    {
        Exception? failure = null;
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-window-log-" + Guid.NewGuid().ToString("N"));
        Thread thread = new(() =>
        {
            try
            {
                MainWindow window = new(false, root);

                window.ExecuteForTesting("test-button", () => throw new InvalidOperationException("test failure"));

                DiagnosticAction action = window.SessionActionsForTesting[^1];
                Assert.AreEqual("test-button", action.Operation);
                Assert.AreEqual("failed", action.Outcome);
                Assert.AreEqual(Visibility.Visible, window.ErrorTextBlock.Visibility);
                StringAssert.Contains(window.ErrorTextBlock.Text, "Open Support");
                Assert.IsFalse(File.Exists(Path.Combine(root, "diagnostics.log")));
                Assert.IsFalse(File.Exists(Path.Combine(root, "activity.log")));
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (Directory.Exists(root)) Directory.Delete(root, true);
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void SupportingTextAreasExposeAccessibleNamesAndFilterResetStaysOutOfActionTrail()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                MainWindow window = new(false);
                window.Show();
                int before = window.SessionActionsForTesting.Count;

                window.ClearArchiveFiltersButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.AreEqual(before, window.SessionActionsForTesting.Count);
                Assert.AreEqual("Selected technical evidence", AutomationProperties.GetName(window.ExpertEvidenceTextBox));
                Assert.AreEqual("Current support report", AutomationProperties.GetName(window.DiagnosticsTextBox));
                Assert.AreEqual("Raw application error log", AutomationProperties.GetName(window.HistoricalDiagnosticsTextBox));
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void WindowDefaultsToActionableCasesAndSupportsProviderLensAndEvidenceDetail()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                ConflictWorkItem blocker = Item("Blocked.Method()", EvidenceClassification.Exclusive, "Alpha", "Beta");
                ConflictWorkItem check = Item("Risky.Method()", EvidenceClassification.Review, "Beta", "Gamma");
                ConflictWorkItem compose = Item("Shared.Observer", EvidenceClassification.Composable, "Alpha", "Gamma");
                ConflictWorkItem shared = Item("UI_State", EvidenceClassification.Informational, "Delta", "Beta");
                MainWindow window = new(false);
                window.Show();
                window.LoadCodeCasesForTesting([blocker, check, compose, shared]);
                DrainDispatcher();

                Assert.AreEqual("1", window.AttentionCountTextBlock.Text);
                Assert.AreEqual("1", window.ReviewCountTextBlock.Text);
                Assert.AreEqual("2", window.NoActionCountTextBlock.Text);
                Assert.AreEqual(2, ((IEnumerable<ConflictWorkItem>)window.WorkQueueDataGrid.ItemsSource).Count());

                window.QueueProviderComboBox.SelectedItem = "Beta";
                DrainDispatcher();
                Assert.IsTrue(((IEnumerable<ConflictWorkItem>)window.WorkQueueDataGrid.ItemsSource).All(value => value.Providers.Contains("Beta")));

                window.WorkQueueDataGrid.SelectedItem = check;
                DrainDispatcher();
                Assert.AreEqual(check.ClassificationLabel, window.SelectedClassificationTextBlock.Text);
                Assert.AreEqual(check.ProofLabel, window.SelectedProofTextBlock.Text);
                Assert.AreEqual(check.NextAction, window.SelectedActionTextBlock.Text);
                Assert.AreEqual(check.Providers.Length, window.SelectedProviderComboBox.Items.Count);
                Assert.IsTrue(window.CopyEvidenceButton.IsEnabled);

                ComboBoxItem compatible = window.QueueViewComboBox.Items.Cast<ComboBoxItem>().Single(value => Equals(value.Tag, "Compatible"));
                window.QueueProviderComboBox.SelectedIndex = 0;
                window.QueueViewComboBox.SelectedItem = compatible;
                DrainDispatcher();
                Assert.AreEqual(2, ((IEnumerable<ConflictWorkItem>)window.WorkQueueDataGrid.ItemsSource).Count());
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void CodeDetailsRemainScrollableWhenTheWindowHasLimitedHeight()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                MainWindow window = new(false) { Height = 760 };
                window.Show();
                window.MainTabControl.SelectedIndex = 1;
                DrainDispatcher();

                Grid codeGrid = (Grid)((TabItem)window.MainTabControl.Items[1]).Content;
                Assert.IsTrue(codeGrid.RowDefinitions[3].Height.IsStar);
                Assert.AreEqual(GridUnitType.Pixel, codeGrid.RowDefinitions[4].Height.GridUnitType);
                Assert.IsTrue(codeGrid.RowDefinitions[5].Height.IsStar);
                Assert.AreEqual(ScrollBarVisibility.Auto, window.CodeCaseDetailScrollViewer.VerticalScrollBarVisibility);
                Assert.AreEqual(GridResizeDirection.Rows, window.CodeDetailSplitter.ResizeDirection);
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void CompletedScanDoesNotLookLikeTheLastPhaseIsStillRunning()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                MainWindow window = new(false);
                window.ScanPhaseTextBlock.Text = "ArchiveXL · 1/1";

                window.CompleteScanPresentationForTesting();

                Assert.AreEqual("Complete", window.ScanPhaseTextBlock.Text);
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void ScanLockBlocksInputWithoutApplyingDisabledSystemColors()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                MainWindow window = new(false);
                window.Show();
                Brush archiveBackground = window.ArchiveOrderListBox.Background;

                window.SetScanLockedForTesting(true);

                Assert.IsTrue(window.ArchiveOrderTabContent.IsEnabled);
                Assert.IsFalse(window.ArchiveOrderTabContent.IsHitTestVisible);
                Assert.IsTrue(window.ManagerModeComboBox.IsEnabled);
                Assert.IsFalse(window.ManagerModeComboBox.IsHitTestVisible);
                Assert.IsTrue(window.Mo2RootTextBox.IsEnabled);
                Assert.IsFalse(window.Mo2RootTextBox.IsHitTestVisible);
                Assert.IsTrue(window.ScanProfileButton.IsEnabled);
                Assert.IsFalse(window.ScanProfileButton.IsHitTestVisible);
                Assert.IsTrue(window.CancelScanButton.IsEnabled);
                Assert.AreSame(archiveBackground, window.ArchiveOrderListBox.Background);
                window.ExecuteForTesting("test-command", () => Assert.Fail("A command ran while the workspace was locked."));
                InvokeLockedHandler(window, "ApplyClicked");
                InvokeLockedHandler(window, "UndoArchiveOrderClicked");
                InvokeLockedHandler(window, "ArchiveOrderMouseDown");
                string[] blockedOperations = ["test-command", "archive-apply", "archive-undo", "archive-drag"];
                CollectionAssert.AreEquivalent(blockedOperations, window.SessionActionsForTesting.Select(value => value.Operation).ToArray());
                Assert.IsTrue(window.SessionActionsForTesting.All(value => value.Outcome == "blocked"));

                window.SetScanLockedForTesting(false);

                Assert.IsTrue(window.ArchiveOrderTabContent.IsHitTestVisible);
                Assert.IsTrue(window.ManagerModeComboBox.IsHitTestVisible);
                Assert.IsTrue(window.ScanProfileButton.IsHitTestVisible);
                Assert.IsFalse(window.CancelScanButton.IsEnabled);

                window.SetArchiveOperationLockedForTesting(true);
                Assert.IsFalse(window.ArchiveOrderTabContent.IsHitTestVisible);
                Assert.IsFalse(window.ManagerModeComboBox.IsHitTestVisible);
                Assert.IsFalse(window.CancelScanButton.IsEnabled);
                int beforeOperationAttempt = window.SessionActionsForTesting.Count;
                window.ExecuteForTesting("operation-overlap", () => Assert.Fail("A command ran during an archive write."));
                Assert.AreEqual(beforeOperationAttempt + 1, window.SessionActionsForTesting.Count);
                Assert.AreEqual("blocked", window.SessionActionsForTesting[^1].Outcome);
                window.SetArchiveOperationLockedForTesting(false);
                Assert.IsTrue(window.ArchiveOrderTabContent.IsHitTestVisible);
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void CrossManagerBlockClearsThePreviousReceiptAndActions()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                ConflictWorkItem item = Item("PlayerPuppet.OnAction", EvidenceClassification.OrderSensitive, "Alpha", "Beta");
                MainWindow window = new(false);
                window.Show();
                window.LoadCodeCasesForTesting([item]);
                window.ExportButton.IsEnabled = true;
                Assert.IsNotNull(window.WorkQueueDataGrid.ItemsSource);

                window.HandleScanFailureForTesting(new CrossManagerDeploymentException("Purge Vortex."));

                Assert.AreEqual(0, window.WorkQueueDataGrid.Items.Count);
                Assert.AreEqual(0, window.ArchiveOrderListBox.Items.Count);
                Assert.IsFalse(window.ExportButton.IsEnabled);
                Assert.AreEqual(Visibility.Visible, window.ErrorTextBlock.Visibility);
                StringAssert.Contains(window.ErrorTextBlock.Text, "Purge Vortex");
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void InvokeLockedHandler(MainWindow window, string method)
    {
        MethodInfo handler = typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingMethodException(method);
        handler.Invoke(window, [window, null]);
    }

    [TestMethod]
    public void CodeWorkspaceUsesClearResultColorAndReachableWideValues()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                ConflictWorkItem check = Item("Vendors.Test.itemStock", EvidenceClassification.OrderSensitive, "Alpha", "Beta");
                MainWindow window = new(false);
                window.Show();
                window.MainTabControl.SelectedIndex = 1;
                window.LoadCodeCasesForTesting([check]);
                DrainDispatcher();

                Assert.AreEqual(ScrollBarVisibility.Auto, ScrollViewer.GetHorizontalScrollBarVisibility(window.WorkQueueDataGrid));
                DataGridRow row = (DataGridRow)window.WorkQueueDataGrid.ItemContainerGenerator.ContainerFromItem(check)!;
                TextBlock result = FindDescendants<TextBlock>(row).First(value => value.Text == check.ClassificationLabel);
                DataGridCell selectedCell = FindDescendants<DataGridCell>(row).First();
                Assert.AreEqual(Color.FromRgb(7, 90, 102), ((SolidColorBrush)selectedCell.Background).Color);
                Assert.AreEqual(Colors.White, ((SolidColorBrush)result.Foreground).Color);
                window.WorkQueueDataGrid.SelectedIndex = -1;
                DrainDispatcher();
                Assert.AreNotEqual(Colors.White, ((SolidColorBrush)result.Foreground).Color);
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void AdvancedExportLivesInSupportAndHeaderHasNoOrnamentalClaim()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                MainWindow window = new(false);
                window.Show();
                window.MainTabControl.SelectedItem = window.SupportTab;
                DrainDispatcher();

                Assert.AreEqual("Export support bundle", window.ExportButton.Content);
                Assert.IsTrue(FindLogicalDescendants<Button>((DependencyObject)window.SupportTab.Content).Contains(window.ExportButton));
                Assert.IsFalse(window.ExportButton.ToolTip?.ToString()?.Contains("bounded", StringComparison.OrdinalIgnoreCase) ?? false);
                Assert.IsFalse(FindDescendants<TextBlock>(window).Any(value => value.Text.Contains("Local only", StringComparison.OrdinalIgnoreCase)));
                Assert.AreEqual("Select a row to see details", window.SelectedTargetTextBlock.Text);
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void TechnicalEvidenceIsAnInlineCodeDetailNotAPrimaryTab()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                ConflictWorkItem item = Item("DamageSystem.ProcessHit()", EvidenceClassification.Review, "Alpha", "Beta");
                MainWindow window = new(false);
                window.Show();
                window.MainTabControl.SelectedIndex = 1;
                window.LoadCodeCasesForTesting([item]);
                DrainDispatcher();

                Assert.IsFalse(window.MainTabControl.Items.Cast<TabItem>().Any(value => string.Equals(value.Header?.ToString(), "Technical evidence", StringComparison.Ordinal)));
                Assert.IsTrue(FindLogicalDescendants<Expander>((DependencyObject)((TabItem)window.MainTabControl.Items[1]).Content).Contains(window.TechnicalEvidenceExpander));
                Assert.IsFalse(window.TechnicalEvidenceExpander.IsExpanded);

                window.ShowTechnicalEvidenceForTesting();

                Assert.IsTrue(window.TechnicalEvidenceExpander.IsExpanded);
                Assert.IsTrue(window.CopyEvidenceButton.IsEnabled);
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void CodeGridExplainsAndShowsColumnResizeAffordance()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                MainWindow window = new(false);
                window.Show();
                window.MainTabControl.SelectedIndex = 1;
                DrainDispatcher();

                Assert.IsTrue(window.WorkQueueDataGrid.CanUserResizeColumns);
                Assert.AreEqual(DataGridGridLinesVisibility.All, window.WorkQueueDataGrid.GridLinesVisibility);
                StringAssert.Contains(window.ColumnResizeHint.Text, "Drag");
                DataGridColumnHeader header = FindDescendants<DataGridColumnHeader>(window.WorkQueueDataGrid).First();
                Assert.AreEqual(Visibility.Visible, header.SeparatorVisibility);
                Assert.IsGreaterThanOrEqualTo(1, header.BorderThickness.Right);
                Assert.IsNotNull(header.ToolTip);
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void CyberpunkPaletteKeepsRuntimeChecksAndOrderSensitiveRowsDistinct()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                ConflictWorkItem runtime = Item("Runtime.Method()", EvidenceClassification.Review, "Alpha", "Beta");
                ConflictWorkItem ordered = Item("Items.Value", EvidenceClassification.OrderSensitive, "Alpha", "Beta");
                MainWindow window = new(false);
                window.Show();
                window.MainTabControl.SelectedIndex = 1;
                window.LoadCodeCasesForTesting([runtime, ordered]);
                window.WorkQueueDataGrid.SelectedIndex = -1;
                DrainDispatcher();

                DataGridRow runtimeRow = (DataGridRow)window.WorkQueueDataGrid.ItemContainerGenerator.ContainerFromItem(runtime)!;
                DataGridRow orderedRow = (DataGridRow)window.WorkQueueDataGrid.ItemContainerGenerator.ContainerFromItem(ordered)!;

                Assert.AreEqual(Color.FromRgb(0xFC, 0xEE, 0x0A), ((SolidColorBrush)runtimeRow.BorderBrush).Color);
                Assert.AreEqual(Color.FromRgb(0xFF, 0x7A, 0x00), ((SolidColorBrush)orderedRow.BorderBrush).Color);
                Assert.AreNotEqual(((SolidColorBrush)runtimeRow.Background).Color, ((SolidColorBrush)orderedRow.Background).Color);
                Assert.AreEqual(Color.FromRgb(0x00, 0xF0, 0xFF), ((SolidColorBrush)window.Resources["Accent"]).Color);
                Assert.AreEqual(Color.FromRgb(0x07, 0x0B, 0x0F), ((SolidColorBrush)window.Background).Color);
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void ScanHistoryDeltaExplainsItsComparisonAndPlainMeaning()
    {
        ProfileScanDrift drift = new([], [], [], [], [], [], [], [], [], new ConflictWorkItem[171], new ConflictWorkItem[138], new ConflictWorkItemChange[83]);

        string summary = ScanHistoryPresentation.Describe(drift);

        Assert.AreEqual("Since previous scan: 171 added · 83 updated · 138 no longer present", summary);
    }

    [TestMethod]
    public void MultipleCodeRowsShowABulkReviewStateInsteadOfOneRowsEvidence()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                ConflictWorkItem first = Item("First.Method()", EvidenceClassification.Review, "Alpha", "Beta");
                ConflictWorkItem second = Item("Second.Method()", EvidenceClassification.OrderSensitive, "Beta", "Gamma") with { ReviewRationale = "Ignore for this profile: checked" };
                MainWindow window = new(false);
                window.Show();
                window.MainTabControl.SelectedIndex = 1;
                window.LoadCodeCasesForTesting([first, second]);
                window.WorkQueueDataGrid.SelectedItems.Clear();
                window.WorkQueueDataGrid.SelectedItems.Add(first);
                window.WorkQueueDataGrid.SelectedItems.Add(second);
                DrainDispatcher();

                Assert.AreEqual(DataGridSelectionMode.Extended, window.WorkQueueDataGrid.SelectionMode);
                Assert.AreEqual("MULTIPLE CASES", window.SelectedClassificationTextBlock.Text);
                Assert.AreEqual("2 cases selected", window.SelectedTargetTextBlock.Text);
                StringAssert.Contains(window.SelectedSummaryTextBlock.Text, first.ClassificationLabel);
                StringAssert.Contains(window.SelectedSummaryTextBlock.Text, second.ClassificationLabel);
                StringAssert.Contains(window.SelectedProvidersTextBlock.Text, "3 mods");
                Assert.AreEqual("Save review (2)", window.SaveReviewButton.Content);
                Assert.AreEqual("Reopen (1)", window.ReopenButton.Content);
                Assert.IsTrue(window.ReopenButton.IsEnabled);
                Assert.IsTrue(window.CopyEvidenceButton.IsEnabled);
                StringAssert.Contains(window.ExpertEvidenceTextBox.Text, first.Target);
                StringAssert.Contains(window.ExpertEvidenceTextBox.Text, second.Target);
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void BulkReviewAndReopenPersistEverySelectedCaseInOneOperation()
    {
        Exception? failure = null;
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-bulk-review-" + Guid.NewGuid().ToString("N"));
        Thread thread = new(() =>
        {
            try
            {
                string decisions = Path.Combine(root, "decisions");
                ConflictWorkItem first = Item("First.Method()", EvidenceClassification.Review, "Alpha", "Beta");
                ConflictWorkItem second = Item("Second.Method()", EvidenceClassification.OrderSensitive, "Beta", "Gamma");
                ProfileScanReceipt receipt = new(1, "Standard", DateTimeOffset.UtcNow, ["Alpha", "Beta", "Gamma"], [], [], [], [], [], [], [], [], [], [], [], InstallationId: "install");
                MainWindow window = new(false, root, decisions);
                window.Show();
                window.MainTabControl.SelectedIndex = 1;
                window.LoadCodeCasesForTesting([first, second], receipt);
                window.WorkQueueDataGrid.SelectedItems.Clear();
                window.WorkQueueDataGrid.SelectedItems.Add(first);
                window.WorkQueueDataGrid.SelectedItems.Add(second);
                DrainDispatcher();

                int saved = window.SaveSelectedReviewsAndReloadForTesting("Ignore for this profile", "same outcome");

                Assert.AreEqual(2, saved);
                Assert.AreEqual(2, new EvidenceDecisionStore(decisions).Load().Length);
                Assert.AreEqual(0, ((IEnumerable<ConflictWorkItem>)window.WorkQueueDataGrid.ItemsSource).Count());
                Assert.AreEqual("Saved review for 2 cases", window.WorkspaceStatusTextBlock.Text);

                ConflictWorkItem reviewedFirst = first with { Classification = EvidenceClassification.Intentional, State = ConflictWorkState.Reviewed, ReviewRationale = "Ignore for this profile: same outcome" };
                ConflictWorkItem reviewedSecond = second with { Classification = EvidenceClassification.Intentional, State = ConflictWorkState.Reviewed, ReviewRationale = "Ignore for this profile: same outcome" };
                window.LoadCodeCasesForTesting([reviewedFirst, reviewedSecond], receipt);
                window.QueueViewComboBox.SelectedItem = window.QueueViewComboBox.Items.Cast<ComboBoxItem>().Single(value => Equals(value.Tag, "All"));
                DrainDispatcher();
                window.WorkQueueDataGrid.SelectedItems.Clear();
                window.WorkQueueDataGrid.SelectedItems.Add(reviewedFirst);
                window.WorkQueueDataGrid.SelectedItems.Add(reviewedSecond);
                DrainDispatcher();

                int reopened = window.ReopenSelectedReviewsAndReloadForTesting();

                Assert.AreEqual(2, reopened);
                Assert.AreEqual(0, new EvidenceDecisionStore(decisions).Load().Length);
                Assert.AreEqual(0, ((IEnumerable<ConflictWorkItem>)window.WorkQueueDataGrid.ItemsSource).Count());
                Assert.AreEqual("Reopened 2 cases", window.WorkspaceStatusTextBlock.Text);
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (Directory.Exists(root)) Directory.Delete(root, true);
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void CodeOverviewCollapsesToGiveTheTableMoreSpace()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                MainWindow window = new(false) { Height = 760 };
                window.Show();
                window.MainTabControl.SelectedIndex = 1;
                DrainDispatcher();
                double expandedTableHeight = window.WorkQueueDataGrid.ActualHeight;

                window.CodeSummaryExpander.IsExpanded = false;
                window.UpdateLayout();
                DrainDispatcher();

                Assert.IsGreaterThan(expandedTableHeight, window.WorkQueueDataGrid.ActualHeight);
                Assert.IsLessThan(55, window.CodeSummaryExpander.ActualHeight);
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void CodeWorkspaceUsesSlimNonObscuringScrollbars()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                ConflictWorkItem[] items = Enumerable.Range(0, 80).Select(index => Item($"Method{index}()", EvidenceClassification.Review, "Alpha", "Beta")).ToArray();
                MainWindow window = new(false) { Width = 1180 };
                window.Show();
                window.MainTabControl.SelectedIndex = 1;
                window.LoadCodeCasesForTesting(items);
                window.UpdateLayout();
                DrainDispatcher();

                ScrollBar[] bars = FindDescendants<ScrollBar>(window.WorkQueueDataGrid).Where(value => value.Visibility == Visibility.Visible).ToArray();
                Assert.IsGreaterThanOrEqualTo(2, bars.Length);
                Assert.IsTrue(bars.Where(value => value.Orientation == Orientation.Vertical).All(value => value.ActualWidth <= 10));
                Assert.IsTrue(bars.Where(value => value.Orientation == Orientation.Horizontal).All(value => value.ActualHeight <= 10));
                foreach (ScrollBar bar in bars)
                {
                    Thumb thumb = FindDescendants<Thumb>(bar).First();
                    Border body = FindDescendants<Border>(thumb).First();
                    RepeatButton[] pageButtons = FindDescendants<RepeatButton>(bar).ToArray();
                    Assert.IsLessThanOrEqualTo(0.5, body.Opacity);
                    Assert.AreEqual(2, pageButtons.Length);
                    Assert.IsTrue(pageButtons.All(value => value.Template.Triggers.Count == 0));
                    Assert.AreEqual(1, RoutedHandlerMethods(bar, UIElement.PreviewMouseLeftButtonDownEvent).Count(value => value == "ScrollbarPreviewMouseLeftButtonDown"));
                    bar.Maximum = 1000;
                    Assert.IsTrue(MainWindow.ApplyScrollbarPointer(bar, new RepeatButton(), new Point(90, 25), new Size(100, 100)));
                    Assert.AreEqual(bar.Orientation == Orientation.Horizontal ? 900 : 250, bar.Value, 0.01);
                }

                window.TechnicalEvidenceExpander.IsExpanded = true;
                window.UpdateLayout();
                DrainDispatcher();
                ScrollBar[] detailBars = FindDescendants<ScrollBar>(window.CodeCaseDetailScrollViewer).ToArray();
                Assert.IsGreaterThanOrEqualTo(1, detailBars.Length);
                foreach (ScrollBar bar in detailBars)
                {
                    Assert.AreEqual(1, RoutedHandlerMethods(bar, UIElement.PreviewMouseLeftButtonDownEvent).Count(value => value == "ScrollbarPreviewMouseLeftButtonDown"));
                    Assert.IsTrue(FindDescendants<RepeatButton>(bar).All(value => value.Template.Triggers.Count == 0));
                }
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static ConflictWorkItem Item(string target, EvidenceClassification classification, params string[] providers)
    {
        ConflictWorkState state = classification is EvidenceClassification.Composable or EvidenceClassification.Informational ? ConflictWorkState.NoActionNeeded : classification == EvidenceClassification.Exclusive ? ConflictWorkState.NeedsAttention : ConflictWorkState.ReviewWhenRelevant;
        return new ConflictWorkItem(ConflictSurface.ScriptAndTweak, target, classification, state, "What happens", "Next step", null, providers, new string('a', 64));
    }

    private static void DrainDispatcher() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static string[] RoutedHandlerMethods(UIElement element, RoutedEvent routedEvent)
    {
        PropertyInfo storeProperty = typeof(UIElement).GetProperty("EventHandlersStore", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new InvalidOperationException("WPF event handler storage is unavailable.");
        object store = storeProperty.GetValue(element) ?? throw new InvalidOperationException("The element has no routed event handlers.");
        MethodInfo getHandlers = store.GetType().GetMethod("GetRoutedEventHandlers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? throw new InvalidOperationException("WPF routed handler lookup is unavailable.");
        RoutedEventHandlerInfo[] handlers = (RoutedEventHandlerInfo[])(getHandlers.Invoke(store, [routedEvent]) ?? Array.Empty<RoutedEventHandlerInfo>());
        return handlers.Select(value => value.Handler.Method.Name).ToArray();
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typed) yield return typed;
            foreach (T nested in FindDescendants<T>(child)) yield return nested;
        }
    }

    private static IEnumerable<T> FindLogicalDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (object child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is T typed) yield return typed;
            if (child is DependencyObject dependency)
            {
                foreach (T nested in FindLogicalDescendants<T>(dependency)) yield return nested;
            }
        }
    }
}

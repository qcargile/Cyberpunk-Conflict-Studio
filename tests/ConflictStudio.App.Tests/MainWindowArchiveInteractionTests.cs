using ConflictStudio.App;
using ConflictStudio.Core;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class MainWindowArchiveInteractionTests
{
    private static readonly string[] ExpectedMultiSelection = ["Alpha.archive", "Beta.archive"];
    private static readonly string[] ExpectedRestoredOrder = ["Alpha.archive", "Beta.archive", "Gamma.archive"];

    [TestMethod]
    public void OverviewScrollbarThumbLetsConflictMarkersRemainVisible()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                MainWindow window = new(false);
                window.Show();
                window.LoadOrderScrollBar.ApplyTemplate();
                Thumb thumb = FindDescendant<Thumb>(window.LoadOrderScrollBar) ?? throw new InvalidOperationException("The overview scrollbar thumb was not rendered.");
                thumb.ApplyTemplate();
                Border border = FindDescendant<Border>(thumb) ?? throw new InvalidOperationException("The overview scrollbar thumb border was not rendered.");
                RepeatButton[] trackButtons = FindDescendants<RepeatButton>(window.LoadOrderScrollBar).ToArray();

                Assert.IsLessThan(0.7, border.Opacity);
                Assert.AreEqual(0, thumb.Template.Triggers.Count);
                Assert.AreEqual(2, trackButtons.Length);
                Assert.IsTrue(trackButtons.All(value => value.Template.Triggers.Count == 0));
                Assert.AreEqual(10, window.LoadOrderScrollBar.Width);
                Assert.AreEqual(Colors.White, ArchiveOverviewBar.SelectionMarkerColor);
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
    public void ArchiveScrollbarTrackClickMapsToTheClickedPosition()
    {
        Assert.AreEqual(900, ArchiveScrollbarNavigation.OffsetAtPointer(90, 100, 1000), 0.01);
        Assert.AreEqual(0, ArchiveScrollbarNavigation.OffsetAtPointer(-10, 100, 1000), 0.01);
        Assert.AreEqual(1000, ArchiveScrollbarNavigation.OffsetAtPointer(110, 100, 1000), 0.01);
    }

    [TestMethod]
    public void ArchiveScrollbarRoutedBehaviorJumpsOnTrackAndPreservesThumbInput()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                MainWindow window = new(false);
                window.Show();
                DrainDispatcher();
                ScrollBar scrollBar = window.LoadOrderScrollBar;
                scrollBar.Maximum = 1000;

                Assert.AreEqual(1, RoutedHandlerMethods(scrollBar, UIElement.PreviewMouseLeftButtonDownEvent).Count(value => value == "ScrollbarPreviewMouseLeftButtonDown"));

                bool trackHandled = MainWindow.ApplyScrollbarPointer(scrollBar, new RepeatButton(), 90, 100);

                Assert.IsTrue(trackHandled);
                Assert.AreEqual(900, scrollBar.Value, 0.01);

                bool thumbHandled = MainWindow.ApplyScrollbarPointer(scrollBar, new Thumb(), 10, 100);

                Assert.IsFalse(thumbHandled);
                Assert.AreEqual(900, scrollBar.Value, 0.01);
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
    public void SelectingANonConflictingArchiveDoesNotChangeTheUserFilter()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                ArchiveConflictSummary noConflict = new("Quiet.archive", "Quiet Mod", 0, [], [], [], [], [new ArchiveResourceOutcome(1, "base\\quiet.mesh", ArchiveResourceDisposition.Unique, null, new string('a', 64), "mesh", ResourcePathConfidence.ResolvedIndex, [], ArchivePayloadRelation.NotApplicable)]);
                MainWindow window = new(false);
                window.ArchiveTreeForTesting.Load([noConflict]);
                window.ArchiveTreeForTesting.Filter(string.Empty, string.Empty, false);
                ArchiveRailItem rail = new("Quiet.archive");
                window.ArchiveOrderListBox.ItemsSource = new[] { rail };
                window.ArchiveConflictTreeView.ItemsSource = window.ArchiveTreeForTesting.VisibleArchives;
                window.Show();

                window.ArchiveOrderListBox.SelectedItem = rail;
                DrainDispatcher();

                Assert.IsFalse(window.ShowNonConflictingFilesCheckBox.IsChecked == true);
                Assert.AreEqual("Quiet.archive", SelectedArchiveName(window));
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
    public void ArchiveOutcomeColumnDoesNotMoveWithTheProviderSubtitle()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                ArchiveResourceOutcome outcome = new(1, "base\\shared.mesh", ArchiveResourceDisposition.Winning, "Other.archive", new string('a', 64), "mesh", ResourcePathConfidence.ResolvedIndex, ["Other.archive"], ArchivePayloadRelation.Different);
                ArchiveConflictSummary shortProvider = new("Alpha.archive", "A", 0, [outcome], [], [], [], []);
                ArchiveConflictSummary longProvider = new("Always_Best_Quality.archive", "Always Best Quality _ Ads - Map - Hud - Photo Mode - Vending Machines and more", 1, [outcome], [], [], [], []);
                MainWindow window = new(false);
                window.ArchiveTreeForTesting.Load([shortProvider, longProvider]);
                window.ArchiveTreeForTesting.Filter(string.Empty, string.Empty, false);
                window.ArchiveConflictTreeView.ItemsSource = window.ArchiveTreeForTesting.VisibleArchives;
                window.Show();
                window.UpdateLayout();

                TreeViewItem first = (TreeViewItem)window.ArchiveConflictTreeView.ItemContainerGenerator.ContainerFromIndex(0)!;
                TreeViewItem second = (TreeViewItem)window.ArchiveConflictTreeView.ItemContainerGenerator.ContainerFromIndex(1)!;
                Ellipse firstDot = FindDescendant<Ellipse>(first) ?? throw new InvalidOperationException("First archive dot was not rendered.");
                Ellipse secondDot = FindDescendant<Ellipse>(second) ?? throw new InvalidOperationException("Second archive dot was not rendered.");
                double firstX = firstDot.TranslatePoint(new Point(), window.ArchiveConflictTreeView).X;
                double secondX = secondDot.TranslatePoint(new Point(), window.ArchiveConflictTreeView).X;

                Assert.AreEqual(firstX, secondX, 1.0);
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
    public void ArchiveDropCueMatchesTheActualBlockInsertionSide()
    {
        string[] order = ["A.archive", "B.archive", "C.archive", "D.archive"];

        Assert.AreEqual(ArchiveDropCue.After, ArchiveDropCueProjection.For(order, ["A.archive"], "C.archive"));
        Assert.AreEqual(ArchiveDropCue.Before, ArchiveDropCueProjection.For(order, ["D.archive"], "B.archive"));
        Assert.AreEqual(ArchiveDropCue.None, ArchiveDropCueProjection.For(order, ["B.archive", "D.archive"], "C.archive"));
        Assert.AreEqual(ArchiveDropCue.None, ArchiveDropCueProjection.For(order, ["B.archive"], "B.archive"));
    }

    [TestMethod]
    public void ArchiveDropCueRendersAVisibleInsertionLine()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                ArchiveRailItem row = new("Alpha.archive") { DropCue = ArchiveDropCue.Before };
                MainWindow window = new(false);
                window.ArchiveOrderListBox.ItemsSource = new[] { row };
                window.Show();
                window.UpdateLayout();
                ListBoxItem item = (ListBoxItem)window.ArchiveOrderListBox.ItemContainerGenerator.ContainerFromItem(row)!;

                Border line = FindDescendants<Border>(item).Single(value => value.Height == 3 && value.VerticalAlignment == VerticalAlignment.Top);

                Assert.AreEqual(Visibility.Visible, line.Visibility);
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
    public void FailedDragPreviewRestoresTheExactOrderBeforeTheDrop()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                MainWindow window = new(false);
                window.SetProposedArchiveOrderForTesting(ExpectedRestoredOrder);

                Assert.Throws<ArchiveOrderException>(() => window.MoveArchiveOrderForTesting(["Alpha.archive"], "Gamma.archive"));

                CollectionAssert.AreEqual(ExpectedRestoredOrder, window.ProposedArchiveOrderForTesting.ToArray());
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
    public void WindowSynchronizesArchiveSelectionAndPreservesItThroughFiltering()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                ArchiveResourceOutcome[] alphaOutcomes = Enumerable.Range(1, 30).Select(value => new ArchiveResourceOutcome((ulong)value, $"base\\alpha{value}.mesh", ArchiveResourceDisposition.Winning, "Alpha.archive", new string('a', 64), "mesh", ResourcePathConfidence.ResolvedIndex, ["Beta.archive"], ArchivePayloadRelation.Different)).ToArray();
                ArchiveResourceOutcome[] betaOutcomes = Enumerable.Range(1, 30).Select(value => new ArchiveResourceOutcome((ulong)value, $"base\\alpha{value}.mesh", ArchiveResourceDisposition.Losing, "Alpha.archive", new string('b', 64), "mesh", ResourcePathConfidence.ResolvedIndex, ["Alpha.archive"], ArchivePayloadRelation.Different)).ToArray();
                ArchiveResourceOutcome redmodOutcome = new(2, "base\\gamma.mesh", ArchiveResourceDisposition.Unresolved, null, null, "mesh", ResourcePathConfidence.ResolvedIndex, [], ArchivePayloadRelation.Unknown);
                ArchiveConflictSummary alpha = new("Alpha.archive", "Alpha Mod", 0, alphaOutcomes, [], [], [], []);
                ArchiveConflictSummary beta = new("Beta.archive", "Beta Mod", 1, [], betaOutcomes, [], [], []);
                ArchiveConflictSummary redmod = new("REDmod/Gamma/Gamma.archive", "REDmod: Gamma", 2, [], [], [], [redmodOutcome], []);
                MainWindow window = new(false);
                window.ArchiveTreeForTesting.Load([alpha, beta, redmod]);
                window.ArchiveTreeForTesting.Filter(string.Empty, string.Empty, false);
                string[] order = ["Alpha.archive", "Beta.archive", "REDmod/Gamma/Gamma.archive", .. Enumerable.Range(0, 80).Select(value => $"Filler{value:D2}.archive")];
                ArchiveRailItem[] rail = order.Select(value => new ArchiveRailItem(value)).ToArray();
                window.ArchiveOrderListBox.ItemsSource = rail;
                window.ArchiveConflictTreeView.ItemsSource = window.ArchiveTreeForTesting.VisibleArchives;
                window.Show();
                window.UpdateLayout();
                Assert.AreEqual(0, window.LoadOrderOverviewBar.Entries.Count);
                Assert.IsNotNull(window.LoadOrderScrollViewerForTesting);
                Assert.IsGreaterThan(0, window.LoadOrderScrollBar.Maximum);

                window.ArchiveModFilterTextBox.Text = " ";
                WaitForDispatcherTimer();
                Assert.AreEqual("Select an archive", window.ArchiveSelectedTitleTextBlock.Text);
                window.ArchiveModFilterTextBox.Text = string.Empty;
                WaitForDispatcherTimer();

                window.ArchiveSearchTextBox.Text = "Alpha.archive";
                DrainDispatcher();
                Assert.AreEqual("Alpha.archive", SelectedArchiveName(window));
                window.ArchiveSearchTextBox.Text = string.Empty;

                window.LoadOrderScrollBar.Value = window.LoadOrderScrollBar.Maximum;
                DrainDispatcher();
                Assert.AreEqual(window.LoadOrderScrollBar.Maximum, window.LoadOrderScrollViewerForTesting.VerticalOffset, 0.01);

                window.ArchiveOrderListBox.SelectedItem = rail.Single(value => value.ArchiveName == "Beta.archive");
                DrainDispatcher();
                ArchiveConflictNode betaNode = window.ArchiveTreeForTesting.Find("Beta.archive")!;
                TreeViewItem betaItem = (TreeViewItem)window.ArchiveConflictTreeView.ItemContainerGenerator.ContainerFromItem(betaNode)!;
                Assert.IsTrue(betaItem.IsSelected);

                ArchiveConflictNode alphaNode = window.ArchiveTreeForTesting.Find("Alpha.archive")!;
                TreeViewItem alphaItem = (TreeViewItem)window.ArchiveConflictTreeView.ItemContainerGenerator.ContainerFromItem(alphaNode)!;
                alphaItem.IsSelected = true;
                DrainDispatcher();
                Assert.AreEqual("Alpha.archive", SelectedArchiveName(window));

                window.ConflictOverviewBar.Entries = [new("Alpha.archive", false, false, false, false, 0, 3), new("Beta.archive", true, false, false, false, 1, 3)];
                window.ConflictOverviewBar.SelectedArchives = ["Alpha.archive"];
                window.RefreshConflictMarkerPositionsForTesting();
                double collapsedRatio = window.ConflictOverviewBar.Entries.Single(value => value.ArchiveName == "Beta.archive").TrackRatio!.Value;
                alphaItem.IsExpanded = true;
                window.UpdateLayout();
                TreeViewItem alphaGroup = (TreeViewItem)alphaItem.ItemContainerGenerator.ContainerFromItem(alphaNode.Children[0])!;
                alphaGroup.IsExpanded = true;
                DrainDispatcher();
                window.RefreshConflictMarkerPositionsForTesting();
                ArchiveOverviewEntry expandedMarker = window.ConflictOverviewBar.Entries.Single(value => value.ArchiveName == "Beta.archive");
                double expandedRatio = expandedMarker.TrackRatio!.Value;
                Assert.IsGreaterThan(0.05, Math.Abs(expandedRatio - collapsedRatio));
                Assert.IsNotNull(expandedMarker.TrackSizeRatio);
                Assert.IsGreaterThan(0, expandedMarker.TrackSizeRatio.Value);
                Assert.IsLessThan(0.2, expandedMarker.TrackSizeRatio.Value);

                IReadOnlyList<ArchiveOverviewEntry> markerEntries = window.ConflictOverviewBar.Entries;
                Assert.IsNotNull(window.ConflictScrollViewerForTesting);
                window.ConflictScrollViewerForTesting.ScrollToVerticalOffset(10);
                DrainDispatcher();
                Assert.AreSame(markerEntries, window.ConflictOverviewBar.Entries);

                window.ArchiveOrderListBox.SelectedItems.Add(rail.Single(value => value.ArchiveName == "Beta.archive"));
                Assert.AreEqual(2, window.ArchiveOrderListBox.SelectedItems.Count);
                Assert.AreEqual(2, window.SelectedArchivesForTesting.Count);
                window.ArchiveModFilterTextBox.Text = "Alpha";
                WaitForDispatcherTimer();
                Assert.AreEqual(2, window.SelectedArchivesForTesting.Count);
                Assert.AreEqual(2, window.ArchiveOrderListBox.SelectedItems.Count);
                CollectionAssert.AreEquivalent(ExpectedMultiSelection, window.ArchiveOrderListBox.SelectedItems.Cast<ArchiveRailItem>().Select(value => value.ArchiveName).ToArray());
                window.ArchiveModFilterTextBox.Text = string.Empty;
                WaitForDispatcherTimer();

                ArchiveConflictNode redmodNode = window.ArchiveTreeForTesting.Find("REDmod/Gamma/Gamma.archive")!;
                TreeViewItem redmodItem = (TreeViewItem)window.ArchiveConflictTreeView.ItemContainerGenerator.ContainerFromItem(redmodNode)!;
                redmodItem.IsSelected = true;
                DrainDispatcher();
                Assert.AreEqual("REDmod/Gamma/Gamma.archive", SelectedArchiveName(window));

                window.ArchiveModFilterTextBox.Text = "Beta";
                WaitForDispatcherTimer();
                Assert.AreEqual("Beta.archive", SelectedArchiveName(window));
                Assert.AreEqual("Beta.archive", window.ArchiveTreeForTesting.VisibleArchives.Single().ArchiveName);
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
    public void WindowRendersRelationshipToneInBothPanesAfterScrolling()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                ArchiveRailItem[] rail = [new("Alpha.archive"), new("Beta.archive"), .. Enumerable.Range(0, 80).Select(value => new ArchiveRailItem($"Filler{value:D2}.archive"))];
                ArchiveConflictNode[] tree = [Node("Alpha.archive"), Node("Beta.archive")];
                ArchiveOverviewEntry[] entries = [new("Beta.archive", true, false, false, false, 1, rail.Length), new("Filler79.archive", false, true, false, false, rail.Length - 1, rail.Length)];
                ArchiveRelationshipPresentation.Apply(rail, tree, entries);
                MainWindow window = new(false);
                window.ArchiveOrderListBox.ItemsSource = rail;
                window.ArchiveConflictTreeView.ItemsSource = tree;
                window.Show();
                window.UpdateLayout();

                TreeViewItem treeItem = (TreeViewItem)window.ArchiveConflictTreeView.ItemContainerGenerator.ContainerFromItem(tree[1])!;
                Assert.AreEqual(Color.FromRgb(0x08, 0x25, 0x1B), RelationshipBorder(treeItem).Background is SolidColorBrush treeBrush ? treeBrush.Color : default);

                ArchiveRailItem offscreen = rail[^1];
                window.ArchiveOrderListBox.ScrollIntoView(offscreen);
                window.UpdateLayout();
                ListBoxItem railItem = (ListBoxItem)window.ArchiveOrderListBox.ItemContainerGenerator.ContainerFromItem(offscreen)!;
                Assert.AreEqual(Color.FromRgb(0x31, 0x09, 0x13), RelationshipBorder(railItem).Background is SolidColorBrush railBrush ? railBrush.Color : default);

                ArchiveRelationshipPresentation.Apply(rail, tree, []);
                DrainDispatcher();
                Assert.AreEqual(Colors.Transparent, RelationshipBorder(treeItem).Background is SolidColorBrush clearedTree ? clearedTree.Color : default);
                Assert.AreEqual(Colors.Transparent, RelationshipBorder(railItem).Background is SolidColorBrush clearedRail ? clearedRail.Color : default);
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
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

    private static string? SelectedArchiveName(MainWindow window) => (window.ArchiveOrderListBox.SelectedItem as ArchiveRailItem)?.ArchiveName;

    private static Border RelationshipBorder(DependencyObject source) => FindRelationshipBorder(source) ?? throw new InvalidOperationException("The relationship row border was not rendered.");

    private static Border? FindRelationshipBorder(DependencyObject source)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(source); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(source, index);
            if (child is Border border && border.BorderThickness.Left == 3) return border;
            Border? descendant = FindRelationshipBorder(child);
            if (descendant is not null) return descendant;
        }
        return null;
    }

    private static T? FindDescendant<T>(DependencyObject source) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(source); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(source, index);
            if (child is T match) return match;
            T? descendant = FindDescendant<T>(child);
            if (descendant is not null) return descendant;
        }
        return null;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject source) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(source); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(source, index);
            if (child is T match) yield return match;
            foreach (T descendant in FindDescendants<T>(child)) yield return descendant;
        }
    }

    private static ArchiveConflictNode Node(string archiveName) => new(new ArchiveConflictSummary(archiveName, archiveName, 0, [], [], [], [], []), []);

    private static void WaitForDispatcherTimer()
    {
        DispatcherFrame frame = new();
        DispatcherTimer timer = new(TimeSpan.FromMilliseconds(260), DispatcherPriority.ApplicationIdle, (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }
}

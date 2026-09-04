using ConflictStudio.App;
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
}

using ConflictStudio.App;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class MainWindowArchiveInteractionTests
{
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
}

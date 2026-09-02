using ConflictStudio.App;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class ArchiveOrderInteractionTests
{
    [TestMethod]
    public void UnknownArchiveCrossingBlocksThePreview()
    {
        UnknownArchiveOrderImpact impact = ArchiveUnknownImpactPolicy.Evaluate(["Alpha.archive", "Unreadable.archive", "Beta.archive"], ["Unreadable.archive", "Alpha.archive", "Beta.archive"], ["Unreadable.archive"]);

        Assert.AreEqual(UnknownArchiveOrderImpact.BlockedCrossing, impact);
    }

    [TestMethod]
    public void UnchangedUnknownArchiveRequiresAcknowledgement()
    {
        UnknownArchiveOrderImpact impact = ArchiveUnknownImpactPolicy.Evaluate(["Alpha.archive", "Unreadable.archive", "Beta.archive"], ["Alpha.archive", "Unreadable.archive", "Beta.archive"], ["Unreadable.archive"]);

        Assert.AreEqual(UnknownArchiveOrderImpact.RequiresAcknowledgement, impact);
    }

    [TestMethod]
    public void DragScrollMovesTheRealArchiveViewport()
    {
        Exception? failure = null;
        double offset = 0;
        Thread thread = new(() =>
        {
            try
            {
                StackPanel content = new();
                for (int index = 0; index < 80; index++) content.Children.Add(new TextBlock { Text = index.ToString(CultureInfo.InvariantCulture), Height = 20 });
                ScrollViewer viewer = new() { Height = 100, Content = content };
                Window window = new() { Content = viewer, Width = 200, Height = 140 };
                window.Show();
                window.UpdateLayout();

                ArchiveDragAutoScroll.Scroll(viewer, 1);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                offset = viewer.VerticalOffset;
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null) throw failure;
        Assert.IsGreaterThan(0, offset);
    }
}

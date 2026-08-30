using ConflictStudio.App;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class ArchiveOverviewBarTests
{
    [TestMethod]
    public void MeasuredMarkerUsesTheExactNormalizedTopAndHeight()
    {
        ArchiveOverviewEntry entry = new("A.archive", true, false, false, false, 9, 10, 0.9, 0.1);

        Rect marker = ArchiveOverviewBar.MarkerBounds(entry, 100, 10);

        Assert.AreEqual(90, marker.Y, 0.001);
        Assert.AreEqual(10, marker.Height, 0.001);
    }

    [TestMethod]
    public void AutomationValueReportsTheSelectedArchiveAndPosition()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                ArchiveOverviewBar bar = new() { Entries = [new("A.archive", false, false, false, false, 4, 10)] };
                AutomationProperties.SetAutomationId(bar, "ArchiveOverviewUnderTest");
                Window window = new() { Content = bar, Width = 200, Height = 200, ShowInTaskbar = false };
                window.Show();
                window.UpdateLayout();
                ArchiveOverviewAutomationPeer peer = (ArchiveOverviewAutomationPeer)UIElementAutomationPeer.CreatePeerForElement(bar)!;

                bar.SelectedArchive = "A.archive";

                IValueProvider value = (IValueProvider)peer.GetPattern(PatternInterface.Value)!;
                Assert.AreEqual("Selected: A.archive, position 5 of 10.", value.Value);

                AutomationElement root = AutomationElement.FromHandle(new WindowInteropHelper(window).Handle);
                AutomationElement element = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "ArchiveOverviewUnderTest"));
                AutomationPropertyChangedEventArgs? observed = null;
                AutomationPropertyChangedEventHandler handler = (_, args) => observed = args;
                Automation.AddAutomationPropertyChangedEventHandler(element, TreeScope.Element, handler, ValuePattern.ValueProperty);

                bar.Entries = [new("A.archive", false, false, false, false, 7, 10)];
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                Assert.AreEqual("Selected: A.archive, position 8 of 10.", value.Value);
                Assert.IsNotNull(observed);
                Assert.AreEqual(ValuePattern.ValueProperty, observed.Property);
                Assert.AreEqual("Selected: A.archive, position 5 of 10.", observed.OldValue);
                Assert.AreEqual("Selected: A.archive, position 8 of 10.", observed.NewValue);
                Automation.RemoveAutomationPropertyChangedEventHandler(element, handler);

                bar.Entries =
                [
                    new("A.archive", false, false, false, false, 7, 10),
                    new("B.archive", true, false, false, false, 8, 10),
                    new("C.archive", false, true, false, false, 3, 10),
                    new("D.archive", false, false, true, false, 2, 10),
                    new("E.archive", false, false, false, true, 1, 10)
                ];

                StringAssert.Contains(value.Value, "Selection wins over: B.archive");
                StringAssert.Contains(value.Value, "Selection loses to: C.archive");
                StringAssert.Contains(value.Value, "Identical payloads: D.archive");
                StringAssert.Contains(value.Value, "Direction can't be determined: E.archive");
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw failure;
        }
    }
}

using ConflictStudio.App;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class ArchiveOrderCloseDialogTests
{
    [TestMethod]
    [DataRow(true, "Apply and exit", ArchiveOrderCloseAction.ApplyAndClose)]
    [DataRow(true, "Discard changes", ArchiveOrderCloseAction.DiscardAndClose)]
    [DataRow(true, "Keep editing", ArchiveOrderCloseAction.Cancel)]
    [DataRow(false, "Discard changes", ArchiveOrderCloseAction.DiscardAndClose)]
    [DataRow(false, "Keep editing", ArchiveOrderCloseAction.Cancel)]
    public void ModalActionsReturnSelectedChoice(bool canApply, string label, ArchiveOrderCloseAction expected)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                ArchiveOrderCloseDialog dialog = new(canApply) { ShowActivated = false, ShowInTaskbar = false };
                dialog.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    try
                    {
                        StackPanel root = (StackPanel)dialog.Content;
                        StackPanel actions = (StackPanel)root.Children[^1];
                        Button[] buttons = actions.Children.OfType<Button>().ToArray();
                        Assert.AreEqual(canApply, buttons.Any(button => Equals(button.Content, "Apply and exit")));
                        Button cancel = buttons.Single(button => button.IsCancel);
                        Assert.AreEqual("Keep editing", cancel.Content);
                        Assert.IsFalse(buttons.Any(button => button.IsDefault));
                        dialog.UpdateLayout();
                        foreach (Button button in buttons)
                        {
                            Assert.IsTrue(button.ActualWidth + button.Margin.Left + button.Margin.Right >= button.DesiredSize.Width - 0.5);
                            Assert.IsTrue(button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize)).Right <= root.ActualWidth);
                        }
                        Button selected = buttons.Single(button => Equals(button.Content, label));
                        ((IInvokeProvider)new ButtonAutomationPeer(selected)).Invoke();
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                        dialog.Close();
                    }
                }));
                bool? result = dialog.ShowDialog();
                if (failure is null)
                {
                    Assert.AreEqual(true, result);
                    Assert.AreEqual(expected, dialog.Action);
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new AssertFailedException(failure.ToString());
    }
}



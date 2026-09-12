using ConflictStudio.App;
using ConflictStudio.Core;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class SourceInspectionWindowTests
{
    [TestMethod]
    public void ChoosesAnAmbiguousDefinitionAndReturnsToTheOriginalSource()
    {
        Run(async () =>
        {
            CodeSourceEvidence original = Original();
            CodeSourceEvidence first = Definition("First");
            CodeSourceEvidence second = Definition("Second");
            TweakReferenceIndex references = new([new(original.OperationId, "Items.Armor")], [new("Items.Armor", [first, second], 2, false)], false, 1);
            int reads = 0;
            SourceInspectionWindow window = Window(original, references, (source, _) =>
            {
                reads++;
                return Task.FromResult(new CodeSourceDocument(source, source == original ? ["Items.Weapon:", "  statModifiers:", "    - !append-once Items.Armor"] : ["Items.Armor:", "  $type: gamedataConstantStatModifier_Record", "  value: " + (source.Provider == "First" ? "0.75" : "0.25"), "Items.Unrelated:"]));
            });
            try
            {
                await window.LoadSourceAsync();
                Assert.AreEqual(2, Get<ComboBox>(window, "DefinitionComboBox").Items.Count);
                StringAssert.Contains(Get<TextBlock>(window, "ReferenceStatusTextBlock").Text, "does not establish priority");
                Get<ComboBox>(window, "DefinitionComboBox").SelectedIndex = 1;
                Click(window, "FollowReferenceButton");
                await window.NavigationReady;
                StringAssert.Contains(Text(window), "value: 0.25");
                Assert.IsFalse(Text(window).Contains("Items.Unrelated", StringComparison.Ordinal));
                Assert.IsTrue(Get<Button>(window, "BackButton").IsEnabled);
                Click(window, "BackButton");
                await window.NavigationReady;
                StringAssert.Contains(Text(window), "!append-once Items.Armor");
                Assert.IsFalse(Get<Button>(window, "BackButton").IsEnabled);
                Assert.AreEqual(3, reads);
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void SearchesTheVerifiedFileAndChangesTextSizeWithoutReReading()
    {
        Run(async () =>
        {
            int reads = 0;
            string[] lines = Enumerable.Range(1, 20).Select(line => line is 3 or 15 ? "needle" : "source " + line).ToArray();
            SourceInspectionWindow window = Window(Original(), TweakReferenceIndex.Empty, (source, _) => { reads++; return Task.FromResult(new CodeSourceDocument(source, lines)); });
            try
            {
                await window.LoadSourceAsync();
                Get<TextBox>(window, "SourceSearchTextBox").Text = "needle";
                Click(window, "FindSourceButton");
                await window.SearchReady;
                StringAssert.Contains(Get<TextBlock>(window, "SearchStatusTextBlock").Text, "line 3, column 1");
                Click(window, "NextMatchButton");
                StringAssert.Contains(Get<TextBlock>(window, "SearchStatusTextBlock").Text, "line 15, column 1");
                StringAssert.Contains(Text(window), "15  needle");
                Get<ComboBox>(window, "SourceFontSizeComboBox").SelectedIndex = 3;
                Assert.AreEqual(17d, Get<RichTextBox>(window, "SourceCodeBox").Document.FontSize);
                Click(window, "OriginalLocationButton");
                StringAssert.Contains(Text(window), "1  source 1");
                Assert.AreEqual(1, reads);
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void FailedSourceVerificationDoesNotEnableReferenceNavigation()
    {
        Run(async () =>
        {
            CodeSourceEvidence source = Original();
            TweakReferenceIndex index = new([new(source.OperationId, "Items.Armor")], [new("Items.Armor", [Definition("Alpha")], 1, false)], false, 1);
            SourceInspectionWindow window = Window(source, index, (_, _) => throw new CodeSourceReadException("Source changed after the scan."));
            try
            {
                await window.LoadSourceAsync();
                Assert.IsFalse(Get<Button>(window, "FollowReferenceButton").IsEnabled);
                Assert.IsFalse(Get<Button>(window, "FindSourceButton").IsEnabled);
                StringAssert.Contains(Get<TextBlock>(window, "InspectorStatusTextBlock").Text, "changed after the scan");
                Assert.IsTrue(string.IsNullOrWhiteSpace(Text(window)));
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void ClosingWhileAReadCompletesCannotRestoreItsDocument()
    {
        Run(async () =>
        {
            TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<CodeSourceDocument> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            CodeSourceEvidence source = Original();
            SourceInspectionWindow window = Window(source, TweakReferenceIndex.Empty, (_, _) => { started.SetResult(); return completion.Task; });
            Task reading = window.LoadSourceAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            window.Close();
            completion.SetResult(new(source, ["late source", "late source", "late source"]));
            await reading;
            Assert.IsTrue(string.IsNullOrWhiteSpace(Text(window)));
        });
    }

    private static CodeSourceEvidence Original()
        => new(ConflictSurface.ScriptAndTweak, "Items.Weapon.statModifiers", "Weapon", "weapon.yaml", "weapon.yaml", new string('a', 64), 3, 3, 3, 3, true, "TweakXL property")
        { OperationId = "add", OperationKind = CodeEvidenceOperationKind.TweakArrayAppendOnce, NormalizedValue = "Items.Armor" };

    private static CodeSourceEvidence Definition(string provider)
        => new(ConflictSurface.ScriptAndTweak, "Items.Armor", provider, provider + ".yaml", provider + ".yaml", new string('b', 64), 1, 3, 1, 1, true, "Local record definition");

    private static SourceInspectionWindow Window(CodeSourceEvidence source, TweakReferenceIndex index, Func<CodeSourceEvidence, CancellationToken, Task<CodeSourceDocument>> reader)
        => new(source, index, new SourceEditorPreferenceStore(Path.Combine(Path.GetTempPath(), "source-editor-test-" + Guid.NewGuid().ToString("N"))), reader);

    private static T Get<T>(SourceInspectionWindow window, string name) where T : FrameworkElement => (T)window.FindName(name);
    private static void Click(SourceInspectionWindow window, string name) => Get<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static string Text(SourceInspectionWindow window)
    {
        FlowDocument document = Get<RichTextBox>(window, "SourceCodeBox").Document;
        return new TextRange(document.ContentStart, document.ContentEnd).Text;
    }

    private static void Run(Func<Task> action)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await action(); }
                catch (Exception exception) { failure = exception; }
                finally { dispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)));
        if (failure is not null) throw new AssertFailedException(failure.ToString(), failure);
    }
}

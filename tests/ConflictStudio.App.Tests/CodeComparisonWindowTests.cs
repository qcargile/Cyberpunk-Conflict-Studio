using ConflictStudio.App;
using ConflictStudio.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using System.Windows.Media;
using System.IO;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class CodeComparisonWindowTests
{
    private static readonly string[] WeaponHeader = ["Items.KVD_Techtronika:", "  $base: Items.Preset_Crusher_Default", "  tags:", "    - !append-once IconicWeapon", "  statModifiers:"];
    [TestMethod]
    public void SameFileOccurrencesShareOneReadAndKeepIndependentLineNumbers()
    {
        Run(async () =>
        {
            int reads = 0;
            CodeSourceEvidence first = Evidence("Alpha", 10);
            CodeSourceEvidence second = first with { StartLine = 20, EndLine = 22, FocusStartLine = 20, FocusEndLine = 22 };
            CodeComparisonWindow window = new(Item(), [Witness(first, second)], (source, _) =>
            {
                Interlocked.Increment(ref reads);
                return Task.FromResult(new CodeSourceDocument(source, Enumerable.Range(1, 40).Select(value => "line " + value).ToArray()));
            });
            try
            {
                await window.LoadSelectionAsync();
                Assert.AreEqual(1, reads);
                StringAssert.Contains(Text(window, "LeftCodeBox"), "7  line 7");
                StringAssert.Contains(Text(window, "RightCodeBox"), "17  line 17");
                ((Button)window.FindName("MoreContextButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.AreEqual(1, reads);
                StringAssert.Contains(Text(window, "LeftCodeBox"), "1  line 1");
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void InitialPairUsesOpposingValuesAndNeverAnEqualValueAlternative()
    {
        Run(async () =>
        {
            CodeSourceEvidence[] evidence = [Evidence("Alpha", 1), Evidence("Alpha", 10) with { OperationOccurrence = 2 }, Evidence("Beta", 1), Evidence("Gamma", 1)];
            CodeFindingParticipant[] participants = evidence.Select((source, index) => Participant(source, index.ToString(System.Globalization.CultureInfo.InvariantCulture), index == 3 ? "20" : "10", CodeFindingParticipantRole.ValueDeclaration, CodeEvidenceOperationKind.TweakScalarAssignment)).ToArray();
            CodeFindingWitness witness = new("values", CodeFindingWitnessKind.ScalarValue, "Different values", "Different values", "Source disagreement only.", null, participants);
            CodeComparisonWindow window = new(Item(), [witness], (source, _) => Task.FromResult(new CodeSourceDocument(source, Enumerable.Repeat(source.Provider, 20).ToArray())));
            try
            {
                await window.LoadSelectionAsync();
                ComboBox left = (ComboBox)window.FindName("LeftSourceComboBox");
                ComboBox right = (ComboBox)window.FindName("RightSourceComboBox");
                Assert.AreEqual(4, left.Items.Count);
                Assert.AreEqual(1, right.Items.Count);
                Assert.AreEqual("Alpha", ((CodeSourceChoice)left.SelectedItem).Participant.Provider);
                Assert.AreEqual("Gamma", ((CodeSourceChoice)right.SelectedItem).Participant.Provider);
                StringAssert.Contains(left.Items[1].ToString()!, "occurrence 2");
                left.SelectedIndex = 3;
                await window.LoadSelectionAsync();
                Assert.AreEqual(3, right.Items.Count);
                StringAssert.Contains(Text(window, "LeftCodeBox"), "Gamma");
                StringAssert.Contains(Text(window, "RightCodeBox"), "Alpha");
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void CompleteCallbackBodiesArePreferredOverRegistrationExcerpts()
    {
        Run(async () =>
        {
            CodeSourceEvidence[] evidence = [Evidence("Alpha", 1) with { IsCompleteBlock = false }, Evidence("Alpha", 10), Evidence("Beta", 1) with { IsCompleteBlock = false }, Evidence("Beta", 10)];
            CodeFindingParticipant[] participants = [Participant(evidence[0], "Alpha", "", CodeFindingParticipantRole.AddedMethod, CodeEvidenceOperationKind.RedScriptAddMethod) with { Sources = evidence[..2] }, Participant(evidence[2], "Beta", "", CodeFindingParticipantRole.AddedMethod, CodeEvidenceOperationKind.RedScriptAddMethod) with { Sources = evidence[2..] }];
            CodeFindingWitness witness = new("declarations", CodeFindingWitnessKind.DuplicateMemberDeclaration, "Duplicate method", "Repeated method", "Compiler evidence.", null, participants);
            CodeComparisonWindow window = new(Item(), [witness], (source, _) => Task.FromResult(new CodeSourceDocument(source, Enumerable.Repeat("source", 20).ToArray())));
            try
            {
                await window.LoadSelectionAsync();
                Assert.AreEqual(10, ((CodeSourceChoice)((ComboBox)window.FindName("LeftSourceComboBox")).SelectedItem).Evidence!.StartLine);
                Assert.AreEqual(10, ((CodeSourceChoice)((ComboBox)window.FindName("RightSourceComboBox")).SelectedItem).Evidence!.StartLine);
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void MainWindowOffersCodeOnlyForOneFindingWithLiveEvidence()
    {
        Run(() =>
        {
            MainWindow window = new();
            try
            {
                ProfileScanReceipt receipt = new(2, "Profile", DateTimeOffset.UtcNow, [], [], [], [], [], [], [], [], [], [], [], []) { CodeEvidence = [Evidence("Alpha", 1)] };
                typeof(MainWindow).GetField("_receipt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window, receipt);
                DataGrid rows = (DataGrid)window.FindName("WorkQueueDataGrid");
                Button view = (Button)window.FindName("ViewCodeButton");
                ConflictWorkItem supported = Item() with { Comparisons = [Witness(Evidence("Alpha", 1), Evidence("Beta", 1))] };
                ConflictWorkItem unsupported = Item() with { Target = "Other" };
                rows.ItemsSource = new[] { supported, unsupported };
                rows.SelectedItem = supported;
                Assert.IsTrue(view.IsEnabled);
                rows.SelectedItems.Add(unsupported);
                Assert.IsFalse(view.IsEnabled);
                rows.SelectedItem = unsupported;
                Assert.IsFalse(view.IsEnabled);
                rows.SelectedItems.Clear();
                Assert.IsFalse(view.IsEnabled);
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void UnavailableSourceDoesNotHideTheVerifiedOtherSide()
    {
        Run(async () =>
        {
            CodeComparisonWindow window = new(Item(), [Witness(Evidence("Alpha", 1), Evidence("Beta", 1))], (source, _) => source.Provider == "Alpha"
                ? Task.FromResult(new CodeSourceDocument(source, ["value: 10", "next: 1", "last: 2"]))
                : Task.FromException<CodeSourceDocument>(new CodeSourceReadException("Source changed; scan again.")));
            try
            {
                await window.LoadSelectionAsync();
                StringAssert.Contains(Text(window, "LeftCodeBox"), "value: 10");
                Assert.IsFalse(Text(window, "RightCodeBox").Contains("value", StringComparison.Ordinal));
                StringAssert.Contains(((TextBlock)window.FindName("RightStatusTextBlock")).Text, "Source changed");
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void SupersededLoadAndClosedWindowCannotDisplayLateResults()
    {
        Run(async () =>
        {
            TaskCompletionSource<CodeSourceDocument> delayed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken firstToken = default;
            int read = 0;
            CodeComparisonWindow window = new(Item(), [Witness(Evidence("Alpha", 1))], (source, cancellation) =>
            {
                if (Interlocked.Increment(ref read) != 1) return Task.FromResult(new CodeSourceDocument(source, ["new", "new", "new"]));
                firstToken = cancellation;
                started.SetResult();
                return delayed.Task;
            });
            Task first = window.LoadSelectionAsync();
            await started.Task;
            await window.LoadSelectionAsync();
            Assert.IsTrue(firstToken.IsCancellationRequested);
            StringAssert.Contains(Text(window, "LeftCodeBox"), "new");
            window.Close();
            delayed.SetResult(new CodeSourceDocument(Evidence("Alpha", 1), ["old", "old", "old"]));
            await first;
            Assert.IsFalse(Text(window, "LeftCodeBox").Contains("old", StringComparison.Ordinal));
        });
    }

    [TestMethod]
    public void StalledReadsStayBoundedAcrossClosedAndReopenedWindows()
    {
        Run(async () =>
        {
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource startedPair = new(TaskCreationOptions.RunContinuationsAsynchronously);
            object counts = new();
            int active = 0;
            int peak = 0;
            int started = 0;
            async Task<CodeSourceDocument> Read(CodeSourceEvidence source, CancellationToken cancellation)
            {
                lock (counts)
                {
                    active++;
                    peak = Math.Max(peak, active);
                    if (++started == 2) startedPair.TrySetResult();
                }
                try
                {
                    await release.Task;
                    return new CodeSourceDocument(source, ["source", "source", "source"]);
                }
                finally { lock (counts) active--; }
            }
            CodeSourceEvidence[] evidence = [Evidence("Alpha", 1), Evidence("Beta", 1)];
            CodeComparisonWindow firstWindow = new(Item(), [Witness(evidence)], Read);
            CodeComparisonWindow secondWindow = new(Item(), [Witness(evidence)], Read);
            Task firstLoad = firstWindow.LoadSelectionAsync();
            Task secondLoad = Task.CompletedTask;
            try
            {
                await startedPair.Task;
                firstWindow.Close();
                secondLoad = secondWindow.LoadSelectionAsync();
                await Task.Delay(75);
                secondWindow.Close();
            }
            finally
            {
                firstWindow.Close();
                secondWindow.Close();
                release.TrySetResult();
                await Task.WhenAll(firstLoad, secondLoad);
            }
            Assert.AreEqual(2, peak);
            Assert.AreEqual(2, started);
        });
    }

    [TestMethod]
    public void ArrayFindingOpensItsArmorOperationsThroughTheFullScanAndViewer()
    {
        Run(async () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "comparison-array-" + Guid.NewGuid().ToString("N"));
            try
            {
                string provider = "Chrome Ballistics - Weapon Rebalance";
                string weapon = Path.Combine(root, "mods", provider, "r6", "tweaks", "KV Inhibitor", "KVD_Techtronika.yaml");
                string global = Path.Combine(root, "mods", provider, "r6", "tweaks", "^global_armor_penetration.yaml");
                Directory.CreateDirectory(Path.GetDirectoryName(weapon)!);
                string[] modifiers = ["Quality.IconicItem", "Items.kvsilentstats_rof", "Items.kvsilentstats_damage", "Items.kvsilentstats_dismember", "Items.kvsilentstats_effectiverange", "Items.kvsilentstats_maxrange", "Items.kvsilentstats_mag", "Items.kvsilentstats_pierce", "Items.kvsilentstats_crit", "Items.kvsilentstats_bleeding", "Items.kvsilentstats_burning", "Items.kvsilentstats_electrocute", "Items.kvsilentstats_poison", "Items.kvsilentstats_stun", "Items.kvsilentstats_headshotmult", "Items.kvsilentstats_firstequip", "Items.kvsilentstats_burst", "Items.kvsilentstats_burst2", "Items.kvsilentstats_armor", "Items.kvsilentstats_silent", "Items.kvsilentstats_noise", "Items.kvsilentstats_spread"];
                File.WriteAllLines(weapon, WeaponHeader.Concat(modifiers.Select(value => "    - !append-once " + value)));
                File.WriteAllLines(global, Enumerable.Repeat(string.Empty, 117).Concat(["Items.KVD_Techtronika:", "  statModifiers:", "    - !remove Items.kvsilentstats_armor", "    - !append-once ChromeBallistics.ArmorPenetrationPlus25"]));
                string modlist = Path.Combine(root, "profiles", "Test", "modlist.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(modlist)!);
                File.WriteAllText(modlist, "+" + provider + "\n");
                ProfileScanReceipt receipt = ProfileScanCoordinator.Scan(root, new Mo2Profile("Test", modlist), DateTimeOffset.UtcNow);
                ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Surface == ConflictSurface.ScriptAndTweak && value.Target == "Items.KVD_Techtronika.statModifiers");
                Assert.AreEqual(24, receipt.CodeEvidence.Count(value => value.Target == item.Target));
                Assert.AreEqual(1, item.Comparisons.Length);
                Assert.AreEqual(2, item.Comparisons[0].Participants.Length);
                CodeComparisonWindow window = new(item, item.Comparisons, CodeSourceReader.ReadAsync);
                try
                {
                    await window.LoadSelectionAsync();
                    CodeSourceChoice left = (CodeSourceChoice)((ComboBox)window.FindName("LeftSourceComboBox")).SelectedItem;
                    CodeSourceChoice right = (CodeSourceChoice)((ComboBox)window.FindName("RightSourceComboBox")).SelectedItem;
                    Assert.AreEqual(24, left.Evidence!.FocusStartLine);
                    Assert.AreEqual(120, right.Evidence!.FocusStartLine);
                    Assert.AreEqual(CodeFindingParticipantRole.ArrayAddition, left.Participant.Role);
                    Assert.AreEqual(CodeFindingParticipantRole.ArrayRemoval, right.Participant.Role);
                    Assert.AreEqual("Items.kvsilentstats_armor", left.Participant.Member);
                    Assert.AreEqual(left.Participant.Member, right.Participant.Member);
                    string leftHighlight = HighlightedText(window, "LeftCodeBox");
                    string rightHighlight = HighlightedText(window, "RightCodeBox");
                    StringAssert.Contains(leftHighlight, "!append-once Items.kvsilentstats_armor");
                    StringAssert.Contains(rightHighlight, "!remove Items.kvsilentstats_armor");
                    ((Button)window.FindName("MoreContextButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    ((Button)window.FindName("MoreContextButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.AreEqual(leftHighlight, HighlightedText(window, "LeftCodeBox"));
                    Assert.AreEqual(rightHighlight, HighlightedText(window, "RightCodeBox"));
                    Assert.IsFalse(leftHighlight.Contains("IconicItem", StringComparison.Ordinal));
                    Assert.IsFalse(leftHighlight.Contains("effectiverange", StringComparison.Ordinal));
                }
                finally { window.Close(); }
            }
            finally { Directory.Delete(root, true); }
        });
    }

    [TestMethod]
    public void OnlySupportingStatementsAreHighlightedUntilContextIsRequested()
    {
        Run(async () =>
        {
            CodeSourceEvidence add = Evidence("Chrome Ballistics", 2) with { FilePath = "weapon.yaml", PhysicalPath = "weapon.yaml", EndLine = 2, FocusEndLine = 2 };
            CodeSourceEvidence remove = add with { FilePath = "global.yaml", PhysicalPath = "global.yaml" };
            CodeFindingWitness witness = ArrayWitness("Items.armor", add, remove);
            CodeComparisonWindow window = new(Item(), [witness], (source, _) => Task.FromResult(new CodeSourceDocument(source, source.FilePath == "weapon.yaml"
                ? ["rateOfFire: 1", "- !append-once Items.armor", "iconic: true"]
                : ["effectiveRange: 30", "- !remove Items.armor", "damage: 40"])));
            try
            {
                await window.LoadSelectionAsync();
                StringAssert.Contains(((TextBlock)window.FindName("LeftRoleTextBlock")).Text, "Adds entry: Items.armor");
                StringAssert.Contains(((TextBlock)window.FindName("RightRoleTextBlock")).Text, "Removes entry: Items.armor");
                StringAssert.Contains(((TextBlock)window.FindName("ScopeTextBlock")).Text, "Chrome Ballistics");
                string highlighted = HighlightedText(window, "LeftCodeBox") + HighlightedText(window, "RightCodeBox");
                StringAssert.Contains(highlighted, "append");
                Assert.IsFalse(highlighted.Contains("rateOfFire", StringComparison.Ordinal));
                Assert.IsFalse(highlighted.Contains("iconic", StringComparison.Ordinal));
                Assert.IsFalse(highlighted.Contains("effectiveRange", StringComparison.Ordinal));
                ((Button)window.FindName("MoreContextButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.AreEqual(highlighted, HighlightedText(window, "LeftCodeBox") + HighlightedText(window, "RightCodeBox"));
                ((CheckBox)window.FindName("ContextDifferencesCheckBox")).IsChecked = true;
                Assert.AreNotEqual(highlighted, HighlightedText(window, "LeftCodeBox") + HighlightedText(window, "RightCodeBox"));
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void IssueSelectionSwitchesMembersWithoutKeepingThePreviousOperands()
    {
        Run(async () =>
        {
            CodeSourceEvidence add = Evidence("Alpha", 2) with { EndLine = 2, FocusEndLine = 2 };
            CodeSourceEvidence remove = Evidence("Beta", 2) with { EndLine = 2, FocusEndLine = 2 };
            CodeFindingWitness armor = ArrayWitness("Items.armor", add, remove);
            CodeFindingWitness damage = ArrayWitness("Items.damage", add with { StartLine = 3, EndLine = 3, FocusStartLine = 3, FocusEndLine = 3 }, remove with { StartLine = 3, EndLine = 3, FocusStartLine = 3, FocusEndLine = 3 });
            CodeComparisonWindow window = new(Item(), [armor, damage], (source, _) => Task.FromResult(new CodeSourceDocument(source, ["header", "armor", "damage"])));
            try
            {
                await window.LoadSelectionAsync();
                ComboBox issues = (ComboBox)window.FindName("WitnessComboBox");
                Assert.AreEqual(Visibility.Visible, issues.Visibility);
                Assert.AreEqual(2, issues.Items.Count);
                issues.SelectedIndex = 1;
                await window.LoadSelectionAsync();
                Assert.AreEqual("Items.damage", ((CodeSourceChoice)((ComboBox)window.FindName("LeftSourceComboBox")).SelectedItem).Participant.Member);
                Assert.AreEqual("Items.damage", ((CodeSourceChoice)((ComboBox)window.FindName("RightSourceComboBox")).SelectedItem).Participant.Member);
                Assert.AreEqual(3, ((CodeSourceChoice)((ComboBox)window.FindName("LeftSourceComboBox")).SelectedItem).Evidence!.FocusStartLine);
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void IdenticalDeclarationsRemainVisibleAsEvidenceOfDuplication()
    {
        Run(async () =>
        {
            CodeComparisonWindow window = new(Item(), [Witness(Evidence("Alpha", 1), Evidence("Beta", 1))], (source, _) => Task.FromResult(new CodeSourceDocument(source, ["@addMethod(PlayerPuppet)", "public func Value() -> Int32 { return 1; }", ""])));
            try
            {
                await window.LoadSelectionAsync();
                StringAssert.Contains(HighlightedText(window, "LeftCodeBox"), "@addMethod");
                StringAssert.Contains(HighlightedText(window, "RightCodeBox"), "@addMethod");
                StringAssert.Contains(((TextBlock)window.FindName("ComparisonStatusTextBlock")).Text, "duplication");
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void MissingSupportingSourceIsNotReplacedWithAnotherParticipant()
    {
        Run(async () =>
        {
            CodeFindingParticipant first = Participant(Evidence("Alpha", 1), "first", "1", CodeFindingParticipantRole.ValueDeclaration, CodeEvidenceOperationKind.TweakScalarAssignment);
            CodeFindingParticipant missing = Participant(Evidence("Beta", 1), "missing", "2", CodeFindingParticipantRole.ValueDeclaration, CodeEvidenceOperationKind.TweakScalarAssignment) with { Sources = [] };
            CodeFindingParticipant other = Participant(Evidence("Gamma", 1), "other", "3", CodeFindingParticipantRole.ValueDeclaration, CodeEvidenceOperationKind.TweakScalarAssignment);
            CodeFindingWitness witness = new("values", CodeFindingWitnessKind.ScalarValue, "Different values", "Different values", "Source disagreement only.", null, [first, missing, other]);
            CodeComparisonWindow window = new(Item(), [witness], (source, _) => Task.FromResult(new CodeSourceDocument(source, [source.Provider, "", ""])));
            try
            {
                await window.LoadSelectionAsync();
                Assert.AreEqual("missing", ((CodeSourceChoice)((ComboBox)window.FindName("RightSourceComboBox")).SelectedItem).Participant.OperationId);
                StringAssert.Contains(((TextBlock)window.FindName("RightStatusTextBlock")).Text, "Exact source was not recorded");
                Assert.IsFalse(Text(window, "RightCodeBox").Contains("Gamma", StringComparison.Ordinal));
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void LongExcerptsRemainBoundedAndKeepTheFocusVisible()
    {
        CodeSourceEvidence evidence = Evidence("Alpha", 1) with { EndLine = 1000, FocusStartLine = 600, FocusEndLine = 600 };
        string[] lines = Enumerable.Repeat(new string('x', 3000), 1000).ToArray();

        CodeComparisonExcerpt excerpt = CodeComparisonExcerpt.Create(new CodeSourceDocument(evidence, lines), evidence, 3, 160);

        Assert.IsLessThanOrEqualTo(160, excerpt.Lines.Length);
        Assert.IsTrue(excerpt.StartLine <= 600 && excerpt.EndLine >= 600);
        Assert.IsTrue(excerpt.RangeLimited);
        Assert.IsTrue(excerpt.LinesShortened);
        Assert.IsTrue(excerpt.Lines.All(value => value.Length <= CodeComparisonExcerpt.MaximumLineCharacters + 2));
    }

    [TestMethod]
    public void CompactComparisonKeepsBothCodePanesAndTheirActionsVisible()
    {
        Run(async () =>
        {
            CodeSourceEvidence[] sources = Enumerable.Range(0, 6).Select(index => Evidence("Provider " + index, 1)).ToArray();
            CodeComparisonWindow window = new(Item(), [Witness(sources)], (source, _) => Task.FromResult(new CodeSourceDocument(source, ["first", "second", "third"])));
            try
            {
                await window.LoadSelectionAsync();
                FrameworkElement layout = (FrameworkElement)window.Content;
                layout.Measure(new Size(900, 570));
                layout.Arrange(new Rect(0, 0, 900, 570));
                layout.UpdateLayout();
                await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                layout.UpdateLayout();
                Assert.IsTrue(((RichTextBox)window.FindName("LeftCodeBox")).ActualHeight >= 60, "The contributor overview hid the left code pane.");
                Assert.IsTrue(((RichTextBox)window.FindName("RightCodeBox")).ActualHeight >= 60, "The contributor overview hid the right code pane.");
                Button open = (Button)window.FindName("LeftOpenButton");
                Assert.IsTrue(open.TranslatePoint(new Point(0, open.ActualHeight), layout).Y <= layout.ActualHeight);
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void ContextSelectionCannotReplaceTheSupportingPairAndTextSizePreservesIt()
    {
        Run(async () =>
        {
            CodeSourceEvidence add = Evidence("Alpha", 1) with { EndLine = 1, FocusEndLine = 1 };
            CodeSourceEvidence remove = Evidence("Beta", 1) with { EndLine = 1, FocusEndLine = 1 };
            CodeSourceEvidence context = Evidence("Alpha", 3) with { EndLine = 3, FocusEndLine = 3, OperationId = "context", OperationKind = CodeEvidenceOperationKind.TweakArrayAppendOnce, NormalizedValue = "Items.other" };
            CodeFindingWitness witness = ArrayWitness("Items.armor", add, remove);
            CodeComparisonWindow window = new(Item(), [witness], (source, _) => Task.FromResult(new CodeSourceDocument(source, ["Items.armor", "", "Items.other"])), [add, remove, context]);
            try
            {
                await window.LoadSelectionAsync();
                DataGrid contributors = (DataGrid)window.FindName("ContributorsDataGrid");
                contributors.SelectedItem = contributors.Items.OfType<CodeContributorRow>().Single(row => row.Source == context);
                Assert.IsFalse(((Button)window.FindName("CompareContributorButton")).IsEnabled);
                ((Button)window.FindName("CompareContributorButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.AreEqual("Items.armor", ((CodeSourceChoice)((ComboBox)window.FindName("LeftSourceComboBox")).SelectedItem).Participant.Member);
                ((ComboBox)window.FindName("ComparisonFontComboBox")).SelectedIndex = 4;
                Assert.AreEqual(20d, ((RichTextBox)window.FindName("LeftCodeBox")).Document.FontSize);
                StringAssert.Contains(HighlightedText(window, "LeftCodeBox"), "Items.armor");
                Assert.IsFalse(HighlightedText(window, "LeftCodeBox").Contains("Items.other", StringComparison.Ordinal));
            }
            finally { window.Close(); }
        });
    }

    private static CodeSourceEvidence Evidence(string provider, int line)
        => new(ConflictSurface.ScriptAndTweak, "Target", provider, "code.reds", provider + ".reds", new string('a', 64), line, line + 2, line, line + 2, true, "Method");

    private static ConflictWorkItem Item()
        => new(ConflictSurface.ScriptAndTweak, "Target", EvidenceClassification.Exclusive, ConflictWorkState.NeedsAttention, "", "", null, ["Alpha", "Beta"], new string('a', 64));

    private static CodeFindingParticipant Participant(CodeSourceEvidence source, string id, string value, CodeFindingParticipantRole role, CodeEvidenceOperationKind kind)
        => new(id, role, role == CodeFindingParticipantRole.AddedMethod ? "Declares method" : "Assigns value", kind, source.Provider, source.FilePath, source.FocusStartLine, null, value, null, [source]);

    private static CodeFindingWitness Witness(params CodeSourceEvidence[] evidence)
        => new("declarations", CodeFindingWitnessKind.DuplicateMemberDeclaration, "Duplicate method", "Repeated method", "Compiler evidence.", null,
            evidence.Select((source, index) => Participant(source, index.ToString(System.Globalization.CultureInfo.InvariantCulture), "", CodeFindingParticipantRole.AddedMethod, CodeEvidenceOperationKind.RedScriptAddMethod)).ToArray());

    private static CodeFindingWitness ArrayWitness(string member, CodeSourceEvidence add, CodeSourceEvidence remove)
        => new(member, CodeFindingWitnessKind.OpposingArrayMutation, "Membership: " + member, "One operation adds this entry and another removes it.", "This may be intentional.", member,
            [Participant(add, member + "-add", member, CodeFindingParticipantRole.ArrayAddition, CodeEvidenceOperationKind.TweakArrayAppendOnce) with { Member = member, RoleLabel = "Adds entry" }, Participant(remove, member + "-remove", member, CodeFindingParticipantRole.ArrayRemoval, CodeEvidenceOperationKind.TweakArrayRemove) with { Member = member, RoleLabel = "Removes entry" }]);

    private static string HighlightedText(CodeComparisonWindow window, string name)
        => string.Join("", ((RichTextBox)window.FindName(name)).Document.Blocks.OfType<Paragraph>().SelectMany(value => value.Inlines.OfType<Run>()).Where(value => value.Background is SolidColorBrush).Select(value => value.Text));

    private static string Text(CodeComparisonWindow window, string name)
    {
        FlowDocument document = ((RichTextBox)window.FindName(name)).Document;
        return new TextRange(document.ContentStart, document.ContentEnd).Text;
    }

    private static void Run(Func<Task> work)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await work(); }
                catch (Exception exception) { failure = exception; }
                finally { dispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "Comparison test timed out.");
        if (failure is not null) throw failure;
    }
}

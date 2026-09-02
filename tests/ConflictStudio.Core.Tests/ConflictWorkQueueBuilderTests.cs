using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class ConflictWorkQueueBuilderTests
{
    [TestMethod]
    [DataRow("TweakXL RED")]
    [DataRow("RedScript registration")]
    [DataRow("CET Lua activation")]
    public void SourceCoverageLimitsRemainVisibleWithoutDemandingARepair(string surface)
    {
        ProfileScanReceipt receipt = Receipt() with { SourceFailures = [new("Example", "source", surface, "Source was not analyzed.")] };
        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Target == "source");

        Assert.AreEqual(ConflictSurface.Diagnostic, item.Surface);
        Assert.AreEqual(EvidenceClassification.Informational, item.Classification);
        Assert.IsFalse(item.IsActionable);
        Assert.AreEqual("Source was not analyzed.", item.MeaningLabel);
        Assert.IsFalse(item.NextAction.Contains("Fix", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BaseRelationshipIsInformationAndPropertyChangeInvalidatesReview()
    {
        ProfileScanReceipt BuildReceipt(string property, string prefix)
        {
            ModSourceInventory inventory = new([], [], [new("Base edit", "base.yaml", prefix + "Items.Base." + property + ": 2"), new("Clone", "clone.yaml", "Items.Child:\n  $base: Items.Base")], []);
            return Receipt() with { InteractionFindings = InteractionReportBuilder.Build(inventory), TweakOverlaps = TweakInteractionAnalyzer.Analyze(inventory.TweakSources) };
        }
        ConflictWorkItem first = ConflictWorkQueueBuilder.Build(BuildReceipt("value", ""), []).Single(value => value.Surface == ConflictSurface.ScriptAndTweak);
        ConflictWorkItem shifted = ConflictWorkQueueBuilder.Build(BuildReceipt("value", "\n\n"), []).Single(value => value.Surface == ConflictSurface.ScriptAndTweak);
        ConflictWorkItem changed = ConflictWorkQueueBuilder.Build(BuildReceipt("other", ""), []).Single(value => value.Surface == ConflictSurface.ScriptAndTweak);

        Assert.AreEqual(EvidenceClassification.Informational, first.Classification);
        Assert.IsFalse(first.IsActionable);
        StringAssert.Contains(first.Summary, "Items.Base.value");
        StringAssert.Contains(first.BoundaryLabel, "final inherited values");
        Assert.AreEqual(first.EvidenceSha256, shifted.EvidenceSha256);
        Assert.AreNotEqual(first.EvidenceSha256, changed.EvidenceSha256);
    }

    [TestMethod]
    [DataRow("array<\nBool >")]
    [DataRow("array<\tBool >")]
    [DataRow("array<Bool>")]
    public void FixRoundTwoFieldTypeFormattingPreservesReviewAndDisplay(string formattedType)
    {
        ProfileScanReceipt BuildReceipt(string type, int count)
        {
            string field = "@addField(PlayerPuppet)\nlet sharedState: " + type + ";";
            ModSourceInventory inventory = new([new("Alpha", "fields.reds", string.Join("\n", Enumerable.Repeat(field, count)))], [], [], []);
            return Receipt() with { InteractionFindings = InteractionReportBuilder.Build(inventory) };
        }
        ProfileScanReceipt originalReceipt = BuildReceipt("array< Bool >", 2);
        ProfileScanReceipt formattedReceipt = BuildReceipt(formattedType, 2);
        ConflictWorkItem original = ConflictWorkQueueBuilder.Build(originalReceipt, []).Single(value => value.Surface == ConflictSurface.ScriptAndTweak);
        EvidenceDecision decision = Decision(originalReceipt, original, original.EvidenceSha256, "Array fields reviewed.");
        ConflictWorkItem formatted = ConflictWorkQueueBuilder.Build(formattedReceipt, [decision]).Single(value => value.Surface == ConflictSurface.ScriptAndTweak);
        ConflictWorkItem changedType = ConflictWorkQueueBuilder.Build(BuildReceipt("array<Int32>", 2), [decision]).Single(value => value.Surface == ConflictSurface.ScriptAndTweak);
        ConflictWorkItem changedCount = ConflictWorkQueueBuilder.Build(BuildReceipt(formattedType, 3), [decision]).Single(value => value.Surface == ConflictSurface.ScriptAndTweak);

        Assert.AreEqual("array< Bool >", originalReceipt.InteractionFindings.Single().DeclarationEvidence![0].Type);
        Assert.AreEqual(formattedType, formattedReceipt.InteractionFindings.Single().DeclarationEvidence![0].Type);
        StringAssert.Contains(formatted.Summary, formattedType);
        Assert.AreEqual(original.EvidenceSha256, formatted.EvidenceSha256);
        Assert.AreEqual(ConflictWorkState.Reviewed, formatted.State);
        Assert.AreNotEqual(original.EvidenceSha256, changedType.EvidenceSha256);
        Assert.AreNotEqual(ConflictWorkState.Reviewed, changedType.State);
        Assert.AreNotEqual(original.EvidenceSha256, changedCount.EvidenceSha256);
        Assert.AreNotEqual(ConflictWorkState.Reviewed, changedCount.State);
    }

    [TestMethod]
    public void FixRoundInternalAddsPreserveExistingMixedLanguageRowIdentity()
    {
        string method = "@addMethod(PlayerPuppet)\npublic func Value() -> Bool { return true; }";
        ModSourceInventory inventory = new([new("Alpha", "one.reds", method + "\n" + method)],
            [new("Beta", "two.lua", "Override('PlayerPuppet', 'Value', function() return false end)")], [], []);
        ProfileScanReceipt current = Receipt() with
        {
            InteractionFindings = InteractionReportBuilder.Build(inventory),
            RedScriptFlows = RedScriptFlowEvidenceAnalyzer.Analyze(inventory.RedScripts),
            LuaCallbacks = LuaCallbackEvidenceAnalyzer.Analyze(inventory.LuaSources)
        };
        ProfileScanReceipt previous = current with
        {
            InteractionFindings = [new("PlayerPuppet.Value()", InteractionFindingKind.Review,
                "A CET override targets a RedScript method added by another active provider. This is a source overlap; the report does not establish a compiler or runtime outcome.", ["Beta", "Alpha"])]
        };
        ConflictWorkItem original = ConflictWorkQueueBuilder.Build(previous, []).Single(value => value.Surface == ConflictSurface.ScriptAndTweak);
        EvidenceDecision decision = Decision(previous, original, original.EvidenceSha256, "Observed this profile.");
        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(current, [decision]).Single(value => value.Surface == ConflictSurface.ScriptAndTweak);

        CollectionAssert.AreEqual(original.Providers, item.Providers);
        Assert.AreEqual(original.EvidenceSha256, item.EvidenceSha256);
        Assert.AreEqual(ConflictWorkState.Reviewed, item.State);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FixRoundCetOccurrenceCountInvalidatesReview(bool crossProvider)
    {
        string registration = "Override('PlayerPuppet', 'Value', function(self, wrapped) return wrapped() end)";
        ProfileScanReceipt BuildReceipt(int count, string prefix)
        {
            LuaSource[] sources = [new("Alpha", "one.lua", prefix + string.Join("\n", Enumerable.Repeat(registration, count)))];
            if (crossProvider) sources = [.. sources, new("Beta", "two.lua", "Observe('PlayerPuppet', 'Value', function() end)")];
            ModSourceInventory inventory = new([], sources, [], []);
            return Receipt() with { InteractionFindings = InteractionReportBuilder.Build(inventory), LuaCallbacks = LuaCallbackEvidenceAnalyzer.Analyze(sources) };
        }
        ProfileScanReceipt originalReceipt = BuildReceipt(2, "");
        ConflictWorkItem original = ConflictWorkQueueBuilder.Build(originalReceipt, []).Single(value => value.Surface == ConflictSurface.ScriptAndTweak);
        EvidenceDecision decision = Decision(originalReceipt, original, original.EvidenceSha256, "Two callbacks reviewed.");
        ConflictWorkItem shifted = ConflictWorkQueueBuilder.Build(BuildReceipt(2, "\n\n"), [decision]).Single(value => value.Surface == ConflictSurface.ScriptAndTweak);
        ConflictWorkItem changed = ConflictWorkQueueBuilder.Build(BuildReceipt(3, ""), [decision]).Single(value => value.Surface == ConflictSurface.ScriptAndTweak);

        Assert.AreEqual(original.EvidenceSha256, shifted.EvidenceSha256);
        Assert.AreEqual(ConflictWorkState.Reviewed, shifted.State);
        Assert.AreNotEqual(original.EvidenceSha256, changed.EvidenceSha256);
        Assert.AreNotEqual(ConflictWorkState.Reviewed, changed.State);
    }

    [TestMethod]
    public void FixRoundMixedLanguageCetCountsSameLineRegistrations()
    {
        string registration = "Override('PlayerPuppet', 'Value', function() return 1 end)";
        ProfileScanReceipt BuildReceipt(int count, string separator)
        {
            ModSourceInventory inventory = new([new("Beta", "two.reds", "@addMethod(PlayerPuppet)\npublic func Value() -> Int32 { return 1; }")],
                [new("Alpha", "one.lua", string.Join(separator, Enumerable.Repeat(registration, count)))], [], []);
            return Receipt() with
            {
                InteractionFindings = InteractionReportBuilder.Build(inventory),
                RedScriptFlows = RedScriptFlowEvidenceAnalyzer.Analyze(inventory.RedScripts),
                LuaCallbacks = LuaCallbackEvidenceAnalyzer.Analyze(inventory.LuaSources)
            };
        }
        ConflictWorkItem original = ConflictWorkQueueBuilder.Build(BuildReceipt(2, " "), []).Single(value => value.Target == "PlayerPuppet.Value()");
        ConflictWorkItem shifted = ConflictWorkQueueBuilder.Build(BuildReceipt(2, "\n"), []).Single(value => value.Target == original.Target);
        ConflictWorkItem changed = ConflictWorkQueueBuilder.Build(BuildReceipt(3, " "), []).Single(value => value.Target == original.Target);

        Assert.AreEqual(original.EvidenceSha256, shifted.EvidenceSha256);
        Assert.AreNotEqual(original.EvidenceSha256, changed.EvidenceSha256);
    }

    [TestMethod]
    [DataRow("Int32", 2)]
    [DataRow("Bool", 3)]
    public void FixRoundFieldTypeAndMultiplicityInvalidateReview(string changedType, int changedCount)
    {
        ProfileScanReceipt BuildReceipt(string type, int count, string prefix)
        {
            string field = "@addField(PlayerPuppet)\nlet sharedState: " + type + ";";
            ModSourceInventory inventory = new([new("Alpha", "fields.reds", prefix + string.Join("\n", Enumerable.Repeat(field, count)))], [], [], []);
            return Receipt() with { InteractionFindings = InteractionReportBuilder.Build(inventory) };
        }
        ProfileScanReceipt originalReceipt = BuildReceipt("Bool", 2, "");
        ConflictWorkItem original = ConflictWorkQueueBuilder.Build(originalReceipt, []).Single(value => value.Surface == ConflictSurface.ScriptAndTweak);
        EvidenceDecision decision = Decision(originalReceipt, original, original.EvidenceSha256, "Fields reviewed.");
        ConflictWorkItem shifted = ConflictWorkQueueBuilder.Build(BuildReceipt("Bool", 2, "\n\n"), [decision]).Single(value => value.Surface == ConflictSurface.ScriptAndTweak);
        ConflictWorkItem changed = ConflictWorkQueueBuilder.Build(BuildReceipt(changedType, changedCount, ""), [decision]).Single(value => value.Surface == ConflictSurface.ScriptAndTweak);

        Assert.AreEqual(original.EvidenceSha256, shifted.EvidenceSha256);
        Assert.AreEqual(ConflictWorkState.Reviewed, shifted.State);
        Assert.AreNotEqual(original.EvidenceSha256, changed.EvidenceSha256);
        Assert.AreNotEqual(ConflictWorkState.Reviewed, changed.State);
        Assert.AreEqual(EvidenceClassification.CompilerEvidence, original.Classification);
        StringAssert.Contains(original.NextAction, "compiler");
    }

    [TestMethod]
    [DataRow("replaceMethod", "return 2;", EvidenceClassification.Exclusive)]
    [DataRow("replaceMethod", "return 1;", EvidenceClassification.Composable)]
    [DataRow("addMethod", "return 2;", EvidenceClassification.CompilerEvidence)]
    public void SameProviderMethodFindingsReachQueueWithDeclarationWording(string annotation, string secondBody, EvidenceClassification expected)
    {
        string text = "@" + annotation + "(PlayerPuppet)\npublic func Value() -> Int32 { return 1; }\n@" + annotation + "(PlayerPuppet)\npublic func Value() -> Int32 { " + secondBody + " }";
        ModSourceInventory inventory = new([new("Alpha", "one.reds", text)], [], [], []);
        InteractionFinding finding = InteractionReportBuilder.Build(inventory).Single();
        ProfileScanReceipt receipt = Receipt() with { InteractionFindings = [finding], RedScriptFlows = RedScriptFlowEvidenceAnalyzer.Analyze(inventory.RedScripts) };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Surface == ConflictSurface.ScriptAndTweak);

        Assert.AreEqual(expected, item.Classification);
        Assert.AreEqual("Alpha", item.Providers.Single());
        AssertSingleProviderWording(finding, item);
        if (expected == EvidenceClassification.Exclusive) StringAssert.Contains(item.NextAction, "declaration");
    }

    [TestMethod]
    [DataRow("Items.Test.value: 1\nItems.Test.value: 2", EvidenceClassification.CompetingDeclaration)]
    [DataRow("Items.Test.tags: [Items.A]\nItems.Test.tags: [Items.B]", EvidenceClassification.CompetingDeclaration)]
    [DataRow("Items.Test:\n  $base: Items.A\nItems.Test:\n  $base: Items.B", EvidenceClassification.Review)]
    [DataRow("Items.Test.tags: [!append Items.A, !append Items.A]", EvidenceClassification.Review)]
    public void SameProviderTweakFindingsReachQueueWithDeclarationWording(string text, EvidenceClassification expected)
    {
        ModSourceInventory inventory = new([], [], [new("Alpha", "one.yaml", text)], []);
        TweakOverlap[] overlaps = TweakInteractionAnalyzer.Analyze(inventory.TweakSources);
        InteractionFinding finding = InteractionReportBuilder.Build(inventory).Single();
        ProfileScanReceipt receipt = Receipt() with { InteractionFindings = [finding], TweakOverlaps = overlaps };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Surface == ConflictSurface.ScriptAndTweak);

        Assert.AreEqual(expected, item.Classification);
        Assert.IsTrue(item.IsActionable);
        Assert.AreEqual("Alpha", item.Providers.Single());
        AssertSingleProviderWording(finding, item);
    }

    [TestMethod]
    public void SameProviderCetOverridesReachQueueForReview()
    {
        string registration = "Override('PlayerPuppet', 'Value', function(self, wrapped) return wrapped() end)";
        ModSourceInventory inventory = new([], [new("Alpha", "one.lua", registration + "\n" + registration)], [], []);
        InteractionFinding finding = InteractionReportBuilder.Build(inventory).Single();
        ProfileScanReceipt receipt = Receipt() with { InteractionFindings = [finding], LuaCallbacks = LuaCallbackEvidenceAnalyzer.Analyze(inventory.LuaSources) };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Surface == ConflictSurface.ScriptAndTweak);

        Assert.AreEqual(EvidenceClassification.Review, item.Classification);
        Assert.AreEqual("Alpha", item.Providers.Single());
        AssertSingleProviderWording(finding, item);
    }

    private static void AssertSingleProviderWording(InteractionFinding finding, ConflictWorkItem item)
    {
        string prose = string.Join(" ", finding.Summary, item.Summary, item.NextAction, item.ClassificationLabel, item.ProofLabel, item.MeaningLabel, item.BoundaryLabel);
        foreach (string phrase in new[] { "mods", "either mod", "another provider", "Multiple providers", "active providers", "Both mods", "Two active tweak files" })
        {
            Assert.IsFalse(prose.Contains(phrase, StringComparison.OrdinalIgnoreCase), prose);
        }
    }

    [TestMethod]
    public void SameProviderDuplicateReplacementsWithStoppingWrapperStillNeedAFeatureCheck()
    {
        string replacement = "@replaceMethod(PlayerPuppet)\npublic func Value() -> Int32 { return 1; }";
        string wrapper = "@wrapMethod(PlayerPuppet)\npublic func Value() -> Int32 { return 0; }";
        ModSourceInventory inventory = new([new("Alpha", "one.reds", replacement + "\n" + replacement + "\n" + wrapper)], [], [], []);
        InteractionFinding finding = InteractionReportBuilder.Build(inventory).Single();
        ProfileScanReceipt receipt = Receipt() with { InteractionFindings = [finding], RedScriptFlows = RedScriptFlowEvidenceAnalyzer.Analyze(inventory.RedScripts) };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Surface == ConflictSurface.ScriptAndTweak);

        Assert.AreEqual(EvidenceClassification.OrderSensitive, item.Classification);
        StringAssert.StartsWith(item.NextAction, "Test");
        AssertSingleProviderWording(finding, item);
    }

    [TestMethod]
    public void SameProviderDuplicateFieldsReachQueueForCompilerEvidence()
    {
        string field = "@addField(PlayerPuppet)\nlet sharedState: Bool;";
        ModSourceInventory inventory = new([new("Alpha", "one.reds", field + "\n" + field)], [], [], []);
        InteractionFinding finding = InteractionReportBuilder.Build(inventory).Single();
        ProfileScanReceipt receipt = Receipt() with { InteractionFindings = [finding], RedScriptFlows = RedScriptFlowEvidenceAnalyzer.Analyze(inventory.RedScripts) };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Surface == ConflictSurface.ScriptAndTweak);

        Assert.AreEqual(EvidenceClassification.CompilerEvidence, item.Classification);
        Assert.AreEqual("PlayerPuppet.sharedState", item.Target);
        AssertSingleProviderWording(finding, item);
    }

    [TestMethod]
    public void SameProviderIntentionalChainsProduceNoQueueRows()
    {
        string wrapper = "@wrapMethod(PlayerPuppet)\npublic func Value() -> Int32 { return wrappedMethod(); }";
        string observer = "Observe('PlayerPuppet', 'Value', function() end)";
        ModSourceInventory inventory = new(
            [new("Alpha", "one.reds", wrapper + "\n" + wrapper)],
            [new("Alpha", "one.lua", observer + "\n" + observer)],
            [new("Alpha", "one.yaml", "Items.Test.tags: [!append Items.A, !append Items.B, !append-once Items.C, !append-once Items.C]")], []);
        ProfileScanReceipt receipt = Receipt() with
        {
            InteractionFindings = InteractionReportBuilder.Build(inventory),
            RedScriptFlows = RedScriptFlowEvidenceAnalyzer.Analyze(inventory.RedScripts),
            LuaCallbacks = LuaCallbackEvidenceAnalyzer.Analyze(inventory.LuaSources),
            TweakOverlaps = TweakInteractionAnalyzer.Analyze(inventory.TweakSources)
        };

        Assert.IsFalse(ConflictWorkQueueBuilder.Build(receipt, []).Any(value => value.Surface == ConflictSurface.ScriptAndTweak));
    }

    [TestMethod]
    public void BuildSeparatesAttentionFromSafeNoiseAndAppliesMatchingReview()
    {
        ProfileScanReceipt receipt = Receipt();
        ConflictWorkItem[] first = ConflictWorkQueueBuilder.Build(receipt, []);
        ConflictWorkItem overwrite = first.Single(value => value.Target == "base\\shared.mesh");
        EvidenceDecision decision = Decision(receipt, overwrite, overwrite.EvidenceSha256, "The patch intentionally wins.");

        ConflictWorkItem[] reviewed = ConflictWorkQueueBuilder.Build(receipt, [decision]);

        Assert.AreEqual(ConflictWorkState.Reviewed, reviewed.Single(value => value.Target == overwrite.Target).State);
        Assert.AreEqual(EvidenceClassification.Intentional, reviewed.Single(value => value.Target == overwrite.Target).Classification);
        Assert.AreEqual(ConflictWorkState.NoActionNeeded, reviewed.Single(value => value.Target == "r6\\scripts\\same.reds").State);
        Assert.AreEqual(ConflictWorkState.NeedsAttention, reviewed.Single(value => value.Target == "broken.archive").State);
    }

    [TestMethod]
    public void BuildExpiresReviewWhenEvidenceChanges()
    {
        ProfileScanReceipt receipt = Receipt();
        ConflictWorkItem original = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Target == "base\\shared.mesh");
        EvidenceDecision decision = Decision(receipt, original, original.EvidenceSha256, "The patch intentionally wins.");
        ResourceConflict changed = receipt.ResourceConflicts[0] with { EngineWinnerArchive = "alpha.archive" };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt with { ResourceConflicts = [changed] }, [decision]).Single(value => value.Target == original.Target);

        Assert.AreEqual(ConflictWorkState.ReviewWhenRelevant, item.State);
        Assert.AreEqual(EvidenceClassification.EffectiveOverwrite, item.Classification);
    }

    [TestMethod]
    public void BuildDoesNotAllowAReviewToSuppressUnresolvedEvidence()
    {
        ProfileScanReceipt receipt = Receipt();
        ConflictWorkItem unresolved = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Target == "broken.archive");
        EvidenceDecision decision = Decision(receipt, unresolved, unresolved.EvidenceSha256, "Ignore the unreadable archive.");

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, [decision]).Single(value => value.Target == unresolved.Target);

        Assert.AreEqual(ConflictWorkState.NeedsAttention, item.State);
        Assert.AreEqual(EvidenceClassification.Unresolved, item.Classification);
    }

    [TestMethod]
    public void BuildKeepsScriptReviewAcrossUnrelatedSourceContainerEdits()
    {
        ProfileScanReceipt receipt = Receipt() with
        {
            InteractionFindings = [new InteractionFinding("DamageSystem.ProcessHit()", InteractionFindingKind.Exclusive, "suppressed", ["Alpha", "Beta"])],
            RedScriptFlows = [new RedScriptFlowEvidence("Alpha", "alpha.reds", "DamageSystem.ProcessHit()", RedScriptFlowKind.Replace, RedScriptContinuationEvidence.NotApplicable, EvidenceConfidence.ExactToken, EvidenceImpact.Review, 1, new string('a', 64), new string('c', 64))]
        };
        ConflictWorkItem original = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Target == "DamageSystem.ProcessHit()");
        EvidenceDecision decision = Decision(receipt, original, original.EvidenceSha256, "Alpha owns the method.");
        ProfileScanReceipt changed = receipt with { RedScriptFlows = [receipt.RedScriptFlows[0] with { SourceHash = new string('b', 64) }] };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(changed, [decision]).Single(value => value.Target == original.Target);

        Assert.AreEqual(ConflictWorkState.Reviewed, item.State);
        Assert.AreEqual(EvidenceClassification.Intentional, item.Classification);
    }

    [TestMethod]
    public void BuildIncludesSharedStateAndUsesTheCurrentMatchingDecision()
    {
        SharedStateWriteFinding shared = new(SharedStateSurface.Blackboard, "UI_System.IsInMenu", EvidenceConfidence.Literal, EvidenceImpact.Review, [new SharedStateWrite("Alpha", "a.reds", SharedStateSurface.Blackboard, "UI_System.IsInMenu", 1), new SharedStateWrite("Beta", "b.reds", SharedStateSurface.Blackboard, "UI_System.IsInMenu", 2)]);
        ProfileScanReceipt receipt = Receipt() with { SharedStateWrites = [shared] };
        ConflictWorkItem original = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Surface == ConflictSurface.SharedState);
        EvidenceDecision expired = Decision(receipt, original, new string('a', 64), "Old review.");
        EvidenceDecision current = Decision(receipt, original, original.EvidenceSha256, "Current review.");

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, [expired, current]).Single(value => value.Surface == ConflictSurface.SharedState);

        Assert.AreEqual(ConflictWorkState.Reviewed, item.State);
        Assert.AreEqual("Current review.", item.ReviewRationale);
    }

    [TestMethod]
    public void BuildKeepsSharedStateReviewAcrossUnrelatedSourceContainerEdits()
    {
        SharedStateWrite alpha = new("Alpha", "a.reds", SharedStateSurface.Blackboard, "UI_System.IsInMenu", 1, "SetBool", "SetBool(UI_System.IsInMenu", new string('a', 64));
        SharedStateWrite beta = new("Beta", "b.reds", SharedStateSurface.Blackboard, "UI_System.IsInMenu", 2, "SetBool", "SetBool(UI_System.IsInMenu", new string('b', 64));
        ProfileScanReceipt receipt = Receipt() with { SharedStateWrites = [new SharedStateWriteFinding(SharedStateSurface.Blackboard, "UI_System.IsInMenu", EvidenceConfidence.Literal, EvidenceImpact.Review, [alpha, beta])] };
        ConflictWorkItem original = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Surface == ConflictSurface.SharedState);
        EvidenceDecision decision = Decision(receipt, original, original.EvidenceSha256, "Reviewed state write.");
        ProfileScanReceipt changed = receipt with { SharedStateWrites = [receipt.SharedStateWrites[0] with { Writes = [alpha with { SourceHash = new string('c', 64) }, beta] }] };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(changed, [decision]).Single(value => value.Surface == ConflictSurface.SharedState);

        Assert.AreEqual(ConflictWorkState.Reviewed, item.State);
        Assert.AreEqual(EvidenceClassification.Intentional, item.Classification);
    }

    [TestMethod]
    public void BuildExplainsWrapperActionsAndContinuationInPlainLanguage()
    {
        InteractionFinding finding = new("CraftingMainLogicController.OnUninitialize()", InteractionFindingKind.Review, "wrappers", ["Upgrade Weapons Unlocked", "Crafting Recipe Owned Labels"]);
        RedScriptFlowEvidence first = new("Upgrade Weapons Unlocked", "upgrade.reds", finding.Target, RedScriptFlowKind.Wrap, RedScriptContinuationEvidence.Continues, EvidenceConfidence.ExactToken, EvidenceImpact.None, 10, new string('a', 64));
        RedScriptFlowEvidence second = new("Crafting Recipe Owned Labels", "crafting.reds", finding.Target, RedScriptFlowKind.Wrap, RedScriptContinuationEvidence.Continues, EvidenceConfidence.ExactToken, EvidenceImpact.None, 20, new string('b', 64));
        ProfileScanReceipt receipt = Receipt() with { InteractionFindings = [finding], RedScriptFlows = [first, second] };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Target == finding.Target);

        Assert.IsTrue(item.Summary.Contains("continues", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(item.Summary.Contains("Upgrade Weapons Unlocked", StringComparison.Ordinal));
        Assert.IsTrue(item.NextAction.Contains("No conflict", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(ConflictWorkState.NoActionNeeded, item.State);
        Assert.AreEqual(EvidenceClassification.Informational, item.Classification);
        Assert.AreEqual(ConflictCaseKind.SharedTarget, item.CaseKind);
        StringAssert.Contains(item.BoundaryLabel, "does not prove behavioral compatibility");
    }

    [TestMethod]
    public void AddedMethodAndCetOverrideNeedRuntimeEvidenceNotCompilerOnly()
    {
        ModSourceInventory inventory = new([new("Alpha", "method.reds", "@addMethod(PlayerPuppet)\npublic func Value() -> Int32 { return 1; }")],
            [new("Beta", "init.lua", "Override('PlayerPuppet', 'Value', function() return 2 end)")], [], []);
        ProfileScanReceipt receipt = Receipt() with
        {
            InteractionFindings = InteractionReportBuilder.Build(inventory),
            RedScriptFlows = RedScriptFlowEvidenceAnalyzer.Analyze(inventory.RedScripts),
            LuaCallbacks = LuaCallbackEvidenceAnalyzer.Analyze(inventory.LuaSources)
        };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Surface == ConflictSurface.ScriptAndTweak);

        Assert.AreEqual(EvidenceClassification.Review, item.Classification);
        Assert.AreEqual(ConflictCaseKind.RuntimeCheck, item.CaseKind);
        StringAssert.Contains(item.NextAction, "CET");
        StringAssert.Contains(item.BoundaryLabel, "compiler alone");
    }

    [TestMethod]
    public void BuildKeepsNonContinuingWrapperAsOneRuntimeCheck()
    {
        InteractionFinding finding = new("DamageSystem.ProcessHit()", InteractionFindingKind.Review, "wrapper can suppress", ["Alpha", "Beta"]);
        RedScriptFlowEvidence first = new("Alpha", "alpha.reds", finding.Target, RedScriptFlowKind.Wrap, RedScriptContinuationEvidence.EarlyReturnBeforeContinuation, EvidenceConfidence.ExactToken, EvidenceImpact.Review, 10, new string('a', 64));
        RedScriptFlowEvidence second = new("Beta", "beta.reds", finding.Target, RedScriptFlowKind.Wrap, RedScriptContinuationEvidence.Continues, EvidenceConfidence.ExactToken, EvidenceImpact.None, 20, new string('b', 64));
        ProfileScanReceipt receipt = Receipt() with { InteractionFindings = [finding], RedScriptFlows = [first, second] };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Target == finding.Target);

        Assert.AreEqual(ConflictWorkState.ReviewWhenRelevant, item.State);
        Assert.AreEqual(EvidenceClassification.OrderSensitive, item.Classification);
        Assert.AreEqual(ConflictCaseKind.OrderSensitive, item.CaseKind);
    }

    [TestMethod]
    public void BuildLabelsCompetingTweakValuesAsOrderSensitive()
    {
        TweakOperation alpha = new("Alpha", "alpha.yaml", "Items.Pistol.damage", "10", false);
        TweakOperation beta = new("Beta", "beta.yaml", "Items.Pistol.damage", "20", false);
        InteractionFinding finding = new("Items.Pistol.damage", InteractionFindingKind.Review, "values differ", ["Alpha", "Beta"]);
        ProfileScanReceipt receipt = Receipt() with { InteractionFindings = [finding], TweakOverlaps = [new TweakOverlap(finding.Target, TweakOverlapKind.ScalarOverwrite, [alpha, beta])] };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Target == finding.Target);

        Assert.AreEqual(EvidenceClassification.CompetingDeclaration, item.Classification);
        Assert.AreEqual(ConflictCaseKind.CompetingDeclaration, item.CaseKind);
        Assert.AreEqual("Review: different values assigned", item.ClassificationLabel);
        Assert.AreEqual("Two active sources assign different values to one field", item.ProofLabel);
        StringAssert.Contains(item.Summary, "10");
        StringAssert.Contains(item.Summary, "20");
    }

    [TestMethod]
    public void BuildExplainsThatTweakRemovesRunBeforeAdds()
    {
        TweakOperation alpha = new("Alpha", "alpha.yaml", "Vendors.Test.itemStock", "Items.StockA", true, TweakOperationKind.ArrayAppend);
        TweakOperation beta = new("Beta", "beta.yaml", "Vendors.Test.itemStock", "Items.StockA", true, TweakOperationKind.ArrayRemove);
        TweakOverlap overlap = new(alpha.Target, TweakOverlapKind.ComposableMutation, [alpha, beta]);
        InteractionFinding finding = new(alpha.Target, InteractionFindingKind.Composable, "phased", ["Alpha", "Beta"]);
        ProfileScanReceipt receipt = Receipt() with { InteractionFindings = [finding], TweakOverlaps = [overlap] };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Target == finding.Target);

        Assert.AreEqual("No action: remove happens before add", item.ClassificationLabel);
        Assert.AreEqual("TweakXL's documented order decides membership", item.ProofLabel);
        StringAssert.Contains(item.Summary, "Items.StockA");
        StringAssert.Contains(item.Summary, "adds");
        StringAssert.Contains(item.Summary, "removes");
    }

    [TestMethod]
    public void BuildKeepsDistinctVendorAppendsOutOfTheActionableQueue()
    {
        TweakOperation alpha = new("Alpha", "alpha.yaml", "Vendors.Test.itemStock", "Items.StockA", true, TweakOperationKind.ArrayAppend);
        TweakOperation beta = new("Beta", "beta.yaml", "Vendors.Test.itemStock", "Items.StockB", true, TweakOperationKind.ArrayAppend);
        TweakOverlap overlap = new(alpha.Target, TweakOverlapKind.ComposableMutation, [alpha, beta]);
        InteractionFinding finding = new(alpha.Target, InteractionFindingKind.Composable, "compose", ["Alpha", "Beta"]);
        ProfileScanReceipt receipt = Receipt() with { InteractionFindings = [finding], TweakOverlaps = [overlap] };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Target == finding.Target);

        Assert.AreEqual(ConflictWorkState.NoActionNeeded, item.State);
        Assert.AreEqual("No action: all additions apply", item.ClassificationLabel);
        StringAssert.Contains(item.Summary, "applies every addition");
    }

    [TestMethod]
    public void BuildExplainsDistinctAddAndRemoveAsCompatibleArrayChanges()
    {
        TweakOperation alpha = new("Alpha", "alpha.yaml", "Items.Test.tags", "Items.A", true, TweakOperationKind.ArrayAppend);
        TweakOperation beta = new("Beta", "beta.yaml", "Items.Test.tags", "Items.B", true, TweakOperationKind.ArrayRemove);
        TweakOverlap overlap = new(alpha.Target, TweakOverlapKind.ComposableMutation, [alpha, beta]);
        InteractionFinding finding = new(alpha.Target, InteractionFindingKind.Composable, "compose", ["Alpha", "Beta"]);
        ProfileScanReceipt receipt = Receipt() with { InteractionFindings = [finding], TweakOverlaps = [overlap] };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Target == finding.Target);

        Assert.AreEqual("No action: array changes can coexist", item.ClassificationLabel);
        Assert.AreEqual("The mods change different array values", item.ProofLabel);
        StringAssert.Contains(item.Summary, "no operation cancels another");
    }

    [TestMethod]
    public void BuildDoesNotClaimLastWinsForCompetingRecordConstruction()
    {
        TweakOperation alpha = new("Alpha", "alpha.yaml", "Items.NewRecord.$base", "Items.TemplateA", false, TweakOperationKind.BaseDeclaration);
        TweakOperation beta = new("Beta", "beta.yaml", "Items.NewRecord.$base", "Items.TemplateB", false, TweakOperationKind.BaseDeclaration);
        TweakOverlap overlap = new(alpha.Target, TweakOverlapKind.RecordDefinitionCollision, [alpha, beta]);
        InteractionFinding finding = new(alpha.Target, InteractionFindingKind.Review, "definition", ["Alpha", "Beta"]);
        ProfileScanReceipt receipt = Receipt() with { InteractionFindings = [finding], TweakOverlaps = [overlap] };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Target == finding.Target);

        Assert.AreEqual(EvidenceClassification.Review, item.Classification);
        Assert.AreEqual("Review: same record is built differently", item.ClassificationLabel);
        Assert.AreEqual("Several mods construct the same record with different source", item.ProofLabel);
        StringAssert.Contains(item.NextAction, "one record definition");
    }

    [TestMethod]
    public void BuildKeepsUnknownSourceArrayContentsAsOneReviewCase()
    {
        TweakOperation alpha = new("Alpha", "alpha.yaml", "Items.Test.tags", "Items.Source.tags", true, TweakOperationKind.ArrayAppendFrom);
        TweakOperation beta = new("Beta", "beta.yaml", "Items.Test.tags", "Items.A", true, TweakOperationKind.ArrayRemove);
        TweakOverlap overlap = new(alpha.Target, TweakOverlapKind.SourceArrayDependency, [alpha, beta]);
        InteractionFinding finding = new(alpha.Target, InteractionFindingKind.Review, "source", ["Alpha", "Beta"]);
        ProfileScanReceipt receipt = Receipt() with { InteractionFindings = [finding], TweakOverlaps = [overlap] };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Target == finding.Target);

        Assert.AreEqual(EvidenceClassification.Review, item.Classification);
        Assert.AreEqual("Review: copied array needs verification", item.ClassificationLabel);
        Assert.AreEqual("One mod copies an array that another mod changes", item.ProofLabel);
    }

    [TestMethod]
    public void BuildGroupsLooseFilesOnlyWhenTheReviewCoversTheNamedGroup()
    {
        VirtualFileProvider alphaOne = new("Alpha", "a1", 1, new string('a', 64), 0, 10);
        VirtualFileProvider betaOne = new("Beta", "b1", 1, new string('b', 64), 1, 9);
        VirtualFileProvider alphaTwo = alphaOne with { PhysicalPath = "a2", Sha256 = new string('c', 64) };
        VirtualFileProvider betaTwo = betaOne with { PhysicalPath = "b2", Sha256 = new string('d', 64) };
        ProfileScanReceipt receipt = Receipt() with { VirtualFileShadows = [new VirtualFileShadow("r6\\scripts\\one.reds", "Alpha", VirtualFileRelation.Different, [alphaOne, betaOne]), new VirtualFileShadow("r6\\scripts\\two.reds", "Alpha", VirtualFileRelation.Different, [alphaTwo, betaTwo])] };

        ConflictWorkItem[] items = ConflictWorkQueueBuilder.Build(receipt, []).Where(value => value.Surface == ConflictSurface.VirtualFile).ToArray();
        ConflictWorkItem group = items.Single();
        EvidenceDecision decision = Decision(receipt, group, group.EvidenceSha256, "Alpha intentionally wins these two named paths.");
        ConflictWorkItem[] reviewed = ConflictWorkQueueBuilder.Build(receipt, [decision]).Where(value => value.Surface == ConflictSurface.VirtualFile).ToArray();
        string[] expectedPaths = ["r6\\scripts\\one.reds", "r6\\scripts\\two.reds"];

        Assert.AreEqual(1, items.Length);
        CollectionAssert.AreEquivalent(expectedPaths, group.RelatedTargets);
        Assert.AreEqual(ConflictWorkState.Reviewed, reviewed.Single().State);
    }

    [TestMethod]
    public void BuildCollapsesASharedArchiveOrderFailureIntoOneAction()
    {
        ResourceProvider alpha = new("alpha.archive", 7, "base\\one.mesh", new string('a', 40), ProviderName: "Alpha");
        ResourceProvider beta = new("beta.archive", 7, "base\\one.mesh", new string('b', 40), ProviderName: "Beta");
        ResourceConflict first = new(7, "base\\one.mesh", ResourceConflictKind.Unresolved, "unresolved", [alpha, beta]);
        ResourceConflict second = first with { ResourceHash = 8, DisplayName = "base\\two.mesh" };
        ArchiveOrderEvidence evidence = new(ArchiveOrderEvidenceKind.Unresolved, "Settings", "modlist.txt", "Beta.archive is missing from the active archive order.") { MissingEntries = ["Beta.archive"] };
        ProfileScanReceipt receipt = Receipt() with { ArchiveFailures = [], ResourceConflicts = [first, second], ArchiveOrderEvidence = evidence };

        ConflictWorkItem[] items = ConflictWorkQueueBuilder.Build(receipt, []);

        ConflictWorkItem diagnostic = items.Single(value => value.Surface == ConflictSurface.Diagnostic && value.Target == "Archive load order");
        Assert.AreEqual(ConflictWorkState.NeedsAttention, diagnostic.State);
        Assert.IsTrue(diagnostic.Summary.Contains("2 packed resources", StringComparison.Ordinal));
        Assert.IsFalse(items.Any(value => value.Surface == ConflictSurface.PackedResource));
    }

    [TestMethod]
    public void BuildTreatsIgnoredInactiveArchiveLinesAsOneMaintenanceItem()
    {
        ArchiveOrderEvidence evidence = new(ArchiveOrderEvidenceKind.ManagedModlist, "Settings", "modlist.txt", "One inactive archive entry was ignored.") { IgnoredEntries = ["Inactive.archive"] };
        ProfileScanReceipt receipt = Receipt() with { ArchiveFailures = [], ArchiveOrderEvidence = evidence };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Target == "Archive order maintenance");

        Assert.AreEqual(ConflictWorkState.ReviewWhenRelevant, item.State);
        Assert.IsTrue(item.NextAction.Contains("Load order", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void BuildKeepsParserCoverageFailuresOutOfTheConflictQueue()
    {
        ProfileScanReceipt receipt = Receipt() with
        {
            ArchiveXlFailures = [new ArchiveXlSourceFailure("Alpha", "alpha.xl", "A future ArchiveXL operation is not covered.", ArchiveXlFailureKind.Coverage)],
            SourceFailures = [new SourceAnalysisFailure("Beta", "beta.yaml", "TweakXL", "TweakXL source could not be represented completely: Unsupported shape")]
        };

        ConflictWorkItem[] items = ConflictWorkQueueBuilder.Build(receipt, []);

        Assert.IsFalse(items.Any(value => value.Target is "alpha.xl" or "beta.yaml"));
    }

    [TestMethod]
    public void BuildKeepsMalformedArchiveXlFailuresVisibleRegardlessOfMessagePrefix()
    {
        ProfileScanReceipt receipt = Receipt() with { ArchiveXlFailures = [new ArchiveXlSourceFailure("Alpha", "alpha.xl", "ArchiveXL source could not be represented completely: invalid mapping", ArchiveXlFailureKind.Malformed)] };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Target == "alpha.xl");

        Assert.AreEqual(ConflictSurface.Diagnostic, item.Surface);
        Assert.AreEqual(ConflictWorkState.NeedsAttention, item.State);
    }

    [TestMethod]
    public void BuildKeepsOperationalSourceFailuresVisible()
    {
        ProfileScanReceipt receipt = Receipt() with { SourceFailures = [new SourceAnalysisFailure("Beta", "Beta", "MO2 path", "Active mod directory missing")] };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Target == "Beta");

        Assert.AreEqual(ConflictSurface.Diagnostic, item.Surface);
        Assert.AreEqual(ConflictWorkState.NeedsAttention, item.State);
    }

    [TestMethod]
    public void BuildDescribesAnInvalidRedmodAsASetupProblemInsteadOfUnknownConflictEvidence()
    {
        SourceAnalysisFailure failure = new("Broken REDmod", "mods\\Broken\\info.json", "REDmod", "This folder will not load as a REDmod because its required info.json descriptor is missing.");
        ProfileScanReceipt receipt = Receipt() with { SourceFailures = [failure] };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Target == failure.FilePath);

        Assert.AreEqual("REDmod not loaded", item.ClassificationLabel);
        Assert.AreEqual("Required descriptor is missing or invalid", item.ProofLabel);
        StringAssert.Contains(item.NextAction, "Reinstall or repair");
    }

    [TestMethod]
    public void BuildKeepsOperationalArchiveXlFailuresVisible()
    {
        ProfileScanReceipt receipt = Receipt() with { ArchiveXlFailures = [new ArchiveXlSourceFailure("Alpha", "missing-root", "The ArchiveXL provider root does not exist.")] };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Target == "missing-root");

        Assert.AreEqual(ConflictSurface.Diagnostic, item.Surface);
        Assert.AreEqual(ConflictWorkState.NeedsAttention, item.State);
    }

    [TestMethod]
    public void BuildRoutesARedmodOrderFailureToRedmodDeployment()
    {
        ArchiveOrderEvidence evidence = new(ArchiveOrderEvidenceKind.Unresolved, "Overwrite", "MO_REDmod_load_order.txt", "The REDmod order is stale.") { SourcePaths = ["MO_REDmod_load_order.txt"], MissingEntries = ["RedmodA"], ProblemLane = ArchiveOrderProblemLane.Redmod };
        ProfileScanReceipt receipt = Receipt() with { ArchiveFailures = [], ArchiveOrderEvidence = evidence };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Target == "Archive load order");

        Assert.IsTrue(item.NextAction.Contains("Re-deploy REDmods", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(item.NextAction.Contains("add every named active archive", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void WorkItemsExposeReadableLabelsForTheDesktop()
    {
        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(Receipt(), []).Single(value => value.Target == "base\\shared.mesh");

        Assert.AreEqual("Review when relevant", item.StateLabel);
        Assert.AreEqual("Packed archive file", item.SurfaceLabel);
        Assert.AreEqual("One deployed file is selected", item.ClassificationLabel);
    }

    [TestMethod]
    public void VortexFileOverrideUsesManagerSpecificActionLanguage()
    {
        VirtualFileProvider winner = new("Beta", "ignored", 4, new string('b', 64), 0);
        VirtualFileProvider loser = new("Alpha", "ignored", 4, new string('a', 64), 1);
        ProfileScanReceipt receipt = Receipt() with { ManagerKind = ModManagerKind.Vortex, VirtualFileShadows = [new VirtualFileShadow("r6\\scripts\\shared.reds", "Beta", VirtualFileRelation.Different, [winner, loser])] };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Surface == ConflictSurface.VirtualFile);

        Assert.AreEqual("The deployed order selects one file", item.ProofLabel);
        StringAssert.Contains(item.Summary, "Vortex");
        StringAssert.Contains(item.NextAction, "Vortex");
        Assert.IsFalse(item.NextAction.Contains("MO2", StringComparison.Ordinal));
    }

    [TestMethod]
    public void InteractionDecisionHashIgnoresPresentationCopyButChangesWithEvidence()
    {
        string target = "DamageSystem.ProcessHit()";
        RedScriptFlowEvidence flow = new("Alpha", "alpha.reds", target, RedScriptFlowKind.Wrap, RedScriptContinuationEvidence.EarlyReturnBeforeContinuation, EvidenceConfidence.ExactToken, EvidenceImpact.Review, 1, new string('a', 64));
        ProfileScanReceipt first = Receipt() with { InteractionFindings = [new InteractionFinding(target, InteractionFindingKind.Review, "First wording", ["Alpha", "Beta"])], RedScriptFlows = [flow] };
        ProfileScanReceipt rewritten = first with { InteractionFindings = [first.InteractionFindings[0] with { Summary = "Better wording" }] };
        ProfileScanReceipt changed = first with { RedScriptFlows = [flow with { Continuation = RedScriptContinuationEvidence.Continues }] };

        string originalHash = ConflictWorkQueueBuilder.Build(first, []).Single(value => value.Target == target).EvidenceSha256;
        string rewrittenHash = ConflictWorkQueueBuilder.Build(rewritten, []).Single(value => value.Target == target).EvidenceSha256;
        string changedHash = ConflictWorkQueueBuilder.Build(changed, []).Single(value => value.Target == target).EvidenceSha256;

        Assert.AreEqual(originalHash, rewrittenHash);
        Assert.AreNotEqual(originalHash, changedHash);
    }

    [TestMethod]
    public void InteractionDecisionHashIgnoresLineShiftsButChangesWithContinuation()
    {
        string target = "DamageSystem.ProcessHit()";
        RedScriptFlowEvidence flow = new("Alpha", "alpha.reds", target, RedScriptFlowKind.Wrap, RedScriptContinuationEvidence.EarlyReturnBeforeContinuation, EvidenceConfidence.ExactToken, EvidenceImpact.Review, 10, new string('a', 64), new string('c', 64));
        ProfileScanReceipt first = Receipt() with { InteractionFindings = [new InteractionFinding(target, InteractionFindingKind.Review, "Review", ["Alpha", "Beta"])], RedScriptFlows = [flow] };
        ProfileScanReceipt shifted = first with { RedScriptFlows = [flow with { Line = 25, SourceHash = new string('b', 64) }] };
        ProfileScanReceipt changed = first with { RedScriptFlows = [flow with { Continuation = RedScriptContinuationEvidence.Continues }] };

        string originalHash = ConflictWorkQueueBuilder.Build(first, []).Single(value => value.Target == target).EvidenceSha256;
        string shiftedHash = ConflictWorkQueueBuilder.Build(shifted, []).Single(value => value.Target == target).EvidenceSha256;
        string changedHash = ConflictWorkQueueBuilder.Build(changed, []).Single(value => value.Target == target).EvidenceSha256;

        Assert.AreEqual(originalHash, shiftedHash);
        Assert.AreNotEqual(originalHash, changedHash);
    }

    [TestMethod]
    public void InteractionDecisionHashUsesTargetScopedRedScriptBodyEvidence()
    {
        string target = "DamageSystem.ProcessHit()";
        RedScriptFlowEvidence flow = new("Alpha", @"C:\old\mods\Alpha\alpha.reds", target, RedScriptFlowKind.Wrap, RedScriptContinuationEvidence.Continues, EvidenceConfidence.ExactToken, EvidenceImpact.None, 10, new string('a', 64), new string('c', 64));
        ProfileScanReceipt first = Receipt() with { InteractionFindings = [new InteractionFinding(target, InteractionFindingKind.Review, "Review", ["Alpha", "Beta"])], RedScriptFlows = [flow] };
        ProfileScanReceipt relocated = first with { RedScriptFlows = [flow with { FilePath = @"D:\new\mods\Alpha\alpha.reds", Line = 30, SourceHash = new string('b', 64) }] };
        ProfileScanReceipt changed = first with { RedScriptFlows = [flow with { BodySha256 = new string('d', 64) }] };

        string originalHash = ConflictWorkQueueBuilder.Build(first, []).Single(value => value.Target == target).EvidenceSha256;
        string relocatedHash = ConflictWorkQueueBuilder.Build(relocated, []).Single(value => value.Target == target).EvidenceSha256;
        string changedHash = ConflictWorkQueueBuilder.Build(changed, []).Single(value => value.Target == target).EvidenceSha256;

        Assert.AreEqual(originalHash, relocatedHash);
        Assert.AreNotEqual(originalHash, changedHash);
    }

    [TestMethod]
    public void InteractionDecisionHashUsesTargetScopedLuaCallbackEvidence()
    {
        string target = "DamageSystem.ProcessHit";
        LuaCallbackEvidence callback = new(LuaCallbackEvidenceKind.Override, target, EvidenceConfidence.Literal, EvidenceImpact.Review, LuaContinuationEvidence.Continues, 10, new string('a', 64), [new LuaSourceCopy("Alpha", @"C:\old\mods\Alpha\alpha.lua")], new string('c', 64));
        ProfileScanReceipt first = Receipt() with { InteractionFindings = [new InteractionFinding(target, InteractionFindingKind.Review, "Review", ["Alpha", "Beta"])], LuaCallbacks = [callback] };
        ProfileScanReceipt relocated = first with { LuaCallbacks = [callback with { Line = 30, SourceHash = new string('b', 64), Copies = [new LuaSourceCopy("Alpha", @"D:\new\mods\Alpha\alpha.lua")] }] };
        ProfileScanReceipt changed = first with { LuaCallbacks = [callback with { CallbackSha256 = new string('d', 64) }] };

        string originalHash = ConflictWorkQueueBuilder.Build(first, []).Single(value => value.Target == target).EvidenceSha256;
        string relocatedHash = ConflictWorkQueueBuilder.Build(relocated, []).Single(value => value.Target == target).EvidenceSha256;
        string changedHash = ConflictWorkQueueBuilder.Build(changed, []).Single(value => value.Target == target).EvidenceSha256;

        Assert.AreEqual(originalHash, relocatedHash);
        Assert.AreNotEqual(originalHash, changedHash);
    }

    [TestMethod]
    public void LuaInteractionDecisionHashIgnoresVendoredGroupingChangesFromUnrelatedEdits()
    {
        string target = "DamageSystem.ProcessHit";
        string registration = "Override('DamageSystem', 'ProcessHit', function(self, wrapped) self.Record(); return wrapped() end)";
        LuaSource alpha = new("Alpha", "alpha.lua", registration);
        LuaSource beta = new("Beta", "beta.lua", registration);
        LuaCallbackEvidence[] grouped = LuaCallbackEvidenceAnalyzer.Analyze([alpha, beta]);
        LuaCallbackEvidence[] splitByUnrelatedEdit = LuaCallbackEvidenceAnalyzer.Analyze([alpha with { Text = registration + "\nlocal unrelated = true" }, beta]);
        ProfileScanReceipt first = Receipt() with { InteractionFindings = [new InteractionFinding(target, InteractionFindingKind.Review, "Review", ["Alpha", "Beta"])], LuaCallbacks = grouped };
        ProfileScanReceipt changed = first with { LuaCallbacks = splitByUnrelatedEdit };

        string originalHash = ConflictWorkQueueBuilder.Build(first, []).Single(value => value.Target == target).EvidenceSha256;
        string changedHash = ConflictWorkQueueBuilder.Build(changed, []).Single(value => value.Target == target).EvidenceSha256;

        Assert.AreEqual(originalHash, changedHash);
    }

    [TestMethod]
    public void LegacyInteractionEvidenceFallsBackToWholeSourceHash()
    {
        string target = "DamageSystem.ProcessHit()";
        RedScriptFlowEvidence flow = new("Alpha", "alpha.reds", target, RedScriptFlowKind.Wrap, RedScriptContinuationEvidence.Continues, EvidenceConfidence.ExactToken, EvidenceImpact.None, 10, new string('a', 64));
        ProfileScanReceipt first = Receipt() with { InteractionFindings = [new InteractionFinding(target, InteractionFindingKind.Review, "Review", ["Alpha", "Beta"])], RedScriptFlows = [flow] };
        ProfileScanReceipt changed = first with { RedScriptFlows = [flow with { SourceHash = new string('b', 64) }] };

        string originalHash = ConflictWorkQueueBuilder.Build(first, []).Single(value => value.Target == target).EvidenceSha256;
        string changedHash = ConflictWorkQueueBuilder.Build(changed, []).Single(value => value.Target == target).EvidenceSha256;

        Assert.AreNotEqual(originalHash, changedHash);
    }

    [TestMethod]
    public void LooseFileDecisionHashIgnoresAbsoluteProfilePositionButChangesWithPayload()
    {
        VirtualFileProvider alpha = new("Alpha", "a", 1, new string('a', 64), 10, 20);
        VirtualFileProvider beta = new("Beta", "b", 1, new string('b', 64), 11, 19);
        ProfileScanReceipt first = Receipt() with { VirtualFileShadows = [new VirtualFileShadow("r6\\scripts\\shared.reds", "Alpha", VirtualFileRelation.Different, [alpha, beta])] };
        ProfileScanReceipt shifted = first with { VirtualFileShadows = [first.VirtualFileShadows[0] with { Providers = [alpha with { ProfilePosition = 30 }, beta with { ProfilePosition = 31 }] }] };
        ProfileScanReceipt changed = first with { VirtualFileShadows = [first.VirtualFileShadows[0] with { Providers = [alpha with { Sha256 = new string('c', 64) }, beta] }] };

        string originalHash = ConflictWorkQueueBuilder.Build(first, []).Single(value => value.Surface == ConflictSurface.VirtualFile).EvidenceSha256;
        string shiftedHash = ConflictWorkQueueBuilder.Build(shifted, []).Single(value => value.Surface == ConflictSurface.VirtualFile).EvidenceSha256;
        string changedHash = ConflictWorkQueueBuilder.Build(changed, []).Single(value => value.Surface == ConflictSurface.VirtualFile).EvidenceSha256;

        Assert.AreEqual(originalHash, shiftedHash);
        Assert.AreNotEqual(originalHash, changedHash);
    }

    [TestMethod]
    public void SharedStateDecisionHashChangesWithOperationButNotLineOrSourceHash()
    {
        SharedStateWrite alpha = new("Alpha", "a.reds", SharedStateSurface.Blackboard, "UI_System.IsInMenu", 1, "SetBool", "SetBool(UI_System.IsInMenu", new string('a', 64));
        SharedStateWrite beta = new("Beta", "b.reds", SharedStateSurface.Blackboard, "UI_System.IsInMenu", 2, "SetBool", "SetBool(UI_System.IsInMenu", new string('b', 64));
        ProfileScanReceipt first = Receipt() with { SharedStateWrites = [new SharedStateWriteFinding(SharedStateSurface.Blackboard, "UI_System.IsInMenu", EvidenceConfidence.Literal, EvidenceImpact.Review, [alpha, beta])] };
        ProfileScanReceipt shifted = first with { SharedStateWrites = [first.SharedStateWrites[0] with { Writes = [alpha with { Line = 20, SourceHash = new string('c', 64) }, beta] }] };
        ProfileScanReceipt changed = first with { SharedStateWrites = [first.SharedStateWrites[0] with { Writes = [alpha with { Operation = "SetInt" }, beta] }] };

        string originalHash = ConflictWorkQueueBuilder.Build(first, []).Single(value => value.Surface == ConflictSurface.SharedState).EvidenceSha256;
        string shiftedHash = ConflictWorkQueueBuilder.Build(shifted, []).Single(value => value.Surface == ConflictSurface.SharedState).EvidenceSha256;
        string changedHash = ConflictWorkQueueBuilder.Build(changed, []).Single(value => value.Surface == ConflictSurface.SharedState).EvidenceSha256;

        Assert.AreEqual(originalHash, shiftedHash);
        Assert.AreNotEqual(originalHash, changedHash);
    }

    private static ProfileScanReceipt Receipt()
    {
        ResourceProvider alpha = new("alpha.archive", 7, "base\\shared.mesh", new string('a', 40), ProviderName: "Alpha");
        ResourceProvider beta = new("beta.archive", 7, "base\\shared.mesh", new string('b', 40), ProviderName: "Beta");
        VirtualFileProvider first = new("Alpha", "ignored", 4, new string('c', 64), 0);
        VirtualFileProvider second = new("Beta", "ignored", 4, new string('c', 64), 1);
        return new ProfileScanReceipt(1, "Standard", DateTimeOffset.UtcNow, ["Alpha", "Beta"], ["beta.archive", "alpha.archive"], [new RdarArchiveFailure("Broken", "broken.archive", "Unreadable archive")], [new ResourceConflict(7, "base\\shared.mesh", ResourceConflictKind.Divergent, "beta.archive", [beta, alpha])], [new VirtualFileShadow("r6\\scripts\\same.reds", "Beta", VirtualFileRelation.Identical, [first, second])], [], [], [], [], [], [], [], InstallationId: "install");
    }

    private static EvidenceDecision Decision(ProfileScanReceipt receipt, ConflictWorkItem item, string hash, string rationale)
        => new(receipt.ProfileName, item.Target, item.Providers, hash, rationale, DateTimeOffset.UtcNow, receipt.InstallationId!, item.Surface);
}

using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class ConflictWorkQueueBuilderTests
{
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
    public void BuildExpiresScriptReviewWhenSourceHashChanges()
    {
        ProfileScanReceipt receipt = Receipt() with
        {
            InteractionFindings = [new InteractionFinding("DamageSystem.ProcessHit()", InteractionFindingKind.Exclusive, "suppressed", ["Alpha", "Beta"])],
            RedScriptFlows = [new RedScriptFlowEvidence("Alpha", "alpha.reds", "DamageSystem.ProcessHit()", RedScriptFlowKind.Replace, RedScriptContinuationEvidence.NotApplicable, EvidenceConfidence.ExactToken, EvidenceImpact.Review, 1, new string('a', 64))]
        };
        ConflictWorkItem original = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Target == "DamageSystem.ProcessHit()");
        EvidenceDecision decision = Decision(receipt, original, original.EvidenceSha256, "Alpha owns the method.");
        ProfileScanReceipt changed = receipt with { RedScriptFlows = [receipt.RedScriptFlows[0] with { SourceHash = new string('b', 64) }] };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(changed, [decision]).Single(value => value.Target == original.Target);

        Assert.AreEqual(ConflictWorkState.NeedsAttention, item.State);
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
    public void BuildExpiresSharedStateReviewWhenSourceChanges()
    {
        SharedStateWrite alpha = new("Alpha", "a.reds", SharedStateSurface.Blackboard, "UI_System.IsInMenu", 1, "SetBool", "SetBool(UI_System.IsInMenu", new string('a', 64));
        SharedStateWrite beta = new("Beta", "b.reds", SharedStateSurface.Blackboard, "UI_System.IsInMenu", 2, "SetBool", "SetBool(UI_System.IsInMenu", new string('b', 64));
        ProfileScanReceipt receipt = Receipt() with { SharedStateWrites = [new SharedStateWriteFinding(SharedStateSurface.Blackboard, "UI_System.IsInMenu", EvidenceConfidence.Literal, EvidenceImpact.Review, [alpha, beta])] };
        ConflictWorkItem original = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Surface == ConflictSurface.SharedState);
        EvidenceDecision decision = Decision(receipt, original, original.EvidenceSha256, "Reviewed state write.");
        ProfileScanReceipt changed = receipt with { SharedStateWrites = [receipt.SharedStateWrites[0] with { Writes = [alpha with { SourceHash = new string('c', 64) }, beta] }] };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(changed, [decision]).Single(value => value.Surface == ConflictSurface.SharedState);

        Assert.AreEqual(ConflictWorkState.NoActionNeeded, item.State);
        Assert.AreEqual(EvidenceClassification.Informational, item.Classification);
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
        Assert.IsTrue(item.NextAction.Contains("No action", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(ConflictWorkState.NoActionNeeded, item.State);
        Assert.AreEqual(EvidenceClassification.Composable, item.Classification);
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
        Assert.AreEqual(EvidenceClassification.Review, item.Classification);
        Assert.AreEqual(ConflictCaseKind.RuntimeCheck, item.CaseKind);
    }

    [TestMethod]
    public void BuildLabelsCompetingTweakValuesAsOrderSensitive()
    {
        TweakOperation alpha = new("Alpha", "alpha.yaml", "Items.Pistol.damage", "10", false);
        TweakOperation beta = new("Beta", "beta.yaml", "Items.Pistol.damage", "20", false);
        InteractionFinding finding = new("Items.Pistol.damage", InteractionFindingKind.Review, "values differ", ["Alpha", "Beta"]);
        ProfileScanReceipt receipt = Receipt() with { InteractionFindings = [finding], TweakOverlaps = [new TweakOverlap(finding.Target, TweakOverlapKind.ScalarOverwrite, [alpha, beta])] };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Target == finding.Target);

        Assert.AreEqual(EvidenceClassification.OrderSensitive, item.Classification);
        Assert.AreEqual(ConflictCaseKind.OrderSensitive, item.CaseKind);
        Assert.AreEqual("Last assigned value wins", item.ClassificationLabel);
        Assert.AreEqual("Providers assign different values", item.ProofLabel);
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

        Assert.AreEqual("Remove first, then add", item.ClassificationLabel);
        Assert.AreEqual("Documented TweakXL phases decide membership", item.ProofLabel);
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
        Assert.AreEqual("All additions are applied", item.ClassificationLabel);
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

        Assert.AreEqual("Array changes compose", item.ClassificationLabel);
        Assert.AreEqual("Distinct array values are changed", item.ProofLabel);
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
        Assert.AreEqual("Record definition collision", item.ClassificationLabel);
        Assert.AreEqual("Providers construct the same record differently", item.ProofLabel);
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
        Assert.AreEqual("Source array needs verification", item.ClassificationLabel);
        Assert.AreEqual("Copied array contents are unknown", item.ProofLabel);
    }

    [TestMethod]
    public void BuildGroupsLooseFileOverwritesWithTheSameProviderChain()
    {
        VirtualFileProvider alphaOne = new("Alpha", "a1", 1, new string('a', 64), 0, 10);
        VirtualFileProvider betaOne = new("Beta", "b1", 1, new string('b', 64), 1, 9);
        VirtualFileProvider alphaTwo = alphaOne with { PhysicalPath = "a2", Sha256 = new string('c', 64) };
        VirtualFileProvider betaTwo = betaOne with { PhysicalPath = "b2", Sha256 = new string('d', 64) };
        ProfileScanReceipt receipt = Receipt() with { VirtualFileShadows = [new VirtualFileShadow("r6\\scripts\\one.reds", "Alpha", VirtualFileRelation.Different, [alphaOne, betaOne]), new VirtualFileShadow("r6\\scripts\\two.reds", "Alpha", VirtualFileRelation.Different, [alphaTwo, betaTwo])] };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Surface == ConflictSurface.VirtualFile);

        Assert.AreEqual(2, item.RelatedTargets.Length);
        Assert.IsTrue(item.Target.Contains("2 files", StringComparison.Ordinal));
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
        Assert.AreEqual("One file version loads", item.ClassificationLabel);
    }

    [TestMethod]
    public void VortexFileOverrideUsesManagerSpecificActionLanguage()
    {
        VirtualFileProvider winner = new("Beta", "ignored", 4, new string('b', 64), 0);
        VirtualFileProvider loser = new("Alpha", "ignored", 4, new string('a', 64), 1);
        ProfileScanReceipt receipt = Receipt() with { ManagerKind = ModManagerKind.Vortex, VirtualFileShadows = [new VirtualFileShadow("r6\\scripts\\shared.reds", "Beta", VirtualFileRelation.Different, [winner, loser])] };

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Surface == ConflictSurface.VirtualFile);

        Assert.AreEqual("One file wins", item.ProofLabel);
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
        ProfileScanReceipt changed = first with { RedScriptFlows = [flow with { SourceHash = new string('b', 64) }] };

        string originalHash = ConflictWorkQueueBuilder.Build(first, []).Single(value => value.Target == target).EvidenceSha256;
        string rewrittenHash = ConflictWorkQueueBuilder.Build(rewritten, []).Single(value => value.Target == target).EvidenceSha256;
        string changedHash = ConflictWorkQueueBuilder.Build(changed, []).Single(value => value.Target == target).EvidenceSha256;

        Assert.AreEqual(originalHash, rewrittenHash);
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

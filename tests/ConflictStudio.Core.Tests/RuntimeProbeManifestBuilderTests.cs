using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class RuntimeProbeManifestBuilderTests
{
    [TestMethod]
    public void InternalOverrideRegistrationsDoNotDemandACallbackProbe()
    {
        string registration = "Override('PlayerPuppet', 'Value', function() return 1 end)";
        ModSourceInventory inventory = new([], [new("Alpha", "one.lua", registration + "\nOverride('PlayerPuppet', 'Value', function() return 2 end)")], [], []);
        ProfileScanReceipt receipt = new(1, "Standard", DateTimeOffset.UtcNow, ["Alpha"], [], [], [], [],
            InteractionReportBuilder.Build(inventory), [], [], LuaCallbackEvidenceAnalyzer.Analyze(inventory.LuaSources), [], [], []);

        Assert.HasCount(2, receipt.LuaCallbacks);
        Assert.IsEmpty(RuntimeProbeManifestBuilder.Build(receipt).Requests);
    }

    private static readonly string[] ExpectedProviders = ["Alpha", "Beta"];
    private static readonly string[] ExpectedRuntimeProviders = ["Beta", "Alpha"];
    private static readonly string[] ExpectedBoundProviders = ["Beta", "Alpha", "Gamma"];
    private static readonly string[] SourceDependencyTargets = ["Items.Target.tags", "Items.Source.tags"];

    [TestMethod]
    public void BuildCreatesNarrowRequestsForReviewEvidence()
    {
        TweakOperation[] operations = [new("Alpha", "alpha.yaml", "Items.Base_HMG.value", "1", false), new("Beta", "beta.yaml", "Items.Base_HMG.value", "2", false)];
        ProfileScanReceipt receipt = new(1, "Standard", new DateTimeOffset(2026, 8, 25, 16, 0, 0, TimeSpan.Zero), ["Alpha", "Beta"], [], [], [], [], [new InteractionFinding("Items.Base_HMG.value", InteractionFindingKind.Review, "Review final value.", ["Alpha", "Beta"])], [], [], [], [new TweakOverlap("Items.Base_HMG.value", TweakOverlapKind.ScalarOverwrite, operations)], [], []);

        RuntimeProbeManifest manifest = RuntimeProbeManifestBuilder.Build(receipt);

        RuntimeProbeRequest request = manifest.Requests.Single();
        Assert.AreEqual(RuntimeProbeKind.PostInitializationTweakValue, request.Kind);
        Assert.AreEqual("Items.Base_HMG.value", request.Target);
        CollectionAssert.AreEqual(ExpectedProviders, request.Providers);
    }

    [TestMethod]
    public void BuildDoesNotGenerateTestsFromSharedTargetEvidenceAlone()
    {
        SharedStateWrite[] writes = [new("Alpha", "alpha.lua", SharedStateSurface.StatusEffect, "BaseStatusEffect.EMP", 1), new("Beta", "beta.lua", SharedStateSurface.StatusEffect, "BaseStatusEffect.EMP", 1)];
        ProfileScanReceipt receipt = new(1, "Standard", DateTimeOffset.UtcNow, ["Alpha", "Beta"], [], [], [], [], [], [], [new SharedStateWriteFinding(SharedStateSurface.StatusEffect, "BaseStatusEffect.EMP", EvidenceConfidence.Literal, EvidenceImpact.Review, writes)], [], [], [], []);

        RuntimeProbeManifest manifest = RuntimeProbeManifestBuilder.Build(receipt);

        Assert.AreEqual(0, manifest.Requests.Length);
    }

    [TestMethod]
    public void WrapperContinuationAloneDoesNotDemandABehaviorCheck()
    {
        string target = "DamageSystem.ProcessHit()";
        RedScriptFlowEvidence flow = new("Alpha", "alpha.reds", target, RedScriptFlowKind.Wrap, RedScriptContinuationEvidence.EarlyReturnBeforeContinuation, EvidenceConfidence.ExactToken, EvidenceImpact.Review, 1, new string('a', 64));
        ProfileScanReceipt receipt = new(1, "Standard", DateTimeOffset.UtcNow, ["Alpha", "Beta"], [], [], [], [], [new InteractionFinding(target, InteractionFindingKind.Review, "Review wrapper.", ["Alpha", "Beta"])], [flow], [], [], [], [], []);

        Assert.IsEmpty(RuntimeProbeManifestBuilder.Build(receipt).Requests);
    }

    [TestMethod]
    public void BuildDoesNotGenerateCallbackTestsForSingleProviderOverrides()
    {
        LuaCallbackEvidence callback = new(LuaCallbackEvidenceKind.Override, "PlayerPuppet.OnAction", EvidenceConfidence.Literal, EvidenceImpact.Review, LuaContinuationEvidence.Continues, 1, new string('a', 64), [new LuaSourceCopy("Alpha", "init.lua")]);
        ProfileScanReceipt receipt = new(1, "Standard", DateTimeOffset.UtcNow, ["Alpha"], [], [], [], [], [], [], [], [callback], [], [], []);

        RuntimeProbeManifest manifest = RuntimeProbeManifestBuilder.Build(receipt);

        Assert.IsFalse(manifest.Requests.Any(value => value.Kind == RuntimeProbeKind.CallbackDelivery));
    }

    [TestMethod]
    public void SourceArrayDependencyObservesRealSourceAndDestinationFlats()
    {
        TweakOperation copy = new("Alpha", "alpha.yaml", "Items.Target.tags", "Items.Source.tags", true, TweakOperationKind.ArrayAppendFrom);
        TweakOperation sourceWrite = new("Beta", "beta.yaml", "Items.Source.tags", "Items.New", true, TweakOperationKind.ArrayAppend);
        TweakOverlap overlap = new("Items.Target.tags <- Items.Source.tags", TweakOverlapKind.SourceArrayDependency, [copy, sourceWrite]);
        ProfileScanReceipt receipt = new(1, "Standard", DateTimeOffset.UtcNow, ["Alpha", "Beta"], [], [], [], [], [new InteractionFinding(overlap.Target, InteractionFindingKind.Review, "source", ["Alpha", "Beta"])], [], [], [], [overlap], [], []);

        RuntimeProbeRequest[] requests = RuntimeProbeManifestBuilder.Build(receipt).Requests;

        CollectionAssert.AreEquivalent(SourceDependencyTargets, requests.Select(value => value.Target).ToArray());
        Assert.IsFalse(requests.Any(value => value.Target.Contains(" <- ", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void CrossLanguageMethodRelationshipDoesNotDemandACallbackProbe()
    {
        string redTarget = "InventoryItemModeLogicController.OnReplacePartNotificationClosed(ref<inkGameNotificationData>)";
        LuaCallbackEvidence callback = new(LuaCallbackEvidenceKind.Override, "InventoryItemModeLogicController.OnReplacePartNotificationClosed", EvidenceConfidence.Literal, EvidenceImpact.Review, LuaContinuationEvidence.Missing, 10, new string('a', 64), [new LuaSourceCopy("WMCO", "quarantine.lua")]);
        ProfileScanReceipt receipt = new(1, "Standard", DateTimeOffset.UtcNow, ["Depeche", "WMCO"], [], [], [], [], [new InteractionFinding(redTarget, InteractionFindingKind.Review, "cross", ["Depeche", "WMCO"])], [new RedScriptFlowEvidence("Depeche", "depeche.reds", redTarget, RedScriptFlowKind.Add, RedScriptContinuationEvidence.NotApplicable, EvidenceConfidence.ExactToken, EvidenceImpact.None, 1, new string('b', 64))], [], [callback], [], [], []);

        Assert.IsEmpty(RuntimeProbeManifestBuilder.Build(receipt).Requests);
    }

    [TestMethod]
    public void BuildForSelectedWorkItemBindsOnlyThatFinding()
    {
        TweakOperation[] firstOperations = [new("Alpha", "alpha.yaml", "Items.First.value", "1", false), new("Beta", "beta.yaml", "Items.First.value", "2", false)];
        TweakOperation[] secondOperations = [new("Alpha", "alpha.yaml", "Items.Second.value", "3", false), new("Beta", "beta.yaml", "Items.Second.value", "4", false)];
        ProfileScanReceipt receipt = new(1, "Standard", new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero), ["Alpha", "Beta"], [], [], [], [],
            [new InteractionFinding("Items.First.value", InteractionFindingKind.Review, "Review first.", ["Alpha", "Beta"]), new InteractionFinding("Items.Second.value", InteractionFindingKind.Review, "Review second.", ["Alpha", "Beta"])], [], [], [],
            [new TweakOverlap("Items.First.value", TweakOverlapKind.ScalarOverwrite, firstOperations), new TweakOverlap("Items.Second.value", TweakOverlapKind.ScalarOverwrite, secondOperations)], [], [])
        { InstallationId = new string('1', 64), ManagerKind = ModManagerKind.Vortex };
        ConflictWorkItem selected = ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Target == "Items.Second.value");
        DateTimeOffset createdAtUtc = new(2026, 9, 12, 13, 0, 0, TimeSpan.Zero);

        RuntimeProbeManifest manifest = RuntimeProbeManifestBuilder.Build(receipt, selected, createdAtUtc);

        Assert.AreEqual(createdAtUtc, manifest.CreatedAtUtc);
        Assert.HasCount(1, manifest.Requests);
        Assert.AreEqual("Items.Second.value", manifest.Requests[0].Target);
        Assert.IsNotNull(manifest.Binding);
        Assert.AreEqual(ModManagerKind.Vortex, manifest.Binding.ManagerKind);
        Assert.AreEqual(receipt.InstallationId, manifest.Binding.InstallationId);
        Assert.AreEqual(ConflictSurface.ScriptAndTweak, manifest.Binding.Surface);
        Assert.AreEqual(selected.EvidenceSha256, manifest.Binding.EvidenceSha256);
        CollectionAssert.AreEqual(selected.Providers, manifest.Binding.Providers);
    }

    [TestMethod]
    public void BuildForSelectedWorkItemRejectsStaleEvidence()
    {
        TweakOperation[] operations = [new("Alpha", "alpha.yaml", "Items.First.value", "1", false), new("Beta", "beta.yaml", "Items.First.value", "2", false)];
        ProfileScanReceipt receipt = new(1, "Standard", DateTimeOffset.UtcNow, ["Alpha", "Beta"], [], [], [], [], [new InteractionFinding("Items.First.value", InteractionFindingKind.Review, "Review first.", ["Alpha", "Beta"])], [], [], [], [new TweakOverlap("Items.First.value", TweakOverlapKind.ScalarOverwrite, operations)], [], [])
        { InstallationId = new string('1', 64) };
        ConflictWorkItem selected = ConflictWorkQueueBuilder.Build(receipt, []).Single() with { EvidenceSha256 = new string('a', 64) };

        Assert.ThrowsExactly<InvalidOperationException>(() => RuntimeProbeManifestBuilder.Build(receipt, selected, DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void BuildForSelectedSourceDependencyKeepsBothRequestsInOneRun()
    {
        TweakOperation copy = new("Alpha", "alpha.yaml", "Items.Target.tags", "Items.Source.tags", true, TweakOperationKind.ArrayAppendFrom);
        TweakOperation sourceWrite = new("Beta", "beta.yaml", "Items.Source.tags", "Items.New", true, TweakOperationKind.ArrayAppend);
        TweakOverlap overlap = new("Items.Target.tags <- Items.Source.tags", TweakOverlapKind.SourceArrayDependency, [copy, sourceWrite]);
        ProfileScanReceipt receipt = new(2, "Standard", DateTimeOffset.UtcNow, ["Alpha", "Beta"], [], [], [], [], [new InteractionFinding(overlap.Target, InteractionFindingKind.Review, "source", ["Alpha", "Beta"])], [], [], [], [overlap], [], [])
        { InstallationId = new string('1', 64) };
        ConflictWorkItem selected = ConflictWorkQueueBuilder.Build(receipt, []).Single();

        RuntimeProbeManifest manifest = RuntimeProbeManifestBuilder.Build(receipt, selected, DateTimeOffset.UtcNow);

        Assert.HasCount(2, manifest.Requests);
        CollectionAssert.AreEquivalent(SourceDependencyTargets, manifest.Requests.Select(value => value.Target).ToArray());
        Assert.AreEqual(selected.Target, manifest.Binding!.Target);
    }

    [TestMethod]
    public void BuildForSelectedWorkItemKeepsRequestsWhoseProvidersAreAReorderedSubset()
    {
        ModSourceInventory inventory = new([], [new("Beta", "runtime.lua", "TweakDB:SetFlat('Items.Test.value', 8)")], [new("Alpha", "alpha.yaml", "Items.Test.value: 4")], []);
        TweakAnalysisResult tweaks = TweakInteractionAnalyzer.AnalyzeDetailed(inventory.TweakSources);
        SharedStateWrite[] writes = SharedStateWriteAnalyzer.Collect(inventory.RedScripts, inventory.LuaSources);
        InteractionFinding finding = InteractionReportBuilder.Build(inventory, [], [], tweaks.Overlaps, tweaks.Operations, writes).Single() with { Providers = ["Beta", "Alpha"] };
        RedScriptFlowEvidence otherContributor = new("Gamma", "gamma.reds", finding.Target, RedScriptFlowKind.Wrap, RedScriptContinuationEvidence.Continues, EvidenceConfidence.ExactToken, EvidenceImpact.Review, 1, new string('4', 64));
        ProfileScanReceipt receipt = new(2, "Standard", DateTimeOffset.UtcNow, ["Alpha", "Beta", "Gamma"], [], [], [], [], [finding], [otherContributor], [], [], tweaks.Overlaps, [], [])
        { InstallationId = new string('1', 64) };
        ConflictWorkItem selected = ConflictWorkQueueBuilder.Build(receipt, []).Single();

        RuntimeProbeManifest manifest = RuntimeProbeManifestBuilder.Build(receipt, selected, DateTimeOffset.UtcNow);

        Assert.HasCount(2, manifest.Requests);
        CollectionAssert.AreEqual(ExpectedBoundProviders, manifest.Binding!.Providers);
        Assert.IsTrue(manifest.Requests.All(value => value.Providers.SequenceEqual(ExpectedRuntimeProviders, StringComparer.OrdinalIgnoreCase)));
    }
}

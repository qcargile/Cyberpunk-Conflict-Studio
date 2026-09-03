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
}

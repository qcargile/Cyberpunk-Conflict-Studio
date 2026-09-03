using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class ResultAdmissionTests
{
    [TestMethod]
    public void OrdinarySourceRelationshipsRemainEvidenceWithoutBecomingCases()
    {
        ModSourceInventory inventory = new([], [new("Settings", "init.lua", "TweakDB:SetFlat('Items.Local.value', config.value)")],
            [new("Settings", "defaults.yaml", "Items.Local.value: 1"), new("Base", "base.yaml", "Items.Base.value: 2"), new("Child", "child.yaml", "Items.Child:\n  $base: Items.Base")], []);
        ProfileScanReceipt receipt = Receipt(inventory);

        Assert.IsNotEmpty(receipt.InteractionFindings);
        Assert.IsEmpty(ConflictWorkQueueBuilder.Build(receipt, []));
        SupportCapsule support = SupportCapsuleBuilder.Build(receipt, []);
        Assert.IsNotEmpty(support.Casefile.Findings);
        Assert.IsEmpty(support.WorkQueue);
        Assert.IsEmpty(support.Probes.Requests);
    }

    [TestMethod]
    public void ForwardingWrappersAreNotStandaloneResolvedCases()
    {
        string method = "@wrapMethod(PlayerPuppet)\npublic func Value() -> Int32 { return wrappedMethod(); }";
        ProfileScanReceipt receipt = Receipt(new([new("Alpha", "a.reds", method), new("Beta", "b.reds", method)], [], [], []));

        Assert.IsNotEmpty(receipt.RedScriptFlows);
        Assert.IsEmpty(ConflictWorkQueueBuilder.Build(receipt, []));
    }

    [TestMethod]
    [DataRow("if enabled { return 1; } return wrappedMethod();")]
    [DataRow("return 1;")]
    public void WrapperContinuationAloneDoesNotEstablishAConflict(string body)
    {
        ProfileScanReceipt receipt = Receipt(new([
            new("Alpha", "a.reds", $"@wrapMethod(PlayerPuppet)\npublic func Value() -> Int32 {{ {body} }}"),
            new("Beta", "b.reds", "@wrapMethod(PlayerPuppet)\npublic func Value() -> Int32 { return wrappedMethod(); }")], [], [], []));

        Assert.HasCount(2, receipt.RedScriptFlows);
        Assert.IsTrue(receipt.InteractionFindings.All(value => value.Kind == InteractionFindingKind.Informational));
        Assert.IsEmpty(ConflictWorkQueueBuilder.Build(receipt, []));
        Assert.IsEmpty(RuntimeProbeManifestBuilder.Build(receipt).Requests);
    }

    [TestMethod]
    public void SharedCallbacksAndAddedMethodExtensionsRemainTechnicalEvidence()
    {
        ProfileScanReceipt receipt = Receipt(new([
            new("Alpha", "a.reds", "@addMethod(PlayerPuppet)\npublic func Value() -> Int32 { return 1; }")], [
            new("Beta", "b.lua", "Override('PlayerPuppet', 'Value', function() return 2 end)"),
            new("Gamma", "c.lua", "Observe('PlayerPuppet', 'Value', function() Notify() end)")], [], []));

        Assert.HasCount(2, receipt.LuaCallbacks);
        Assert.IsNotEmpty(receipt.InteractionFindings);
        Assert.IsTrue(receipt.InteractionFindings.All(value => value.Kind == InteractionFindingKind.Informational));
        Assert.IsEmpty(ConflictWorkQueueBuilder.Build(receipt, []));
        Assert.IsEmpty(RuntimeProbeManifestBuilder.Build(receipt).Requests);
    }

    [TestMethod]
    [DataRow("replaceMethod", EvidenceClassification.Exclusive)]
    [DataRow("addMethod", EvidenceClassification.CompilerEvidence)]
    public void CompetingMethodDeclarationsRemainCases(string annotation, EvidenceClassification classification)
    {
        ProfileScanReceipt receipt = Receipt(new([
            new("Alpha", "a.reds", $"@{annotation}(PlayerPuppet)\npublic func Value() -> Int32 {{ return 1; }}"),
            new("Beta", "b.reds", $"@{annotation}(PlayerPuppet)\npublic func Value() -> Int32 {{ return 2; }}")], [], [], []));

        ConflictWorkItem[] cases = ConflictWorkQueueBuilder.Build(receipt, []);
        Assert.HasCount(1, cases);
        Assert.AreEqual(classification, cases[0].Classification);
        Assert.IsTrue(cases[0].IsActionable);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void ExtensionsDoNotHideDuplicateMethodsOrChangeTheirCompilerGuidance(bool wrapper, bool callback)
    {
        List<RedScriptSource> sources = [
            new("Alpha", "a.reds", "@addMethod(PlayerPuppet)\npublic func Value() -> Int32 { return 1; }"),
            new("Beta", "b.reds", "@addMethod(PlayerPuppet)\npublic func Value() -> Int32 { return 2; }")];
        if (wrapper) sources.Add(new("Gamma", "c.reds", "@wrapMethod(PlayerPuppet)\npublic func Value() -> Int32 { return wrappedMethod(); }"));
        LuaSource[] lua = callback ? [new("Delta", "init.lua", "Override('PlayerPuppet', 'Value', function(self, wrapped) return wrapped() end)")] : [];
        ProfileScanReceipt receipt = Receipt(new(sources.ToArray(), lua, [], []));

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single();
        Assert.AreEqual(EvidenceClassification.CompilerEvidence, item.Classification);
        Assert.AreEqual(InteractionFindingKind.Review, receipt.InteractionFindings.Single().Kind);
        StringAssert.Contains(item.NextAction, "compiler");
        Assert.IsFalse(item.NextAction.Contains("no forwarding", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(item.Summary.Contains("Gamma", StringComparison.Ordinal));
        Assert.IsFalse(item.Summary.Contains("Delta", StringComparison.Ordinal));
        Assert.IsEmpty(RuntimeProbeManifestBuilder.Build(receipt).Requests);
    }

    [TestMethod]
    [DataRow("3.85", "2", true)]
    [DataRow("60.0", "70.0", true)]
    [DataRow("20", "20.0", false)]
    [DataRow("true", "false", true)]
    [DataRow("1", "settings.value", false)]
    public void RuntimeWritersNeedDifferentLiteralValuesToBecomeCases(string first, string second, bool conflict)
    {
        ProfileScanReceipt receipt = Receipt(new([], [
            new("Alpha", "a.lua", $"TweakDB:SetFlat('Camera.limit', {first})"),
            new("Beta", "b.lua", $"TweakDB:SetFlat('Camera.limit', {second})")], [], []));

        ConflictWorkItem[] cases = ConflictWorkQueueBuilder.Build(receipt, []);
        Assert.AreEqual(conflict ? 1 : 0, cases.Length);
        Assert.AreEqual(conflict, RuntimeProbeManifestBuilder.Build(receipt).Requests.Length > 0);
        if (conflict)
        {
            Assert.AreEqual(EvidenceClassification.CompetingDeclaration, cases[0].Classification);
            StringAssert.Contains(cases[0].Summary, first);
            StringAssert.Contains(cases[0].Summary, second);
        }
    }

    [TestMethod]
    public void CommonStatusEffectNamesDoNotCreateCases()
    {
        ProfileScanReceipt receipt = Receipt(new([
            new("Alpha", "a.reds", "StatusEffectHelper.ApplyStatusEffect(target, t\"BaseStatusEffect.Blind\");"),
            new("Beta", "b.reds", "StatusEffectHelper.ApplyStatusEffect(player, t\"BaseStatusEffect.Blind\");")], [], [], []));

        Assert.HasCount(1, receipt.SharedStateWrites);
        Assert.IsEmpty(ConflictWorkQueueBuilder.Build(receipt, []));
    }

    private static ProfileScanReceipt Receipt(ModSourceInventory inventory)
    {
        TweakAnalysisResult tweaks = TweakInteractionAnalyzer.AnalyzeDetailed(inventory.TweakSources);
        RedScriptFlowEvidence[] flows = RedScriptFlowEvidenceAnalyzer.Analyze(inventory.RedScripts);
        LuaCallbackEvidence[] callbacks = LuaCallbackEvidenceAnalyzer.Analyze(inventory.LuaSources);
        return new(2, "Test", DateTimeOffset.UtcNow, [], [], [], [], [], InteractionReportBuilder.Build(inventory, flows, callbacks, tweaks.Overlaps),
            flows, SharedStateWriteAnalyzer.Analyze(inventory.RedScripts, inventory.LuaSources), callbacks, tweaks.Overlaps, [], []);
    }
}

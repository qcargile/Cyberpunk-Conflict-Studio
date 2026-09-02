using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class ConflictRelevanceTests
{
    [TestMethod]
    [DataRow("Alpha", "0", "8.0")]
    [DataRow("Beta", "20", "20.0")]
    [DataRow("Beta", "true", "true")]
    [DataRow("Beta", "0", "settings.value")]
    public void DefaultsAndUnopposedRuntimeWritesDoNotCreateConflictWork(string writer, string initial, string updated)
    {
        ProfileScanReceipt receipt = Receipt(new([], [new(writer, "init.lua", $"TweakDB:SetFlat('Items.Test.value', {updated})")], [new("Alpha", "values.yaml", $"Items.Test.value: {initial}")], []));

        Assert.IsFalse(ConflictWorkQueueBuilder.Build(receipt, []).Any(value => value.IsActionable));
        Assert.IsEmpty(RuntimeProbeManifestBuilder.Build(receipt).Requests);
        Assert.IsFalse(SupportCapsuleBuilder.Build(receipt, []).WorkQueue.Any(value => value.IsActionable));
    }

    [TestMethod]
    public void DifferentProvidersRequestingDifferentLiteralValuesRemainActionable()
    {
        ProfileScanReceipt receipt = Receipt(new([], [new("Camera settings", "init.lua", "TweakDB:SetFlat('Camera.limit', 8.0)")], [new("No centering", "camera.yaml", "Camera.limit: 0")], []));

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single();
        Assert.AreEqual(EvidenceClassification.CompetingDeclaration, item.Classification);
        StringAssert.Contains(item.Summary, "0");
        StringAssert.Contains(item.Summary, "8.0");
        Assert.IsNotEmpty(RuntimeProbeManifestBuilder.Build(receipt).Requests);
    }

    [TestMethod]
    public void PreservingRuntimeAppendDoesNotTurnVendorAdditionsIntoConflictWork()
    {
        ProfileScanReceipt receipt = Receipt(new([new("Tool hand", "vendors.reds", "let stock = TweakDBInterface.GetForeignKeyArray(t\"Vendors.Test.itemStock\"); ArrayPush(stock, t\"Items.ToolHand\"); TweakDBManager.SetFlat(t\"Vendors.Test.itemStock\", stock);")], [], [new("Cyberarms", "stock.yaml", "Vendors.Test.itemStock:\n  - !append Items.Arm"), new("Cyberdecks", "stock.yaml", "Vendors.Test.itemStock:\n  - !append Items.Deck")], []));

        Assert.IsFalse(ConflictWorkQueueBuilder.Build(receipt, []).Any(value => value.IsActionable));
        Assert.IsEmpty(RuntimeProbeManifestBuilder.Build(receipt).Requests);
    }

    [TestMethod]
    public void RuntimeContextDoesNotOverwriteCompetingYamlEvidence()
    {
        ProfileScanReceipt receipt = Receipt(new([], [new("Settings", "init.lua", "TweakDB:SetFlat('Items.Test.value', settings.value)")], [new("Alpha", "a.yaml", "Items.Test.value: 1"), new("Beta", "b.yaml", "Items.Test.value: 2")], []));

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single();
        Assert.AreEqual(EvidenceClassification.CompetingDeclaration, item.Classification);
        StringAssert.Contains(item.Summary, "1");
        StringAssert.Contains(item.Summary, "2");
    }

    [TestMethod]
    public void AddedMethodAndItsConditionalExtensionAreNotAConflictByThemselves()
    {
        ProfileScanReceipt receipt = Receipt(new([new("Workshop", "workshop.reds", "@addMethod(InventoryItemModeLogicController)\npublic func CompletePart() -> Bool { return true; }")], [new("Workshop extension", "init.lua", "Override('InventoryItemModeLogicController', 'CompletePart', function(self, wrapped) if permitted then return wrapped() end return false end)")], [], []));

        Assert.IsFalse(ConflictWorkQueueBuilder.Build(receipt, []).Any(value => value.IsActionable));
        Assert.IsEmpty(RuntimeProbeManifestBuilder.Build(receipt).Requests);
    }

    [TestMethod]
    public void InterpretedSourceWarningDoesNotDemandRepair()
    {
        ProfileScanReceipt receipt = Receipt(new([], [], [], [new("Scope", "scope.yaml", "TweakXL interpretation", "Mixed array entries: only mutations apply.")]));

        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single();
        Assert.IsFalse(item.IsActionable);
        Assert.IsFalse(item.NextAction.Contains("repair", StringComparison.OrdinalIgnoreCase));
        Assert.IsEmpty(RuntimeProbeManifestBuilder.Build(receipt).Requests);
    }

    [TestMethod]
    public void RepeatedTooltipNumbersDoNotCreateConflictWorkOrProbes()
    {
        ProfileScanReceipt receipt = Receipt(new([], [], [new("Cyberarms", "tooltip.yaml", "Items.Tooltip.floatValues: [!append 5, !append 50, !append 10, !append 5, !append 25]")], []));

        Assert.IsEmpty(ConflictWorkQueueBuilder.Build(receipt, []));
        Assert.IsEmpty(RuntimeProbeManifestBuilder.Build(receipt).Requests);
    }

    [TestMethod]
    public void ForwardingInternalOverridesRemainContextWithoutLosingOccurrences()
    {
        string callback = "Override('PlayerPuppet', 'Value', function(self, wrapped) return wrapped() end)";
        ModSourceInventory inventory = new([], [new("Alpha", "init.lua", callback + "\n" + callback)], [], []);
        ProfileScanReceipt receipt = Receipt(inventory);

        Assert.HasCount(2, receipt.LuaCallbacks);
        Assert.IsFalse(ConflictWorkQueueBuilder.Build(receipt, []).Any(value => value.IsActionable));
        Assert.IsEmpty(RuntimeProbeManifestBuilder.Build(receipt).Requests);
    }

    [TestMethod]
    public void ZeroArgumentOverrideStillReplacesAnotherProvidersAddedMethod()
    {
        ProfileScanReceipt receipt = Receipt(new([new("Alpha", "method.reds", "@addMethod(PlayerPuppet)\npublic func Value() -> Int32 { return 1; }")], [new("Beta", "init.lua", "Override('PlayerPuppet', 'Value', function() return 2 end)")], [], []));

        Assert.IsTrue(ConflictWorkQueueBuilder.Build(receipt, []).Single().IsActionable);
        Assert.IsNotEmpty(RuntimeProbeManifestBuilder.Build(receipt).Requests);
    }

    [TestMethod]
    public void IdenticalAccessorTextInDifferentFilesDoesNotProveSharedSettings()
    {
        ProfileScanReceipt receipt = Receipt(new([], [
            new("Bundle", "one.lua", "local settings = {value = 1}\nOverride('PlayerPuppet', 'Value', function(_) return settings.value end)"),
            new("Bundle", "two.lua", "local settings = {value = 2}\nOverride('PlayerPuppet', 'Value', function(_) return settings.value end)")], [], []));

        Assert.HasCount(2, receipt.LuaCallbacks);
        Assert.IsTrue(ConflictWorkQueueBuilder.Build(receipt, []).Single().IsActionable);
        Assert.IsNotEmpty(RuntimeProbeManifestBuilder.Build(receipt).Requests);
    }

    private static ProfileScanReceipt Receipt(ModSourceInventory inventory)
    {
        TweakAnalysisResult tweaks = TweakInteractionAnalyzer.AnalyzeDetailed(inventory.TweakSources);
        RedScriptFlowEvidence[] flows = RedScriptFlowEvidenceAnalyzer.Analyze(inventory.RedScripts);
        LuaCallbackEvidence[] callbacks = LuaCallbackEvidenceAnalyzer.Analyze(inventory.LuaSources);
        return new(2, "Test", DateTimeOffset.UtcNow, [], [], [], [], [], InteractionReportBuilder.Build(inventory, flows, callbacks, tweaks.Overlaps), flows, SharedStateWriteAnalyzer.Analyze(inventory.RedScripts, inventory.LuaSources), callbacks, tweaks.Overlaps, [], [], inventory.Failures.Concat(tweaks.Failures).ToArray());
    }
}

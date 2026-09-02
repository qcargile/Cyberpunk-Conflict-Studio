using System.IO;
using System.Text.Json;
using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class TweakRuntimeJoinTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void SingleDeclarativeSourceJoinsSingleRuntimeSource(bool redscript, bool sameProvider)
    {
        string provider = sameProvider ? "Alpha" : "Beta";
        ModSourceInventory inventory = Inventory(redscript ? "TweakDBManager.SetFlat(t\"Items.Test.value\", 2);" : "TweakDB:SetFlat('Items.Test.value', 2)", redscript, provider);

        InteractionFinding[] findings = InteractionReportBuilder.Build(inventory);

        Assert.HasCount(1, findings);
        Assert.AreEqual(InteractionFindingKind.Review, findings[0].Kind);
        Assert.AreEqual("Items.Test.value", findings[0].Target);
        Assert.HasCount(sameProvider ? 1 : 2, findings[0].Providers);
        JsonElement evidence = JsonSerializer.SerializeToElement(findings[0]).GetProperty("TweakRuntimeEvidence");
        Assert.HasCount(1, evidence.GetProperty("Declarations").EnumerateArray().ToArray());
        Assert.HasCount(1, evidence.GetProperty("Writes").EnumerateArray().ToArray());
    }

    [TestMethod]
    public void RuntimeJoinOverridesRedundantDeclarativeClassificationAndRequestsObservation()
    {
        ModSourceInventory inventory = Inventory("TweakDB:SetFlat('Items.Test.value', 1)");
        inventory = inventory with { TweakSources = [.. inventory.TweakSources, new("Gamma", "g.yaml", "Items.Test.value: 1")] };
        ProfileScanReceipt receipt = Receipt(inventory);
        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single();

        Assert.AreEqual(EvidenceClassification.Review, item.Classification);
        Assert.IsFalse(receipt.InteractionFindings.Single().Summary.Contains("same value", StringComparison.Ordinal));
        Assert.IsNull(item.Winner);
        Assert.IsFalse(item.ClassificationLabel.Contains("No action", StringComparison.Ordinal));
        StringAssert.Contains(item.NextAction, "value");
        RuntimeProbeManifest probes = RuntimeProbeManifestBuilder.Build(receipt);
        RuntimeProbeRequest request = probes.Requests.Single(value => value.Kind == RuntimeProbeKind.PostInitializationTweakValue);
        Assert.HasCount(3, request.Providers);
        Assert.HasCount(1, probes.Requests.Where(value => value.Kind == RuntimeProbeKind.SharedStateValue).ToArray());
    }

    [TestMethod]
    [DataRow("TweakDB:SetFlat('Items.Test.value', 2)", "TweakDB:SetFlat('Items.Test.value', 3)")]
    [DataRow("TweakDB:SetFlat('Items.Test.value', 2)", "TweakDB:SetFlat('Items.Test.value', 2)\nTweakDB:SetFlat('Items.Test.value', 2)")]
    [DataRow("TweakDB:SetFlat('Items.Test.value', compute(2))", "TweakDB:SetFlat('Items.Test.value', compute(3))")]
    [DataRow("TweakDB:SetFlat('Items.Test.value', compute(2)", "TweakDB:SetFlat('Items.Test.value', compute(3)")]
    public void RuntimeSemanticOrCountChangesInvalidateSavedReview(string original, string changed)
    {
        ProfileScanReceipt first = Receipt(Inventory(original));
        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(first, []).Single();
        EvidenceDecision decision = new(first.ProfileName, item.Target, item.Providers, item.EvidenceSha256, "Observed", DateTimeOffset.UtcNow, first.InstallationId!, item.Surface);
        ConflictWorkItem next = ConflictWorkQueueBuilder.Build(Receipt(Inventory(changed)), [decision]).Single();

        Assert.AreNotEqual(item.EvidenceSha256, next.EvidenceSha256);
        Assert.AreEqual(ConflictWorkState.ReviewWhenRelevant, next.State);
    }

    [TestMethod]
    public void LineOnlyShiftsPreserveReviewAndUnrelatedFindingOmitsOptionalEvidence()
    {
        ProfileScanReceipt first = Receipt(Inventory("TweakDB:SetFlat('Items.Test.value', compute(2))"));
        ProfileScanReceipt shifted = Receipt(Inventory("\n\nTweakDB:SetFlat('Items.Test.value', compute(2))"));
        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(first, []).Single();
        EvidenceDecision decision = new(first.ProfileName, item.Target, item.Providers, item.EvidenceSha256, "Observed", DateTimeOffset.UtcNow, first.InstallationId!, item.Surface);

        Assert.AreEqual(ConflictWorkState.Reviewed, ConflictWorkQueueBuilder.Build(shifted, [decision]).Single().State);
        Assert.IsFalse(JsonSerializer.SerializeToElement(new InteractionFinding("Other", InteractionFindingKind.Review, "", [])).TryGetProperty("TweakRuntimeEvidence", out _));
    }

    [TestMethod]
    [DataRow("TweakDB:SetFlat(target, 2)")]
    [DataRow("TweakDB:SetFlat('Items.Test.value' .. suffix, 2)")]
    [DataRow("print(\"TweakDB:SetFlat('Items.Test.value', 2)\")")]
    [DataRow("-- TweakDB:SetFlat('Items.Test.value', 2)")]
    [DataRow("local sample = [[TweakDB:SetFlat('Items.Test.value', 2)]]")]
    [DataRow("local sample = [=[TweakDB:SetFlat('Items.Test.value', 2)]=]")]
    [DataRow("--[=[\nTweakDB:SetFlat('Items.Test.value', 2)\n]=]")]
    public void DynamicAndNonExecutableTargetsAreExcluded(string text)
        => Assert.IsEmpty(InteractionReportBuilder.Build(Inventory(text)));

    [TestMethod]
    public void InactiveRedScriptConditionalWritesAreExcluded()
    {
        ModSourceInventory inventory = Inventory("@if(ModuleExists(\"Absent\"))\npublic func Apply() -> Void { TweakDBManager.SetFlat(t\"Items.Test.value\", 2); }", true);

        Assert.IsEmpty(InteractionReportBuilder.Build(inventory));
    }

    [TestMethod]
    [DataRow("@if(ModuleExists(\"Absent\"))\n@addMethod(PlayerPuppet)\npublic func Apply() -> Void {\nTweakDBManager.SetFlat(t\"Items.Test.value\", 2);\n}")]
    [DataRow("@if(ModuleExists(\"Absent\")) @addMethod(PlayerPuppet)\npublic func Apply() -> Void {\nTweakDBManager.SetFlat(t\"Items.Test.value\", 2);\n}")]
    [DataRow("@if(UnknownCondition())\npublic func Apply() -> Void {\nTweakDBManager.SetFlat(t\"Items.Test.value\", 2);\n}")]
    public void ConditionalCollectionSkipsTheWholeInactiveBodyButRetainsFollowingWrites(string conditional)
    {
        string text = conditional + "\npublic func Active() -> Void { TweakDBManager.SetFlat(t\"Items.Other.value\", 7); }";
        SharedStateWrite[] writes = SharedStateWriteAnalyzer.Collect([new("Alpha", "a.reds", text)], []);

        Assert.HasCount(1, writes);
        Assert.AreEqual("Items.Other.value", writes[0].Target);
    }

    [TestMethod]
    public void ConditionalCollectionUsesTheEffectiveModuleSet()
    {
        string text = "@if(ModuleExists(\"Present\"))\n@addMethod(PlayerPuppet)\npublic func Apply() -> Void { TweakDBManager.SetFlat(t\"Items.Test.value\", 2); }";

        Assert.HasCount(1, SharedStateWriteAnalyzer.Collect([new("Alpha", "a.reds", text), new("Beta", "b.reds", "module Present")], []));
        Assert.IsEmpty(SharedStateWriteAnalyzer.Collect([new("Alpha", "a.reds", text)], []));
    }

    [TestMethod]
    public void SupportExportRetainsBothSourceKindsAndRuntimeSemanticEvidence()
    {
        string root = Path.Combine(Path.GetTempPath(), "tweak-runtime-support-" + Guid.NewGuid().ToString("N"));
        try
        {
            ProfileScanReceipt receipt = Receipt(Inventory("TweakDB:SetFlat('Items.Test.value', compute(42))"));
            SupportCapsuleWriter.Write(root, SupportCapsuleBuilder.Build(receipt, []));
            SupportCapsule restored = SupportCapsuleWriter.Read(Path.Combine(root, "conflict-casefile.json"));

            Assert.AreEqual(JsonSerializer.Serialize(receipt.InteractionFindings), JsonSerializer.Serialize(restored.Casefile.Findings));
            string html = File.ReadAllText(Path.Combine(root, "conflict-casefile.html"));
            StringAssert.Contains(html, "compute(42)");
            StringAssert.Contains(html, "a.yaml:1");
            StringAssert.Contains(html, "runtime.lua:1");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void LongStringValueContentChangesInvalidateRuntimeEvidence()
    {
        ConflictWorkItem first = ConflictWorkQueueBuilder.Build(Receipt(Inventory("TweakDB:SetFlat('Items.Test.value', [[a  b]])")), []).Single();
        ConflictWorkItem changed = ConflictWorkQueueBuilder.Build(Receipt(Inventory("TweakDB:SetFlat('Items.Test.value', [[a b]])")), []).Single();

        Assert.AreNotEqual(first.EvidenceSha256, changed.EvidenceSha256);
    }

    [TestMethod]
    public void PrecomputedOperationsIncludeSingleSourcesAndPreventASecondParse()
    {
        ModSourceInventory inventory = Inventory("TweakDB:SetFlat('Items.Test.value', 2)");
        TweakAnalysisResult parsed = TweakInteractionAnalyzer.AnalyzeDetailed(inventory.TweakSources);
        Assert.IsEmpty(parsed.Overlaps);
        Assert.HasCount(1, parsed.Operations);
        SharedStateWrite[] writes = SharedStateWriteAnalyzer.Collect([], inventory.LuaSources);
        ModSourceInventory withoutYaml = inventory with { TweakSources = [] };

        Assert.HasCount(1, InteractionReportBuilder.Build(withoutYaml, [], [], parsed.Overlaps, parsed.Operations, writes));
    }

    [TestMethod]
    [DataRow("Items.Test.value: [ !append A ]", "Items.Test.value: [ !append B ]")]
    [DataRow("Items.Test.value: [ A ]", "Items.Test.value: [ !append B ]")]
    public void RuntimeJoinRemainsReviewAlongsideComposableArrayOperations(string first, string second)
    {
        ModSourceInventory inventory = Inventory("TweakDB:SetFlat('Items.Test.value', {})") with
        {
            TweakSources = [new("Alpha", "a.yaml", first), new("Gamma", "g.yaml", second)]
        };
        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(Receipt(inventory), []).Single();

        Assert.AreEqual(EvidenceClassification.Review, item.Classification);
        Assert.IsFalse(item.ClassificationLabel.Contains("No action", StringComparison.Ordinal));
    }

    [TestMethod]
    public void RuntimeCollectionRetainsAllLiteralApisBeforeProviderGrouping()
    {
        SharedStateWrite[] writes = SharedStateWriteAnalyzer.Collect(
            [new("Alpha", "a.reds", "TweakDBManager.SetFlat(t\"Items.Test.value\", 2); TweakDBInterface.SetFlat(t\"Items.Other.value\", 3);")],
            [new("Alpha", "a.lua", "TweakDB:SetFlatNoUpdate('Items.Third.value', 4)")]);

        Assert.HasCount(3, writes);
        Assert.IsEmpty(SharedStateWriteAnalyzer.Analyze(writes));
        Assert.IsTrue(writes.All(value => value.CallSha256 is { Length: 64 }));
    }

    [TestMethod]
    public void TweakJoinDoesNotRewriteUnrelatedSharedStateEvidence()
    {
        SharedStateWrite[] writes = SharedStateWriteAnalyzer.Collect(
            [new("Alpha", "a.reds", "board.SetBool(GetAllBlackboardDefs().UI_System.IsInMenu, true);")], []);

        Assert.AreEqual("board.SetBool(GetAllBlackboardDefs().UI_System.IsInMenu", writes.Single().Evidence);
    }

    [TestMethod]
    public void UnscopedRuntimeOnlyTweakRowsDoNotRetainTargetOnlyReviews()
    {
        ProfileScanReceipt first = Receipt(Inventory("")) with
        {
            SharedStateWrites = SharedStateWriteAnalyzer.Analyze([], [new("Alpha", "a.lua", "TweakDB:SetFlat('Items.Test.value', compute(2)"), new("Beta", "b.lua", "TweakDB:SetFlat('Items.Test.value', 5)")])
        };
        ProfileScanReceipt changed = first with
        {
            SharedStateWrites = SharedStateWriteAnalyzer.Analyze([], [new("Alpha", "a.lua", "TweakDB:SetFlat('Items.Test.value', compute(3)"), new("Beta", "b.lua", "TweakDB:SetFlat('Items.Test.value', 5)")])
        };

        Assert.AreNotEqual(ConflictWorkQueueBuilder.Build(first, []).Single().EvidenceSha256, ConflictWorkQueueBuilder.Build(changed, []).Single().EvidenceSha256);
    }

    private static ModSourceInventory Inventory(string text, bool redscript = false, string provider = "Beta")
        => new(redscript ? [new(provider, "runtime.reds", text)] : [], redscript ? [] : [new(provider, "runtime.lua", text)], [new("Alpha", "a.yaml", "Items.Test.value: 1")], []);

    private static ProfileScanReceipt Receipt(ModSourceInventory inventory)
        => new(2, "Test", DateTimeOffset.UtcNow, ["Alpha", "Beta"], [], [], [], [], InteractionReportBuilder.Build(inventory), [], [], [], TweakInteractionAnalyzer.Analyze(inventory.TweakSources), [], [], InstallationId: "test");
}

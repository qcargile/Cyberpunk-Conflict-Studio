using System.Text.Json;
using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class ScriptSourceEvidenceTests
{
    [TestMethod]
    [DataRow("@if(\nModuleExists(\"Absent\")\n)")]
    [DataRow("@if(ModuleExists(\"Present\")) @if(ModuleExists(\"Absent\"))")]
    [DataRow("@if(\nUnknownCondition()\n)")]
    public void InactiveConditionsExcludeAnnotationsAcrossAllConsumers(string condition)
    {
        string method = "@addMethod(PlayerPuppet)\npublic func Guarded() -> Void { TweakDBManager.SetFlat(t\"Items.Guarded.value\", 1); }";
        RedScriptSource[] sources = [new("Alpha", "a.reds", condition + "\n" + method), new("Beta", "b.reds", "module Present\n" + method)];
        ModSourceInventory inventory = new(sources, [], [], []);
        RedScriptFlowEvidence[] flows = RedScriptFlowEvidenceAnalyzer.Analyze(sources);

        Assert.HasCount(1, flows);
        Assert.AreEqual("Beta", flows[0].Provider);
        Assert.HasCount(1, SharedStateWriteAnalyzer.Collect(sources, []));
        Assert.IsEmpty(RedScriptInteractionAnalyzer.Analyze(sources));
        Assert.IsEmpty(InteractionReportBuilder.Build(inventory));
    }

    [TestMethod]
    [DataRow("@if(\nUnknownCondition()\n)")]
    [DataRow("@if(ModuleExists(\"Present\")) @if(UnknownCondition())")]
    public void UnknownConditionCoverageUsesTheSameMultilineStackedScan(string condition)
    {
        RedScriptSource[] sources = [new("Alpha", "a.reds", condition + "\n@addMethod(PlayerPuppet)\npublic func Guarded() -> Void {}"), new("Beta", "b.reds", "module Present")];

        SourceAnalysisFailure failure = RedScriptConditionalSourceFilter.Failures(sources).Single();
        Assert.AreEqual("Alpha", failure.Provider);
        Assert.AreEqual("RedScript condition", failure.Surface);
        Assert.IsEmpty(RedScriptFlowEvidenceAnalyzer.Analyze(sources));
    }

    [TestMethod]
    public void QuotedAndCommentedUnknownConditionsDoNotProduceCoverageFailures()
    {
        RedScriptSource[] sources = [new("Alpha", "a.reds", "let text: String = \"@if(UnknownCondition())\";\n// @if(UnknownCondition())")];

        Assert.IsEmpty(RedScriptConditionalSourceFilter.Failures(sources));
    }

    [TestMethod]
    public void OverrideLongStringPreservesContinuationAndCompleteCallbackHash()
    {
        string text = "Override('PlayerPuppet', 'Real', function(self, wrapped) local text = [[-- quoted )]]; return wrapped(self) end)";
        LuaCallbackEvidence continuing = LuaCallbackEvidenceAnalyzer.Analyze([new("Alpha", "a.lua", text)]).Single();
        LuaCallbackEvidence suppressing = LuaCallbackEvidenceAnalyzer.Analyze([new("Alpha", "a.lua", text.Replace("return wrapped(self)", "return 99", StringComparison.Ordinal))]).Single();

        Assert.AreEqual(LuaCallbackEvidenceKind.Override, continuing.Kind);
        Assert.AreEqual(LuaContinuationEvidence.Continues, continuing.Continuation);
        Assert.AreEqual(LuaContinuationEvidence.Missing, suppressing.Continuation);
        Assert.IsNotNull(continuing.CallbackSha256);
        Assert.IsNotNull(suppressing.CallbackSha256);
        Assert.AreNotEqual(continuing.CallbackSha256, suppressing.CallbackSha256);
    }

    [TestMethod]
    public void ConditionalImportWithoutSemicolonDoesNotConsumeFollowingMethod()
    {
        string text = "@if(ModuleExists(\"Absent\"))\nimport Absent.*\npublic func Active() -> Void { TweakDBManager.SetFlat(t\"Items.Active.value\", 1); }";
        SharedStateWrite[] writes = SharedStateWriteAnalyzer.Collect([new("Alpha", "a.reds", text)], []);

        Assert.HasCount(1, writes);
        Assert.AreEqual("Items.Active.value", writes[0].Target);
        Assert.AreEqual(3, writes[0].Line);
    }

    [TestMethod]
    [DataRow("@if(\nModuleExists(\"Absent\")\n)")]
    [DataRow("@if(ModuleExists(\"Present\")) @if(ModuleExists(\"Absent\"))")]
    [DataRow("@if(ModuleExists(\"Absent\")) @if(ModuleExists(\"Present\"))")]
    [DataRow("@if(ModuleExists(\"Present\"))\n@if(ModuleExists(\"Absent\"))")]
    [DataRow("@if(\nUnknownCondition()\n)")]
    public void MultilineAndStackedConditionsExcludeOnlyGuardedWrites(string condition)
    {
        string text = condition + "\n@addMethod(PlayerPuppet)\npublic func Guarded() -> Void { TweakDBManager.SetFlat(t\"Items.Guarded.value\", 1); }\npublic func Active() -> Void { TweakDBManager.SetFlat(t\"Items.Active.value\", 2); }";
        SharedStateWrite[] writes = SharedStateWriteAnalyzer.Collect([new("Alpha", "a.reds", text), new("Beta", "b.reds", "module Present")], []);

        Assert.HasCount(1, writes);
        Assert.AreEqual("Items.Active.value", writes[0].Target);
    }

    [TestMethod]
    public void ActiveMultilineStackedConditionsRetainWrites()
    {
        string text = "@if(\nModuleExists(\"Present\")\n) @ifNot(ModuleExists(\"Absent\"))\npublic func Active() -> Void { TweakDBManager.SetFlat(t\"Items.Active.value\", 2); }";
        Assert.HasCount(1, SharedStateWriteAnalyzer.Collect([new("Alpha", "a.reds", text), new("Beta", "b.reds", "module Present")], []));
    }

    [TestMethod]
    [DataRow("TweakDB:SetFlat(t\"Items.Test.value\", 1)")]
    [DataRow("other.TweakDB:SetFlat('Items.Test.value', 1)")]
    [DataRow("other . TweakDB:SetFlat('Items.Test.value', 1)")]
    [DataRow("other -- receiver\n . TweakDB:SetFlat('Items.Test.value', 1)")]
    public void LuaFunctionArgumentsAndQualifiedReceiversAreNotGlobalLiteralWrites(string text)
        => Assert.IsEmpty(SharedStateWriteAnalyzer.Collect([], [new("Alpha", "a.lua", text)]));

    [TestMethod]
    public void GenuineLuaAndRedScriptLiteralWritesRemainRecognized()
    {
        Assert.HasCount(1, SharedStateWriteAnalyzer.Collect([], [new("Alpha", "a.lua", "TweakDB:SetFlat('Items.Test.value', 1)")]));
        Assert.HasCount(1, SharedStateWriteAnalyzer.Collect([new("Alpha", "a.reds", "TweakDBManager.SetFlat(t\"Items.Test.value\", 1);")], []));
    }

    [TestMethod]
    [DataRow("[[", "]]")]
    [DataRow("[=[", "]=]")]
    public void LongStringContentsDoNotRegisterPhantomHooks(string opening, string closing)
    {
        string text = "local example = " + opening + "\nObserve('PlayerPuppet', 'Phantom', function() end)\nregisterForEvent('onInit', function() end)\n" + closing + "\nObserve('PlayerPuppet', 'Real', function() end)";
        LuaSource[] sources = [new("Alpha", "a.lua", text), new("Beta", "b.lua", text)];
        LuaCallbackEvidence[] callbacks = LuaCallbackEvidenceAnalyzer.Analyze(sources);

        Assert.HasCount(1, callbacks);
        Assert.AreEqual("PlayerPuppet.Real", callbacks[0].Target);
        Assert.HasCount(1, LuaInteractionAnalyzer.Analyze(sources));
    }

    [TestMethod]
    [DataRow(")")]
    [DataRow("'")]
    [DataRow("], function() end, (")]
    public void LongStringPunctuationDoesNotTruncateCallbackHash(string contents)
    {
        string before = "Observe('PlayerPuppet', 'Real', function() local text = [=[" + contents + "]=]; apply(1) end)";
        string after = before.Replace("apply(1)", "apply(2)", StringComparison.Ordinal);
        LuaCallbackEvidence first = LuaCallbackEvidenceAnalyzer.Analyze([new("Alpha", "a.lua", before)]).Single();
        LuaCallbackEvidence changed = LuaCallbackEvidenceAnalyzer.Analyze([new("Alpha", "a.lua", after)]).Single();

        Assert.IsNotNull(first.CallbackSha256);
        Assert.IsNotNull(changed.CallbackSha256);
        Assert.AreNotEqual(first.CallbackSha256, changed.CallbackSha256);
    }

    [TestMethod]
    public void ProbeRequestsRoundTripTypedTweakRuntimeEvidence()
    {
        ModSourceInventory inventory = new([], [new("Beta", "runtime.lua", "TweakDB:SetFlat('Items.Test.value', 2)")], [new("Alpha", "initial.yaml", "Items.Test.value: 1")], []);
        InteractionFinding finding = InteractionReportBuilder.Build(inventory).Single();
        ProfileScanReceipt receipt = new(2, "Test", DateTimeOffset.UtcNow, ["Alpha", "Beta"], [], [], [], [], [finding], [], [], [], [], [], []);
        RuntimeProbeManifest manifest = RuntimeProbeManifestBuilder.Build(receipt);
        RuntimeProbeManifest restored = JsonSerializer.Deserialize<RuntimeProbeManifest>(JsonSerializer.Serialize(manifest))!;

        Assert.HasCount(2, restored.Requests);
        foreach (RuntimeProbeRequest request in restored.Requests)
        {
            JsonElement evidence = JsonSerializer.SerializeToElement(request).GetProperty("TweakRuntimeEvidence");
            Assert.AreEqual(JsonSerializer.Serialize(finding.TweakRuntimeEvidence), evidence.GetRawText());
        }
        Assert.IsFalse(JsonSerializer.SerializeToElement(new RuntimeProbeRequest(RuntimeProbeKind.BehaviorCheck, "other", [], "", "")).TryGetProperty("TweakRuntimeEvidence", out _));
    }
}

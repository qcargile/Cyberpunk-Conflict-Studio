using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class ScriptInteractionEvidenceTests
{
    [TestMethod]
    [DataRow("public func Inactive() -> Int32 = 1", true)]
    [DataRow("public func Inactive() -> Int32 =\n  1 +\n  2", true)]
    [DataRow("public func Inactive() -> Int32 = 1", false)]
    [DataRow("public\nfunc Inactive() -> Int32 = 1", true)]
    public void InactiveExpressionDeclarationDoesNotConsumeTheNextMethod(string inactive, bool annotated)
    {
        string active = (annotated ? "@addMethod(PlayerPuppet)\n" : "") + "public func Active() -> Void { TweakDBManager.SetFlat(t\"Items.Active.value\", 1); }";
        RedScriptSource[] sources = [new("Example", "example.reds", "@if(ModuleExists(\"Absent\")) " + inactive + "\n" + active)];

        Assert.AreEqual(annotated ? 1 : 0, RedScriptFlowEvidenceAnalyzer.Analyze(sources).Length);
        Assert.AreEqual("Items.Active.value", SharedStateWriteAnalyzer.Collect(sources, []).Single().Target);
    }

    [TestMethod]
    [DataRow("local example = [[wrapped(self)]]", LuaContinuationEvidence.Missing)]
    [DataRow("local example = [[return]]; return wrapped(self)", LuaContinuationEvidence.Continues)]
    [DataRow("local example = [=[wrapped(self)]=]", LuaContinuationEvidence.Missing)]
    [DataRow("local example = [=[return]=]; return wrapped(self)", LuaContinuationEvidence.Continues)]
    public void QuotedContinuationTokensDoNotDetermineOverrideFlow(string body, LuaContinuationEvidence expected)
    {
        LuaCallbackEvidence callback = LuaCallbackEvidenceAnalyzer.Analyze([new("Example", "init.lua", "Override('PlayerPuppet', 'Value', function(self, wrapped) " + body + " end)")]).Single();
        Assert.AreEqual(expected, callback.Continuation);
    }

    [TestMethod]
    public void ContinuingWrapperDoesNotRelabelCompetingReplacementsAsInformation()
    {
        RedScriptSource[] sources =
        [
            new("Alpha", "a.reds", "@replaceMethod(PlayerPuppet)\npublic func Value() -> Int32 { return 1; }"),
            new("Beta", "b.reds", "@replaceMethod(PlayerPuppet)\npublic func Value() -> Int32 { return 2; }"),
            new("Gamma", "c.reds", "@wrapMethod(PlayerPuppet)\npublic func Value() -> Int32 { return wrappedMethod(); }")
        ];
        ModSourceInventory inventory = new(sources, [], [], []);
        ProfileScanReceipt receipt = new(2, "Example", DateTimeOffset.UtcNow, ["Alpha", "Beta", "Gamma"], [], [], [], [], InteractionReportBuilder.Build(inventory), RedScriptFlowEvidenceAnalyzer.Analyze(sources), [], [], [], [], []);
        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single();

        Assert.AreEqual(EvidenceClassification.Exclusive, item.Classification);
        StringAssert.StartsWith(item.ClassificationLabel, "Confirmed:");
        StringAssert.Contains(item.ProofLabel, "replacement");
    }

    [TestMethod]
    [DataRow("t")]
    [DataRow("n")]
    public void TweakDbManagerLiteralOverloadsJoinTheSameYamlTarget(string prefix)
    {
        ModSourceInventory inventory = new([new("Runtime", "runtime.reds", "public func Apply() -> Void { TweakDBManager.SetFlat(" + prefix + "\"Items.Test.value\", 2); }")], [], [new("Initial", "initial.yaml", "Items.Test.value: 1")], []);
        InteractionFinding finding = InteractionReportBuilder.Build(inventory).Single();
        Assert.AreEqual("Items.Test.value", finding.Target);
        Assert.AreEqual(1, finding.TweakRuntimeEvidence!.Writes.Length);
    }

    [TestMethod]
    [DataRow("n")]
    [DataRow("t")]
    public void LuaPrefixCallsAreNotLiteralTargets(string prefix)
        => Assert.IsEmpty(SharedStateWriteAnalyzer.Collect([], [new("Example", "init.lua", "TweakDB:SetFlat(" + prefix + "\"Items.Test.value\", 1)")]));
}

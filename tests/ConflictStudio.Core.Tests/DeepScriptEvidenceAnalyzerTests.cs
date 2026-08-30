using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class DeepScriptEvidenceAnalyzerTests
{
    [TestMethod]
    public void RedScriptFlowFindsContinuationAndEarlierReturn()
    {
        RedScriptSource[] sources =
        [
            new("Alpha", "alpha.reds", "@wrapMethod(DamageSystem)\npublic final func ProcessHit(value: Int32) -> Void {\n  if value <= 0 {\n    return;\n  };\n  wrappedMethod(value);\n}")
        ];

        RedScriptFlowEvidence evidence = RedScriptFlowEvidenceAnalyzer.Analyze(sources).Single();

        Assert.AreEqual("DamageSystem.ProcessHit(Int32)", evidence.Target);
        Assert.AreEqual(RedScriptContinuationEvidence.EarlyReturnBeforeContinuation, evidence.Continuation);
        Assert.AreEqual(EvidenceConfidence.ExactToken, evidence.Confidence);
        Assert.AreEqual(EvidenceImpact.Review, evidence.Impact);
        Assert.AreEqual(2, evidence.Line);
    }

    [TestMethod]
    public void RedScriptFlowMarksUnconditionalContinuationAsNoKnownImpact()
    {
        RedScriptSource[] sources =
        [
            new("Alpha", "alpha.reds", "@wrapMethod(DamageSystem)\npublic final func ProcessHit() -> Void {\n  wrappedMethod();\n}")
        ];

        RedScriptFlowEvidence evidence = RedScriptFlowEvidenceAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(RedScriptContinuationEvidence.Continues, evidence.Continuation);
        Assert.AreEqual(EvidenceImpact.None, evidence.Impact);
    }

    [TestMethod]
    public void RedScriptReturnOfWrappedMethodContinuesTheChain()
    {
        RedScriptSource[] sources =
        [
            new("Alpha", "alpha.reds", "@wrapMethod(DamageSystem)\nprotected final func GetValue() -> Float {\n  return wrappedMethod() * 1.5;\n}")
        ];

        RedScriptFlowEvidence evidence = RedScriptFlowEvidenceAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(RedScriptContinuationEvidence.Continues, evidence.Continuation);
        Assert.AreEqual(EvidenceImpact.None, evidence.Impact);
    }

    [TestMethod]
    public void ConditionalReturnWrappedMethodDoesNotHideAnotherNonContinuingPath()
    {
        RedScriptSource[] sources =
        [
            new("Alpha", "alpha.reds", "@wrapMethod(DamageSystem)\nprotected final func GetValue(flag: Bool) -> Float {\n  if flag {\n    return wrappedMethod(flag);\n  };\n  return 0.0;\n}")
        ];

        RedScriptFlowEvidence evidence = RedScriptFlowEvidenceAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(RedScriptContinuationEvidence.EarlyReturnBeforeContinuation, evidence.Continuation);
        Assert.AreEqual(EvidenceImpact.Review, evidence.Impact);
    }

    [TestMethod]
    public void RedScriptIfElseThatContinuesOnBothBranchesContinuesTheChain()
    {
        RedScriptSource[] sources =
        [
            new("Alpha", "alpha.reds", "@wrapMethod(ScriptableDeviceAction)\npublic final func GetAwarenessCost(gameInstance: GameInstance) -> Float {\n  if Equals(this.GetClassName(), n\"MemoryWipe\") {\n    let value = wrappedMethod(gameInstance);\n    return value / 0.7;\n  } else {\n    return wrappedMethod(gameInstance);\n  };\n}")
        ];

        RedScriptFlowEvidence evidence = RedScriptFlowEvidenceAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(RedScriptContinuationEvidence.Continues, evidence.Continuation);
        Assert.AreEqual(EvidenceImpact.None, evidence.Impact);
    }

    [TestMethod]
    public void RedScriptEarlierSuppressingReturnWinsOverLaterContinuingBranches()
    {
        RedScriptSource source = new("Alpha", "alpha.reds", "@wrapMethod(DamageSystem)\npublic final func ProcessHit() -> Void { if this.Skip() { return; }; if this.UseA() { wrappedMethod(); } else { wrappedMethod(); }; }");

        RedScriptFlowEvidence evidence = RedScriptFlowEvidenceAnalyzer.Analyze([source]).Single();

        Assert.AreEqual(RedScriptContinuationEvidence.EarlyReturnBeforeContinuation, evidence.Continuation);
    }

    [TestMethod]
    public void RedScriptReturnAfterContinuationDoesNotSuppressALaterContinuingBranch()
    {
        RedScriptSource source = new("Alpha", "alpha.reds", "@wrapMethod(DamageSystem)\npublic final func ProcessHit() -> Void { if this.Guard() { wrappedMethod(); return; }; if this.UseA() { wrappedMethod(); } else { wrappedMethod(); }; }");

        RedScriptFlowEvidence evidence = RedScriptFlowEvidenceAnalyzer.Analyze([source]).Single();

        Assert.AreEqual(RedScriptContinuationEvidence.Continues, evidence.Continuation);
    }

    [TestMethod]
    public void RedScriptClosedNestedBlockDoesNotHideEarlierContinuationOnTheSamePath()
    {
        RedScriptSource source = new("Alpha", "alpha.reds", "@wrapMethod(DamageSystem)\npublic final func ProcessHit() -> Void { if this.Guard() { wrappedMethod(); if this.Nested() { this.DoThing(); }; return; }; if this.Alternate() { wrappedMethod(); } else { wrappedMethod(); }; }");

        RedScriptFlowEvidence evidence = RedScriptFlowEvidenceAnalyzer.Analyze([source]).Single();

        Assert.AreEqual(RedScriptContinuationEvidence.Continues, evidence.Continuation);
    }

    [TestMethod]
    public void RedScriptZeroIterationLoopDoesNotProveContinuation()
    {
        RedScriptSource source = new("Alpha", "alpha.reds", "@wrapMethod(DamageSystem)\npublic final func ProcessHit() -> Void { while this.ShouldContinue() { wrappedMethod(); }; }");

        RedScriptFlowEvidence evidence = RedScriptFlowEvidenceAnalyzer.Analyze([source]).Single();

        Assert.AreEqual(RedScriptContinuationEvidence.EarlyReturnBeforeContinuation, evidence.Continuation);
    }

    [TestMethod]
    public void RedScriptFlowUsesCanonicalParameterTypesForItsTarget()
    {
        RedScriptSource source = new("Alpha", "alpha.reds", "@wrapMethod(InventorySystem)\npublic final func Update(items: [InventoryItemData], const context: ref<StateContext>) -> Void { wrappedMethod(items, context); }");

        RedScriptFlowEvidence evidence = RedScriptFlowEvidenceAnalyzer.Analyze([source]).Single();

        Assert.AreEqual("InventorySystem.Update(array<InventoryItemData>, ref<StateContext>)", evidence.Target);
    }

    [TestMethod]
    public void RedScriptFlowExcludesInactiveGuardedDeclarations()
    {
        RedScriptSource[] sources =
        [
            new("Running Man", "running.reds", "module RunningMan\n@if(!ModuleExists(\"EasierControllerSprint\"))\n@replaceMethod(SprintDecisions)\nprotected func EnterCondition() -> Bool { return true; }"),
            new("Easier Sprint", "easier.reds", "module EasierControllerSprint\n@if(ModuleExists(\"RunningMan\"))\n@replaceMethod(SprintDecisions)\nprotected func EnterCondition() -> Bool { return true; }")
        ];

        RedScriptFlowEvidence evidence = RedScriptFlowEvidenceAnalyzer.Analyze(sources).Single();

        Assert.AreEqual("Easier Sprint", evidence.Provider);
    }

    [TestMethod]
    public void RedScriptFlowIgnoresContinuationTokensInStringsAndComments()
    {
        RedScriptSource[] sources =
        [
            new("Alpha", "alpha.reds", "@wrapMethod(DamageSystem)\npublic final func ProcessHit() -> Void {\n  let marker = \"wrappedMethod()\";\n  // wrappedMethod();\n  return;\n}")
        ];

        RedScriptFlowEvidence evidence = RedScriptFlowEvidenceAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(RedScriptContinuationEvidence.Missing, evidence.Continuation);
    }

    [TestMethod]
    public void SharedStateWritesGroupOnlyLiteralTokensWrittenByMultipleProviders()
    {
        RedScriptSource[] sources =
        [
            new("Alpha", "alpha.reds", "TweakDBInterface.SetFlat(t\"Items.Pistol.damage\", 2.0);\nblackboard.SetBool(GetAllBlackboardDefs().UI_System.IsInMenu, true);\nStatusEffectHelper.ApplyStatusEffect(player, t\"BaseStatusEffect.Burning\");\nstatPools.RequestChangingStatPoolValue(id, gamedataStatPoolType.Health, -10.0, instigator);\nquests.SetFactStr(n\"alpha_fact\", 1);"),
            new("Beta", "beta.reds", "TweakDBInterface.SetFlatNoUpdate(t\"Items.Pistol.damage\", 3.0);\nblackboard.SetBool(GetAllBlackboardDefs().UI_System.IsInMenu, false);\nStatusEffectHelper.RemoveStatusEffect(player, t\"BaseStatusEffect.Burning\");\nstatPools.RequestSettingStatPoolValue(id, gamedataStatPoolType.Health, 100.0, instigator);\nquests.SetFactStr(n\"alpha_fact\", 0);\nTweakDBInterface.SetFlat(dynamicTarget, 4.0);")
        ];

        SharedStateWriteFinding[] findings = SharedStateWriteAnalyzer.Analyze(sources, []);

        string[] expected =
        [
            "TweakDb:Items.Pistol.damage",
            "Blackboard:GetAllBlackboardDefs().UI_System.IsInMenu",
            "StatusEffect:BaseStatusEffect.Burning",
            "StatPool:Health",
            "Persistence:alpha_fact"
        ];
        CollectionAssert.AreEquivalent(expected, findings.Select(value => $"{value.Surface}:{value.Target}").ToArray());
        Assert.IsTrue(findings.All(value => value.Confidence == EvidenceConfidence.Literal));
        Assert.IsTrue(findings.All(value => value.Impact == EvidenceImpact.Review));
        Assert.IsTrue(findings.All(value => value.Writes.Select(write => write.Provider).Distinct().Count() == 2));
    }

    [TestMethod]
    public void SharedStateWritesIgnoreCommentedCalls()
    {
        RedScriptSource[] sources =
        [
            new("Alpha", "alpha.reds", "TweakDBInterface.SetFlat(t\"Items.Pistol.damage\", 2.0);"),
            new("Beta", "beta.reds", "// TweakDBInterface.SetFlat(t\"Items.Pistol.damage\", 3.0);")
        ];

        SharedStateWriteFinding[] findings = SharedStateWriteAnalyzer.Analyze(sources, []);

        Assert.AreEqual(0, findings.Length);
    }

    [TestMethod]
    public void LuaEvidenceKeepsLifecycleDynamicTargetAndOverrideContinuationSeparate()
    {
        LuaSource[] sources =
        [
            new("Alpha", "alpha.lua", "registerForEvent('onInit', function() end)\nOverride(className, 'ProcessHit', function(self, value, wrappedMethod)\n  return wrappedMethod(value)\nend)")
        ];

        LuaCallbackEvidence[] evidence = LuaCallbackEvidenceAnalyzer.Analyze(sources);
        LuaCallbackEvidence lifecycle = evidence.Single(value => value.Kind == LuaCallbackEvidenceKind.Lifecycle);
        LuaCallbackEvidence callback = evidence.Single(value => value.Kind == LuaCallbackEvidenceKind.Override);

        Assert.AreEqual("onInit", lifecycle.Target);
        Assert.AreEqual(EvidenceConfidence.Literal, lifecycle.Confidence);
        Assert.AreEqual("className.ProcessHit", callback.Target);
        Assert.AreEqual(EvidenceConfidence.Dynamic, callback.Confidence);
        Assert.AreEqual(LuaContinuationEvidence.Continues, callback.Continuation);
        Assert.AreEqual(EvidenceImpact.Review, callback.Impact);
    }

    [TestMethod]
    public void LuaOverrideContinuationUsesTheDeclaredCallbackParameterName()
    {
        LuaSource[] sources = [new("Alpha", "alpha.lua", "Override('DamageSystem', 'ProcessHit', function(self, value, wrapped)\n  return wrapped(value)\nend)")];

        LuaCallbackEvidence evidence = LuaCallbackEvidenceAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(LuaContinuationEvidence.Continues, evidence.Continuation);
    }

    [TestMethod]
    public void LuaOverrideThatCallsWrappedThenReturnsContinues()
    {
        LuaSource source = new("Alpha", "alpha.lua", "Override('DamageSystem', 'ProcessHit', function(self, wrapped) wrapped(); return end)");

        LuaCallbackEvidence evidence = LuaCallbackEvidenceAnalyzer.Analyze([source]).Single();

        Assert.AreEqual(LuaContinuationEvidence.Continues, evidence.Continuation);
    }

    [TestMethod]
    public void LuaConditionalWrappedCallDoesNotProveEveryPathContinues()
    {
        LuaSource source = new("Alpha", "alpha.lua", "Override('DamageSystem', 'ProcessHit', function(self, flag, wrapped) if flag then wrapped() end return end)");

        LuaCallbackEvidence evidence = LuaCallbackEvidenceAnalyzer.Analyze([source]).Single();

        Assert.AreEqual(LuaContinuationEvidence.Missing, evidence.Continuation);
    }

    [TestMethod]
    public void LuaOverrideWithDynamicCallbackHasUnknownContinuation()
    {
        LuaSource[] sources = [new("Alpha", "alpha.lua", "Override('DamageSystem', 'ProcessHit', callback)")];

        LuaCallbackEvidence evidence = LuaCallbackEvidenceAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(LuaContinuationEvidence.Unknown, evidence.Continuation);
    }

    [TestMethod]
    public void LuaEvidenceResolvesHelperTargetAndUsesThePassedCallback()
    {
        LuaSource source = new("Alpha", "alpha.lua", "local function registerOverride(className, methodName, callback)\n  Override(className, methodName, callback)\nend\nregisterOverride('SettingsMainGameController', 'RequestRestoreDefaults', function(this, wrapped) return wrapped() end)");

        LuaCallbackEvidence evidence = LuaCallbackEvidenceAnalyzer.Analyze([source]).Single(value => value.Confidence == EvidenceConfidence.Literal);

        Assert.AreEqual("SettingsMainGameController.RequestRestoreDefaults", evidence.Target);
        Assert.AreEqual(LuaContinuationEvidence.Continues, evidence.Continuation);
    }

    [TestMethod]
    public void LuaEvidenceCollapsesByteIdenticalVendoredHelpersByHash()
    {
        const string helper = "ObserveAfter('PlayerPuppet', 'OnAction', function() end)";
        LuaSource[] sources =
        [
            new("Alpha", "vendor/helper.lua", helper),
            new("Beta", "lib/helper.lua", helper)
        ];

        LuaCallbackEvidence evidence = LuaCallbackEvidenceAnalyzer.Analyze(sources).Single();

        Assert.AreEqual("PlayerPuppet.OnAction", evidence.Target);
        Assert.AreEqual(2, evidence.Copies.Length);
        string[] expectedProviders = ["Alpha", "Beta"];
        CollectionAssert.AreEquivalent(expectedProviders, evidence.Copies.Select(value => value.Provider).ToArray());
        Assert.AreEqual(64, evidence.SourceHash.Length);
        Assert.AreEqual(EvidenceImpact.None, evidence.Impact);
    }

    [TestMethod]
    public void LuaEvidenceIgnoresCommentedRegistrations()
    {
        LuaSource[] sources =
        [
            new("Alpha", "alpha.lua", "-- Observe('PlayerPuppet', 'OnAction', function() end)\n--[[ registerForEvent('onInit', function() end) ]]")
        ];

        LuaCallbackEvidence[] evidence = LuaCallbackEvidenceAnalyzer.Analyze(sources);

        Assert.AreEqual(0, evidence.Length);
    }
}

using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class ScriptInteractionAnalyzerTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void SameProviderReplacementsRetainDuplicateOrDifferentBodies(bool identical, bool sameFile)
    {
        string first = "@replaceMethod(PlayerPuppet)\npublic func Value() -> Int32 { return 1; }";
        string second = "@replaceMethod(PlayerPuppet)\npublic func Value() -> Int32 { return " + (identical ? "1" : "2") + "; }";
        RedScriptSource[] sources = sameFile
            ? [new("Alpha", "one.reds", first + "\n" + second)]
            : [new("Alpha", "one.reds", first), new("ALPHA", "two.reds", second)];

        RedScriptOverlap overlap = RedScriptInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(identical ? RedScriptOverlapKind.RedundantReplacement : RedScriptOverlapKind.ExclusiveReplacement, overlap.Kind);
        Assert.AreEqual(2, overlap.Hooks.Length);
        Assert.AreEqual("PlayerPuppet.Value()", overlap.Target);
    }

    [TestMethod]
    [DataRow("@addMethod(PlayerPuppet)\npublic func Value() -> Int32 { return 1; }", "PlayerPuppet.Value()")]
    [DataRow("@addField(PlayerPuppet)\nlet sharedState: Bool;", "PlayerPuppet.sharedState")]
    public void SameProviderDuplicateMembersAreReported(string declaration, string target)
    {
        RedScriptOverlap overlap = RedScriptInteractionAnalyzer.Analyze([new("Alpha", "one.reds", declaration + "\n" + declaration)]).Single();

        Assert.AreEqual(RedScriptOverlapKind.AddedMemberCollision, overlap.Kind);
        Assert.AreEqual(target, overlap.Target);
        Assert.AreEqual(2, overlap.Hooks.Length);
    }

    [TestMethod]
    public void SameProviderWrapperDoesNotHideDuplicateAddedMethods()
    {
        string added = "@addMethod(PlayerPuppet)\npublic func Value() -> Int32 { return 1; }";
        string wrapper = "@wrapMethod(PlayerPuppet)\npublic func Value() -> Int32 { return wrappedMethod(); }";

        RedScriptOverlap overlap = RedScriptInteractionAnalyzer.Analyze([new("Alpha", "one.reds", added + "\n" + added + "\n" + wrapper)]).Single();

        Assert.AreEqual(RedScriptOverlapKind.AddedMemberCollision, overlap.Kind);
    }

    [TestMethod]
    [DataRow("@wrapMethod(PlayerPuppet)\npublic func Value() -> Int32 { return wrappedMethod(); }")]
    [DataRow("@addMethod(PlayerPuppet)\npublic func Value() -> Int32 { return 1; }")]
    [DataRow("@replaceMethod(PlayerPuppet)\npublic func Value() -> Int32 { return 1; }")]
    public void SameProviderInternalWrapperChainsStayOutOfReport(string first)
    {
        string wrapper = "@wrapMethod(PlayerPuppet)\npublic func Value() -> Int32 { return wrappedMethod(); }";

        Assert.AreEqual(0, RedScriptInteractionAnalyzer.Analyze([new("Alpha", "one.reds", first + "\n" + wrapper + "\n" + wrapper)]).Length);
    }

    [TestMethod]
    public void SameProviderMultipleCetOverridesAreReported()
    {
        string registration = "Override('PlayerPuppet', 'Value', function(self, wrapped) return wrapped() end)";
        LuaOverlap overlap = LuaInteractionAnalyzer.Analyze([new("Alpha", "one.lua", registration + "\n" + registration)]).Single();

        Assert.AreEqual(LuaOverlapKind.OverrideReview, overlap.Kind);
        Assert.AreEqual(2, overlap.Hooks.Length);
    }

    [TestMethod]
    [DataRow("settings.transparentSlotBg")]
    [DataRow("true")]
    [DataRow("false")]
    [DataRow("nil")]
    [DataRow("1")]
    [DataRow("-0.5")]
    [DataRow("'custom'")]
    public void SameProviderIdenticalReturnOnlyOverridesAreRedundant(string result)
    {
        string registration = "Override('HotkeyItemController', 'TransparentSlotBackgrounds', function(_) return " + result + " end)";
        LuaSource[] sources = [new("Quickslots", "init.lua", registration + "\n" + registration)];

        LuaOverlap overlap = LuaInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual("RedundantOverride", overlap.Kind.ToString());
        Assert.HasCount(2, overlap.Hooks);
        Assert.HasCount(2, LuaCallbackEvidenceAnalyzer.Analyze(sources));
    }

    [TestMethod]
    [DataRow("counter = counter + 1 return settings.transparentSlotBg")]
    [DataRow("return readSetting()")]
    [DataRow("return settings.transparentSlotBg + 1")]
    [DataRow("return settings['transparentSlotBg']")]
    [DataRow("if enabled then return settings.transparentSlotBg end return false")]
    public void SameProviderMatchingNontrivialCallbacksRemainReview(string body)
    {
        string registration = "Override('HotkeyItemController', 'TransparentSlotBackgrounds', function(_) " + body + " end)";

        LuaOverlap overlap = LuaInteractionAnalyzer.Analyze([new("Alpha", "init.lua", registration + "\n" + registration)]).Single();

        Assert.AreEqual(LuaOverlapKind.OverrideReview, overlap.Kind);
        Assert.HasCount(2, overlap.Hooks);
    }

    [TestMethod]
    [DataRow("settings.transparentSlotBg", "settings.smallSlots")]
    [DataRow("true", "false")]
    [DataRow("'a b'", "'a  b'")]
    public void DifferentReturnOnlyOverridesRemainReview(string first, string second)
    {
        string text = "Override('PlayerPuppet', 'Value', function(_) return " + first + " end)\nOverride('PlayerPuppet', 'Value', function(_) return " + second + " end)";

        Assert.AreEqual(LuaOverlapKind.OverrideReview, LuaInteractionAnalyzer.Analyze([new("Alpha", "init.lua", text)]).Single().Kind);
    }

    [TestMethod]
    [DataRow("settings.transparentSlotBg")]
    [DataRow("true")]
    public void MatchingReturnOnlyOverridesAcrossProvidersRemainReview(string result)
    {
        string registration = "Override('PlayerPuppet', 'Value', function(_) return " + result + " end)";

        LuaOverlap overlap = LuaInteractionAnalyzer.Analyze([new("Alpha", "alpha.lua", registration), new("Beta", "beta.lua", registration)]).Single();

        Assert.AreEqual(LuaOverlapKind.OverrideReview, overlap.Kind);
        Assert.HasCount(2, overlap.Hooks);
    }

    [TestMethod]
    [DataRow("Observe")]
    [DataRow("Override")]
    public void SameProviderObserversAndOneOverrideStayOutOfReport(string firstKind)
    {
        string text = firstKind + "('PlayerPuppet', 'Value', function() end)\nObserveBefore('PlayerPuppet', 'Value', function() end)\nObserveAfter('PlayerPuppet', 'Value', function() end)";

        Assert.AreEqual(0, LuaInteractionAnalyzer.Analyze([new("Alpha", "one.lua", text)]).Length);
    }

    [TestMethod]
    public void RedScriptAnalyzerMarksTwoReplacementsAsExclusive()
    {
        RedScriptSource[] sources =
        [
            new("Alpha", "alpha.reds", "@replaceMethod(DamageSystem)\npublic final func ProcessHit() -> Void { }"),
            new("Beta", "beta.reds", "@replaceMethod(DamageSystem)\npublic final func ProcessHit() -> Void { }")
        ];

        RedScriptOverlap overlap = RedScriptInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(RedScriptOverlapKind.RedundantReplacement, overlap.Kind);
        Assert.AreEqual("DamageSystem.ProcessHit()", overlap.Target);
    }

    [TestMethod]
    public void RedScriptAnalyzerKeepsMultipleWrappersAsCompositionReview()
    {
        RedScriptSource[] sources =
        [
            new("Alpha", "alpha.reds", "@wrapMethod(DamageSystem)\npublic final func ProcessHit() -> Void { wrappedMethod(); }"),
            new("Beta", "beta.reds", "@wrapMethod(DamageSystem)\npublic final func ProcessHit() -> Void { wrappedMethod(); }")
        ];

        RedScriptOverlap overlap = RedScriptInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(RedScriptOverlapKind.CompositionReview, overlap.Kind);
    }

    [TestMethod]
    public void RedScriptAnalyzerEvaluatesActiveModuleCompatibilityGuards()
    {
        RedScriptSource[] sources =
        [
            new("Running Man", "running.reds", "module RunningMan\n@if(!ModuleExists(\"EasierControllerSprint\"))\n@replaceMethod(SprintDecisions)\nprotected func EnterCondition() -> Bool { return true; }"),
            new("Easier Sprint", "easier.reds", "module EasierControllerSprint\n@if(ModuleExists(\"RunningMan\"))\n@replaceMethod(SprintDecisions)\nprotected func EnterCondition() -> Bool { return true; }")
        ];

        RedScriptOverlap[] overlaps = RedScriptInteractionAnalyzer.Analyze(sources);

        Assert.AreEqual(0, overlaps.Length);
    }

    [TestMethod]
    public void RedScriptAnalyzerEvaluatesCompoundAndInlineModuleGuards()
    {
        RedScriptSource[] sources =
        [
            new("Modules", "modules.reds", "module AlphaModule\nmodule BetaModule"),
            new("Guarded", "guarded.reds", "@if(ModuleExists(\"AlphaModule\") && ModuleExists(\"BetaModule\")) @replaceMethod(DamageSystem) public func ProcessHit() -> Void {}"),
            new("Inactive", "inactive.reds", "@if(!ModuleExists(\"AlphaModule\") && !ModuleExists(\"BetaModule\"))\n@replaceMethod(DamageSystem)\npublic func ProcessHit() -> Void {}"),
            new("Third", "third.reds", "@replaceMethod(DamageSystem)\npublic func ProcessHit() -> Void {}")
        ];

        RedScriptOverlap overlap = RedScriptInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(2, overlap.Hooks.Length);
        Assert.IsTrue(overlap.Hooks.Any(value => value.Provider == "Guarded"));
        Assert.IsTrue(overlap.Hooks.Any(value => value.Provider == "Third"));
    }

    [TestMethod]
    public void RedScriptUnknownConditionProducesNamedEvidenceFailure()
    {
        RedScriptSource source = new("Alpha", "alpha.reds", "@if(GameVersion(\"2.3\"))\n@replaceMethod(DamageSystem)\npublic func ProcessHit() -> Void {}");
        RedScriptSource other = new("Beta", "beta.reds", "@replaceMethod(DamageSystem)\npublic func ProcessHit() -> Void {}");

        SourceAnalysisFailure failure = RedScriptConditionalSourceFilter.Failures([source]).Single();

        Assert.AreEqual("RedScript condition", failure.Surface);
        Assert.AreEqual("alpha.reds", failure.FilePath);
        Assert.AreEqual(0, RedScriptInteractionAnalyzer.Analyze([source, other]).Length);
    }

    [TestMethod]
    public void RedScriptCommentedConditionDoesNotGuardTheNextDeclaration()
    {
        RedScriptSource[] sources =
        [
            new("Alpha", "alpha.reds", "// @if(!ModuleExists(\"BetaModule\"))\n@replaceMethod(DamageSystem)\npublic func ProcessHit() -> Void {}"),
            new("Beta", "beta.reds", "module BetaModule\n@replaceMethod(DamageSystem)\npublic func ProcessHit() -> Void {}")
        ];

        RedScriptOverlap overlap = RedScriptInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(RedScriptOverlapKind.RedundantReplacement, overlap.Kind);
    }

    [TestMethod]
    public void RedScriptInlineConditionalImportDoesNotGuardALaterMethod()
    {
        RedScriptSource[] sources =
        [
            new("Alpha", "alpha.reds", "@if(!ModuleExists(\"BetaModule\")) import Optional.*\n@replaceMethod(DamageSystem)\npublic func ProcessHit() -> Void {}"),
            new("Beta", "beta.reds", "module BetaModule\n@replaceMethod(DamageSystem)\npublic func ProcessHit() -> Void {}")
        ];

        RedScriptOverlap overlap = RedScriptInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(RedScriptOverlapKind.RedundantReplacement, overlap.Kind);
    }

    [TestMethod]
    public void RedScriptBlockCommentedModuleAndConditionDoNotAffectActiveGuards()
    {
        RedScriptSource[] sources =
        [
            new("Alpha", "alpha.reds", "/*\nmodule GhostModule\n@if(GameVersion(\"2.0\"))\n*/\n@if(!ModuleExists(\"GhostModule\"))\n@replaceMethod(DamageSystem)\npublic func ProcessHit() -> Void {}"),
            new("Beta", "beta.reds", "@replaceMethod(DamageSystem)\npublic func ProcessHit() -> Void {}")
        ];

        RedScriptOverlap overlap = RedScriptInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(RedScriptOverlapKind.RedundantReplacement, overlap.Kind);
        Assert.AreEqual(0, RedScriptConditionalSourceFilter.Failures(sources).Length);
    }

    [TestMethod]
    public void RedScriptAnalyzerDoesNotGroupDifferentDeclaredOverloads()
    {
        RedScriptSource[] sources =
        [
            new("Alpha", "alpha.reds", "@replaceMethod(DamageSystem)\npublic final func ProcessHit(value: Int32) -> Void { }"),
            new("Beta", "beta.reds", "@replaceMethod(DamageSystem)\npublic final func ProcessHit(value: Float) -> Void { }")
        ];

        RedScriptOverlap[] overlaps = RedScriptInteractionAnalyzer.Analyze(sources);

        Assert.AreEqual(0, overlaps.Length);
    }

    [TestMethod]
    public void TweakAnalyzerSeparatesScalarOverwriteFromAppend()
    {
        TweakSource[] sources =
        [
            new("Alpha", "alpha.yaml", "Items.Base_HMG:\n  value: 1.0\n  list:\n    - !append Items.A\n"),
            new("Beta", "beta.yaml", "Items.Base_HMG:\n  value: 2.0\n  list:\n    - !append Items.B\n")
        ];

        TweakOverlap[] overlaps = TweakInteractionAnalyzer.Analyze(sources);

        Assert.AreEqual(TweakOverlapKind.ScalarOverwrite, overlaps.Single(value => value.Target == "Items.Base_HMG.value").Kind);
        Assert.AreEqual(TweakOverlapKind.ComposableMutation, overlaps.Single(value => value.Target == "Items.Base_HMG.list").Kind);
    }

    [TestMethod]
    public void TweakAnalyzerComparesInlineRecordPayloads()
    {
        TweakSource[] sources =
        [
            new("Alpha", "alpha.yaml", "Items.Base_HMG:\n  inlineRecord:\n    value: 1.0\n"),
            new("Beta", "beta.yaml", "Items.Base_HMG:\n  inlineRecord:\n    value: 2.0\n")
        ];

        TweakOverlap[] overlaps = TweakInteractionAnalyzer.Analyze(sources);

        Assert.AreEqual(TweakOverlapKind.ScalarOverwrite, overlaps.Single().Kind);
    }

    [TestMethod]
    public void RedScriptAnalyzerIgnoresReplacementSignaturesInsideBlockComments()
    {
        RedScriptSource[] sources =
        [
            new("Alpha", "alpha.reds", "/* @replaceMethod(DamageSystem)\npublic func ProcessHit() -> Void {} */\n@replaceMethod(DamageSystem)\npublic func ProcessHit() -> Void {}"),
            new("Beta", "beta.reds", "@wrapMethod(DamageSystem)\npublic func ProcessHit() -> Void { wrappedMethod(); }")
        ];

        RedScriptOverlap overlap = RedScriptInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(RedScriptOverlapKind.CompositionReview, overlap.Kind);
    }

    [TestMethod]
    public void RedScriptAnalyzerReadsMultilineAnnotatedMethodSignatures()
    {
        RedScriptSource[] sources =
        [
            new("Alpha", "alpha.reds", "@wrapMethod(InventorySystem)\npublic final func Update(\n  items: [InventoryItemData],\n  const context: ref<StateContext>\n) -> Void { wrappedMethod(items, context); }"),
            new("Beta", "beta.reds", "@wrapMethod(InventorySystem)\npublic final func Update(\n  items: [InventoryItemData],\n  const context: ref<StateContext>\n) -> Void { wrappedMethod(items, context); }")
        ];

        RedScriptOverlap overlap = RedScriptInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual("InventorySystem.Update(array<InventoryItemData>, ref<StateContext>)", overlap.Target);
        Assert.AreEqual(2, overlap.Hooks.Length);
    }

    [TestMethod]
    public void TweakAnalyzerTreatsDisjointCrossProviderArrayMutationsAsComposable()
    {
        TweakSource[] sources =
        [
            new("Alpha", "alpha.yaml", "Items.Base_HMG:\n  list:\n    - !append Items.A\n"),
            new("Beta", "beta.yaml", "Items.Base_HMG:\n  list:\n    - !remove Items.B\n")
        ];

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(TweakOverlapKind.ComposableMutation, overlap.Kind);
    }

    [TestMethod]
    public void TweakAnalyzerReportsOpposingMembershipAcrossComponents()
    {
        TweakSource[] sources =
        [
            new("Alpha", "alpha.yaml", "Items.Base_HMG:\n  list:\n    - !append Items.A\n"),
            new("Beta", "beta.yaml", "Items.Base_HMG:\n  list:\n    - !remove Items.A\n")
        ];

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(TweakOverlapKind.OpposingMutation, overlap.Kind);
    }

    [TestMethod]
    public void TweakAnalyzerAppliesArrayAssignmentsBeforeMutations()
    {
        TweakSource[] sources =
        [
            new("Alpha", "alpha.yaml", "Items.Base_HMG:\n  list: [Items.A]\n"),
            new("Beta", "beta.yaml", "Items.Base_HMG:\n  list:\n    - !append Items.B\n")
        ];

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(TweakOverlapKind.AssignmentThenMutation, overlap.Kind);
    }

    [TestMethod]
    public void TweakAnalyzerFlagsDuplicatePlainAppendsButNotAppendOnce()
    {
        TweakSource[] duplicate =
        [
            new("Alpha", "alpha.yaml", "Items.Base_HMG:\n  list:\n    - !append Items.A\n"),
            new("Beta", "beta.yaml", "Items.Base_HMG:\n  list:\n    - !append Items.A\n")
        ];
        TweakSource[] unique =
        [
            new("Alpha", "alpha.yaml", "Items.Base_HMG:\n  list:\n    - !append-once Items.A\n"),
            new("Beta", "beta.yaml", "Items.Base_HMG:\n  list:\n    - !append-once Items.A\n")
        ];

        Assert.AreEqual(TweakOverlapKind.DuplicateMutation, TweakInteractionAnalyzer.Analyze(duplicate).Single().Kind);
        Assert.AreEqual(TweakOverlapKind.Redundant, TweakInteractionAnalyzer.Analyze(unique).Single().Kind);
    }

    [TestMethod]
    public void LuaAnalyzerFlagsDuplicateOverridesMoreStronglyThanObservers()
    {
        LuaSource[] sources =
        [
            new("Alpha", "alpha.lua", "Override('DamageSystem', 'ProcessHit', function() end)\nObserve('PlayerPuppet', 'OnAction', function() end)"),
            new("Beta", "beta.lua", "Override('DamageSystem', 'ProcessHit', function() end)\nObserve('PlayerPuppet', 'OnAction', function() end)")
        ];

        LuaOverlap[] overlaps = LuaInteractionAnalyzer.Analyze(sources);

        Assert.AreEqual(LuaOverlapKind.OverrideReview, overlaps.Single(value => value.Target == "DamageSystem.ProcessHit").Kind);
        Assert.AreEqual(LuaOverlapKind.ObserverComposition, overlaps.Single(value => value.Target == "PlayerPuppet.OnAction").Kind);
    }

    [TestMethod]
    public void LuaAnalyzerDoesNotCallOneOverridePlusObserversMultiOverride()
    {
        LuaSource[] sources =
        [
            new("Alpha", "alpha.lua", "Override('SettingsMainGameController', 'RequestRestoreDefaults', function(self, wrapped) return wrapped() end)"),
            new("Beta", "beta.lua", "ObserveBefore('SettingsMainGameController', 'RequestRestoreDefaults', function() end)\nObserveAfter('SettingsMainGameController', 'RequestRestoreDefaults', function() end)")
        ];

        LuaOverlap overlap = LuaInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(LuaOverlapKind.OverrideWithObservers, overlap.Kind);
    }

    [TestMethod]
    public void LuaAnalyzerResolvesLiteralTargetsPassedThroughRegistrationHelpers()
    {
        LuaSource[] sources =
        [
            new("Alpha", "alpha.lua", "local function registerOverride(className, methodName, callback)\n  Override(className, methodName, callback)\nend\nregisterOverride('SettingsMainGameController', 'RequestRestoreDefaults', function(this, wrapped) return wrapped() end)"),
            new("Beta", "beta.lua", "Override('SettingsMainGameController', 'RequestRestoreDefaults', function(this, wrapped) return wrapped() end)")
        ];

        LuaOverlap overlap = LuaInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual("SettingsMainGameController.RequestRestoreDefaults", overlap.Target);
        Assert.AreEqual(LuaOverlapKind.OverrideReview, overlap.Kind);
    }

    [TestMethod]
    public void LuaAnalyzerResolvesNestedLiteralRegistrationHelpers()
    {
        LuaSource[] sources =
        [
            new("Alpha", "alpha.lua", "local function RegisterGuard(className, methodName, callback)\n  Override(className, methodName, callback)\nend\nlocal function RegisterPart(methodName)\n  RegisterGuard('InventoryItemModeLogicController', methodName, function(this, data, wrapped) return wrapped(data) end)\nend\nRegisterPart('OnReplacePartNotificationClosed')"),
            new("Beta", "beta.lua", "ObserveAfter('InventoryItemModeLogicController', 'OnReplacePartNotificationClosed', function() end)")
        ];

        LuaOverlap overlap = LuaInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual("InventoryItemModeLogicController.OnReplacePartNotificationClosed", overlap.Target);
        Assert.AreEqual(LuaOverlapKind.OverrideWithObservers, overlap.Kind);
    }

    [TestMethod]
    public void LuaAnalyzerDoesNotCountAHelperDefinitionAndItsInvocationTwice()
    {
        LuaSource[] sources =
        [
            new("Alpha", "alpha.lua", "local function Register()\n  Override('DamageSystem', 'ProcessHit', function(this, wrapped) return wrapped() end)\nend\nRegister()"),
            new("Beta", "beta.lua", "ObserveAfter('DamageSystem', 'ProcessHit', function() end)")
        ];

        LuaOverlap overlap = LuaInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(LuaOverlapKind.OverrideWithObservers, overlap.Kind);
    }

    [TestMethod]
    public void LuaAnalyzerCountsTwoOverrideRegistrationsFromOneProvider()
    {
        LuaSource[] sources =
        [
            new("Alpha", "alpha.lua", "Override('DamageSystem', 'ProcessHit', function(this, wrapped) return wrapped() end)\nOverride('DamageSystem', 'ProcessHit', function(this, wrapped) return wrapped() end)"),
            new("Beta", "beta.lua", "ObserveAfter('DamageSystem', 'ProcessHit', function() end)")
        ];

        LuaOverlap overlap = LuaInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(LuaOverlapKind.OverrideReview, overlap.Kind);
    }

    [TestMethod]
    public void LuaAnalyzerDoesNotEmitAnUncalledHardcodedHelper()
    {
        LuaSource[] sources =
        [
            new("Alpha", "alpha.lua", "local function NeverCalled()\n  Override('DamageSystem', 'ProcessHit', function(this, wrapped) return wrapped() end)\nend"),
            new("Beta", "beta.lua", "ObserveAfter('DamageSystem', 'ProcessHit', function() end)")
        ];

        Assert.AreEqual(0, LuaInteractionAnalyzer.Analyze(sources).Length);
    }

    [TestMethod]
    public void LuaAnalyzerExpandsNestedHelperTopologyOnceAtTheOuterCall()
    {
        LuaSource[] sources =
        [
            new("Alpha", "alpha.lua", "local function Inner(className, methodName, callback) Override(className, methodName, callback) end\nlocal function Register() Inner('DamageSystem', 'ProcessHit', function(this, wrapped) return wrapped() end) end\nRegister()"),
            new("Beta", "beta.lua", "ObserveAfter('DamageSystem', 'ProcessHit', function() end)")
        ];

        LuaOverlap overlap = LuaInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(LuaOverlapKind.OverrideWithObservers, overlap.Kind);
    }

    [TestMethod]
    public void LuaAnalyzerExpandsAnExportedRegistrationRootOnce()
    {
        LuaSource[] sources =
        [
            new("Alpha", "alpha.lua", "local function registerOverride(className, methodName, callback) Override(className, methodName, callback) end\nfunction Suppression.init() Suppression.registerOverrides() end\nfunction Suppression.registerOverrides() registerOverride('DamageSystem', 'ProcessHit', function(this, wrapped) return wrapped() end) end"),
            new("Beta", "beta.lua", "ObserveAfter('DamageSystem', 'ProcessHit', function() end)")
        ];

        LuaOverlap overlap = LuaInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(LuaOverlapKind.OverrideWithObservers, overlap.Kind);
    }

    [TestMethod]
    public void LuaAnalyzerIgnoresCommentedHooks()
    {
        LuaSource[] sources =
        [
            new("Alpha", "alpha.lua", "-- Observe('PlayerPuppet', 'OnAction', function() end)"),
            new("Beta", "beta.lua", "Observe('PlayerPuppet', 'OnAction', function() end)")
        ];

        LuaOverlap[] overlaps = LuaInteractionAnalyzer.Analyze(sources);

        Assert.AreEqual(0, overlaps.Length);
    }

    [TestMethod]
    public void RedScriptAnalyzerGroupsEquivalentParameterNamesWhitespaceAndArrayAliases()
    {
        RedScriptSource[] sources =
        [
            new("Alpha", "alpha.reds", "@replaceMethod(InventorySystem)\npublic final func Update(items: script_ref<[InventoryItemData]>, const context: ref<StateContext>) -> Void { }"),
            new("Beta", "beta.reds", "@replaceMethod(InventorySystem)\npublic final func Update(values: script_ref<array<InventoryItemData>>,const state:ref<StateContext>) -> Void { }")
        ];

        RedScriptOverlap overlap = RedScriptInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual("InventorySystem.Update(script_ref<array<InventoryItemData>>, ref<StateContext>)", overlap.Target);
        Assert.AreEqual(RedScriptOverlapKind.ExclusiveReplacement, overlap.Kind);
    }

    [TestMethod]
    public void RedScriptAnalyzerFindsCrossProviderAddedMethodAndFieldCollisions()
    {
        RedScriptSource[] sources =
        [
            new("Alpha", "alpha.reds", "@addMethod(PlayerPuppet)\npublic func SharedValue(item: ItemID) -> Bool { return true; }\n@addField(PlayerPuppet)\nlet sharedState: Bool;"),
            new("Beta", "beta.reds", "@addMethod(PlayerPuppet)\npublic func SharedValue(value: ItemID) -> Bool { return false; }\n@addField(PlayerPuppet)\nlet sharedState: Int32;")
        ];

        RedScriptOverlap[] overlaps = RedScriptInteractionAnalyzer.Analyze(sources);

        Assert.AreEqual(2, overlaps.Length);
        Assert.IsTrue(overlaps.All(value => value.Kind == RedScriptOverlapKind.AddedMemberCollision));
        Assert.IsTrue(overlaps.Any(value => value.Target == "PlayerPuppet.SharedValue(ItemID)"));
        Assert.IsTrue(overlaps.Any(value => value.Target == "PlayerPuppet.sharedState"));
    }

    [TestMethod]
    public void RedScriptAnalyzerDoesNotCallOneAddPlusOneWrapMultipleAdditions()
    {
        RedScriptSource[] sources =
        [
            new("Alpha", "alpha.reds", "@addMethod(PlayerPuppet)\npublic func SharedValue(item: ItemID) -> Bool { return true; }"),
            new("Beta", "beta.reds", "@wrapMethod(PlayerPuppet)\npublic func SharedValue(value: ItemID) -> Bool { return wrappedMethod(value); }")
        ];

        RedScriptOverlap overlap = RedScriptInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(RedScriptOverlapKind.AddedMemberInteraction, overlap.Kind);
        InteractionFinding finding = InteractionReportBuilder.Build(new ModSourceInventory(sources, [], [], [])).Single();
        Assert.AreEqual(InteractionFindingKind.Informational, finding.Kind);
    }
}

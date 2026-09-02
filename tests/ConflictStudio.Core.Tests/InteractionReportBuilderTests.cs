using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class InteractionReportBuilderTests
{
    private static readonly string[] CrossLanguageProviders = ["Depeche", "WMCO"];

    [TestMethod]
    public void BuildSeparatesExclusiveOrderSensitiveAndComposableFindings()
    {
        ModSourceInventory inventory = new(
            [new RedScriptSource("Alpha", "alpha.reds", "@replaceMethod(DamageSystem)\npublic func ProcessHit() -> Void {}"), new RedScriptSource("Beta", "beta.reds", "@replaceMethod(DamageSystem)\npublic func ProcessHit() -> Void {}")],
            [new LuaSource("Alpha", "alpha.lua", "Observe('PlayerPuppet', 'OnAction', function() end)"), new LuaSource("Beta", "beta.lua", "Observe('PlayerPuppet', 'OnAction', function() end)")],
            [new TweakSource("Alpha", "alpha.yaml", "Items.Base_HMG:\n  value: 1.0"), new TweakSource("Beta", "beta.yaml", "Items.Base_HMG:\n  value: 2.0")],
            []);

        InteractionFinding[] findings = InteractionReportBuilder.Build(inventory);

        Assert.AreEqual(InteractionFindingKind.Composable, findings.Single(value => value.Target == "DamageSystem.ProcessHit()").Kind);
        Assert.AreEqual(InteractionFindingKind.Review, findings.Single(value => value.Target == "PlayerPuppet.OnAction").Kind);
        Assert.AreEqual(InteractionFindingKind.Review, findings.Single(value => value.Target == "Items.Base_HMG.value").Kind);
    }

    [TestMethod]
    public void BuildTreatsContinuingWrappersAsComposableAndEarlyReturnAsReview()
    {
        ModSourceInventory inventory = new(
            [
                new RedScriptSource("Alpha", "alpha.reds", "@wrapMethod(DamageSystem)\npublic func ProcessHit() -> Void { wrappedMethod(); }\n@wrapMethod(DamageSystem)\npublic func ProcessValue() -> Void { if true { return; }; wrappedMethod(); }"),
                new RedScriptSource("Beta", "beta.reds", "@wrapMethod(DamageSystem)\npublic func ProcessHit() -> Void { wrappedMethod(); }\n@wrapMethod(DamageSystem)\npublic func ProcessValue() -> Void { wrappedMethod(); }")
            ], [], [], []);

        InteractionFinding[] findings = InteractionReportBuilder.Build(inventory);

        Assert.AreEqual(InteractionFindingKind.Composable, findings.Single(value => value.Target == "DamageSystem.ProcessHit()").Kind);
        Assert.AreEqual(InteractionFindingKind.Review, findings.Single(value => value.Target == "DamageSystem.ProcessValue()").Kind);
    }

    [TestMethod]
    public void BuildRequiresReviewWhenAnOverrideCanSuppressTheUnderlyingCall()
    {
        ModSourceInventory inventory = new(
            [],
            [
                new LuaSource("Alpha", "alpha.lua", "Override('SettingsMainGameController', 'PopulateCategorySettingsOptions', function(this, fromMods, wrapped) if fromMods then return end return wrapped(fromMods) end)"),
                new LuaSource("Beta", "beta.lua", "ObserveAfter('SettingsMainGameController', 'PopulateCategorySettingsOptions', function() end)")
            ],
            [],
            []);

        InteractionFinding finding = InteractionReportBuilder.Build(inventory).Single();

        Assert.AreEqual(InteractionFindingKind.Review, finding.Kind);
        StringAssert.Contains(finding.Summary, "does not establish");
    }

    [TestMethod]
    public void BuildStatesTheDefinedCetChainAndUnknownCrossModRegistrationOrder()
    {
        ModSourceInventory inventory = new(
            [],
            [
                new LuaSource("Alpha", "alpha.lua", "Override('DamageSystem', 'ProcessHit', function(this, wrapped) return wrapped() end)"),
                new LuaSource("Beta", "beta.lua", "Override('DamageSystem', 'ProcessHit', function(this, wrapped) return wrapped() end)")
            ],
            [],
            []);

        InteractionFinding finding = InteractionReportBuilder.Build(inventory).Single();

        Assert.AreEqual(InteractionFindingKind.Review, finding.Kind);
        StringAssert.Contains(finding.Summary, "does not establish");
    }

    [TestMethod]
    public void BuildConnectsACetOverrideToARedScriptMethodAddedByAnotherProvider()
    {
        ModSourceInventory inventory = new(
            [new RedScriptSource("Depeche", "depeche.reds", "@addMethod(InventoryItemModeLogicController)\npublic func OnReplacePartNotificationClosed(data: ref<inkGameNotificationData>) -> Bool { return true; }")],
            [new LuaSource("WMCO", "quarantine.lua", "Override('InventoryItemModeLogicController', 'OnReplacePartNotificationClosed', function(this, data, wrapped) return wrapped(data) end)")],
            [],
            []);

        InteractionFinding finding = InteractionReportBuilder.Build(inventory).Single();

        Assert.AreEqual(InteractionFindingKind.Informational, finding.Kind);
        Assert.AreEqual("InventoryItemModeLogicController.OnReplacePartNotificationClosed(ref<inkGameNotificationData>)", finding.Target);
        CollectionAssert.AreEquivalent(CrossLanguageProviders, finding.Providers);
    }
}

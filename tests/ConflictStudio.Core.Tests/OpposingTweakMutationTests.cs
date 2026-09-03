using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class OpposingTweakMutationTests
{
    [TestMethod]
    [DataRow("[!remove Items.Selected, !append-once Items.Selected]", TweakOverlapKind.Redundant)]
    [DataRow("[!append-once Items.Selected]", TweakOverlapKind.ComposableMutation)]
    [DataRow("[!remove Items.Selected]", TweakOverlapKind.OpposingMutation)]
    public void NormalizationIsNotAnOpposingRemovalUnlessAnotherComponentRequestsAbsence(string other, TweakOverlapKind expected)
    {
        TweakSource[] sources = [
            new("Alpha", "normalize.yaml", "Items.Test.tags: [!remove Items.Selected, !append-once Items.Selected]"),
            new("Beta", "other.yaml", "Items.Test.tags: " + other)];

        Assert.AreEqual(expected, TweakInteractionAnalyzer.Analyze(sources).Single().Kind);
    }

    [TestMethod]
    [DataRow("Vendors.cct_dtn_guns_01.itemStock")]
    [DataRow("Vendors.hey_gle_gunsmith_01.itemStock")]
    [DataRow("Vendors.hey_rey_gunsmith_01.itemStock")]
    [DataRow("Vendors.hey_spr_gunsmith_01.itemStock")]
    [DataRow("Vendors.pac_wwd_gunsmith_01.itemStock")]
    [DataRow("Vendors.std_arr_gunsmith_01.itemStock")]
    [DataRow("Vendors.std_rcr_gunsmith_01.itemStock")]
    [DataRow("Vendors.wat_kab_gunsmith_01.itemStock")]
    [DataRow("Vendors.wat_kab_gunsmith_02.itemStock")]
    [DataRow("Vendors.wat_lch_gunsmith_01.itemStock")]
    [DataRow("Vendors.wat_nid_gunsmith_01.itemStock")]
    [DataRow("Vendors.wbr_jpn_gunsmith_01.itemStock")]
    public void SeparateStockComponentsRetainAuditedVendorOpposition(string target)
    {
        TweakSource[] sources =
        [
            new("Alpha", "B23R_Suppressor.yaml", target + ":\n  - !append Items.b23_supp_stock"),
            new("ALPHA", "Gravitazer.yaml", target + ":\n  - !append Items.grav_stock"),
            new("Alpha", "zzz_strip_last/zzz_CB_StripVanillaRecipes.yaml", target + ":\n  - !remove Items.b23_supp_stock\n  - !remove Items.grav_stock\n  - !remove Items.Other"),
            new("Beta", "other.yaml", target + ": [!append Items.Unrelated]")
        ];

        TweakAnalysisResult result = TweakInteractionAnalyzer.AnalyzeDetailed(sources);

        Assert.IsEmpty(result.Failures);
        TweakOverlap overlap = result.Overlaps.Single();
        Assert.AreEqual(target, overlap.Target);
        Assert.AreEqual("OpposingMutation", overlap.Kind.ToString());
        string[] values = ["Items.b23_supp_stock", "Items.grav_stock", "Items.b23_supp_stock", "Items.grav_stock"];
        string[] files = ["B23R_Suppressor.yaml", "Gravitazer.yaml", "zzz_strip_last/zzz_CB_StripVanillaRecipes.yaml", "zzz_strip_last/zzz_CB_StripVanillaRecipes.yaml"];
        CollectionAssert.AreEqual(values, overlap.Operations.Select(value => value.Value).ToArray());
        CollectionAssert.AreEqual(files, overlap.Operations.Select(value => value.FilePath).ToArray());
        int[] lines = [2, 2, 2, 3];
        CollectionAssert.AreEqual(lines, overlap.Operations.Select(value => value.LineNumber).ToArray());
        Assert.AreEqual("OpposingMutation", TweakInteractionAnalyzer.Analyze(sources[..3]).Single().Kind.ToString());
    }

    [TestMethod]
    [DataRow("!append")]
    [DataRow("!append-once")]
    [DataRow("!prepend")]
    [DataRow("!prepend-once")]
    public void DifferentProvidersWithTheSameFileNameStillOpposeMembership(string addition)
    {
        TweakSource[] sources =
        [
            new("Alpha", "stock.yaml", "Vendors.Test.itemStock: [!remove Items.Stock]"),
            new("Beta", "stock.yaml", "Vendors.Test.itemStock: [" + addition + " Items.Stock]")
        ];

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual("OpposingMutation", overlap.Kind.ToString());
        Assert.AreEqual(2, overlap.Operations.Length);
    }

    [TestMethod]
    [DataRow("Items.MantisBladesRPGStats.statModifiers")]
    [DataRow("Items.Nano_Wires_RPG_Stats.statModifiers")]
    public void OneComponentSelectingArmorPenetrationDoesNotOpposeUnrelatedBonuses(string target)
    {
        TweakSource[] sources =
        [
            new("Alpha", "armor_pen.yaml", target + ":\n  - !remove Items.ArmorPenLow\n  - !remove Items.ArmorPenMedium\n  - !remove Items.ArmorPenHigh\n  - !append Items.ArmorPenMedium"),
            new("Beta", "bonuses.yaml", target + ": [!append Items.UnarmoredBonus]")
        ];

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(TweakOverlapKind.ComposableMutation, overlap.Kind);
        Assert.AreEqual(5, overlap.Operations.Length);
        Assert.IsEmpty(TweakInteractionAnalyzer.Analyze(sources[..1]));
    }

    [TestMethod]
    [DataRow("!append-from")]
    [DataRow("!prepend-from")]
    public void ArrayCopySourceIsNotAnExplicitMembershipAddition(string addition)
    {
        TweakSource[] sources =
        [
            new("Alpha", "one.yaml", "Items.Test.tags: [!remove Items.Source.tags]"),
            new("Beta", "two.yaml", "Items.Test.tags: [" + addition + " Items.Source.tags]")
        ];

        Assert.AreEqual(TweakOverlapKind.ComposableMutation, TweakInteractionAnalyzer.Analyze(sources).Single().Kind);
    }

    [TestMethod]
    public void EqualProviderOperationSetsDoNotHideSeparateComponentOpposition()
    {
        TweakSource[] sources =
        [
            new("Alpha", "strip.yaml", "Vendors.Test.itemStock: [!remove Items.Stock]"),
            new("Alpha", "add.yaml", "Vendors.Test.itemStock: [!append-once Items.Stock]"),
            new("Beta", "strip.yaml", "Vendors.Test.itemStock: [!remove Items.Stock]"),
            new("Beta", "add.yaml", "Vendors.Test.itemStock: [!append-once Items.Stock]")
        ];

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual("OpposingMutation", overlap.Kind.ToString());
        Assert.AreEqual(4, overlap.Operations.Length);
    }

    [TestMethod]
    public void MembershipOppositionRetainsOtherCompetingOperationsOnTheSameArray()
    {
        TweakSource[] sources =
        [
            new("Alpha", "one.yaml", "Items.Test.tags: [!remove Items.Stock, !append Items.Duplicate, !append Items.Guarded, !append Items.Unrelated]"),
            new("Beta", "two.yaml", "Items.Test.tags: [!append Items.Stock, !append Items.Duplicate, !append-once Items.Guarded]")
        ];

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual("OpposingMutation", overlap.Kind.ToString());
        string[] values = ["Items.Stock", "Items.Duplicate", "Items.Guarded", "Items.Stock", "Items.Duplicate", "Items.Guarded"];
        CollectionAssert.AreEqual(values, overlap.Operations.Select(value => value.Value).ToArray());
        Assert.AreEqual(3, overlap.Operations.Count(value => value.Provider == "Alpha"));
        Assert.AreEqual(3, overlap.Operations.Count(value => value.Provider == "Beta"));
    }

    [TestMethod]
    [DataRow("[Items.First]", "[Items.Second]", TweakOverlapKind.MixedArrayOperations, false)]
    [DataRow("[Items.First]", "[Items.Second]", TweakOverlapKind.MixedArrayOperations, true)]
    [DataRow("1", "2", TweakOverlapKind.ScalarOverwrite, false)]
    [DataRow("1", "2", TweakOverlapKind.ScalarOverwrite, true)]
    public void StrongerAssignmentsRetainOpposingMutationEvidence(string first, string second, TweakOverlapKind expected, bool sameProvider)
    {
        TweakSource[] sources =
        [
            new("Alpha", "one.yaml", "Items.Test.tags: " + first),
            new(sameProvider ? "Alpha" : "Beta", "two.yaml", "Items.Test.tags: " + second),
            new("Alpha", "strip.yaml", "Items.Test.tags: [!remove Items.Stock]"),
            new("Alpha", "add.yaml", "Items.Test.tags: [!append Items.Stock]")
        ];

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(expected, overlap.Kind);
        Assert.AreEqual(4, overlap.Operations.Length);
    }
}

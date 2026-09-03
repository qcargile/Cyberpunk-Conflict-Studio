using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class TweakSourceOwnershipTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ReservedAttributesAreNotFlatsButConstructionAndInstancesRemain(bool loose)
    {
        string prefix = loose ? "Items.Other.value: 1\nItems.Other.value: 1\n" : "";
        TweakSource source = new("Alpha", "one.yaml", prefix + """
            Vendors.Test${tier}:
              $instances:
                - {tier: Rare}
              $type: Vendor
              $base: Vendors.Base$(tier)
              $dlc: EP1
              $unknown: [Items.NotAFlat]
              $metadata: {value: 3}
              itemStock: [!append Items.Stock$(tier)]
              nested:
                $type: Vendor
                $dlc: EP1
                $unknown: true
                value: 2
            """);

        TweakAnalysisResult result = TweakInteractionAnalyzer.AnalyzeDetailed([source]);

        Assert.IsEmpty(result.Failures);
        Assert.IsEmpty(result.Overlaps);
        TweakOperation[] operations = result.Operations.Where(value => value.Target.StartsWith("Vendors.", StringComparison.Ordinal)).ToArray();
        string[] targets = ["Vendors.TestRare.$definition", "Vendors.TestRare.itemStock", "Vendors.TestRare.nested.$definition", "Vendors.TestRare.nested.value"];
        string[] values = ["Vendors.BaseRare", "Items.StockRare", "Vendor", "2"];
        CollectionAssert.AreEqual(targets, operations.Select(value => value.Target).ToArray());
        CollectionAssert.AreEqual(values, operations.Select(value => value.Value).ToArray());
        CollectionAssert.AreEqual(new[] { TweakOperationKind.BaseDeclaration, TweakOperationKind.ArrayAppend, TweakOperationKind.TypeDeclaration, TweakOperationKind.ScalarAssignment }, operations.Select(value => value.Kind).ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void BraceAndParenthesisTemplatesKeepDistinctInstanceTargets(bool loose)
    {
        string prefix = loose ? "Items.Other.value: 1\nItems.Other.value: 1\n" : "";
        TweakSource source = new("Alpha", "one.yaml", prefix + "Items.Mod${rarity}:\n  $instances:\n    - {rarity: Rare}\n    - {rarity: Epic}\n  quality: Quality.$(rarity)\n  sibling: Items.Other${rarity}\n");

        TweakAnalysisResult result = TweakInteractionAnalyzer.AnalyzeDetailed([source]);

        Assert.IsEmpty(result.Failures);
        Assert.IsEmpty(result.Overlaps);
        TweakOperation[] operations = result.Operations.Where(value => value.Target.StartsWith("Items.Mod", StringComparison.Ordinal)).ToArray();
        string[] targets = ["Items.ModRare.quality", "Items.ModRare.sibling", "Items.ModEpic.quality", "Items.ModEpic.sibling"];
        string[] values = ["Quality.Rare", "Items.OtherRare", "Quality.Epic", "Items.OtherEpic"];
        CollectionAssert.AreEqual(targets, operations.Select(value => value.Target).ToArray());
        CollectionAssert.AreEqual(values, operations.Select(value => value.Value).ToArray());
    }

    [TestMethod]
    [DataRow("Items.Test:\n  value: 1\n  value: 2\n", "Items.Test.value", "2", 3)]
    [DataRow("Items.Test:\n  tags: []\n  tags: [Items.Use]\n", "Items.Test.tags", "[Items.Use]", 3)]
    [DataRow("Items.Test:\n  nested:\n    $base: Items.Base\n    value: 1\n    value: 2\n", "Items.Test.nested.value", "2", 5)]
    [DataRow("Items.Test$(tier):\n  $instances:\n    - {tier: Rare}\n  value: 1\n  value: 2\n", "Items.TestRare.value", "2", 5)]
    public void DuplicateMappingAssignmentsKeepLastValueAndSourceLine(string text, string target, string expected, int line)
    {
        TweakAnalysisResult result = TweakInteractionAnalyzer.AnalyzeDetailed([new("Alpha", "one.yaml", text)]);

        Assert.IsEmpty(result.Failures);
        Assert.IsEmpty(result.Overlaps);
        TweakOperation operation = result.Operations.Single(value => value.Target == target);
        Assert.AreEqual(expected, operation.Value);
        Assert.AreEqual(line, operation.LineNumber);
    }

    [TestMethod]
    public void SupersededMappingValueDoesNotCompeteWithAnotherFile()
    {
        TweakSource[] sources =
        [
            new("Alpha", "one.yaml", "Items.Test:\n  value: 1\n  value: 2"),
            new("Alpha", "two.yaml", "Items.Test.value: 2")
        ];

        TweakAnalysisResult result = TweakInteractionAnalyzer.AnalyzeDetailed(sources);

        Assert.IsEmpty(result.Overlaps);
        string[] expected = ["2", "2"];
        CollectionAssert.AreEqual(expected, result.Operations.Select(value => value.Value).ToArray());
    }

    [TestMethod]
    public void MappingAssignmentsDoNotDiscardOrderedMutations()
    {
        TweakSource source = new("Alpha", "one.yaml", "Items.Test:\n  tags: [Items.A]\n  tags: [!append Items.C]\n  tags: [Items.B]\n  tags: [!prepend Items.D]\n");

        TweakAnalysisResult result = TweakInteractionAnalyzer.AnalyzeDetailed([source]);

        Assert.IsEmpty(result.Failures);
        Assert.IsEmpty(result.Overlaps);
        CollectionAssert.AreEqual(new[] { TweakOperationKind.ArrayAppend, TweakOperationKind.ArrayReplacement, TweakOperationKind.ArrayPrepend }, result.Operations.Select(value => value.Kind).ToArray());
        string[] expected = ["Items.C", "[Items.B]", "Items.D"];
        CollectionAssert.AreEqual(expected, result.Operations.Select(value => value.Value).ToArray());
    }

    [TestMethod]
    [DataRow("[10, 10, 5, 30]")]
    [DataRow("[!append 10, !append 10, !append 5, !append 30]")]
    public void RepeatedPositionalValuesWithinOneProviderAreNotDuplicateConflicts(string values)
    {
        TweakAnalysisResult result = TweakInteractionAnalyzer.AnalyzeDetailed([new("Alpha", "one.yaml", "Items.Tooltip.floatValues: " + values)]);

        Assert.IsEmpty(result.Failures);
        Assert.IsEmpty(result.Overlaps);
        Assert.AreEqual(values.Contains('!') ? 4 : 1, result.Operations.Length);
    }

    [TestMethod]
    [DataRow("DynamicVehicleData.AnimalsRare4")]
    [DataRow("DynamicVehicleData.TygerBikeRangedNormal1")]
    [DataRow("DynamicVehicleData.TygerCarWeakNormal5")]
    [DataRow("DynamicVehicleData.ValentinoWeakBike5")]
    public void RepeatedTopLevelSpawnDefinitionsRemainContext(string target)
    {
        TweakSource source = new("Alpha", "one.yaml", target + ":\n  unitRecordsPool: [{character: Character.One, weight: 1}]\n" + target + ":\n  unitRecordsPool: [{character: Character.Two, weight: 1}]\n");

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze([source]).Single();

        Assert.AreEqual("InternalContext", overlap.Kind.ToString());
        Assert.AreEqual(target + ".unitRecordsPool", overlap.Target);
        Assert.AreEqual(2, overlap.Operations.Length);
        int[] lines = [2, 4];
        CollectionAssert.AreEqual(lines, overlap.Operations.Select(value => value.LineNumber).ToArray());
    }

    [TestMethod]
    public void RepeatedInlineConstructionPreservesBothBasesAsContext()
    {
        TweakSource source = new("Alpha", "one.yaml", "Vehicle.Test:\n  camera:\n    $type: VehicleFPPCameraParams\n    $base: Vehicle.PorscheCamera\n  camera:\n    $base: Vehicle.CaliburnCamera\n    $type: VehicleFPPCameraParams\n");

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze([source]).Single();

        Assert.AreEqual("InternalContext", overlap.Kind.ToString());
        Assert.AreEqual("Vehicle.Test.camera.$definition", overlap.Target);
        string[] bases = ["Vehicle.PorscheCamera", "Vehicle.CaliburnCamera"];
        int[] lines = [4, 6];
        CollectionAssert.AreEqual(bases, overlap.Operations.Select(value => value.Value).ToArray());
        CollectionAssert.AreEqual(lines, overlap.Operations.Select(value => value.LineNumber).ToArray());
    }

    [TestMethod]
    public void CaseOnlyImplicitScalarsRemainContextWithoutEquivalenceClaim()
    {
        TweakSource[] sources =
        [
            new("Alpha", "one.yaml", "Character.Test.secondaryEquipment: empty"),
            new("ALPHA", "two.yaml", "Character.Test.secondaryEquipment: Empty")
        ];

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual("InternalContext", overlap.Kind.ToString());
        string[] expected = ["empty", "Empty"];
        CollectionAssert.AreEqual(expected, overlap.Operations.Select(value => value.Value).ToArray());
    }

    [TestMethod]
    [DataRow("Items.Base_chete_inline1.itemPartList", "[Items.GripOne]", "[Items.GripTwo]", TweakOverlapKind.MixedArrayOperations)]
    [DataRow("Items.vmcstats_fov.value", "15", "20", TweakOverlapKind.ScalarOverwrite)]
    [DataRow("UIIcon.porsche_Logo.atlasPartName", "porsche_gt3", "porsche", TweakOverlapKind.ScalarOverwrite)]
    [DataRow("UIIcon.porsche_Logo.atlasResourcePath", "one.inkatlas", "two.inkatlas", TweakOverlapKind.ScalarOverwrite)]
    [DataRow("Vehicle.lamborghini.enumName", "ferrari_Logo", "lamborghini_Logo", TweakOverlapKind.ScalarOverwrite)]
    [DataRow("Vehicle.manufacturer_mazda.enumName", "mazda_one", "mazda_two", TweakOverlapKind.ScalarOverwrite)]
    public void DifferentFilesRetainTheSixAssessedCompetingTargets(string target, string first, string second, TweakOverlapKind kind)
    {
        TweakSource[] sources =
        [
            new("Alpha", "one.yaml", target + ": " + first),
            new("ALPHA", "two.yaml", target + ": " + second)
        ];

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(target, overlap.Target);
        Assert.AreEqual(kind, overlap.Kind);
        string[] files = ["one.yaml", "two.yaml"];
        CollectionAssert.AreEqual(files, overlap.Operations.Select(value => value.FilePath).ToArray());
    }
}

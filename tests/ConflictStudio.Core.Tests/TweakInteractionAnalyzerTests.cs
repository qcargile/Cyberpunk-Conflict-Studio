using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class TweakInteractionAnalyzerTests
{
    [TestMethod]
    public void DirectBaseDependencyKeepsChangedPropertyAndDerivedOverrideEvidence()
    {
        TweakSource[] sources =
        [
            new("Base edit", "base.yaml", "Items.Base.value: 2\nItems.BaseExtra.noise: 3\nItems.Base.inline.noise: 4"),
            new("Clone", "clone.yaml", "Items.Child:\n  $base: Items.Base\n  value: 5\n  other: 6")
        ];

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual("Items.Child <- Items.Base", overlap.Target);
        Assert.AreEqual("BaseRecordDependency", overlap.Kind.ToString());
        string[] expected = ["Items.Child.$definition", "Items.Base.value", "Items.Child.value"];
        CollectionAssert.AreEquivalent(expected, overlap.Operations.Select(value => value.Target).ToArray());
    }

    [TestMethod]
    [DataRow("Items.Unrelated.value: 2")]
    [DataRow("Items.BaseExtra.value: 2")]
    [DataRow("Items.Base.inline.value: 2")]
    public void BaseDependencyDoesNotMatchUnrelatedOrNestedRecordProperties(string otherSource)
    {
        Assert.AreEqual(0, TweakInteractionAnalyzer.Analyze([new("Base edit", "base.yaml", otherSource), new("Clone", "clone.yaml", "Items.Child:\n  $base: Items.Base")]).Length);
    }

    [TestMethod]
    public void BaseDependencyDoesNotExpandIndirectOrInternalRelationships()
    {
        TweakSource[] sources =
        [
            new("Base edit", "base.yaml", "Items.Base.tags: [!append Items.A]"),
            new("Clone", "clone.yaml", "Items.Child:\n  $base: Items.Base\nItems.Grandchild:\n  $base: Items.Child\nItems.Internal:\n  $base: Items.Local\nItems.Local.value: 1")
        ];

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual("Items.Child <- Items.Base", overlap.Target);
        Assert.AreEqual(2, overlap.Operations.Length);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FixRoundTypeAndBaseInOneMappingUseOneEffectiveConstruction(bool loose)
    {
        string prefix = loose ? "Items.Other.value: 1\nItems.Other.value: 1\n" : "";
        TweakSource alpha = new("Alpha", "one.yaml", prefix + "Items.Test:\n  $type: Clothing\n  $base: Items.Base\n");
        TweakSource beta = new("Beta", "two.yaml", "Items.Test:\n  $base: Items.Base\n");

        Assert.AreEqual(0, TweakInteractionAnalyzer.Analyze([alpha]).Length);
        TweakAnalysisResult result = TweakInteractionAnalyzer.AnalyzeDetailed([alpha, beta]);
        Assert.AreEqual(0, result.Failures.Length);
        TweakOverlap overlap = result.Overlaps.Single();
        Assert.AreEqual(TweakOverlapKind.Redundant, overlap.Kind);
        Assert.AreEqual(2, overlap.Operations.Length);
        Assert.IsTrue(overlap.Operations.All(value => value.Kind == TweakOperationKind.BaseDeclaration && value.Value == "Items.Base"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FixRoundSeparateConstructionBlocksCompareEffectiveBases(bool loose)
    {
        string first = "Items.Test:\n  $base: Items.First\n  $type: Clothing\n";
        string second = "Items.Test:\n  $type: Clothing\n  $base: Items.Second\n";
        TweakSource[] sources = loose ? [new("Alpha", "one.yaml", first + second)] : [new("Alpha", "one.yaml", first), new("Alpha", "two.yaml", second)];

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(TweakOverlapKind.RecordDefinitionCollision, overlap.Kind);
        Assert.AreEqual(2, overlap.Operations.Length);
        string[] expectedValues = ["Items.First", "Items.Second"];
        CollectionAssert.AreEqual(expectedValues, overlap.Operations.Select(value => value.Value).ToArray());
        Assert.IsTrue(overlap.Operations.All(value => value.Kind == TweakOperationKind.BaseDeclaration));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FixRoundNestedAndInstanceConstructionUsesBaseBeforeType(bool loose)
    {
        string prefix = loose ? "Items.Other.value: 1\nItems.Other.value: 1\n" : "";
        TweakSource alpha = new("Alpha", "one.yaml", prefix + "Items.Template$(tier):\n  $instances:\n    - {tier: Rare}\n  $type: Clothing\n  $base: Items.Base$(tier)\n  nested:\n    $type: Clothing\n    $base: Items.Nested\n");
        TweakSource beta = new("Beta", "two.yaml", "Items.TemplateRare:\n  $base: Items.BaseRare\n  nested:\n    $base: Items.Nested\n");

        TweakAnalysisResult result = TweakInteractionAnalyzer.AnalyzeDetailed([alpha, beta]);

        Assert.AreEqual(0, result.Failures.Length);
        Assert.AreEqual(2, result.Overlaps.Length);
        Assert.IsTrue(result.Overlaps.All(value => value.Kind == TweakOverlapKind.Redundant && value.Operations.Length == 2));
        Assert.IsTrue(result.Overlaps.SelectMany(value => value.Operations).All(value => value.Kind == TweakOperationKind.BaseDeclaration));
    }

    [TestMethod]
    [DataRow("Items.Test.value: 1\nItems.Test.value: 2\n", "Items.Test.value", TweakOverlapKind.ScalarOverwrite)]
    [DataRow("Items.Test:\n  value: 1\n  value: 2\n", "Items.Test.value", TweakOverlapKind.ScalarOverwrite)]
    [DataRow("Items.Test.tags: [Items.A]\nItems.Test.tags: [Items.B]\n", "Items.Test.tags", TweakOverlapKind.MixedArrayOperations)]
    [DataRow("Items.Test.tags: [Items.A]\nItems.Test.tags: [Items.B]\nItems.Test.tags: [!append Items.C]\n", "Items.Test.tags", TweakOverlapKind.MixedArrayOperations)]
    [DataRow("Items.Test:\n  $base: Items.A\nItems.Test:\n  $base: Items.B\n", "Items.Test.$definition", TweakOverlapKind.RecordDefinitionCollision)]
    [DataRow("Items.Test:\n  $type: Clothing\nItems.Test:\n  $base: Items.B\n", "Items.Test.$definition", TweakOverlapKind.RecordDefinitionCollision)]
    [DataRow("Items.Test:\n  tags:\n    - !append Items.A\n    - !append Items.A\n", "Items.Test.tags", TweakOverlapKind.DuplicateMutation)]
    [DataRow("Items.Test:\n  tags:\n    - !append Items.A\n    - !prepend Items.A\n    - !append Items.B\n", "Items.Test.tags", TweakOverlapKind.DuplicateMutation)]
    [DataRow("Items.Test:\n  tags:\n    - !remove Items.A\n    - !append Items.A\n    - !append Items.A\n", "Items.Test.tags", TweakOverlapKind.DuplicateMutation)]
    public void SameProviderCompetingDeclarationsAreReported(string text, string target, TweakOverlapKind kind)
    {
        TweakAnalysisResult result = TweakInteractionAnalyzer.AnalyzeDetailed([new("Alpha", "one.yaml", text)]);

        Assert.AreEqual(0, result.Failures.Length);
        TweakOverlap overlap = result.Overlaps.Single();
        Assert.AreEqual(target, overlap.Target);
        Assert.AreEqual(kind, overlap.Kind);
        Assert.AreEqual(2, overlap.Operations.Length);
        Assert.IsTrue(overlap.Operations.All(value => value.Provider == "Alpha" && value.FilePath == "one.yaml" && value.LineNumber > 0));
    }

    [TestMethod]
    [DataRow("Items.Test.value: 1\nItems.Test.value: 1\n")]
    [DataRow("Items.Test.tags: [Items.A]\nItems.Test.tags: [Items.A]\n")]
    [DataRow("Items.Test:\n  $base: Items.A\nItems.Test:\n  $base: Items.A\n")]
    [DataRow("Items.Test:\n  tags:\n    - !append Items.A\n    - !append Items.B\n")]
    [DataRow("Items.Test:\n  tags:\n    - !append-once Items.A\n    - !append-once Items.A\n")]
    [DataRow("Items.Test:\n  tags:\n    - !prepend-once Items.A\n    - !append-once Items.A\n")]
    [DataRow("Items.Test:\n  tags:\n    - !remove Items.A\n    - !append Items.A\n")]
    [DataRow("Items.Test.tags: [Items.A]\nItems.Test.tags: [!append Items.B]\n")]
    [DataRow("Items.Test:\n  tags:\n    - !append Items.A\n    - !append-once Items.A\n")]
    public void SameProviderIntentionalTweakChainsStayOutOfReport(string text)
    {
        TweakAnalysisResult result = TweakInteractionAnalyzer.AnalyzeDetailed([new("Alpha", "one.yaml", text)]);

        Assert.AreEqual(0, result.Failures.Length);
        Assert.AreEqual(0, result.Overlaps.Length);
    }

    [TestMethod]
    public void SameProviderAssignmentsAcrossFilesUseCaseInsensitiveOwnership()
    {
        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze([new("Alpha", "one.yaml", "Items.Test.value: 1"), new("ALPHA", "two.yaml", "Items.Test.value: 2")]).Single();

        Assert.AreEqual(TweakOverlapKind.ScalarOverwrite, overlap.Kind);
        string[] expectedFiles = ["one.yaml", "two.yaml"];
        CollectionAssert.AreEqual(expectedFiles, overlap.Operations.Select(value => value.FilePath).ToArray());
    }

    [TestMethod]
    public void AnalyzerNormalizesRecordMetadataScalarArraysAndInlineRecords()
    {
        TweakSource[] sources =
        [
            new("Alpha", "alpha.yaml", "Items.Test:\n  $type: gamedataItem_Record\n  $base: Items.Base\n  value: 2\n  tags: [Items.A, Items.B]\n  inline:\n    $type: gamedataFoo_Record\n    enabled: true\n"),
            new("Beta", "beta.yaml", "Items.Test:\n  $type: \"gamedataItem_Record\"\n  $base: 'Items.Base'\n  value: 2\n  tags:\n    - Items.A\n    - Items.B\n  inline: { enabled: true, $type: gamedataFoo_Record }\n")
        ];

        TweakOverlap[] overlaps = TweakInteractionAnalyzer.Analyze(sources);

        TweakOverlap definition = overlaps.Single(value => value.Target == "Items.Test.$definition");
        Assert.AreEqual(2, definition.Operations.Length);
        Assert.IsTrue(definition.Operations.All(value => value.Kind == TweakOperationKind.BaseDeclaration));
        Assert.AreEqual(TweakOperationKind.ScalarAssignment, Operation(overlaps, "Items.Test.value").Kind);
        Assert.AreEqual(TweakOperationKind.ArrayReplacement, Operation(overlaps, "Items.Test.tags").Kind);
        Assert.AreEqual(TweakOperationKind.TypeDeclaration, Operation(overlaps, "Items.Test.inline.$definition").Kind);
        Assert.AreEqual(TweakOperationKind.ScalarAssignment, Operation(overlaps, "Items.Test.inline.enabled").Kind);
        Assert.IsTrue(overlaps.All(value => value.Kind == TweakOverlapKind.Redundant));
    }

    [TestMethod]
    public void AnalyzerNormalizesPerItemMutationVariants()
    {
        TweakSource[] sources =
        [
            new("Alpha", "alpha.yaml", "Items.Test:\n  packages:\n    - !append Items.A\n  tags:\n    - !prepend Items.B\n    - !remove Items.C\n"),
            new("Beta", "beta.yaml", "Items.Test:\n  packages:\n    - !append Items.D\n  tags:\n    - !prepend Items.E\n    - !remove Items.F\n")
        ];

        TweakOverlap[] overlaps = TweakInteractionAnalyzer.Analyze(sources);

        Assert.AreEqual(TweakOverlapKind.ComposableMutation, overlaps.Single(value => value.Target == "Items.Test.packages").Kind);
        Assert.AreEqual(TweakOverlapKind.ComposableMutation, overlaps.Single(value => value.Target == "Items.Test.tags").Kind);
        CollectionAssert.AreEquivalent(
            new[] { TweakOperationKind.ArrayAppend, TweakOperationKind.ArrayPrepend, TweakOperationKind.ArrayRemove },
            overlaps.SelectMany(value => value.Operations).Select(value => value.Kind).Distinct().ToArray());
    }

    [TestMethod]
    public void SequenceNodeMutationTagRemainsAnArrayReplacement()
    {
        TweakSource alpha = new("Alpha", "alpha.yaml", "Items.Test:\n  packages: !append [Items.A]\n");
        TweakSource beta = new("Beta", "beta.yaml", "Items.Test:\n  packages: [Items.B]\n");

        TweakOperation operation = TweakInteractionAnalyzer.Analyze([alpha, beta]).Single().Operations.Single(value => value.Provider == "Alpha");

        Assert.AreEqual(TweakOperationKind.ArrayReplacement, operation.Kind);
        Assert.IsFalse(operation.IsMutation);
    }

    [TestMethod]
    public void MixedSequenceKeepsOnlyMutationsAndReportsTweakXlWarning()
    {
        TweakSource alpha = new("Alpha", "alpha.yaml", "Items.Test:\n  packages:\n    - Items.Assigned\n    - !append Items.Mutated\n");
        TweakSource beta = new("Beta", "beta.yaml", "Items.Test:\n  packages:\n    - !append Items.Other\n");

        TweakAnalysisResult result = TweakInteractionAnalyzer.AnalyzeDetailed([alpha, beta]);

        TweakOperation[] alphaOperations = result.Overlaps.Single().Operations.Where(value => value.Provider == "Alpha").ToArray();
        Assert.AreEqual(1, alphaOperations.Length);
        Assert.AreEqual(TweakOperationKind.ArrayAppend, alphaOperations[0].Kind);
        SourceAnalysisFailure failure = result.Failures.Single();
        Assert.AreEqual("TweakXL", failure.Surface);
        Assert.AreEqual("Items.Test.packages: Mixed definition of array replacement and mutations. Only mutations will take effect.", failure.Message);
    }

    [TestMethod]
    public void InstanceMixedSequenceWarningsUseConcreteTargetsAcrossYamlReaders()
    {
        TweakSource normal = new("Normal", "normal.yaml", "Items.Normal$(tier):\n  $instances:\n    - {tier: Rare}\n    - {tier: Epic}\n  packages:\n    - Items.Assigned\n    - !append Items.Normal$(tier)\n");
        TweakSource repeated = new("Repeated", "repeated.yaml", "Items.Repeated.value: 1\nItems.Repeated.value: 2\nItems.Loose$(tier):\n  $instances:\n    - {tier: Rare}\n    - {tier: Epic}\n  packages:\n    - Items.Assigned\n    - !append Items.Loose$(tier)\n");
        TweakSource comparison = new("Comparison", "comparison.yaml", "Items.NormalRare:\n  packages:\n    - !append Items.Other\nItems.NormalEpic:\n  packages:\n    - !append Items.Other\nItems.LooseRare:\n  packages:\n    - !append Items.Other\nItems.LooseEpic:\n  packages:\n    - !append Items.Other\n");

        TweakAnalysisResult result = TweakInteractionAnalyzer.AnalyzeDetailed([normal, repeated, comparison]);

        Assert.AreEqual(4, result.Failures.Length);
        string[] expectedMessages =
        [
            "Items.NormalRare.packages: Mixed definition of array replacement and mutations. Only mutations will take effect.",
            "Items.NormalEpic.packages: Mixed definition of array replacement and mutations. Only mutations will take effect.",
            "Items.LooseRare.packages: Mixed definition of array replacement and mutations. Only mutations will take effect.",
            "Items.LooseEpic.packages: Mixed definition of array replacement and mutations. Only mutations will take effect."
        ];
        CollectionAssert.AreEqual(expectedMessages, result.Failures.Select(value => value.Message).ToArray());
        Assert.IsTrue(result.Failures.Take(2).All(value => value.Provider == "Normal" && value.FilePath == "normal.yaml"));
        Assert.IsTrue(result.Failures.Skip(2).All(value => value.Provider == "Repeated" && value.FilePath == "repeated.yaml"));
        TweakOperation[] normalOperations = result.Overlaps.SelectMany(value => value.Operations).Where(value => value.Provider == "Normal").ToArray();
        TweakOperation[] repeatedOperations = result.Overlaps.Where(value => value.Target.StartsWith("Items.Loose", StringComparison.Ordinal)).SelectMany(value => value.Operations).Where(value => value.Provider == "Repeated").ToArray();
        Assert.AreEqual(TweakOverlapKind.ScalarOverwrite, result.Overlaps.Single(value => value.Target == "Items.Repeated.value").Kind);
        Assert.AreEqual(2, normalOperations.Length);
        Assert.AreEqual(2, repeatedOperations.Length);
        Assert.IsTrue(normalOperations.All(value => value.LineNumber == 7));
        Assert.IsTrue(repeatedOperations.All(value => value.LineNumber == 9));
    }

    [TestMethod]
    public void FailedParserAttemptsDiscardMixedWarningsAndOperations()
    {
        TweakSource invalid = new("Invalid", "invalid.yaml", "Items.Test:\n  packages:\n    - Items.Assigned\n    - !append Items.Mutated\n---\n- Items.InvalidRoot\n");
        TweakSource comparison = new("Comparison", "comparison.yaml", "Items.Test:\n  packages:\n    - !append Items.Other\n");

        TweakAnalysisResult result = TweakInteractionAnalyzer.AnalyzeDetailed([invalid, comparison]);

        Assert.AreEqual(0, result.Overlaps.Length);
        Assert.AreEqual(1, result.Failures.Length);
        SourceAnalysisFailure failure = result.Failures[0];
        Assert.AreEqual("Invalid", failure.Provider);
        Assert.AreEqual("invalid.yaml", failure.FilePath);
        Assert.AreEqual("TweakXL", failure.Surface);
        StringAssert.StartsWith(failure.Message, "TweakXL source could not be represented completely:");
        StringAssert.Contains(failure.Message, "TweakXL document root must be a mapping.");
        Assert.IsFalse(result.Failures.Any(value => value.Message.Contains("Mixed definition", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void AnalyzerFlagsEquivalentPlainAppendsAcrossProvidersAsDuplicateRisk()
    {
        TweakSource[] sources =
        [
            new("Alpha", "alpha.yaml", "Items.Test:\n  packages:\n    - !append Items.A\n"),
            new("Beta", "beta.yaml", "Items.Test:\n  packages:\n    - !append Items.A\n")
        ];

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(TweakOverlapKind.DuplicateMutation, overlap.Kind);
        Assert.IsTrue(overlap.Operations.All(value => value.Value == "Items.A"));
    }

    [TestMethod]
    public void AnalyzerTreatsTopLevelScalarKeysAsFlatAssignments()
    {
        TweakSource[] sources =
        [
            new("Alpha", "alpha.yaml", "Items.Test.value: 1\n"),
            new("Beta", "beta.yaml", "Items.Test.value: 2\n")
        ];

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual("Items.Test.value", overlap.Target);
        Assert.AreEqual(TweakOperationKind.ScalarAssignment, overlap.Operations[0].Kind);
        Assert.AreEqual(TweakOverlapKind.ScalarOverwrite, overlap.Kind);
    }

    [TestMethod]
    public void AnalyzerKeepsEmptyArrayReplacementAsEvidence()
    {
        TweakSource[] sources =
        [
            new("Alpha", "alpha.yaml", "Items.Test:\n  tags: []\n"),
            new("Beta", "beta.yaml", "Items.Test:\n  tags: [Items.A]\n")
        ];

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(TweakOverlapKind.MixedArrayOperations, overlap.Kind);
        Assert.AreEqual("[]", overlap.Operations.Single(value => value.Provider == "Alpha").Value);
    }

    [TestMethod]
    public void AnalyzerRetainsProviderFileAndLineForEveryOperation()
    {
        TweakSource[] sources =
        [
            new("Alpha", "r6/tweaks/alpha.yaml", "Items.Test:\n  value: 1\n"),
            new("Beta", "r6/tweaks/beta.yaml", "Items.Test:\n  value: 2\n")
        ];

        TweakOperation[] operations = TweakInteractionAnalyzer.Analyze(sources).Single().Operations;

        Assert.IsTrue(operations.All(value => value.Provider is "Alpha" or "Beta"));
        Assert.IsTrue(operations.All(value => value.FilePath.StartsWith("r6/tweaks/", StringComparison.Ordinal)));
        Assert.IsTrue(operations.All(value => value.LineNumber == 2));
    }

    [TestMethod]
    public void AnalyzerDoesNotClaimOverlapFromMalformedYaml()
    {
        TweakSource[] sources =
        [
            new("Alpha", "alpha.yaml", "Items.Test:\n  value: [\n"),
            new("Beta", "beta.yaml", "Items.Test:\n  value: 2\n")
        ];

        TweakOverlap[] overlaps = TweakInteractionAnalyzer.Analyze(sources);

        Assert.AreEqual(0, overlaps.Length);
    }

    [TestMethod]
    public void AnalyzerReportsMalformedYaml()
    {
        TweakAnalysisResult result = TweakInteractionAnalyzer.AnalyzeDetailed([new TweakSource("Alpha", "alpha.yaml", "Items.Test:\n  value: [\n")]);

        Assert.AreEqual(0, result.Overlaps.Length);
        Assert.AreEqual("Alpha", result.Failures.Single().Provider);
        Assert.AreEqual("TweakXL", result.Failures.Single().Surface);
    }

    [TestMethod]
    public void AnalyzerPreservesRepeatedTopLevelAndRecordProperties()
    {
        TweakSource source = new("Alpha", "alpha.yaml", "Items.Test.value: 1\nItems.Test.value: 2\nItems.Other:\n  value: 3\n  value: 4\n");
        TweakSource comparison = new("Beta", "beta.yaml", "Items.Test.value: 5\nItems.Other:\n  value: 6\n");

        TweakAnalysisResult result = TweakInteractionAnalyzer.AnalyzeDetailed([source, comparison]);

        Assert.AreEqual(0, result.Failures.Length);
        Assert.AreEqual(2, result.Overlaps.Length);
        Assert.IsTrue(result.Overlaps.All(value => value.Operations.Length == 3));
    }

    [TestMethod]
    public void AnalyzerPreservesAnchorsAliasesAndInstancesWhenKeysRepeat()
    {
        TweakSource alpha = new("Alpha", "alpha.yaml", "Vendors.One: &AddToVendor\n  itemStock:\n    - !append Items.A\nVendors.One: *AddToVendor\nItems.Template$(tier):\n  $instances:\n    - {tier: Rare, value: 2}\n  value: $(value)\n");
        TweakSource beta = new("Beta", "beta.yaml", "Vendors.One:\n  itemStock:\n    - !append Items.B\nItems.TemplateRare:\n  value: 3\n");

        TweakAnalysisResult result = TweakInteractionAnalyzer.AnalyzeDetailed([alpha, beta]);

        Assert.AreEqual(0, result.Failures.Length);
        Assert.IsTrue(result.Overlaps.Any(value => value.Target == "Vendors.One.itemStock"));
    }

    [TestMethod]
    public void DuplicateSafeParserConsumesEveryYamlDocument()
    {
        TweakSource alpha = new("Alpha", "alpha.yaml", "Items.One.value: 1\nItems.One.value: 2\n---\nItems.Two.value: 3\n");
        TweakSource beta = new("Beta", "beta.yaml", "Items.One.value: 4\nItems.Two.value: 5\n");

        TweakAnalysisResult result = TweakInteractionAnalyzer.AnalyzeDetailed([alpha, beta]);

        Assert.AreEqual(0, result.Failures.Length);
        Assert.IsTrue(result.Overlaps.Any(value => value.Target == "Items.One.value"));
        Assert.IsTrue(result.Overlaps.Any(value => value.Target == "Items.Two.value"));
    }

    [TestMethod]
    public void DuplicateSafeParserDoesNotResolveAnchorsAcrossDocuments()
    {
        TweakSource source = new("Alpha", "alpha.yaml", "Items.One: &Template\n  value: 1\nItems.One: *Template\n---\nItems.Two: *Template\n");

        TweakAnalysisResult result = TweakInteractionAnalyzer.AnalyzeDetailed([source]);

        Assert.AreEqual(1, result.Failures.Length);
        Assert.AreEqual(0, result.Overlaps.Length);
    }

    [TestMethod]
    public void AnalyzerExpandsInstanceTemplatesBeforeComparingProviders()
    {
        TweakSource alpha = new("Alpha", "alpha.yaml", "$(name):\n  $instances:\n    - {name: Items.Alpha, value: 1}\n  $base: Items.Base\n  amount: $(value)\n");
        TweakSource beta = new("Beta", "beta.yaml", "$(name):\n  $instances:\n    - {name: Items.Beta, value: 2}\n  $base: Items.Base\n  amount: $(value)\n");

        TweakOverlap[] overlaps = TweakInteractionAnalyzer.Analyze([alpha, beta]);

        Assert.AreEqual(0, overlaps.Length);
    }

    [TestMethod]
    public void AnalyzerDoesNotSuppressUnknownTaggedValues()
    {
        TweakSource[] sources =
        [
            new("Alpha", "alpha.yaml", "Items.Test:\n  value: !custom Items.A\n"),
            new("Beta", "beta.yaml", "Items.Test:\n  value: Items.A\n")
        ];

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze(sources).Single();

        Assert.AreEqual(TweakOverlapKind.ScalarOverwrite, overlap.Kind);
    }

    [TestMethod]
    public void AnalyzerUsesMutationPhasesAndFlagsPlainPlusUniqueAddsForReview()
    {
        TweakSource[] opposing = [new("Alpha", "alpha.yaml", "Items.Test:\n  tags:\n    - !append Items.A\n"), new("Beta", "beta.yaml", "Items.Test:\n  tags:\n    - !remove Items.A\n")];
        TweakSource[] stateDependent = [new("Alpha", "alpha.yaml", "Items.Test:\n  tags:\n    - !append Items.A\n"), new("Beta", "beta.yaml", "Items.Test:\n  tags:\n    - !append-once Items.A\n")];

        Assert.AreEqual(TweakOverlapKind.ComposableMutation, TweakInteractionAnalyzer.Analyze(opposing).Single().Kind);
        Assert.AreEqual(TweakOverlapKind.MixedArrayOperations, TweakInteractionAnalyzer.Analyze(stateDependent).Single().Kind);
    }

    [TestMethod]
    public void RepeatedIdenticalScalarAssignmentsRemainRedundantAcrossProviders()
    {
        TweakSource alpha = new("Alpha", "alpha.yaml", "Vendors.cz_stadium_ripperdoc_01:\n  $dlc: EP1\nVendors.cz_stadium_ripperdoc_01:\n  $dlc: EP1\n");
        TweakSource beta = new("Beta", "beta.yaml", "Vendors.cz_stadium_ripperdoc_01:\n  $dlc: EP1\n");

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze([alpha, beta]).Single();

        Assert.AreEqual(TweakOverlapKind.Redundant, overlap.Kind);
    }

    [TestMethod]
    public void VendorRemoveThenAppendIsDeterministicAndKeepsRelevantOperations()
    {
        TweakSource alpha = new("Alpha", "alpha.yaml", "Vendors.Test:\n  itemStock:\n    - !append Items.Contested\n    - !append Items.AlphaOnly\n");
        TweakSource beta = new("Beta", "beta.yaml", "Vendors.Test:\n  itemStock:\n    - !remove Items.Contested\n    - !append Items.BetaOnly\n");

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze([alpha, beta]).Single();

        Assert.AreEqual(TweakOverlapKind.ComposableMutation, overlap.Kind);
        Assert.AreEqual(4, overlap.Operations.Length);
    }

    [TestMethod]
    public void AppendAndPrependOfTheSameValueAreDuplicateAddsNotOpposingOperations()
    {
        TweakSource alpha = new("Alpha", "alpha.yaml", "Items.Test:\n  tags:\n    - !append Items.A\n");
        TweakSource beta = new("Beta", "beta.yaml", "Items.Test:\n  tags:\n    - !prepend Items.A\n");

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze([alpha, beta]).Single();

        Assert.AreEqual(TweakOverlapKind.DuplicateMutation, overlap.Kind);
    }

    [TestMethod]
    public void AnalyzerRecognizesDocumentedArrayCopyMutations()
    {
        TweakSource alpha = new("Alpha", "alpha.yaml", "Items.Test:\n  attacks:\n    - !append-from Items.SourceA.attacks\n");
        TweakSource beta = new("Beta", "beta.yaml", "Items.Test:\n  attacks:\n    - !prepend-from Items.SourceB.attacks\n");

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze([alpha, beta]).Single();

        Assert.AreEqual(TweakOverlapKind.ComposableMutation, overlap.Kind);
        CollectionAssert.AreEquivalent(new[] { TweakOperationKind.ArrayAppendFrom, TweakOperationKind.ArrayPrependFrom }, overlap.Operations.Select(value => value.Kind).ToArray());
        Assert.IsTrue(overlap.Operations.All(value => value.IsMutation));
    }

    [TestMethod]
    public void ArrayCopyAndMutationOnTheDestinationUseDocumentedMutationPhases()
    {
        TweakSource alpha = new("Alpha", "alpha.yaml", "Items.Test:\n  tags:\n    - !append-from Items.Source.tags\n");
        TweakSource beta = new("Beta", "beta.yaml", "Items.Test:\n  tags:\n    - !remove Items.A\n");

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze([alpha, beta]).Single();

        Assert.AreEqual(TweakOverlapKind.ComposableMutation, overlap.Kind);
    }

    [TestMethod]
    public void ArrayCopyDependsOnOperationsAppliedToItsSourceFlat()
    {
        TweakSource alpha = new("Alpha", "alpha.yaml", "Items.Target:\n  tags:\n    - !append-from Items.Source.tags\n");
        TweakSource beta = new("Beta", "beta.yaml", "Items.Source:\n  tags:\n    - !append Items.NewTag\n");

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze([alpha, beta]).Single();

        Assert.AreEqual("Items.Target.tags <- Items.Source.tags", overlap.Target);
        Assert.AreEqual(TweakOverlapKind.SourceArrayDependency, overlap.Kind);
    }

    [TestMethod]
    public void InstancesExpandWithoutAPlaceholderInTheTemplateName()
    {
        TweakSource alpha = new("Alpha", "alpha.yaml", "Items.Template:\n  $instances:\n    - {value: 1}\n  amount: $(value)\n");
        TweakSource beta = new("Beta", "beta.yaml", "Items.Template:\n  amount: 2\n");

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze([alpha, beta]).Single();

        Assert.AreEqual("Items.Template.amount", overlap.Target);
        Assert.AreEqual(TweakOverlapKind.ScalarOverwrite, overlap.Kind);
    }

    [TestMethod]
    public void InlineRecordDefinitionsUseRecordConstructionSemantics()
    {
        TweakSource alpha = new("Alpha", "alpha.yaml", "Items.Owner:\n  nested:\n    $type: gamedataItem_Record\n    value: 1\n");
        TweakSource beta = new("Beta", "beta.yaml", "Items.Owner:\n  nested:\n    $base: Items.Base\n    value: 1\n");

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze([alpha, beta]).Single(value => value.Target == "Items.Owner.nested.$definition");

        Assert.AreEqual(TweakOverlapKind.RecordDefinitionCollision, overlap.Kind);
    }

    [TestMethod]
    public void UntypedMappingsDoNotClaimDisjointFieldsCompose()
    {
        TweakSource alpha = new("Alpha", "alpha.yaml", "Items.Owner:\n  nested:\n    alpha: 1\n    shared: 10\n");
        TweakSource beta = new("Beta", "beta.yaml", "Items.Owner:\n  nested:\n    beta: 2\n    shared: 20\n");

        TweakOverlap[] overlaps = TweakInteractionAnalyzer.Analyze([alpha, beta]);

        TweakOverlap overlap = overlaps.Single();
        Assert.AreEqual("Items.Owner.nested", overlap.Target);
        Assert.AreEqual(TweakOverlapKind.ScalarOverwrite, overlap.Kind);
    }

    [TestMethod]
    public void NonMappingRootAndDuplicateInstanceVariablesBecomeNamedFailures()
    {
        TweakSource sequenceRoot = new("Sequence", "sequence.yaml", "- Items.One\n- Items.Two\n");
        TweakSource duplicateVariable = new("Duplicate", "duplicate.yaml", "Items.Template$(name):\n  $instances:\n    - {name: One, name: Two}\n  value: 1\n");

        TweakAnalysisResult result = TweakInteractionAnalyzer.AnalyzeDetailed([sequenceRoot, duplicateVariable]);

        Assert.AreEqual(0, result.Overlaps.Length);
        Assert.AreEqual(2, result.Failures.Length);
        Assert.IsTrue(result.Failures.Any(value => value.Provider == "Sequence"));
        Assert.IsTrue(result.Failures.Any(value => value.Provider == "Duplicate"));
    }

    [TestMethod]
    public void DifferentRecordConstructionDirectivesAreNotScalarLastWins()
    {
        TweakSource alpha = new("Alpha", "alpha.yaml", "Items.NewRecord:\n  $base: Items.TemplateA\n");
        TweakSource beta = new("Beta", "beta.yaml", "Items.NewRecord:\n  $base: Items.TemplateB\n");

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze([alpha, beta]).Single();

        Assert.AreEqual(TweakOverlapKind.RecordDefinitionCollision, overlap.Kind);
    }

    [TestMethod]
    public void TypeAndBaseOnTheSameRecordShareOneDefinitionCollision()
    {
        TweakSource alpha = new("Alpha", "alpha.yaml", "Items.NewRecord:\n  $type: Clothing\n");
        TweakSource beta = new("Beta", "beta.yaml", "Items.NewRecord:\n  $base: Items.Template\n");

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze([alpha, beta]).Single();

        Assert.AreEqual("Items.NewRecord.$definition", overlap.Target);
        Assert.AreEqual(TweakOverlapKind.RecordDefinitionCollision, overlap.Kind);
    }

    [TestMethod]
    public void RepeatedAppendOnceRemainsRedundantAcrossUnevenProviderCounts()
    {
        TweakSource alpha = new("Alpha", "alpha.yaml", "Items.Test:\n  tags:\n    - !append-once Items.A\n    - !append-once Items.A\n");
        TweakSource beta = new("Beta", "beta.yaml", "Items.Test:\n  tags:\n    - !append-once Items.A\n");

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze([alpha, beta]).Single();

        Assert.AreEqual(TweakOverlapKind.Redundant, overlap.Kind);
    }

    private static TweakOperation Operation(TweakOverlap[] overlaps, string target)
        => overlaps.Single(value => value.Target == target).Operations[0];
}

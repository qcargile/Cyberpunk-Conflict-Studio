using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class TweakInteractionAnalyzerTests
{
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
        CollectionAssert.AreEquivalent(new[] { TweakOperationKind.TypeDeclaration, TweakOperationKind.BaseDeclaration }, definition.Operations.Select(value => value.Kind).Distinct().ToArray());
        Assert.AreEqual(TweakOperationKind.ScalarAssignment, Operation(overlaps, "Items.Test.value").Kind);
        Assert.AreEqual(TweakOperationKind.ArrayReplacement, Operation(overlaps, "Items.Test.tags").Kind);
        Assert.AreEqual(TweakOperationKind.TypeDeclaration, Operation(overlaps, "Items.Test.inline.$definition").Kind);
        Assert.AreEqual(TweakOperationKind.ScalarAssignment, Operation(overlaps, "Items.Test.inline.enabled").Kind);
        Assert.IsTrue(overlaps.All(value => value.Kind == TweakOverlapKind.Redundant));
    }

    [TestMethod]
    public void AnalyzerNormalizesWholeArrayAndPerItemMutationVariants()
    {
        TweakSource[] sources =
        [
            new("Alpha", "alpha.yaml", "Items.Test:\n  packages: !append [Items.A]\n  tags:\n    - !prepend Items.B\n    - !remove Items.C\n"),
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
    public void AnalyzerFlagsEquivalentPlainAppendsAcrossSyntaxFormsAsDuplicateRisk()
    {
        TweakSource[] sources =
        [
            new("Alpha", "alpha.yaml", "Items.Test:\n  packages: !append [Items.A]\n"),
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

        Assert.AreEqual(TweakOverlapKind.ScalarOverwrite, overlap.Kind);
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
        TweakSource[] opposing = [new("Alpha", "alpha.yaml", "Items.Test:\n  tags: !append [Items.A]\n"), new("Beta", "beta.yaml", "Items.Test:\n  tags: !remove [Items.A]\n")];
        TweakSource[] stateDependent = [new("Alpha", "alpha.yaml", "Items.Test:\n  tags: !append [Items.A]\n"), new("Beta", "beta.yaml", "Items.Test:\n  tags: !append-once [Items.A]\n")];

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
        TweakSource alpha = new("Alpha", "alpha.yaml", "Items.Test:\n  tags: !append [Items.A]\n");
        TweakSource beta = new("Beta", "beta.yaml", "Items.Test:\n  tags: !prepend [Items.A]\n");

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze([alpha, beta]).Single();

        Assert.AreEqual(TweakOverlapKind.DuplicateMutation, overlap.Kind);
    }

    [TestMethod]
    public void AnalyzerRecognizesDocumentedArrayCopyMutations()
    {
        TweakSource alpha = new("Alpha", "alpha.yaml", "Items.Test:\n  attacks: !append-from Items.SourceA.attacks\n");
        TweakSource beta = new("Beta", "beta.yaml", "Items.Test:\n  attacks: !prepend-from Items.SourceB.attacks\n");

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze([alpha, beta]).Single();

        Assert.AreEqual(TweakOverlapKind.ComposableMutation, overlap.Kind);
        CollectionAssert.AreEquivalent(new[] { TweakOperationKind.ArrayAppendFrom, TweakOperationKind.ArrayPrependFrom }, overlap.Operations.Select(value => value.Kind).ToArray());
        Assert.IsTrue(overlap.Operations.All(value => value.IsMutation));
    }

    [TestMethod]
    public void ArrayCopyAndMutationOnTheDestinationUseDocumentedMutationPhases()
    {
        TweakSource alpha = new("Alpha", "alpha.yaml", "Items.Test:\n  tags: !append-from Items.Source.tags\n");
        TweakSource beta = new("Beta", "beta.yaml", "Items.Test:\n  tags: !remove Items.A\n");

        TweakOverlap overlap = TweakInteractionAnalyzer.Analyze([alpha, beta]).Single();

        Assert.AreEqual(TweakOverlapKind.ComposableMutation, overlap.Kind);
    }

    [TestMethod]
    public void ArrayCopyDependsOnOperationsAppliedToItsSourceFlat()
    {
        TweakSource alpha = new("Alpha", "alpha.yaml", "Items.Target:\n  tags: !append-from Items.Source.tags\n");
        TweakSource beta = new("Beta", "beta.yaml", "Items.Source:\n  tags: !append [Items.NewTag]\n");

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

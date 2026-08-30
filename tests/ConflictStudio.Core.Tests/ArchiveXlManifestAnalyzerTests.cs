using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class ArchiveXlManifestAnalyzerTests
{
    private static readonly string[] AlphabeticalProviderOrder = ["First Mod", "Second Mod"];
    private static readonly string[] SequencePatchTargets = ["base\\mesh.mesh", "base\\other.mesh"];

    [TestMethod]
    public void AnalyzeSeparatesMergeRegistrationsFromDirectedResourceOperations()
    {
        ArchiveXlSource source = new("Vehicle", "vehicle.xl", "factories:\n  - vehicle\\factory.csv\nresource:\n  patch:\n    vehicle\\patch.ent:\n      - base\\vehicle.ent\n  link:\n    base\\paint.mlsetup: vehicle\\paint.mlsetup\nstreaming:\n  blocks:\n    - vehicle\\sector.streamingblock\n");

        ArchiveXlOperation[] operations = ArchiveXlManifestAnalyzer.Analyze([source]);

        Assert.AreEqual(ArchiveXlOperationKind.FactoryRegistration, operations.Single(value => value.Target == "vehicle\\factory.csv").Kind);
        Assert.AreEqual(ArchiveXlOperationKind.ResourcePatch, operations.Single(value => value.Target == "base\\vehicle.ent").Kind);
        StringAssert.StartsWith(operations.Single(value => value.Target == "base\\vehicle.ent").Payload, "vehicle\\patch.ent");
        Assert.AreEqual(ArchiveXlOperationKind.ResourceLink, operations.Single(value => value.Target == "vehicle\\paint.mlsetup").Kind);
        Assert.AreEqual("base\\paint.mlsetup", operations.Single(value => value.Target == "vehicle\\paint.mlsetup").Payload);
        Assert.AreEqual(ArchiveXlOperationKind.StreamingMutation, operations.Single(value => value.Target == "vehicle\\sector.streamingblock").Kind);
    }

    [TestMethod]
    public void AnalyzeKeepsFactoryAndLocalizationRegistrationsDistinct()
    {
        ArchiveXlSource source = new("Vehicle", "vehicle.xl", "factories:\n  - vehicle\\factory.csv\nlocalization:\n  onscreens:\n    en-us: vehicle\\en-us.json\n");

        ArchiveXlOperation[] operations = ArchiveXlManifestAnalyzer.Analyze([source]);

        Assert.AreEqual(ArchiveXlOperationKind.FactoryRegistration, operations.Single(value => value.Target == "vehicle\\factory.csv").Kind);
        Assert.AreEqual(ArchiveXlOperationKind.LocalizationRegistration, operations.Single(value => value.Target == "vehicle\\en-us.json").Kind);
    }

    [TestMethod]
    public void AnalyzeReadsListFormLocalizationRegistrations()
    {
        ArchiveXlSource source = new("Vehicle", "vehicle.xl", "localization:\n  onscreens:\n    en-us:\n      - vehicle\\en-us.json\n");

        ArchiveXlOperation operation = ArchiveXlManifestAnalyzer.Analyze([source]).Single();

        Assert.AreEqual(ArchiveXlOperationKind.LocalizationRegistration, operation.Kind);
        Assert.AreEqual("vehicle\\en-us.json", operation.Target);
    }

    [TestMethod]
    public void AnalyzeDetailedReportsMalformedAndUnsupportedManifests()
    {
        ArchiveXlSource malformed = new("Broken", "broken.xl", "resource:\n  patch: [\n");
        ArchiveXlSource unsupported = new("Future", "future.xl", "resource:\n  transform:\n    base\\shared.mesh: future\\shared.mesh\n");

        ArchiveXlAnalysisResult result = ArchiveXlManifestAnalyzer.AnalyzeDetailed([malformed, unsupported]);

        Assert.AreEqual(0, result.Operations.Length);
        Assert.AreEqual(2, result.Failures.Length);
        Assert.IsTrue(result.Failures.Any(value => value.Provider == "Broken" && value.Message.Contains("represented", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(result.Failures.Any(value => value.Provider == "Future" && value.Message.Contains("transform", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void AnalyzeCoversJournalQuestAndCustomizationSectionsUsedByActiveMods()
    {
        ArchiveXlSource source = new("Expansion", "expansion.xl", "journal:\n  - mod\\journal\\entry.journal\nquest:\n  phases:\n    - path: mod\\quest\\phase.questphase\n      parent: base\\quest\\cyberpunk2077.quest\ncustomizations:\n  female: mod\\female.inkcharcustomization\n  male: mod\\male.inkcharcustomization\n");

        ArchiveXlOperation[] operations = ArchiveXlManifestAnalyzer.Analyze([source]);

        Assert.AreEqual(ArchiveXlOperationKind.JournalRegistration, operations.Single(value => value.Target.EndsWith("entry.journal", StringComparison.Ordinal)).Kind);
        Assert.AreEqual(ArchiveXlOperationKind.QuestPhaseRegistration, operations.Single(value => value.Target.EndsWith("phase.questphase", StringComparison.Ordinal)).Kind);
        Assert.AreEqual(2, operations.Count(value => value.Kind == ArchiveXlOperationKind.CustomizationRegistration));
    }

    [TestMethod]
    public void AnalyzeReadsStreamingSectorPaths()
    {
        ArchiveXlSource source = new("World", "world.xl", "streaming:\n  sectors:\n    - path: base\\world\\shared.streamingsector\n      expectedNodes: 20\n");

        ArchiveXlOperation operation = ArchiveXlManifestAnalyzer.Analyze([source]).Single();

        Assert.AreEqual(ArchiveXlOperationKind.StreamingMutation, operation.Kind);
        Assert.AreEqual("base\\world\\shared.streamingsector", operation.Target);
    }

    [TestMethod]
    public void AnalyzePreservesRepeatedResourceTargetsAndTheirPayloads()
    {
        ArchiveXlSource source = new("Vehicle", "vehicle.xl", "resource:\n  link:\n    base\\shared.mi:\n      - vehicle\\body.mi\n    base\\shared.mi:\n      - vehicle\\mirror.mi\n");

        ArchiveXlAnalysisResult result = ArchiveXlManifestAnalyzer.AnalyzeDetailed([source]);

        Assert.AreEqual(0, result.Failures.Length);
        Assert.AreEqual(2, result.Operations.Length);
        Assert.AreEqual(2, result.Operations.Select(value => value.Target).Distinct(StringComparer.Ordinal).Count());
        Assert.AreEqual(1, result.Operations.Select(value => value.Payload).Distinct(StringComparer.Ordinal).Count());
    }

    [TestMethod]
    public void AnalyzeGroupsLinksCopiesAndPatchesByTheirEffectiveEndpoints()
    {
        ArchiveXlSource source = new("Vehicle", "vehicle.xl", "resource:\n  link:\n    source\\paint.mi: [alias\\paint.mi]\n  copy:\n    source\\mesh.mesh: [copy\\mesh.mesh]\n  patch:\n    patch\\mesh.mesh:\n      order: 200\n      targets: [base\\mesh.mesh]\n");

        ArchiveXlOperation[] operations = ArchiveXlManifestAnalyzer.Analyze([source]);

        Assert.AreEqual("source\\paint.mi", operations.Single(value => value.Kind == ArchiveXlOperationKind.ResourceLink && value.Target == "alias\\paint.mi").Payload);
        Assert.AreEqual("source\\mesh.mesh", operations.Single(value => value.Kind == ArchiveXlOperationKind.ResourceCopy && value.Target == "copy\\mesh.mesh").Payload);
        ArchiveXlOperation patch = operations.Single(value => value.Kind == ArchiveXlOperationKind.ResourcePatch && value.Target == "base\\mesh.mesh");
        StringAssert.Contains(patch.Payload, "patch\\mesh.mesh");
        StringAssert.Contains(patch.Payload, "order:200");
    }

    [TestMethod]
    public void AnalyzeReadsSequenceFormResourcePatches()
    {
        ArchiveXlSource source = new("Vehicle", "vehicle.xl", "resource:\n  patch:\n    - source: patch\\mesh.mesh\n      order: 300\n      targets: [base\\mesh.mesh, base\\other.mesh]\n");

        ArchiveXlAnalysisResult result = ArchiveXlManifestAnalyzer.AnalyzeDetailed([source]);

        Assert.AreEqual(0, result.Failures.Length);
        CollectionAssert.AreEquivalent(SequencePatchTargets, result.Operations.Select(value => value.Target).ToArray());
        Assert.IsTrue(result.Operations.All(value => value.Kind == ArchiveXlOperationKind.ResourcePatch));
        Assert.IsTrue(result.Operations.All(value => value.Payload.Contains("patch\\mesh.mesh", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void DuplicateResourceFallbackDoesNotHideMalformedOtherSections()
    {
        ArchiveXlSource source = new("Vehicle", "vehicle.xl", "resource:\n  link:\n    base\\shared.mi:\n      - vehicle\\body.mi\n    base\\shared.mi:\n      - vehicle\\mirror.mi\nlocalization: [\n");

        ArchiveXlAnalysisResult result = ArchiveXlManifestAnalyzer.AnalyzeDetailed([source]);

        Assert.AreEqual(0, result.Operations.Length);
        Assert.AreEqual(1, result.Failures.Length);
    }

    [TestMethod]
    public void DuplicateResourceFallbackPreservesOtherValidSections()
    {
        ArchiveXlSource source = new("Vehicle", "vehicle.xl", "factories:\n  - vehicle\\factory.csv\nresource:\n  link:\n    base\\shared.mi:\n      - vehicle\\body.mi\n    base\\shared.mi:\n      - vehicle\\mirror.mi\nstreaming:\n  blocks:\n    - vehicle\\world.streamingblock\n");

        ArchiveXlAnalysisResult result = ArchiveXlManifestAnalyzer.AnalyzeDetailed([source]);

        Assert.AreEqual(0, result.Failures.Length);
        Assert.AreEqual(2, result.Operations.Count(value => value.Kind == ArchiveXlOperationKind.ResourceLink));
        Assert.IsTrue(result.Operations.Any(value => value.Kind == ArchiveXlOperationKind.FactoryRegistration));
        Assert.IsTrue(result.Operations.Any(value => value.Kind == ArchiveXlOperationKind.StreamingMutation));
    }

    [TestMethod]
    public void DuplicateResourceFallbackReportsUnsupportedSiblingOperations()
    {
        ArchiveXlSource source = new("Vehicle", "vehicle.xl", "resource:\n  link:\n    base\\shared.mi:\n      - vehicle\\body.mi\n    base\\shared.mi:\n      - vehicle\\mirror.mi\n  transform:\n    base\\other.mi: vehicle\\other.mi\n");

        ArchiveXlAnalysisResult result = ArchiveXlManifestAnalyzer.AnalyzeDetailed([source]);

        Assert.AreEqual(2, result.Operations.Length);
        Assert.IsTrue(result.Failures.Any(value => value.Message.Contains("transform", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ScanAndGroupContinuesAfterUnavailableProviderAndOrdersSharedTargets()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-archivexl-" + Guid.NewGuid().ToString("N"));
        string first = Path.Combine(root, "First");
        string second = Path.Combine(root, "Second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        try
        {
            File.WriteAllText(Path.Combine(first, "first.archive.xl"), "resource:\n  patch:\n    first\\patch.mesh:\n      - base\\shared.mesh\n");
            File.WriteAllText(Path.Combine(second, "second.xl"), "resource:\n  patch:\n    second\\patch.mesh:\n      - base\\shared.mesh\n");

            ArchiveXlSourceScanResult scan = ArchiveXlSourceScanner.Scan([
                new ArchiveXlProviderSource("First Mod", first),
                new ArchiveXlProviderSource("Unavailable Mod", Path.Combine(root, "Missing")),
                new ArchiveXlProviderSource("Second Mod", second)]);
            ArchiveXlOperationChain chain = ArchiveXlProviderChainAnalyzer.Group(
                ArchiveXlManifestAnalyzer.Analyze(scan.Sources)).Single(value => value.Target == "base\\shared.mesh");

            Assert.AreEqual("Unavailable Mod", scan.Failures.Single().Provider);
            CollectionAssert.AreEqual(AlphabeticalProviderOrder, chain.Operations.Select(value => value.Provider).ToArray());
            Assert.AreEqual(ArchiveXlOperationKind.ResourcePatch, chain.Kind);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void SourceScanCollapsesSamePathManifestsToTheEffectiveProvider()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-archivexl-shadow-" + Guid.NewGuid().ToString("N"));
        string first = Path.Combine(root, "First");
        string second = Path.Combine(root, "Second");
        try
        {
            WriteManifest(first, "shared.xl", "factories:\n  - first\\factory.csv\n");
            WriteManifest(second, "shared.xl", "factories:\n  - second\\factory.csv\n");

            ArchiveXlSourceScanResult scan = ArchiveXlSourceScanner.Scan([new ArchiveXlProviderSource("First", first), new ArchiveXlProviderSource("Second", second)]);

            Assert.AreEqual("First", scan.Sources.Single().Provider);
            Assert.IsTrue(scan.Sources.Single().Text.Contains("first", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ProviderChainsPreserveManagerOrderInsteadOfAlphabetizingProviders()
    {
        ArchiveXlOperation first = new("Zeta", "zeta.xl", ArchiveXlOperationKind.ResourcePatch, "base\\shared.mesh", "zeta\\patch.mesh");
        ArchiveXlOperation second = new("Alpha", "alpha.xl", ArchiveXlOperationKind.ResourcePatch, "base\\shared.mesh", "alpha\\patch.mesh");
        string[] expected = ["Zeta", "Alpha"];

        ArchiveXlOperationChain chain = ArchiveXlProviderChainAnalyzer.Group([first, second]).Single();

        CollectionAssert.AreEqual(expected, chain.Operations.Select(value => value.Provider).ToArray());
    }

    [TestMethod]
    public void SourceScanUsesTheAuthoritativeVortexDeploymentWinner()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-archivexl-" + Guid.NewGuid().ToString("N"));
        string first = Path.Combine(root, "First");
        string second = Path.Combine(root, "Second");
        try
        {
            WriteManifest(first, "shared.xl", "factories:\n  - first\\factory.csv\n");
            WriteManifest(second, "shared.xl", "factories:\n  - second\\factory.csv\n");
            Dictionary<string, string> winners = new(StringComparer.OrdinalIgnoreCase) { ["archive\\pc\\mod\\shared.xl"] = "second" };

            ArchiveXlSourceScanResult scan = ArchiveXlSourceScanner.Scan([new ArchiveXlProviderSource("First", first, "first"), new ArchiveXlProviderSource("Second", second, "second")], winners);

            Assert.AreEqual("Second", scan.Sources.Single().Provider);
            Assert.IsTrue(scan.Sources.Single().Text.Contains("second", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void WriteManifest(string root, string name, string text)
    {
        string directory = Path.Combine(root, "archive", "pc", "mod");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), text);
    }
}

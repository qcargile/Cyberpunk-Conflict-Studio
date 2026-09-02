using ConflictStudio.Core;
using System.IO;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class RedmodArchiveProfileScannerTests
{
    [TestMethod]
    public void ScanUsesExplicitFirstWinsRedmodOrderAndProviderIdentity()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-redmod-" + Guid.NewGuid().ToString("N"));
        try
        {
            string high = Path.Combine(root, "high");
            string low = Path.Combine(root, "low");
            WriteRedmod(high, "Beta", "Beta.archive");
            WriteRedmod(low, "Alpha", "Alpha.archive");
            Write(low, "mods\\ScriptsOnly\\info.json", "{\"name\":\"ScriptsOnly\",\"version\":\"1.0.0\"}");
            Write(low, "r6\\cache\\modded\\MO_REDmod_load_order.txt", "Beta\nScriptsOnly\nAlpha\n");

            RedmodArchiveProfile profile = RedmodArchiveProfileScanner.Scan([new DeploymentProvider("High Provider", high, 2), new DeploymentProvider("Low Provider", low, 1)]);

            string[] expected = ["REDmod/Beta/Beta.archive", "REDmod/Alpha/Alpha.archive"];
            CollectionAssert.AreEqual(expected, profile.EffectiveOrder);
            Assert.AreEqual("High Provider", profile.Archives[0].Provider);
            Assert.AreEqual("REDmod: Beta", profile.Archives[0].LogicalProvider);
            Assert.AreEqual(ArchiveOrderEvidenceKind.ManagedModlist, profile.OrderEvidence.Kind);
            Assert.AreEqual(0, profile.Failures.Length);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanMarksAnIncompleteExplicitOrderUnresolved()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-redmod-stale-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteRedmod(root, "Alpha", "Alpha.archive");
            WriteRedmod(root, "Beta", "Beta.archive");
            Write(root, "r6\\cache\\modded\\MO_REDmod_load_order.txt", "Alpha\n");

            RedmodArchiveProfile profile = RedmodArchiveProfileScanner.Scan([new DeploymentProvider("Provider", root, 1)]);

            Assert.AreEqual(ArchiveOrderEvidenceKind.Unresolved, profile.OrderEvidence.Kind);
            string[] expected = ["REDmod/Alpha/Alpha.archive", "REDmod/Beta/Beta.archive"];
            CollectionAssert.AreEqual(expected, profile.EffectiveOrder);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanReportsMalformedDescriptorWithoutObscuringRecognizedRedmods()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-redmod-invalid-" + Guid.NewGuid().ToString("N"));
        try
        {
            Write(root, "mods\\Broken\\info.json", "{");

            RedmodArchiveProfile profile = RedmodArchiveProfileScanner.Scan([new DeploymentProvider("Provider", root, 1)]);

            Assert.AreEqual(ArchiveOrderEvidenceKind.FilenameFallback, profile.OrderEvidence.Kind);
            Assert.AreEqual(1, profile.Failures.Length);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanReportsStructurallyInvalidDescriptorsWithoutObscuringRecognizedRedmods()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-redmod-structure-" + Guid.NewGuid().ToString("N"));
        try
        {
            Write(root, "mods\\Empty\\info.json", "{}");
            Write(root, "mods\\WrongType\\info.json", "{\"name\":1,\"version\":\"1.0.0\"}");

            RedmodArchiveProfile profile = RedmodArchiveProfileScanner.Scan([new DeploymentProvider("Provider", root, 1)]);

            Assert.AreEqual(ArchiveOrderEvidenceKind.FilenameFallback, profile.OrderEvidence.Kind);
            Assert.AreEqual(2, profile.Failures.Length);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanReportsMissingDescriptorWithoutObscuringRecognizedRedmods()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-redmod-missing-info-" + Guid.NewGuid().ToString("N"));
        try
        {
            Write(root, "mods\\Broken\\archives\\Broken.archive", "broken");
            WriteRedmod(root, "Alpha", "Alpha.archive");

            RedmodArchiveProfile profile = RedmodArchiveProfileScanner.Scan([new DeploymentProvider("Provider", root, 1)]);

            Assert.AreEqual(ArchiveOrderEvidenceKind.FilenameFallback, profile.OrderEvidence.Kind);
            Assert.AreEqual("REDmod/Alpha/Alpha.archive", profile.Archives.Single().ArchiveName);
            StringAssert.Contains(profile.Failures.Single().Message, "will not load");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanUsesTheEffectiveDescriptorAndMergedArchiveFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-redmod-merged-folder-" + Guid.NewGuid().ToString("N"));
        try
        {
            string high = Path.Combine(root, "high");
            string low = Path.Combine(root, "low");
            Write(high, "mods\\Shared\\archives\\High.archive", "high");
            Write(high, "mods\\Shared\\archives\\Same.archive", "high winner");
            WriteRedmod(low, "Shared", "Low.archive");
            Write(low, "mods\\Shared\\archives\\Same.archive", "low loser");

            RedmodArchiveProfile profile = RedmodArchiveProfileScanner.Scan([new DeploymentProvider("High Provider", high, 2), new DeploymentProvider("Low Provider", low, 1)]);

            string[] expected = ["REDmod/Shared/High.archive", "REDmod/Shared/Low.archive", "REDmod/Shared/Same.archive"];
            CollectionAssert.AreEqual(expected, profile.Archives.Select(value => value.ArchiveName).ToArray());
            Assert.AreEqual("High Provider", profile.Archives.Single(value => value.ArchiveName.EndsWith("High.archive", StringComparison.Ordinal)).Provider);
            Assert.AreEqual("Low Provider", profile.Archives.Single(value => value.ArchiveName.EndsWith("Low.archive", StringComparison.Ordinal)).Provider);
            Assert.AreEqual("High Provider", profile.Archives.Single(value => value.ArchiveName.EndsWith("Same.archive", StringComparison.Ordinal)).Provider);
            Assert.IsTrue(profile.Archives.All(value => value.LogicalProvider == "REDmod: Shared"));
            Assert.AreEqual(Path.Combine(high, "mods", "Shared", "archives", "Same.archive"), profile.Archives.Single(value => value.ArchiveName.EndsWith("Same.archive", StringComparison.Ordinal)).PhysicalPath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanDoesNotFallThroughAnInvalidEffectiveDescriptor()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-redmod-shadowed-info-" + Guid.NewGuid().ToString("N"));
        try
        {
            string high = Path.Combine(root, "high");
            string low = Path.Combine(root, "low");
            Write(high, "mods\\Shared\\info.json", "{");
            WriteRedmod(low, "Shared", "Low.archive");

            RedmodArchiveProfile profile = RedmodArchiveProfileScanner.Scan([new DeploymentProvider("High Provider", high, 2), new DeploymentProvider("Low Provider", low, 1)]);

            Assert.AreEqual(0, profile.Archives.Length);
            Assert.AreEqual(1, profile.Failures.Length);
            Assert.AreEqual("High Provider", profile.Failures[0].Provider);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanIgnoresInvalidRedmodFoldersInAnOtherwiseCompleteManagedOrder()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-redmod-inactive-order-entry-" + Guid.NewGuid().ToString("N"));
        try
        {
            Write(root, "mods\\Broken\\archives\\Broken.archive", "broken");
            WriteRedmod(root, "Alpha", "Alpha.archive");
            Write(root, "r6\\cache\\modded\\MO_REDmod_load_order.txt", "Broken\nAlpha\n");

            RedmodArchiveProfile profile = RedmodArchiveProfileScanner.Scan([new DeploymentProvider("Provider", root, 1)]);

            Assert.AreEqual(ArchiveOrderEvidenceKind.ManagedModlist, profile.OrderEvidence.Kind);
            string[] ignored = ["Broken"];
            string[] expected = ["REDmod/Alpha/Alpha.archive"];
            CollectionAssert.AreEqual(ignored, profile.OrderEvidence.IgnoredEntries);
            CollectionAssert.AreEqual(expected, profile.EffectiveOrder);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanContainsAnUnreadableRedmodOrderFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-redmod-order-failure-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteRedmod(root, "Alpha", "Alpha.archive");
            string order = Path.Combine(root, "r6", "cache", "modded", "MO_REDmod_load_order.txt");
            Write(root, "r6\\cache\\modded\\MO_REDmod_load_order.txt", "Alpha\n");
            using FileStream locked = File.Open(order, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            RedmodArchiveProfile profile = RedmodArchiveProfileScanner.Scan([new DeploymentProvider("Provider", root, 1)]);

            Assert.AreEqual(ArchiveOrderEvidenceKind.Unresolved, profile.OrderEvidence.Kind);
            Assert.AreEqual("REDmod order", profile.Failures.Single().Surface);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanUsesTheAuthoritativeVortexRedmodOrderWinner()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-redmod-order-winner-" + Guid.NewGuid().ToString("N"));
        try
        {
            string high = Path.Combine(root, "high");
            string low = Path.Combine(root, "low");
            WriteRedmod(high, "Alpha", "Alpha.archive");
            WriteRedmod(low, "Alpha", "Alpha.archive");
            Write(high, "r6\\cache\\modded\\MO_REDmod_load_order.txt", "Wrong\n");
            Write(low, "r6\\cache\\modded\\MO_REDmod_load_order.txt", "Alpha\n");
            Dictionary<string, string> winners = new(StringComparer.OrdinalIgnoreCase)
            {
                ["mods\\Alpha\\info.json"] = "low",
                ["mods\\Alpha\\archives\\Alpha.archive"] = "low",
                ["r6\\cache\\modded\\MO_REDmod_load_order.txt"] = "low"
            };

            RedmodArchiveProfile profile = RedmodArchiveProfileScanner.Scan([new DeploymentProvider("High", high, null, "high"), new DeploymentProvider("Low", low, null, "low")], winners);

            Assert.AreEqual(ArchiveOrderEvidenceKind.ManagedModlist, profile.OrderEvidence.Kind);
            Assert.AreEqual("Low", profile.OrderEvidence.Provider);
            Assert.AreEqual("Low", profile.Archives.Single().Provider);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanNamesAMissingAuthoritativeRedmodArchiveWinner()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-redmod-missing-archive-winner-" + Guid.NewGuid().ToString("N"));
        try
        {
            Write(root, "mods\\Alpha\\info.json", "{\"name\":\"Alpha\",\"version\":\"1.0.0\"}");
            Dictionary<string, string> winners = new(StringComparer.OrdinalIgnoreCase)
            {
                ["mods\\Alpha\\info.json"] = "alpha",
                ["mods\\Alpha\\archives\\Missing.archive"] = "alpha"
            };

            RedmodArchiveProfile profile = RedmodArchiveProfileScanner.Scan([new DeploymentProvider("Alpha Provider", root, null, "alpha")], winners);

            Assert.AreEqual(ArchiveOrderEvidenceKind.Unresolved, profile.OrderEvidence.Kind);
            Assert.IsTrue(profile.Failures.Any(value => value.Surface == "REDmod archive winner" && value.FilePath.EndsWith("Missing.archive", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanRetainsReadableArchivesAndNamesAnUnreadableArchive()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-redmod-archive-failure-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteRedmod(root, "Alpha", "Good.archive");
            Write(root, "r6\\cache\\modded\\MO_REDmod_load_order.txt", "Inactive\nAlpha\n");
            string broken = Path.Combine(root, "mods", "Alpha", "archives", "Broken.archive");
            File.WriteAllText(broken, "broken");
            using FileStream locked = File.Open(broken, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            RedmodArchiveProfile profile = RedmodArchiveProfileScanner.Scan([new DeploymentProvider("Provider", root, 1)]);

            Assert.AreEqual("REDmod/Alpha/Good.archive", profile.Archives.Single().ArchiveName);
            Assert.AreEqual("Broken.archive", Path.GetFileName(profile.Failures.Single().FilePath));
            Assert.AreEqual(ArchiveOrderEvidenceKind.Unresolved, profile.OrderEvidence.Kind);
            string[] ignored = ["Inactive"];
            CollectionAssert.AreEqual(ignored, profile.OrderEvidence.IgnoredEntries);
            Assert.IsTrue(profile.OrderEvidence.SourcePaths.Any(value => value.EndsWith("MO_REDmod_load_order.txt", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ComposePlacesLegacyArchivesBeforeRedmods()
    {
        Mo2Archive legacyArchive = new("Legacy Provider", "Legacy.archive", "legacy", 1, new string('a', 64));
        Mo2Archive redmodArchive = new("REDmod: Alpha", "REDmod/Alpha/Alpha.archive", "redmod", 1, new string('b', 64));
        Mo2ArchiveProfile legacy = new("Standard", "profile", [legacyArchive], [legacyArchive.ArchiveName], new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.ManagedModlist, "Settings", "legacy-order", "legacy"));
        RedmodArchiveProfile redmods = new([redmodArchive], [redmodArchive.ArchiveName], new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.FilenameFallback, null, null, "redmod ASCII"), []);

        Mo2ArchiveProfile combined = PackedArchiveTopology.Compose(legacy, redmods);

        string[] expected = ["Legacy.archive", "REDmod/Alpha/Alpha.archive"];
        CollectionAssert.AreEqual(expected, combined.EffectiveOrder);
        Assert.AreEqual(2, combined.Archives.Length);
        Assert.AreEqual(ArchiveOrderEvidenceKind.ManagedModlist, combined.OrderEvidence!.Kind);
    }

    [TestMethod]
    public void ComposeRetainsTheExplicitRedmodOrderSource()
    {
        Mo2ArchiveProfile legacy = new("Standard", "profile", [], [], new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.FilenameFallback, null, null, "legacy ASCII"));
        ArchiveOrderEvidence redmodEvidence = new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.ManagedModlist, "Overwrite", "redmod-order.txt", "REDmod managed") { SourcePaths = ["redmod-order.txt"] };
        RedmodArchiveProfile redmods = new([], [], redmodEvidence, []);

        Mo2ArchiveProfile combined = PackedArchiveTopology.Compose(legacy, redmods);

        Assert.AreEqual("redmod-order.txt", combined.OrderEvidence!.SourcePath);
        string[] expected = ["redmod-order.txt"];
        CollectionAssert.AreEqual(expected, combined.OrderEvidence.SourcePaths);
    }

    [TestMethod]
    public void ComposeDoesNotOfferLegacyMaintenanceForIgnoredRedmodFolders()
    {
        Mo2ArchiveProfile legacy = new("Standard", "profile", [], [], new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.FilenameFallback, null, null, "legacy ASCII"));
        ArchiveOrderEvidence redmodEvidence = new(ArchiveOrderEvidenceKind.ManagedModlist, "REDmod", "redmod-order.txt", "REDmod managed") { IgnoredEntries = ["Broken"] };
        RedmodArchiveProfile redmods = new([], [], redmodEvidence, []);

        Mo2ArchiveProfile combined = PackedArchiveTopology.Compose(legacy, redmods);

        Assert.AreEqual(0, combined.OrderEvidence!.IgnoredEntries.Length);
    }

    [TestMethod]
    public void ComposeNamesTheLaneThatActuallyBlocksOrderEvidence()
    {
        ArchiveOrderEvidence legacyFailure = new(ArchiveOrderEvidenceKind.Unresolved, "Legacy", "legacy.txt", "legacy failed") { ProblemLane = ArchiveOrderProblemLane.Legacy };
        ArchiveOrderEvidence redmodManaged = new(ArchiveOrderEvidenceKind.ManagedModlist, "REDmod", "redmod.txt", "redmod managed");
        Mo2ArchiveProfile legacy = new("Standard", "profile", [], [], legacyFailure);
        RedmodArchiveProfile redmods = new([], [], redmodManaged, []);

        ArchiveOrderEvidence legacyBlocked = PackedArchiveTopology.Compose(legacy, redmods).OrderEvidence!;
        ArchiveOrderEvidence redmodBlocked = PackedArchiveTopology.Compose(legacy with { OrderEvidence = redmodManaged }, redmods with { OrderEvidence = new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.Unresolved, "REDmod", "redmod.txt", "redmod failed") { ProblemLane = ArchiveOrderProblemLane.Redmod } }).OrderEvidence!;

        Assert.AreEqual(ArchiveOrderProblemLane.Legacy, legacyBlocked.ProblemLane);
        Assert.AreEqual("legacy.txt", legacyBlocked.SourcePath);
        Assert.AreEqual(ArchiveOrderProblemLane.Redmod, redmodBlocked.ProblemLane);
        Assert.AreEqual("redmod.txt", redmodBlocked.SourcePath);

        ArchiveOrderEvidence bothBlocked = PackedArchiveTopology.Compose(legacy, redmods with { OrderEvidence = new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.Unresolved, "REDmod", "redmod.txt", "redmod failed") { ProblemLane = ArchiveOrderProblemLane.Redmod } }).OrderEvidence!;
        Assert.AreEqual(ArchiveOrderProblemLane.Combined, bothBlocked.ProblemLane);
    }

    [TestMethod]
    public void CrossLaneConflictNamesLegacyAsTheEngineWinner()
    {
        Mo2Archive legacyArchive = new("Legacy Provider", "Legacy.archive", "legacy", 1, new string('a', 64));
        Mo2Archive redmodArchive = new("REDmod: Alpha", "REDmod/Alpha/Alpha.archive", "redmod", 1, new string('b', 64));
        Mo2ArchiveProfile legacy = new("Standard", "profile", [legacyArchive], [legacyArchive.ArchiveName], new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.FilenameFallback, null, null, "legacy ASCII"));
        RedmodArchiveProfile redmods = new([redmodArchive], [redmodArchive.ArchiveName], new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.FilenameFallback, null, null, "redmod ASCII"), []);
        Mo2ArchiveProfile combined = PackedArchiveTopology.Compose(legacy, redmods);
        ResourceProvider[] providers = [new("Legacy.archive", 9, "base\\shared.mesh", new string('a', 40), ProviderName: "Legacy Provider"), new("REDmod/Alpha/Alpha.archive", 9, "base\\shared.mesh", new string('b', 40), ProviderName: "REDmod: Alpha")];

        ResourceConflict conflict = ResourceConflictAnalyzer.Analyze(providers, combined.EffectiveOrder).Single();

        Assert.AreEqual("Legacy.archive", conflict.EngineWinnerArchive);
        Assert.AreEqual("Legacy Provider", conflict.Providers[0].Provider);
    }

    [TestMethod]
    public void SameRedmodFolderArchiveCollisionRemainsUnresolved()
    {
        string redmodProvider = "REDmod: Alpha (Provider)";
        ResourceProvider[] providers = [new("REDmod/Alpha/A.archive", 9, "base\\shared.mesh", new string('a', 40), ProviderName: redmodProvider), new("REDmod/Alpha/B.archive", 9, "base\\shared.mesh", new string('b', 40), ProviderName: redmodProvider)];
        string[] order = ["REDmod/Alpha/A.archive", "REDmod/Alpha/B.archive"];
        Mo2Archive[] archives = [new(redmodProvider, order[0], "a", 1, new string('a', 64)), new(redmodProvider, order[1], "b", 1, new string('b', 64))];

        ResourceConflict conflict = ResourceConflictAnalyzer.Analyze(providers, order).Single();
        ArchiveConflictSummary[] summaries = ArchiveResourceIndexBuilder.Build(providers, archives, order);

        Assert.AreEqual(ResourceConflictKind.Unresolved, conflict.Kind);
        Assert.AreEqual("unresolved", conflict.EngineWinnerArchive);
        Assert.AreEqual(2, summaries.Sum(value => value.Unresolved.Length));
        Assert.AreEqual(0, summaries.Sum(value => value.Winning.Length + value.Losing.Length));
    }

    [TestMethod]
    public void EarlierLegacyWinnerSurvivesLaterInternalRedmodAmbiguity()
    {
        string redmodProvider = "REDmod: Alpha (Provider)";
        ResourceProvider[] providers = [new("Legacy.archive", 9, "base\\shared.mesh", new string('l', 40), ProviderName: "Legacy"), new("REDmod/Alpha/A.archive", 9, "base\\shared.mesh", new string('a', 40), ProviderName: redmodProvider), new("REDmod/Alpha/B.archive", 9, "base\\shared.mesh", new string('b', 40), ProviderName: redmodProvider)];
        string[] order = ["Legacy.archive", "REDmod/Alpha/A.archive", "REDmod/Alpha/B.archive"];
        Mo2Archive[] archives = [new("Legacy", order[0], "legacy", 1, new string('l', 64)), new(redmodProvider, order[1], "a", 1, new string('a', 64)), new(redmodProvider, order[2], "b", 1, new string('b', 64))];

        ResourceConflict conflict = ResourceConflictAnalyzer.Analyze(providers, order).Single();
        ArchiveConflictSummary[] summaries = ArchiveResourceIndexBuilder.Build(providers, archives, order);

        Assert.AreEqual(ResourceConflictKind.Divergent, conflict.Kind);
        Assert.AreEqual("Legacy.archive", conflict.EngineWinnerArchive);
        Assert.AreEqual(1, summaries.Sum(value => value.Winning.Length));
        Assert.AreEqual(2, summaries.Sum(value => value.Losing.Length));
        Assert.AreEqual(0, summaries.Sum(value => value.Unresolved.Length));
    }

    [TestMethod]
    public void EquivalentInternalRedmodPayloadsDoNotObscureEffectiveBytes()
    {
        string redmodProvider = "REDmod: Alpha (Provider)";
        ResourceProvider[] providers = [new("REDmod/Alpha/A.archive", 9, "base\\shared.mesh", new string('a', 40), ProviderName: redmodProvider), new("REDmod/Alpha/B.archive", 9, "base\\shared.mesh", new string('a', 40), ProviderName: redmodProvider), new("REDmod/Beta/C.archive", 9, "base\\shared.mesh", new string('c', 40), ProviderName: "REDmod: Beta (Provider)")];
        string[] order = ["REDmod/Alpha/A.archive", "REDmod/Alpha/B.archive", "REDmod/Beta/C.archive"];
        Mo2Archive[] archives = [new(redmodProvider, order[0], "a", 1, new string('a', 64)), new(redmodProvider, order[1], "b", 1, new string('b', 64)), new("REDmod: Beta (Provider)", order[2], "c", 1, new string('c', 64))];

        ResourceConflict conflict = ResourceConflictAnalyzer.Analyze(providers, order).Single();
        ArchiveConflictSummary[] summaries = ArchiveResourceIndexBuilder.Build(providers, archives, order);

        Assert.AreEqual(ResourceConflictKind.Divergent, conflict.Kind);
        Assert.AreEqual(redmodProvider, conflict.EngineWinnerArchive);
        Assert.AreEqual(0, summaries.Sum(value => value.Winning.Length));
        Assert.AreEqual(2, summaries.Sum(value => value.Unresolved.Length));
        Assert.AreEqual(0, summaries.Sum(value => value.Redundant.Length));
        Assert.AreEqual(1, summaries.Sum(value => value.Losing.Length));
        Assert.IsTrue(summaries.Where(value => value.Provider == redmodProvider).SelectMany(value => value.Unresolved).All(value => value.WinnerArchive == redmodProvider));
    }

    [TestMethod]
    public void RdarFailuresRetainLogicalRedmodArchiveIdentity()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-redmod-rdar-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "Shared.archive");
            File.WriteAllText(path, "invalid");

            RdarResourceScanResult result = RdarResourceScanner.ScanResilient([new RdarArchiveInput("REDmod: Alpha", path, "REDmod/Alpha/Shared.archive")]);

            Assert.AreEqual("REDmod/Alpha/Shared.archive", result.Failures.Single().ArchiveName);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void WriteRedmod(string root, string folder, string archive)
    {
        Write(root, $"mods\\{folder}\\info.json", $"{{\"name\":\"{folder}\",\"version\":\"1.0.0\"}}");
        Write(root, $"mods\\{folder}\\archives\\{archive}", folder);
    }

    private static void Write(string root, string relative, string text)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}

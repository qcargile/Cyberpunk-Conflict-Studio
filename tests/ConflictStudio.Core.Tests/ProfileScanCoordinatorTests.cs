using ConflictStudio.Core;
using System.IO;
using System.Text.Json;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class ProfileScanCoordinatorTests
{
    private static readonly string[] ExpectedVortexProviders = ["Alpha", "Beta"];
    private static readonly string[] ExpectedManualProviders = ["Game directory"];
    private static readonly string[] ExpectedMissingArchives = ["Beta.archive"];
    private static readonly string[] ExpectedDuplicateArchives = ["Alpha.archive"];
    private static readonly string[] ExpectedIgnoredArchives = ["stale.archive"];
    private static readonly int[] ExpectedDeploymentProgress = [0, 1, 2, 3, 4];
    private static readonly int[] ExpectedManualDeploymentProgress = [0, 1, 2, 3];

    [TestMethod]
    public void VortexScanUsesBridgeProfileProvidersAndDeploymentWinners()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-scan-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            string alpha = Path.Combine(staging, "Alpha");
            string beta = Path.Combine(staging, "Beta");
            WriteRoot(alpha, "r6\\scripts\\shared.reds", "@wrapMethod(DamageSystem)\npublic func ProcessHit() -> Void { wrappedMethod(); }");
            WriteRoot(beta, "r6\\scripts\\shared.reds", "@wrapMethod(DamageSystem)\npublic func ProcessHit() -> Void { wrappedMethod(); }");
            Directory.CreateDirectory(game);
            Dictionary<string, string> winners = new(StringComparer.OrdinalIgnoreCase) { ["r6\\scripts\\shared.reds"] = "beta" };
            VortexManagerContext context = new(1, new string('a', 64), DateTimeOffset.UtcNow, "profile", "Standard", game, staging, true, [new("alpha", "Alpha", alpha, 0), new("beta", "Beta", beta, 1)], winners, [], null);
            string contextPath = Path.Combine(root, "context.json");
            File.WriteAllText(contextPath, System.Text.Json.JsonSerializer.Serialize(context));
            RecordingProgress progress = new();

            ProfileScanReceipt receipt = ProfileScanCoordinator.ScanVortex(contextPath, DateTimeOffset.UtcNow, progress, CancellationToken.None);

            Assert.AreEqual(ModManagerKind.Vortex, receipt.ManagerKind);
            Assert.AreEqual("Standard", receipt.ProfileName);
            CollectionAssert.AreEqual(ExpectedVortexProviders, receipt.ActiveProviders);
            Assert.AreEqual("Beta", receipt.VirtualFileShadows.Single().WinnerProvider);
            Assert.IsTrue(receipt.RedScriptFlows.All(value => value.Provider == "Beta"));
            Assert.AreEqual(Path.GetFullPath(contextPath), receipt.ManagerContextPath);
            CollectionAssert.AreEqual(ExpectedDeploymentProgress, progress.Values.Where(value => value.Total == 4 && value.Phase.StartsWith("deployment", StringComparison.Ordinal)).Select(value => value.Completed).ToArray());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private sealed class RecordingProgress : IProgress<ScanProgress>
    {
        public List<ScanProgress> Values { get; } = [];
        public void Report(ScanProgress value) => Values.Add(value);
    }

    [TestMethod]
    public void VortexScanAllowsTimestampOnlyBridgeHeartbeat()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-heartbeat-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(game);
            Directory.CreateDirectory(staging);
            VortexManagerContext context = new(1, new string('a', 64), DateTimeOffset.UtcNow, "profile", "Standard", game, staging, true, [], [], [], null);
            string contextPath = Path.Combine(root, "context.json");
            File.WriteAllText(contextPath, System.Text.Json.JsonSerializer.Serialize(context));

            ProfileScanReceipt receipt = ProfileScanCoordinator.ScanVortex(contextPath, DateTimeOffset.UtcNow, null, () => File.WriteAllText(contextPath, System.Text.Json.JsonSerializer.Serialize(context with { CapturedAtUtc = DateTimeOffset.UtcNow.AddSeconds(5) })), CancellationToken.None);

            Assert.AreEqual(ModManagerKind.Vortex, receipt.ManagerKind);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void Mo2AndManualScansReportEveryDeploymentStage()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-deployment-progress-" + Guid.NewGuid().ToString("N"));
        try
        {
            string profileRoot = Path.Combine(root, "profiles", "Standard");
            Directory.CreateDirectory(profileRoot);
            Directory.CreateDirectory(Path.Combine(root, "mods", "Alpha"));
            string modlist = Path.Combine(profileRoot, "modlist.txt");
            File.WriteAllText(modlist, "+Alpha\n");
            RecordingProgress mo2Progress = new();

            ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", modlist), DateTimeOffset.UtcNow, mo2Progress, CancellationToken.None);

            CollectionAssert.AreEqual(ExpectedDeploymentProgress, mo2Progress.Values.Where(value => value.Total == 4 && value.Phase.StartsWith("deployment", StringComparison.Ordinal)).Select(value => value.Completed).ToArray());

            string game = Path.Combine(root, "game");
            Directory.CreateDirectory(Path.Combine(game, "archive", "pc", "content"));
            Directory.CreateDirectory(Path.Combine(game, "archive", "pc", "mod"));
            RecordingProgress manualProgress = new();

            ProfileScanCoordinator.ScanManual(game, DateTimeOffset.UtcNow, manualProgress, CancellationToken.None);

            CollectionAssert.AreEqual(ExpectedManualDeploymentProgress, manualProgress.Values.Where(value => value.Total == 3 && value.Phase.StartsWith("deployment", StringComparison.Ordinal)).Select(value => value.Completed).ToArray());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void StaleVortexContextScansOnlyDeployedGameFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-offline-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            string alpha = Path.Combine(staging, "Alpha");
            WriteRoot(alpha, "r6\\scripts\\shared.reds", "@replaceMethod(DamageSystem)\npublic func ProcessHit() -> Void {}");
            WriteRoot(game, "r6\\scripts\\deployed.reds", "@wrapMethod(DamageSystem)\npublic func ProcessHit() -> Void { wrappedMethod(); }");
            VortexManagerContext context = new(1, new string('a', 64), new DateTimeOffset(2026, 8, 29, 18, 0, 0, TimeSpan.Zero), "profile", "Standard", game, staging, true, [new("alpha", "Alpha", alpha, 0)], new Dictionary<string, string> { ["r6\\scripts\\shared.reds"] = "alpha" }, [], null);
            string contextPath = Path.Combine(root, "context.json");
            File.WriteAllText(contextPath, System.Text.Json.JsonSerializer.Serialize(context));

            ProfileScanReceipt receipt = ProfileScanCoordinator.ScanVortex(contextPath, new DateTimeOffset(2026, 8, 29, 18, 1, 0, TimeSpan.Zero), null, CancellationToken.None);

            Assert.IsFalse(receipt.DeploymentFresh);
            Assert.AreEqual(0, receipt.ActiveProviders.Length);
            Assert.IsFalse(receipt.VirtualFileShadows.Any(value => value.RelativePath == "r6\\scripts\\shared.reds"));
            Assert.IsTrue(receipt.RedScriptFlows.All(value => value.Provider == "Game directory"));
            StringAssert.Contains(receipt.SourceFailures!.Single(value => value.Surface == "Deployment").Message, "offline or stale");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void VortexScanRejectsDeploymentIdentityChangeDuringScan()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-context-mutation-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(game);
            Directory.CreateDirectory(staging);
            VortexManagerContext context = new(1, new string('a', 64), DateTimeOffset.UtcNow, "profile", "Standard", game, staging, true, [], [], [], null);
            string contextPath = Path.Combine(root, "context.json");
            File.WriteAllText(contextPath, System.Text.Json.JsonSerializer.Serialize(context));

            Assert.ThrowsExactly<ProfileInputChangedException>(() => ProfileScanCoordinator.ScanVortex(contextPath, DateTimeOffset.UtcNow, null, () => File.WriteAllText(contextPath, System.Text.Json.JsonSerializer.Serialize(context with { ContextId = new string('b', 64) })), CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanBuildsOneReceiptAcrossActiveProfileSurfaces()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-profile-scan-" + Guid.NewGuid().ToString("N"));
        try
        {
            Write(root, "Alpha", "r6\\scripts\\shared.reds", "@replaceMethod(DamageSystem)\npublic func ProcessHit() -> Void {}");
            Write(root, "Beta", "r6\\scripts\\shared.reds", "@replaceMethod(DamageSystem)\npublic func ProcessHit() -> Void {}");
            Write(root, "Alpha", "archive\\pc\\mod\\Alpha.archive", "broken archive fixture");
            string profileRoot = Path.Combine(root, "profiles", "Standard");
            Directory.CreateDirectory(profileRoot);
            Directory.CreateDirectory(Path.Combine(root, "mods", "Alpha"));
            string modlist = Path.Combine(profileRoot, "modlist.txt");
            File.WriteAllText(modlist, "+Alpha\n+Beta\n");

            ProfileScanReceipt receipt = ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", modlist), new DateTimeOffset(2026, 8, 25, 16, 0, 0, TimeSpan.Zero));

            Assert.AreEqual(2, receipt.ActiveProviders.Length);
            Assert.AreEqual(1, receipt.ArchiveFailures.Length);
            Assert.IsFalse(receipt.InteractionFindings.Any(value => value.Kind == InteractionFindingKind.Exclusive));
            Assert.IsTrue(receipt.VirtualFileShadows.Any(value => value.RelativePath == "r6\\scripts\\shared.reds"));
            Assert.IsNotNull(receipt.ArchiveInventory);
            Assert.AreEqual("Alpha.archive", receipt.ArchiveInventory.Single().ArchiveName);
            Assert.IsNotNull(receipt.EditableArchiveInventory);
            Assert.AreEqual("Alpha.archive", receipt.EditableArchiveInventory.Single().ArchiveName);
            Assert.IsNotNull(receipt.Metrics);
            Assert.IsTrue(receipt.Metrics.Phases.Any(value => value.Name == "effective source"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ManualScanUsesOnlyTheDeployedGameTopology()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-manual-scan-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "archive", "pc", "content"));
            WriteRoot(root, "archive\\pc\\mod\\Alpha.archive", "archive fixture");
            WriteRoot(root, "r6\\scripts\\deployed.reds", "@wrapMethod(DamageSystem)\npublic func ProcessHit() -> Void { wrappedMethod(); }");

            ProfileScanReceipt receipt = ProfileScanCoordinator.ScanManual(root, DateTimeOffset.UtcNow, null, CancellationToken.None);

            Assert.AreEqual(ModManagerKind.Manual, receipt.ManagerKind);
            CollectionAssert.AreEqual(ExpectedManualProviders, receipt.ActiveProviders);
            Assert.AreEqual("Alpha.archive", receipt.EditableArchiveInventory!.Single().ArchiveName);
            Assert.IsTrue(receipt.RedScriptFlows.All(value => value.Provider == "Game directory"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ManualScanKeepsAStaleOrderAsOneUnresolvedDiagnostic()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-manual-order-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "archive", "pc", "content"));
            WriteRoot(root, "archive\\pc\\mod\\Alpha.archive", "archive fixture");
            WriteRoot(root, "archive\\pc\\mod\\Beta.archive", "archive fixture");
            WriteRoot(root, "archive\\pc\\mod\\modlist.txt", "Alpha.archive\nAlpha.archive\nstale.archive\n");

            ProfileScanReceipt receipt = ProfileScanCoordinator.ScanManual(root, DateTimeOffset.UtcNow, null, CancellationToken.None);
            ConflictWorkItem[] work = ConflictWorkQueueBuilder.Build(receipt, []);

            Assert.AreEqual(ArchiveOrderEvidenceKind.Unresolved, receipt.ArchiveOrderEvidence!.Kind);
            CollectionAssert.AreEqual(ExpectedMissingArchives, receipt.ArchiveOrderEvidence.MissingEntries);
            CollectionAssert.AreEqual(ExpectedDuplicateArchives, receipt.ArchiveOrderEvidence.DuplicateEntries);
            CollectionAssert.AreEqual(ExpectedIgnoredArchives, receipt.ArchiveOrderEvidence.IgnoredEntries);
            Assert.AreEqual(1, work.Count(value => value.Surface == ConflictSurface.Diagnostic && value.Target == "Archive load order"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanHonorsCancellationBeforeReadingTheProfile()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() => ProfileScanCoordinator.Scan("ignored", new Mo2Profile("Standard", "ignored"), DateTimeOffset.UtcNow, null, cancellation.Token));
    }

    [TestMethod]
    public void ScanRejectsAProfileChangedBeforeFinalValidation()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-profile-mutation-" + Guid.NewGuid().ToString("N"));
        try
        {
            string profileRoot = Path.Combine(root, "profiles", "Standard");
            Directory.CreateDirectory(profileRoot);
            Directory.CreateDirectory(Path.Combine(root, "mods", "Alpha"));
            string modlist = Path.Combine(profileRoot, "modlist.txt");
            File.WriteAllText(modlist, "+Alpha\n");

            Assert.ThrowsExactly<ProfileInputChangedException>(() => ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", modlist), DateTimeOffset.UtcNow, null, () => File.WriteAllText(modlist, "+Beta\n"), CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanNamesAMissingConfiguredMo2ModsDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-missing-mods-path-" + Guid.NewGuid().ToString("N"));
        try
        {
            string profileRoot = Path.Combine(root, "profiles", "Standard");
            Directory.CreateDirectory(profileRoot);
            string modlist = Path.Combine(profileRoot, "modlist.txt");
            File.WriteAllText(modlist, "+Alpha\n");

            DirectoryNotFoundException exception = Assert.ThrowsExactly<DirectoryNotFoundException>(() => ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", modlist), DateTimeOffset.UtcNow));

            StringAssert.Contains(exception.Message, Path.Combine(root, "mods"));
            StringAssert.Contains(exception.Message, "Settings > Paths");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanRejectsAnActiveSourceChangedBeforeFinalValidation()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-source-mutation-" + Guid.NewGuid().ToString("N"));
        try
        {
            string source = Path.Combine(root, "mods", "Alpha", "r6", "scripts", "active.reds");
            Write(root, "Alpha", "r6\\scripts\\active.reds", "alpha");
            DateTime timestamp = File.GetLastWriteTimeUtc(source);
            string profileRoot = Path.Combine(root, "profiles", "Standard");
            Directory.CreateDirectory(profileRoot);
            string modlist = Path.Combine(profileRoot, "modlist.txt");
            File.WriteAllText(modlist, "+Alpha\n");

            ProfileInputChangedException exception = Assert.ThrowsExactly<ProfileInputChangedException>(() => ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", modlist), DateTimeOffset.UtcNow, null, () =>
            {
                File.WriteAllText(source, "omega");
                File.SetLastWriteTimeUtc(source, timestamp);
            }, CancellationToken.None));

            StringAssert.Contains(exception.Message, source);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanRejectsAnArchiveReplacedWithTheSameLengthAndTimestamp()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-archive-mutation-" + Guid.NewGuid().ToString("N"));
        try
        {
            string archive = Path.Combine(root, "mods", "Alpha", "archive", "pc", "mod", "Alpha.archive");
            Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
            File.WriteAllBytes(archive, Enumerable.Repeat((byte)1, 32).ToArray());
            DateTime timestamp = File.GetLastWriteTimeUtc(archive);
            string profileRoot = Path.Combine(root, "profiles", "Standard");
            Directory.CreateDirectory(profileRoot);
            string modlist = Path.Combine(profileRoot, "modlist.txt");
            File.WriteAllText(modlist, "+Alpha\n");

            Assert.ThrowsExactly<ProfileInputChangedException>(() => ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", modlist), DateTimeOffset.UtcNow, null, () =>
            {
                File.WriteAllBytes(archive, Enumerable.Repeat((byte)2, 32).ToArray());
                File.SetLastWriteTimeUtc(archive, timestamp);
            }, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanRejectsAConflictingDeploymentFileChangedBeforeFinalValidation()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-virtual-file-mutation-" + Guid.NewGuid().ToString("N"));
        try
        {
            string alpha = Path.Combine(root, "mods", "Alpha", "engine", "config", "shared.ini");
            Write(root, "Alpha", "engine\\config\\shared.ini", "alpha");
            Write(root, "Beta", "engine\\config\\shared.ini", "bravo");
            DateTime timestamp = File.GetLastWriteTimeUtc(alpha);
            string profileRoot = Path.Combine(root, "profiles", "Standard");
            Directory.CreateDirectory(profileRoot);
            string modlist = Path.Combine(profileRoot, "modlist.txt");
            File.WriteAllText(modlist, "+Alpha\n+Beta\n");

            ProfileInputChangedException exception = Assert.ThrowsExactly<ProfileInputChangedException>(() => ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", modlist), DateTimeOffset.UtcNow, null, () =>
            {
                File.WriteAllText(alpha, "omega");
                File.SetLastWriteTimeUtc(alpha, timestamp);
            }, CancellationToken.None));

            StringAssert.Contains(exception.Message, alpha);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanRejectsTheActiveResourcePathIndexChangedBeforeFinalValidation()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-path-index-mutation-" + Guid.NewGuid().ToString("N"));
        try
        {
            string index = Path.Combine(root, "mods", "Alpha", "bin", "x64", "plugins", "cyber_engine_tweaks", "tweakdb", "usedhashes.kark");
            Write(root, "Alpha", "bin\\x64\\plugins\\cyber_engine_tweaks\\tweakdb\\usedhashes.kark", "first");
            DateTime timestamp = File.GetLastWriteTimeUtc(index);
            string profileRoot = Path.Combine(root, "profiles", "Standard");
            Directory.CreateDirectory(profileRoot);
            string modlist = Path.Combine(profileRoot, "modlist.txt");
            File.WriteAllText(modlist, "+Alpha\n");

            Assert.ThrowsExactly<ProfileInputChangedException>(() => ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", modlist), DateTimeOffset.UtcNow, null, () =>
            {
                File.WriteAllText(index, "other");
                File.SetLastWriteTimeUtc(index, timestamp);
            }, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanRejectsAnArchiveOrderCreatedBeforeFinalValidation()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-order-mutation-" + Guid.NewGuid().ToString("N"));
        try
        {
            Write(root, "Alpha", "archive\\pc\\mod\\Alpha.archive", "broken archive fixture");
            string profileRoot = Path.Combine(root, "profiles", "Standard");
            Directory.CreateDirectory(profileRoot);
            string modlist = Path.Combine(profileRoot, "modlist.txt");
            File.WriteAllText(modlist, "+Alpha\n");
            string order = Path.Combine(root, "mods", "Alpha", "archive", "pc", "mod", "modlist.txt");

            Assert.ThrowsExactly<ProfileInputChangedException>(() => ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", modlist), DateTimeOffset.UtcNow, null, () => File.WriteAllText(order, "Alpha.archive\n"), CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanRejectsAnExistingArchiveOrderChangedBeforeFinalValidation()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-order-rewrite-" + Guid.NewGuid().ToString("N"));
        try
        {
            Write(root, "Alpha", "archive\\pc\\mod\\Alpha.archive", "broken archive fixture");
            string order = Path.Combine(root, "mods", "Alpha", "archive", "pc", "mod", "modlist.txt");
            File.WriteAllText(order, "Alpha.archive\n");
            string profileRoot = Path.Combine(root, "profiles", "Standard");
            Directory.CreateDirectory(profileRoot);
            string modlist = Path.Combine(profileRoot, "modlist.txt");
            File.WriteAllText(modlist, "+Alpha\n");

            Assert.ThrowsExactly<ProfileInputChangedException>(() => ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", modlist), DateTimeOffset.UtcNow, null, () => File.WriteAllText(order, "Changed.archive\n"), CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanRejectsAVortexDeploymentThatAppearsDuringMo2Analysis()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-cross-manager-mutation-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "vortex-staging");
            string provider = Path.Combine(staging, "Alpha");
            Directory.CreateDirectory(game);
            Directory.CreateDirectory(provider);
            File.WriteAllText(Path.Combine(root, "ModOrganizer.ini"), "gamePath=@ByteArray(game)\n");
            string profileRoot = Path.Combine(root, "profiles", "Standard");
            Directory.CreateDirectory(profileRoot);
            string modlist = Path.Combine(profileRoot, "modlist.txt");
            File.WriteAllText(modlist, string.Empty);
            string contextPath = Path.Combine(root, "vortex-context.json");
            VortexManagerContext context = new(1, new string('a', 64), DateTimeOffset.UtcNow, "vortex-profile", "Default", game, staging, true, [new("alpha", "Alpha", provider, 0)], new Dictionary<string, string> { ["r6\\scripts\\deployed.reds"] = "alpha" }, [], null);

            Assert.ThrowsExactly<CrossManagerDeploymentException>(() => ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", modlist), DateTimeOffset.UtcNow, null, () =>
            {
                WriteRoot(game, "r6\\scripts\\deployed.reds", "deployed");
                File.WriteAllText(contextPath, JsonSerializer.Serialize(context));
            }, contextPath, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void Write(string root, string provider, string relative, string text)
    {
        string path = Path.Combine(root, "mods", provider, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static void WriteRoot(string root, string relative, string text)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}

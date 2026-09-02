using ConflictStudio.Core;
using System.IO;
using System.Text.Json;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class ProfileScanCoordinatorTests
{
    [TestMethod]
    public void ManualScanJoinsSingleYamlAndRuntimeWriteAcrossCachedScans()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-runtime-join-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "archive", "pc", "content"));
            WriteRoot(root, "r6\\tweaks\\initial.yaml", "Items.Test.value: 1");
            WriteRoot(root, "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\Test\\init.lua", "TweakDB:SetFlat('Items.Test.value', 2)");
            ProfileScanReceipt first = ProfileScanCoordinator.ScanManual(root, DateTimeOffset.UtcNow, null, CancellationToken.None);
            ProfileScanReceipt cached = ProfileScanCoordinator.ScanManual(root, DateTimeOffset.UtcNow, null, CancellationToken.None);

            Assert.IsEmpty(first.TweakOverlaps);
            Assert.IsEmpty(first.SharedStateWrites);
            Assert.HasCount(1, first.InteractionFindings);
            Assert.AreEqual(JsonSerializer.Serialize(first.InteractionFindings), JsonSerializer.Serialize(cached.InteractionFindings));
            Assert.AreEqual(1, cached.Metrics!.CodeCacheHits);
            string initialHash = ConflictWorkQueueBuilder.Build(first, []).Single(value => value.Target == "Items.Test.value").EvidenceSha256;
            WriteRoot(root, "bin\\x64\\plugins\\cyber_engine_tweaks\\mods\\Test\\init.lua", "TweakDB:SetFlat('Items.Test.value', 3)");
            ProfileScanReceipt changed = ProfileScanCoordinator.ScanManual(root, DateTimeOffset.UtcNow, null, CancellationToken.None);
            Assert.AreEqual(0, changed.Metrics!.CodeCacheHits);
            Assert.AreNotEqual(initialHash, ConflictWorkQueueBuilder.Build(changed, []).Single(value => value.Target == "Items.Test.value").EvidenceSha256);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

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
            VortexManagerContext context = Context(DateTimeOffset.UtcNow, "profile", "Standard", game, staging, true, [new("alpha", "Alpha", alpha, 0), new("beta", "Beta", beta, 1)], winners, [], null, deploymentInventoryComplete: true, deploymentFileCount: 1, relevantDeploymentFileCount: 1);
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

    [TestMethod]
    public void VortexScanUsesDeploymentWinnersInsideRedmodFolders()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-redmod-winner-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            string alpha = Path.Combine(staging, "Alpha");
            string beta = Path.Combine(staging, "Beta");
            WriteRoot(alpha, "mods\\Shared\\info.json", "{\"name\":\"Shared\",\"version\":\"1.0.0\"}");
            WriteRoot(beta, "mods\\Shared\\info.json", "{\"name\":\"Shared\",\"version\":\"1.0.0\"}");
            WriteRoot(alpha, "mods\\Shared\\archives\\Same.archive", "alpha");
            WriteRoot(beta, "mods\\Shared\\archives\\Same.archive", "bravo");
            Directory.CreateDirectory(game);
            Dictionary<string, string> winners = new(StringComparer.OrdinalIgnoreCase)
            {
                ["mods\\Shared\\info.json"] = "beta",
                ["mods\\Shared\\archives\\Same.archive"] = "beta"
            };
            VortexManagerContext context = Context(DateTimeOffset.UtcNow, "profile", "Standard", game, staging, true, [new("alpha", "Alpha", alpha, 0), new("beta", "Beta", beta, 1)], winners, [], null, DateTimeOffset.UtcNow, true, 2, 2);
            string contextPath = Path.Combine(root, "context.json");
            File.WriteAllText(contextPath, JsonSerializer.Serialize(context));

            ProfileScanReceipt receipt = ProfileScanCoordinator.ScanVortex(contextPath, DateTimeOffset.UtcNow, null, CancellationToken.None);

            string expected = Path.Combine(beta, "mods", "Shared", "archives", "Same.archive");
            Assert.AreEqual(expected, receipt.ArchiveInventory!.Single().PhysicalPath);
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
            VortexManagerContext context = Context(DateTimeOffset.UtcNow, "profile", "Standard", game, staging, true, [], [], [], null);
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
    public void VortexScanUsesFreshHeartbeatWithoutRewritingEvidenceCaptureTime()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-live-heartbeat-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(game);
            Directory.CreateDirectory(staging);
            VortexManagerContext context = Context(DateTimeOffset.UtcNow.AddHours(-1), "profile", "Standard", game, staging, true, [], [], [], null, deploymentInventoryComplete: true);
            string contextPath = Path.Combine(root, "context.json");
            File.WriteAllText(contextPath, System.Text.Json.JsonSerializer.Serialize(context));
            VortexBridgeHeartbeatStore.Write(VortexBridgeHeartbeatStore.PathForContext(contextPath), new VortexBridgeHeartbeat(1, context.ContextId, context.ProfileId, DateTimeOffset.UtcNow));

            ProfileScanReceipt receipt = ProfileScanCoordinator.ScanVortex(contextPath, DateTimeOffset.UtcNow, null, CancellationToken.None);

            Assert.IsTrue(receipt.DeploymentFresh);
            Assert.IsFalse((receipt.SourceFailures ?? []).Any(value => value.Provider == "Vortex" && value.Surface == "Deployment"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void VortexScanNamesAMissingProviderInsteadOfRejectingTheContext()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-vortex-missing-provider-scan-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "staging");
            string missing = Path.Combine(staging, "Missing");
            Directory.CreateDirectory(game);
            Directory.CreateDirectory(staging);
            VortexManagerContext context = Context(DateTimeOffset.UtcNow, "profile", "Standard", game, staging, true, [new("missing", "Missing provider", missing, 0)], [], [], null, DateTimeOffset.UtcNow);
            string contextPath = Path.Combine(root, "context.json");
            File.WriteAllText(contextPath, JsonSerializer.Serialize(context));

            ProfileScanReceipt receipt = ProfileScanCoordinator.ScanVortex(contextPath, DateTimeOffset.UtcNow, null, CancellationToken.None);

            Assert.IsFalse(receipt.DeploymentFresh);
            Assert.IsTrue((receipt.SourceFailures ?? []).Any(value => value.Provider == "Missing provider" && value.Surface == "Vortex path"));
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
            VortexManagerContext context = Context(new DateTimeOffset(2026, 8, 29, 18, 0, 0, TimeSpan.Zero), "profile", "Standard", game, staging, true, [new("alpha", "Alpha", alpha, 0)], new Dictionary<string, string> { ["r6\\scripts\\shared.reds"] = "alpha" }, [], null);
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
            VortexManagerContext context = Context(DateTimeOffset.UtcNow, "profile", "Standard", game, staging, true, [], [], [], null);
            string contextPath = Path.Combine(root, "context.json");
            File.WriteAllText(contextPath, System.Text.Json.JsonSerializer.Serialize(context));

            Assert.ThrowsExactly<ProfileInputChangedException>(() => ProfileScanCoordinator.ScanVortex(contextPath, DateTimeOffset.UtcNow, null, () =>
            {
                VortexManagerContext changed = context with { DeploymentFresh = false };
                File.WriteAllText(contextPath, System.Text.Json.JsonSerializer.Serialize(changed with { ContextId = VortexManagerContextStore.ComputeContextId(changed) }));
            }, CancellationToken.None));
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
    public void ScanCapturesAndFingerprintsTheEffectiveRedTweakForDiagnostics()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-red-tweak-profile-" + Guid.NewGuid().ToString("N"));
        try
        {
            Write(root, "Alpha", "r6\\tweaks\\shared.tweak", "RED4ext.TweakDB:SetFlat('Items.Test.Value', 1)");
            Write(root, "Beta", "r6\\tweaks\\shared.tweak", "RED4ext.TweakDB:SetFlat('Items.Test.Value', 2)");
            string profileRoot = Path.Combine(root, "profiles", "Standard");
            Directory.CreateDirectory(profileRoot);
            string modlist = Path.Combine(profileRoot, "modlist.txt");
            File.WriteAllText(modlist, "+Alpha\n+Beta\n");

            ProfileScanReceipt receipt = ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", modlist), DateTimeOffset.UtcNow);

            VirtualFileShadow shadow = receipt.VirtualFileShadows.Single(value => value.RelativePath == "r6\\tweaks\\shared.tweak");
            Assert.AreEqual("Alpha", shadow.WinnerProvider);
            Assert.IsTrue(shadow.Providers.All(value => value.Sha256.Length == 64));
            SourceAnalysisFailure failure = receipt.SourceFailures!.Single(value => value.Surface == "TweakXL RED");
            Assert.AreEqual("Alpha", failure.Provider);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ManifestCapturesRedTweakTextWithItsFingerprint()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-red-tweak-manifest-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(root, "r6", "tweaks", "shared.tweak");
            WriteRoot(root, "r6\\tweaks\\shared.tweak", "first");
            DateTime timestamp = File.GetLastWriteTimeUtc(path);
            DeploymentFileManifest manifest = DeploymentFileManifest.Build([new DeploymentProvider("Alpha", root)]);
            DeploymentFileEntry file = manifest.Files.Single();

            ProfileFileSnapshot snapshot = manifest.Capture(file, true);
            File.WriteAllText(path, "other");
            File.SetLastWriteTimeUtc(path, timestamp);

            Assert.AreEqual("first", manifest.ReadText(file));
            Assert.AreEqual(snapshot.Sha256, manifest.Capture(file, true).Sha256);
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
    public void ScanRejectsALoneRedTweakChangedBeforeFinalValidation()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-red-tweak-mutation-" + Guid.NewGuid().ToString("N"));
        try
        {
            string tweak = Path.Combine(root, "mods", "Alpha", "r6", "tweaks", "active.tweak");
            Write(root, "Alpha", "r6\\tweaks\\active.tweak", "first");
            DateTime timestamp = File.GetLastWriteTimeUtc(tweak);
            string profileRoot = Path.Combine(root, "profiles", "Standard");
            Directory.CreateDirectory(profileRoot);
            string modlist = Path.Combine(profileRoot, "modlist.txt");
            File.WriteAllText(modlist, "+Alpha\n");

            Assert.ThrowsExactly<ProfileInputChangedException>(() => ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", modlist), DateTimeOffset.UtcNow, null, () =>
            {
                File.WriteAllText(tweak, "other");
                File.SetLastWriteTimeUtc(tweak, timestamp);
            }, CancellationToken.None));
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
    public void ScanRefreshesAStaleInMemoryArchiveFingerprint()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-stale-archive-cache-" + Guid.NewGuid().ToString("N"));
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
            Mo2ArchiveProfileScanner.ScanInstance(root, modlist);
            File.WriteAllBytes(archive, Enumerable.Repeat((byte)2, 32).ToArray());
            File.SetLastWriteTimeUtc(archive, timestamp);

            ProfileScanReceipt receipt = ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", modlist), DateTimeOffset.UtcNow);

            string currentHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(archive)));
            Assert.AreEqual(currentHash, receipt.EditableArchiveInventory!.Single().Sha256);
            Assert.AreEqual(1, receipt.Metrics!.RefreshedArchiveFingerprints);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanRejectsAFileThatChangesAgainDuringTheFreshRetry()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-fresh-retry-mutation-" + Guid.NewGuid().ToString("N"));
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
            Mo2ArchiveProfileScanner.ScanInstance(root, modlist);
            File.WriteAllBytes(archive, Enumerable.Repeat((byte)2, 32).ToArray());
            File.SetLastWriteTimeUtc(archive, timestamp);
            int validations = 0;

            ProfileInputChangedException exception = Assert.ThrowsExactly<ProfileInputChangedException>(() => ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", modlist), DateTimeOffset.UtcNow, null, () =>
            {
                validations++;
                File.WriteAllBytes(archive, Enumerable.Repeat((byte)(validations + 2), 32).ToArray());
                File.SetLastWriteTimeUtc(archive, timestamp);
            }, CancellationToken.None));

            Assert.AreEqual(2, validations);
            StringAssert.Contains(exception.Message, "fresh fingerprint", StringComparison.OrdinalIgnoreCase);
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
    public void ScanAcceptsTimestampOnlyOodleChange()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-oodle-metadata-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string oodle = Path.Combine(game, "bin", "x64", "oo2ext_7_win64.dll");
            WriteRoot(game, "bin\\x64\\oo2ext_7_win64.dll", "oodle");
            File.WriteAllText(Path.Combine(root, "ModOrganizer.ini"), "gamePath=@ByteArray(game)\n");
            string profileRoot = Path.Combine(root, "profiles", "Standard");
            Directory.CreateDirectory(profileRoot);
            string modlist = Path.Combine(profileRoot, "modlist.txt");
            File.WriteAllText(modlist, string.Empty);
            DateTime timestamp = File.GetLastWriteTimeUtc(oodle);

            ProfileScanReceipt receipt = ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", modlist), DateTimeOffset.UtcNow, null, () => File.SetLastWriteTimeUtc(oodle, timestamp.AddSeconds(2)), CancellationToken.None);

            Assert.AreEqual("Standard", receipt.ProfileName);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ScanRejectsSameMetadataOodleByteChange()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-oodle-content-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string oodle = Path.Combine(game, "bin", "x64", "oo2ext_7_win64.dll");
            WriteRoot(game, "bin\\x64\\oo2ext_7_win64.dll", "oodle");
            File.WriteAllText(Path.Combine(root, "ModOrganizer.ini"), "gamePath=@ByteArray(game)\n");
            string profileRoot = Path.Combine(root, "profiles", "Standard");
            Directory.CreateDirectory(profileRoot);
            string modlist = Path.Combine(profileRoot, "modlist.txt");
            File.WriteAllText(modlist, string.Empty);
            DateTime timestamp = File.GetLastWriteTimeUtc(oodle);

            Assert.ThrowsExactly<ProfileInputChangedException>(() => ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", modlist), DateTimeOffset.UtcNow, null, () =>
            {
                File.WriteAllText(oodle, "OOdle");
                File.SetLastWriteTimeUtc(oodle, timestamp);
            }, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
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
            VortexManagerContext context = Context(DateTimeOffset.UtcNow, "vortex-profile", "Default", game, staging, true, [new("alpha", "Alpha", provider, 0)], new Dictionary<string, string> { ["r6\\scripts\\deployed.reds"] = "alpha" }, [], null);

            Assert.ThrowsExactly<CrossManagerDeploymentException>(() => ProfileScanCoordinator.Scan(root, new Mo2Profile("Standard", modlist), DateTimeOffset.UtcNow, null, () =>
            {
                WriteRoot(provider, "r6\\scripts\\deployed.reds", "deployed");
                WriteRoot(game, "r6\\scripts\\deployed.reds", "deployed");
                File.WriteAllText(contextPath, JsonSerializer.Serialize(context));
            }, contextPath, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ManualScanRejectsAVortexDeploymentThatAppearsDuringAnalysis()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-manual-cross-manager-mutation-" + Guid.NewGuid().ToString("N"));
        try
        {
            string game = Path.Combine(root, "game");
            string staging = Path.Combine(root, "vortex-staging");
            string provider = Path.Combine(staging, "Alpha");
            Directory.CreateDirectory(Path.Combine(game, "archive", "pc", "content"));
            Directory.CreateDirectory(provider);
            string contextPath = Path.Combine(root, "vortex-context.json");
            VortexManagerContext context = Context(DateTimeOffset.UtcNow, "vortex-profile", "Default", game, staging, true, [new("alpha", "Alpha", provider, 0)], new Dictionary<string, string> { ["r6\\scripts\\deployed.reds"] = "alpha" }, [], null);

            Assert.ThrowsExactly<CrossManagerDeploymentException>(() => ProfileScanCoordinator.ScanManual(game, DateTimeOffset.UtcNow, null, () =>
            {
                WriteRoot(provider, "r6\\scripts\\deployed.reds", "deployed");
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

    private static VortexManagerContext Context(DateTimeOffset capturedAtUtc, string profileId, string profileName, string gameRoot, string stagingRoot, bool deploymentFresh, VortexProviderContext[] providers, Dictionary<string, string> deployedWinners, string[] archiveOrder, string? archiveOrderSha256, DateTimeOffset? heartbeatAtUtc = null, bool deploymentInventoryComplete = false, int deploymentFileCount = 0, int relevantDeploymentFileCount = 0)
    {
        VortexManagerContext context = new(1, string.Empty, capturedAtUtc, profileId, profileName, gameRoot, stagingRoot, deploymentFresh, providers, deployedWinners, archiveOrder, archiveOrderSha256, heartbeatAtUtc, deploymentInventoryComplete, deploymentFileCount, relevantDeploymentFileCount);
        return context with { ContextId = VortexManagerContextStore.ComputeContextId(context) };
    }
}

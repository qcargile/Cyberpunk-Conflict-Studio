using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace ConflictStudio.Core;

public sealed record ScanPhaseMetric(string Name, long ElapsedMilliseconds, int ItemCount);

public sealed record ProfileScanMetrics(long TotalElapsedMilliseconds, ScanPhaseMetric[] Phases);

public sealed record ScanProgress(string Phase, int Completed, int Total);

public sealed record ProfileScanReceipt(
    int SchemaVersion,
    string ProfileName,
    DateTimeOffset ScannedAtUtc,
    string[] ActiveProviders,
    string[] ArchiveOrder,
    RdarArchiveFailure[] ArchiveFailures,
    ResourceConflict[] ResourceConflicts,
    VirtualFileShadow[] VirtualFileShadows,
    InteractionFinding[] InteractionFindings,
    RedScriptFlowEvidence[] RedScriptFlows,
    SharedStateWriteFinding[] SharedStateWrites,
    LuaCallbackEvidence[] LuaCallbacks,
    TweakOverlap[] TweakOverlaps,
    ArchiveXlOperationChain[] ArchiveXlChains,
    ArchiveXlSourceFailure[] ArchiveXlFailures,
    SourceAnalysisFailure[]? SourceFailures = null,
    ProfileScanMetrics? Metrics = null,
    string? InstallationId = null,
    ArchiveConflictSummary[]? ArchiveSummaries = null,
    ArchiveOrderEvidence? ArchiveOrderEvidence = null,
    ResourcePathIndexEvidence? ResourcePathIndexEvidence = null,
    RdarArchiveWarning[]? ArchiveWarnings = null,
    Mo2Archive[]? ArchiveInventory = null,
    Mo2Archive[]? EditableArchiveInventory = null,
    string[]? EditableArchiveOrder = null,
    ArchiveOrderEvidence? EditableArchiveOrderEvidence = null,
    ModManagerKind ManagerKind = ModManagerKind.Mo2,
    string? ManagerContextPath = null,
    bool DeploymentFresh = true);

public static class ProfileScanCoordinator
{
    public static ProfileScanReceipt Scan(string mo2Root, Mo2Profile profile, DateTimeOffset scannedAtUtc)
        => Scan(mo2Root, profile, scannedAtUtc, null, CancellationToken.None);

    public static ProfileScanReceipt Scan(string mo2Root, Mo2Profile profile, DateTimeOffset scannedAtUtc, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
        => Scan(mo2Root, profile, scannedAtUtc, progress, null, cancellationToken);

    internal static ProfileScanReceipt Scan(string mo2Root, Mo2Profile profile, DateTimeOffset scannedAtUtc, IProgress<ScanProgress>? progress, Action? beforeFinalValidation, CancellationToken cancellationToken)
        => Scan(mo2Root, profile, scannedAtUtc, progress, beforeFinalValidation, null, cancellationToken);

    internal static ProfileScanReceipt Scan(string mo2Root, Mo2Profile profile, DateTimeOffset scannedAtUtc, IProgress<ScanProgress>? progress, Action? beforeFinalValidation, string? vortexContextPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mo2Root);
        ArgumentNullException.ThrowIfNull(profile);
        if (scannedAtUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Scan timestamps must use UTC.", nameof(scannedAtUtc));
        Stopwatch total = Stopwatch.StartNew();
        PreparedProfileScan prepared = PrepareMo2(mo2Root, profile, progress, vortexContextPath, cancellationToken);
        return ScanPrepared(prepared, scannedAtUtc, progress, beforeFinalValidation, total, cancellationToken);
    }

    public static ProfileScanReceipt ScanVortex(string contextPath, DateTimeOffset scannedAtUtc, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
        => ScanVortex(contextPath, scannedAtUtc, progress, null, cancellationToken);

    internal static ProfileScanReceipt ScanVortex(string contextPath, DateTimeOffset scannedAtUtc, IProgress<ScanProgress>? progress, Action? beforeFinalValidation, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contextPath);
        if (scannedAtUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Scan timestamps must use UTC.", nameof(scannedAtUtc));
        Stopwatch total = Stopwatch.StartNew();
        PreparedProfileScan prepared = PrepareVortex(contextPath, scannedAtUtc, progress, cancellationToken);
        return ScanPrepared(prepared, scannedAtUtc, progress, beforeFinalValidation, total, cancellationToken);
    }

    public static ProfileScanReceipt ScanManual(string gameRoot, DateTimeOffset scannedAtUtc, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);
        if (scannedAtUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Scan timestamps must use UTC.", nameof(scannedAtUtc));
        Stopwatch total = Stopwatch.StartNew();
        PreparedProfileScan prepared = PrepareManual(gameRoot, progress, cancellationToken);
        return ScanPrepared(prepared, scannedAtUtc, progress, null, total, cancellationToken);
    }

    private static PreparedProfileScan PrepareMo2(string mo2Root, Mo2Profile profile, IProgress<ScanProgress>? progress, string? vortexContextPath, CancellationToken cancellationToken)
    {
        Mo2InstancePaths instancePaths = Mo2InstancePathResolver.Resolve(mo2Root);
        CrossManagerGuardBinding? crossManagerBinding = null;
        if (instancePaths.GameRoot is not null)
        {
            string vortexContext = vortexContextPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cyberpunk Conflict Studio", "vortex", "context.json");
            VortexDeploymentGuard.RequireNoDeployment(instancePaths.GameRoot, vortexContext);
            crossManagerBinding = new CrossManagerGuardBinding(instancePaths.GameRoot, vortexContext);
        }
        Stopwatch phase = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new ScanProgress("deployment · reading MO2 profile", 0, 4));
        ProfileInputSnapshot profileSnapshot = ProfileInputGuard.Capture(profile.ModlistPath);
        Mo2ActiveProvider[] activeProviderEntries = Mo2ProfileReader.ReadActiveProviderEntries(profileSnapshot.Content);
        string[] activeProviders = activeProviderEntries.Select(value => value.Name).ToArray();
        progress?.Report(new ScanProgress("deployment · resolving MO2 paths", 1, 4));
        if (activeProviderEntries.Length > 0 && !Directory.Exists(instancePaths.ModsRoot)) throw new DirectoryNotFoundException($"The configured MO2 mods directory does not exist: {instancePaths.ModsRoot}. Check MO2 Settings > Paths and make sure Conflict Studio starts in the instance folder containing ModOrganizer.ini.");
        Mo2ActiveProvider[] missingProviders = activeProviderEntries.Where(value => !Directory.Exists(Path.Combine(instancePaths.ModsRoot, value.Name))).ToArray();
        if (activeProviderEntries.Length > 0 && missingProviders.Length == activeProviderEntries.Length) throw new DirectoryNotFoundException($"None of the {activeProviderEntries.Length:N0} active MO2 mods were found under {instancePaths.ModsRoot}. Check the instance's mod_directory setting.");
        SourceAnalysisFailure[] pathFailures = missingProviders.Select(value => new SourceAnalysisFailure(value.Name, value.Name, "MO2 path", "The active mod directory was not found under the configured MO2 mods path.")).ToArray();
        DeploymentProvider[] deploymentProviders = Mo2ProviderTopology.Discover(mo2Root, activeProviderEntries);
        progress?.Report(new ScanProgress("deployment · indexing archives", 2, 4));
        string fingerprintCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cyberpunk Conflict Studio", "cache", "archive-fingerprints-v1.json");
        Mo2ArchiveProfile legacyArchives = Mo2ArchiveProfileScanner.ScanInstance(mo2Root, profile.ModlistPath, activeProviderEntries, fingerprintCache, false, cancellationToken);
        RedmodArchiveProfile redmods = RedmodArchiveProfileScanner.Scan(deploymentProviders, cancellationToken);
        Mo2ArchiveProfile archives = PackedArchiveTopology.Compose(legacyArchives, redmods);
        ProfileInputSnapshot[] inputs = [profileSnapshot, .. EvidenceSnapshots(archives.OrderEvidence)];
        progress?.Report(new ScanProgress("deployment · capturing active files", 3, 4));
        EvidenceFileCapture evidenceFiles = EvidenceFileSnapshots(archives, deploymentProviders, instancePaths.GameRoot, cancellationToken);
        progress?.Report(new ScanProgress("deployment · ready", 4, 4));
        return new PreparedProfileScan(ModManagerKind.Mo2, profile.Name, activeProviders, ProfileInstallationIdentity.Create(mo2Root), instancePaths, deploymentProviders, legacyArchives, redmods, archives, null, inputs, archives.OrderEvidence?.AbsentSources ?? [], evidenceFiles.Files, null, true, phase.ElapsedMilliseconds, pathFailures.Concat(evidenceFiles.Failures).ToArray(), null, crossManagerBinding);
    }

    private static PreparedProfileScan PrepareVortex(string contextPath, DateTimeOffset scannedAtUtc, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        Stopwatch phase = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new ScanProgress("deployment · reading Vortex profile", 0, 4));
        string fullContextPath = Path.GetFullPath(contextPath);
        ProfileInputSnapshot contextSnapshot = ProfileInputGuard.Capture(fullContextPath);
        VortexManagerContext context = VortexManagerContextStore.Read(contextSnapshot.Content);
        TimeSpan contextAge = scannedAtUtc - context.CapturedAtUtc;
        bool bridgeLive = contextAge >= TimeSpan.FromSeconds(-5) && contextAge <= TimeSpan.FromSeconds(15);
        bool managerEvidenceReliable = bridgeLive && context.DeploymentFresh;
        DeploymentProvider[] active = managerEvidenceReliable ? context.Providers.OrderBy(value => value.Order).Select(value => new DeploymentProvider(value.Name, value.RootPath, null, value.Id)).ToArray() : [];
        DeploymentProvider[] providers = [.. active, new DeploymentProvider("Game directory", context.GameRoot, -1, "game-directory")];
        progress?.Report(new ScanProgress("deployment · indexing archives", 1, 4));
        VortexManagerContext scanContext = managerEvidenceReliable ? context : context with { DeploymentFresh = false, Providers = [], DeployedWinners = [] };
        Mo2ArchiveProfile legacyArchives = VortexArchiveProfileScanner.Scan(scanContext, cancellationToken);
        RedmodArchiveProfile redmods = RedmodArchiveProfileScanner.Scan(providers, cancellationToken);
        Mo2ArchiveProfile archives = PackedArchiveTopology.Compose(legacyArchives, redmods);
        progress?.Report(new ScanProgress("deployment · resolving active files", 2, 4));
        Mo2InstancePaths paths = new(context.StagingRoot, context.StagingRoot, string.Empty, string.Empty, context.GameRoot, context.ProfileName);
        ProfileInputSnapshot[] inputs = EvidenceSnapshots(archives.OrderEvidence);
        bool deploymentFresh = context.DeploymentFresh && bridgeLive;
        SourceAnalysisFailure[] failures = deploymentFresh ? [] : [new SourceAnalysisFailure("Vortex", Path.GetFileName(fullContextPath), "Deployment", bridgeLive ? "Vortex has pending deployment changes. Deploy the active profile before relying on winners or applying an order." : "The Vortex bridge is offline or stale. Only deployed game files were scanned; open Vortex and refresh before relying on provider losers or applying an order.")];
        VortexContextBinding binding = new(fullContextPath, context.ContextId, context.ProfileId);
        progress?.Report(new ScanProgress("deployment · capturing active files", 3, 4));
        EvidenceFileCapture evidenceFiles = EvidenceFileSnapshots(archives, providers, context.GameRoot, cancellationToken);
        progress?.Report(new ScanProgress("deployment · ready", 4, 4));
        return new PreparedProfileScan(ModManagerKind.Vortex, context.ProfileName, active.Select(value => value.Name).ToArray(), ProfileInstallationIdentity.Create("Vortex", context.GameRoot + "|" + context.ProfileId), paths, providers, legacyArchives, redmods, archives, managerEvidenceReliable ? context.DeployedWinners : null, inputs, archives.OrderEvidence?.AbsentSources ?? [], evidenceFiles.Files, fullContextPath, deploymentFresh, phase.ElapsedMilliseconds, failures.Concat(evidenceFiles.Failures).ToArray(), binding);
    }

    private static PreparedProfileScan PrepareManual(string gameRoot, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        Stopwatch phase = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new ScanProgress("deployment · reading deployed game", 0, 3));
        string root = Path.GetFullPath(gameRoot);
        if (!Directory.Exists(Path.Combine(root, "archive", "pc", "content")) && !File.Exists(Path.Combine(root, "bin", "x64", "Cyberpunk2077.exe"))) throw new DirectoryNotFoundException("Choose the Cyberpunk 2077 installation folder.");
        DeploymentProvider[] providers = [new DeploymentProvider("Game directory", root, -1, "game-directory")];
        Mo2ArchiveProfile legacyArchives = ManualArchiveProfileScanner.Scan(root, cancellationToken);
        progress?.Report(new ScanProgress("deployment · indexing archives", 1, 3));
        RedmodArchiveProfile redmods = RedmodArchiveProfileScanner.Scan(providers, cancellationToken);
        Mo2ArchiveProfile archives = PackedArchiveTopology.Compose(legacyArchives, redmods);
        progress?.Report(new ScanProgress("deployment · capturing active files", 2, 3));
        Mo2InstancePaths paths = new(root, root, string.Empty, root, root, "Deployed game");
        ProfileInputSnapshot[] inputs = EvidenceSnapshots(archives.OrderEvidence);
        EvidenceFileCapture evidenceFiles = EvidenceFileSnapshots(archives, providers, root, cancellationToken);
        progress?.Report(new ScanProgress("deployment · ready", 3, 3));
        return new PreparedProfileScan(ModManagerKind.Manual, "Deployed game", ["Game directory"], ProfileInstallationIdentity.Create("Manual", root), paths, providers, legacyArchives, redmods, archives, null, inputs, archives.OrderEvidence?.AbsentSources ?? [], evidenceFiles.Files, null, true, phase.ElapsedMilliseconds, evidenceFiles.Failures);
    }

    private static ProfileScanReceipt ScanPrepared(PreparedProfileScan prepared, DateTimeOffset scannedAtUtc, IProgress<ScanProgress>? progress, Action? beforeFinalValidation, Stopwatch total, CancellationToken cancellationToken)
    {
        List<ScanPhaseMetric> phases = [new ScanPhaseMetric("deployment", prepared.DeploymentElapsedMilliseconds, prepared.Archives.Archives.Length)];
        progress?.Report(new ScanProgress("deployment", 1, 1));
        Stopwatch phase = Stopwatch.StartNew();
        List<RdarArchiveInput> archiveInputs = prepared.Archives.Archives.Select(value => new RdarArchiveInput(value.Provider, value.PhysicalPath, value.ArchiveName)).ToList();
        string? oodlePath = prepared.Paths.GameRoot is null ? null : Path.Combine(prepared.Paths.GameRoot, "bin", "x64", "oo2ext_7_win64.dll");
        RdarResourceScanResult resources = RdarResourceScanner.ScanResilient(archiveInputs, null, oodlePath, progress, cancellationToken);
        ResourcePathIndexResult pathIndex = ResourcePathIndex.Resolve(prepared.Paths, prepared.DeploymentProviders, resources.Resources.Select(value => value.ResourceHash).ToHashSet(), cancellationToken);
        ResourceProvider[] resolvedResources = ResourcePathIndex.Apply(resources.Resources, pathIndex.Paths);
        resolvedResources = RdarPayloadFingerprint.Apply(resolvedResources, resources.PayloadIndexes ?? [], oodlePath, cancellationToken);
        ResourceConflict[] resourceConflicts = ResourceConflictAnalyzer.Analyze(resolvedResources, prepared.Archives.EffectiveOrder);
        if (resources.Failures.Length > 0 || prepared.Archives.OrderEvidence?.Kind == ArchiveOrderEvidenceKind.Unresolved) resourceConflicts = resourceConflicts.Select(value => value with { Kind = ResourceConflictKind.Unresolved, EngineWinnerArchive = "unresolved" }).ToArray();
        bool archiveResultsResolved = prepared.Archives.OrderEvidence?.Kind != ArchiveOrderEvidenceKind.Unresolved && resources.Failures.Length == 0;
        ArchiveConflictSummary[] archiveSummaries = ArchiveResourceIndexBuilder.Build(resolvedResources, prepared.Archives.Archives, archiveResultsResolved ? prepared.Archives.EffectiveOrder : [], resources.Failures, prepared.Redmods.Failures.Length > 0);
        phases.Add(new ScanPhaseMetric("packed resources", phase.ElapsedMilliseconds, resources.Resources.Length));
        phase.Restart();
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new ScanProgress("effective source", 0, 2));
        ModSourceInventory inventory = ModSourceScanner.ScanProviders(prepared.DeploymentProviders, prepared.DeployedWinners, cancellationToken);
        progress?.Report(new ScanProgress("effective source", 1, 2));
        VirtualFileShadow[] virtualShadows = VirtualFileShadowScanner.ScanProviders(prepared.DeploymentProviders, prepared.DeployedWinners, cancellationToken);
        RedScriptFlowEvidence[] redScriptFlows = RedScriptFlowEvidenceAnalyzer.Analyze(inventory.RedScripts);
        SharedStateWriteFinding[] sharedStateWrites = SharedStateWriteAnalyzer.Analyze(inventory.RedScripts, inventory.LuaSources);
        LuaCallbackEvidence[] luaCallbacks = LuaCallbackEvidenceAnalyzer.Analyze(inventory.LuaSources);
        TweakAnalysisResult tweakAnalysis = TweakInteractionAnalyzer.AnalyzeDetailed(inventory.TweakSources);
        InteractionFinding[] findings = InteractionReportBuilder.Build(inventory, redScriptFlows, luaCallbacks, tweakAnalysis.Overlaps);
        phases.Add(new ScanPhaseMetric("effective source", phase.ElapsedMilliseconds, inventory.RedScripts.Length + inventory.LuaSources.Length + inventory.TweakSources.Length));
        progress?.Report(new ScanProgress("effective source", 2, 2));
        phase.Restart();
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new ScanProgress("ArchiveXL", 0, 1));
        ArchiveXlSourceScanResult archiveXlSources = ArchiveXlSourceScanner.Scan(prepared.DeploymentProviders.Select(provider => new ArchiveXlProviderSource(provider.Name, provider.RootPath, provider.ManagerId)).ToArray(), prepared.DeployedWinners, cancellationToken);
        ArchiveXlAnalysisResult archiveXlAnalysis = ArchiveXlManifestAnalyzer.AnalyzeDetailed(archiveXlSources.Sources);
        ArchiveXlOperationChain[] archiveXlChains = ArchiveXlProviderChainAnalyzer.Group(archiveXlAnalysis.Operations);
        ArchiveXlSourceFailure[] archiveXlFailures = archiveXlSources.Failures.Concat(archiveXlAnalysis.Failures).ToArray();
        phases.Add(new ScanPhaseMetric("ArchiveXL", phase.ElapsedMilliseconds, archiveXlSources.Sources.Length));
        progress?.Report(new ScanProgress("ArchiveXL", 1, 1));
        phase.Restart();
        beforeFinalValidation?.Invoke();
        ProfileInputSnapshot[] requiredInputs = prepared.RequiredInputs.DistinctBy(value => value.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        int validationTotal = requiredInputs.Length + prepared.RequiredAbsentInputs.Length + prepared.EvidenceFiles.Length + (prepared.VortexBinding is null ? 0 : 1) + (prepared.CrossManagerBinding is null ? 0 : 1);
        int validationCompleted = 0;
        progress?.Report(new ScanProgress("validation", validationCompleted, Math.Max(1, validationTotal)));
        foreach (ProfileInputSnapshot input in requiredInputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProfileInputGuard.RequireUnchanged(input);
            progress?.Report(new ScanProgress("validation", ++validationCompleted, Math.Max(1, validationTotal)));
        }
        foreach (string path in prepared.RequiredAbsentInputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProfileInputGuard.RequireStillAbsent(path);
            progress?.Report(new ScanProgress("validation", ++validationCompleted, Math.Max(1, validationTotal)));
        }
        int validationBase = validationCompleted;
        ProfileInputGuard.RequireAllUnchanged(prepared.EvidenceFiles, cancellationToken, count => progress?.Report(new ScanProgress("validation", validationBase + count, Math.Max(1, validationTotal))));
        validationCompleted += prepared.EvidenceFiles.Length;
        if (prepared.VortexBinding is not null)
        {
            VortexManagerContext current = VortexManagerContextStore.Read(prepared.VortexBinding.ContextPath);
            if (!string.Equals(current.ContextId, prepared.VortexBinding.ContextId, StringComparison.Ordinal) || !string.Equals(current.ProfileId, prepared.VortexBinding.ProfileId, StringComparison.Ordinal)) throw new ProfileInputChangedException("The active Vortex profile or deployment changed during the scan. Run the scan again to produce one consistent result.");
            progress?.Report(new ScanProgress("validation", ++validationCompleted, Math.Max(1, validationTotal)));
        }
        if (prepared.CrossManagerBinding is not null)
        {
            VortexDeploymentGuard.RequireNoDeployment(prepared.CrossManagerBinding.GameRoot, prepared.CrossManagerBinding.ContextPath);
            progress?.Report(new ScanProgress("validation", ++validationCompleted, Math.Max(1, validationTotal)));
        }
        phases.Add(new ScanPhaseMetric("validation", phase.ElapsedMilliseconds, validationTotal));
        total.Stop();
        return new ProfileScanReceipt(
            2,
            prepared.ProfileName,
            scannedAtUtc,
            prepared.ActiveProviders,
            prepared.Archives.EffectiveOrder,
            resources.Failures,
            resourceConflicts,
            virtualShadows,
            findings,
            redScriptFlows,
            sharedStateWrites,
            luaCallbacks,
            tweakAnalysis.Overlaps,
            archiveXlChains,
            archiveXlFailures,
            inventory.Failures.Concat(tweakAnalysis.Failures).Concat(prepared.Redmods.Failures).Concat(prepared.AdditionalFailures).ToArray(),
            new ProfileScanMetrics(total.ElapsedMilliseconds, phases.ToArray()),
            prepared.InstallationId,
            archiveSummaries,
            prepared.Archives.OrderEvidence,
            pathIndex.Evidence,
            resources.Warnings,
            prepared.Archives.Archives,
            prepared.LegacyArchives.Archives,
            prepared.LegacyArchives.EffectiveOrder,
            prepared.LegacyArchives.OrderEvidence,
            prepared.ManagerKind,
            prepared.ManagerContextPath,
            prepared.DeploymentFresh);
    }

    private static ProfileInputSnapshot[] EvidenceSnapshots(ArchiveOrderEvidence? evidence)
        => (evidence?.SourceFingerprints ?? []).Select(value => new ProfileInputSnapshot(value.Key, value.Value, [])).ToArray();

    private static EvidenceFileCapture EvidenceFileSnapshots(Mo2ArchiveProfile archives, IReadOnlyList<DeploymentProvider> providers, string? gameRoot, CancellationToken cancellationToken)
    {
        List<ProfileFileSnapshot> files = [];
        List<SourceAnalysisFailure> failures = [];
        foreach (Mo2Archive archive in archives.Archives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { files.Add(ProfileInputGuard.CaptureFile(archive.PhysicalPath, archive.Sha256)); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ProfileInputChangedException) { failures.Add(new SourceAnalysisFailure(archive.Provider, archive.ArchiveName, "Packed archive", exception.Message)); }
        }

        string[] relativeRoots = ["archive\\pc\\mod", "bin\\x64\\plugins", "engine\\config", "r6\\input", "r6\\scripts", "r6\\tweaks", "red4ext\\plugins"];
        string[] sourceExtensions = [".reds", ".lua", ".yaml", ".yml", ".xl"];
        Dictionary<string, List<(string Provider, string Path)>> candidates = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, bool> required = new(StringComparer.OrdinalIgnoreCase);
        foreach (DeploymentProvider provider in providers)
        {
            foreach (string relativeRoot in relativeRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string root = Path.Combine(provider.RootPath, relativeRoot);
                if (!Directory.Exists(root)) continue;
                try
                {
                    foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (string.Equals(Path.GetExtension(path), ".archive", StringComparison.OrdinalIgnoreCase)) continue;
                        string relative = Path.GetRelativePath(provider.RootPath, path).Replace('/', '\\');
                        if (DeploymentFilePolicy.IsMutableOutput(relative)) continue;
                        if (!candidates.TryGetValue(relative, out List<(string Provider, string Path)>? values)) candidates[relative] = values = [];
                        values.Add((provider.Name, path));
                        if (sourceExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) required[path] = true;
                        if (relative.Equals("bin\\x64\\plugins\\cyber_engine_tweaks\\tweakdb\\usedhashes.kark", StringComparison.OrdinalIgnoreCase)) required[path] = true;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    failures.Add(new SourceAnalysisFailure(provider.Name, relativeRoot, "Deployment evidence", exception.Message));
                }
            }
        }
        foreach (List<(string Provider, string Path)> values in candidates.Values.Where(value => value.Count > 1)) foreach ((string _, string path) in values) required[path] = true;
        if (gameRoot is not null)
        {
            string oodlePath = Path.Combine(gameRoot, "bin", "x64", "oo2ext_7_win64.dll");
            if (File.Exists(oodlePath)) required.TryAdd(oodlePath, false);
        }
        foreach ((string path, bool hashContent) in required)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { files.Add(ProfileInputGuard.CaptureFile(path, hashContent)); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ProfileInputChangedException) { failures.Add(new SourceAnalysisFailure("Deployment evidence", Path.GetFileName(path), "Evidence snapshot", exception.Message)); }
        }
        return new EvidenceFileCapture(files.DistinctBy(value => value.Path, StringComparer.OrdinalIgnoreCase).ToArray(), failures.ToArray());
    }

    private sealed record EvidenceFileCapture(ProfileFileSnapshot[] Files, SourceAnalysisFailure[] Failures);

    private sealed record PreparedProfileScan(ModManagerKind ManagerKind, string ProfileName, string[] ActiveProviders, string InstallationId, Mo2InstancePaths Paths, DeploymentProvider[] DeploymentProviders, Mo2ArchiveProfile LegacyArchives, RedmodArchiveProfile Redmods, Mo2ArchiveProfile Archives, IReadOnlyDictionary<string, string>? DeployedWinners, ProfileInputSnapshot[] RequiredInputs, string[] RequiredAbsentInputs, ProfileFileSnapshot[] EvidenceFiles, string? ManagerContextPath, bool DeploymentFresh, long DeploymentElapsedMilliseconds, SourceAnalysisFailure[]? Failures = null, VortexContextBinding? VortexBinding = null, CrossManagerGuardBinding? CrossManagerBinding = null)
    {
        public SourceAnalysisFailure[] AdditionalFailures => Failures ?? [];
    }

    private sealed record VortexContextBinding(string ContextPath, string ContextId, string ProfileId);
    private sealed record CrossManagerGuardBinding(string GameRoot, string ContextPath);
}

public static class ProfileInstallationIdentity
{
    public static string Create(string mo2Root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mo2Root);
        string identity = Path.GetFullPath(mo2Root).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    public static string Create(string manager, string identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manager);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        string value = manager.ToUpperInvariant() + "|" + identity.ToUpperInvariant();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}

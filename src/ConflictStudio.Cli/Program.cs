using ConflictStudio.Core;

if (args.Length == 0 || args[0] is "--help" or "-h")
{
    Console.WriteLine("ConflictStudio.Cli scan --mo2 <root> --profile <name> --output <receipt.json> [--capsule <directory>] [--previous <receipt.json>] [--decisions <directory>]");
    Console.WriteLine("ConflictStudio.Cli scan --vortex-context <context.json> --output <receipt.json> [--capsule <directory>] [--previous <receipt.json>] [--decisions <directory>]");
    Console.WriteLine("ConflictStudio.Cli scan --game <Cyberpunk root> --output <receipt.json> [--capsule <directory>] [--previous <receipt.json>] [--decisions <directory>]");
    Console.WriteLine("ConflictStudio.Cli probe-import --manifest <probe-manifest.json> --log <CET log> --output <runtime-receipt.json> [--answers <manual-answers.json>] [--capsule <casefile-directory>]");
    return 0;
}

if (string.Equals(args[0], "probe-import", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        Dictionary<string, string> probeOptions = ParseOptions(args[1..]);
        RuntimeProbeBundleManifest manifest = RuntimeProbeBundleStore.ReadManifest(Required(probeOptions, "--manifest"));
        string? answersPath = Optional(probeOptions, "--answers");
        Dictionary<string, string>? answers = answersPath is null ? null : RuntimeProbeBundleStore.ReadManualAnswers(answersPath);
        RuntimeProbeReceipt receipt = RuntimeProbeReceiptReader.Read(manifest, File.ReadAllText(Required(probeOptions, "--log")), DateTimeOffset.UtcNow, answers);
        string output = Required(probeOptions, "--output");
        RuntimeProbeBundleStore.WriteReceipt(output, receipt);
        string? capsule = Optional(probeOptions, "--capsule");
        if (capsule is not null) RuntimeProbeBundleStore.WriteCasefileReceipt(capsule, receipt);
        if (!receipt.CompleteRun)
        {
            Console.Error.WriteLine($"PROBE INCOMPLETE missing={receipt.Observations.Count(value => value.State == RuntimeProbeObservationState.Missing)} output={Path.GetFullPath(output)}");
            return 3;
        }
        Console.WriteLine($"PROBE PASS complete={receipt.CompleteRun} observed={receipt.Observations.Count(value => value.State == RuntimeProbeObservationState.Observed)} failed={receipt.Observations.Count(value => value.State == RuntimeProbeObservationState.Failed)} manual={receipt.Observations.Count(value => value.State == RuntimeProbeObservationState.ManualRequired)} recorded={receipt.Observations.Count(value => value.State == RuntimeProbeObservationState.ManualRecorded)} missing={receipt.Observations.Count(value => value.State == RuntimeProbeObservationState.Missing)} output={Path.GetFullPath(output)}");
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"PROBE FAILED {exception.Message}");
        return 1;
    }
}

if (!string.Equals(args[0], "scan", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Unknown command. Use --help.");
    return 2;
}

using CancellationTokenSource cancellation = new();
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
Console.CancelKeyPress += cancelHandler;

try
{
    Dictionary<string, string> options = ParseOptions(args[1..]);
    string output = Required(options, "--output");
    string? vortexContext = Optional(options, "--vortex-context");
    string? gameRoot = Optional(options, "--game");
    ProfileScanReceipt receipt;
    if (vortexContext is not null) receipt = ProfileScanCoordinator.ScanVortex(vortexContext, DateTimeOffset.UtcNow, new CliProgress(), cancellation.Token);
    else if (gameRoot is not null) receipt = ProfileScanCoordinator.ScanManual(gameRoot, DateTimeOffset.UtcNow, new CliProgress(), cancellation.Token);
    else
    {
        string mo2Root = Required(options, "--mo2");
        string profileName = Required(options, "--profile");
        Mo2Profile profile = Mo2ProfileDiscovery.Discover(mo2Root).SingleOrDefault(value => string.Equals(value.Name, profileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"MO2 profile was not found: {profileName}");
        receipt = ProfileScanCoordinator.Scan(mo2Root, profile, DateTimeOffset.UtcNow, new CliProgress(), cancellation.Token);
    }
    string? previousPath = Optional(options, "--previous");
    if (previousPath is not null && File.Exists(previousPath))
    {
        ProfileScanDrift drift = ProfileScanDriftAnalyzer.Compare(ProfileScanReceiptStore.Read(previousPath), receipt);
        Console.WriteLine($"DRIFT newResources={drift.NewResourceConflicts.Length} removedResources={drift.RemovedResourceConflicts.Length} changedResources={drift.ChangedResourceConflicts.Length} newShadows={drift.NewVirtualShadows.Length} removedShadows={drift.RemovedVirtualShadows.Length} changedShadows={drift.ChangedVirtualShadows.Length} newFindings={drift.NewInteractionFindings.Length} removedFindings={drift.RemovedInteractionFindings.Length} changedFindings={drift.ChangedInteractionFindings.Length} newWork={drift.NewWorkItems.Length} removedWork={drift.RemovedWorkItems.Length} changedWork={drift.ChangedWorkItems.Length}");
    }
    ProfileScanReceiptStore.Write(output, receipt);
    string? capsuleDirectory = Optional(options, "--capsule");
    string? decisionDirectory = Optional(options, "--decisions");
    EvidenceDecision[] decisions = decisionDirectory is null ? [] : new EvidenceDecisionStore(decisionDirectory).Load();
    ConflictWorkItem[] workQueue = ConflictWorkQueueBuilder.Build(receipt, decisions);
    if (capsuleDirectory is not null) SupportCapsuleWriter.Write(capsuleDirectory, SupportCapsuleBuilder.Build(receipt, decisions));
    Console.WriteLine($"QUEUE attention={workQueue.Count(value => value.State == ConflictWorkState.NeedsAttention)} review={workQueue.Count(value => value.State == ConflictWorkState.ReviewWhenRelevant)} reviewed={workQueue.Count(value => value.State == ConflictWorkState.Reviewed)} noAction={workQueue.Count(value => value.State == ConflictWorkState.NoActionNeeded)}");
    ConflictWorkItem[] codeCases = workQueue.Where(value => value.Surface != ConflictSurface.PackedResource).ToArray();
    Console.WriteLine($"CODE proven={codeCases.Count(value => value.CaseKind == ConflictCaseKind.ProvenConflict && value.State != ConflictWorkState.Reviewed)} decisions={codeCases.Count(value => value.IsActionable && value.CaseKind != ConflictCaseKind.ProvenConflict && value.State != ConflictWorkState.Reviewed)} reviewed={codeCases.Count(value => value.State == ConflictWorkState.Reviewed)} compatible={codeCases.Count(value => !value.IsActionable && value.State != ConflictWorkState.Reviewed)}");
    RuntimeProbeManifest probes = RuntimeProbeManifestBuilder.Build(receipt);
    Console.WriteLine($"PROBES tweak={probes.Requests.Count(value => value.Kind == RuntimeProbeKind.PostInitializationTweakValue)} callback={probes.Requests.Count(value => value.Kind == RuntimeProbeKind.CallbackDelivery)} behavior={probes.Requests.Count(value => value.Kind == RuntimeProbeKind.BehaviorCheck)}");
    if (receipt.Metrics is not null) Console.WriteLine($"TIMING totalMs={receipt.Metrics.TotalElapsedMilliseconds} {string.Join(' ', receipt.Metrics.Phases.Select(value => $"{value.Name.Replace(' ', '-')}Ms={value.ElapsedMilliseconds}"))}");
    Console.WriteLine($"SCAN PASS manager={receipt.ManagerKind} profile={receipt.ProfileName} providers={receipt.ActiveProviders.Length} archives={receipt.ArchiveOrder.Length} resourceConflicts={receipt.ResourceConflicts.Length} findings={receipt.InteractionFindings.Length} output={Path.GetFullPath(output)}");
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("SCAN CANCELLED");
    return 130;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"SCAN FAILED {exception.Message}");
    return 1;
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}

static Dictionary<string, string> ParseOptions(string[] values)
{
    Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase);
    for (int index = 0; index < values.Length; index += 2)
    {
        if (index + 1 >= values.Length || !values[index].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Options must use --name value pairs.");
        options.Add(values[index], values[index + 1]);
    }
    return options;
}

static string Required(IReadOnlyDictionary<string, string> options, string name)
    => options.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException($"Missing required option: {name}");

static string? Optional(IReadOnlyDictionary<string, string> options, string name)
    => options.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : null;

sealed class CliProgress : IProgress<ScanProgress>
{
    private string? _phase;

    public void Report(ScanProgress value)
    {
        if (_phase == value.Phase && value.Completed != value.Total && value.Completed % 50 != 0) return;
        _phase = value.Phase;
        Console.WriteLine($"PROGRESS phase={value.Phase.Replace(' ', '-')} completed={value.Completed} total={value.Total}");
    }
}

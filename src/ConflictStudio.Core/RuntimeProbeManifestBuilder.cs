using System.Text.Json.Serialization;

namespace ConflictStudio.Core;

public enum RuntimeProbeKind { ProviderPresence, PostInitializationTweakValue, CallbackDelivery, SharedStateValue, BehaviorCheck }

public sealed record RuntimeProbeRequest(RuntimeProbeKind Kind, string Target, string[] Providers, string Observation, string Decides)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TweakRuntimeEvidence? TweakRuntimeEvidence { get; init; }
}

public sealed record RuntimeProbeManifest(int SchemaVersion, string ProfileName, DateTimeOffset CreatedAtUtc, RuntimeProbeRequest[] Requests, string? InstallationId = null);

public static class RuntimeProbeManifestBuilder
{
    public static RuntimeProbeManifest Build(ProfileScanReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        List<RuntimeProbeRequest> requests = [];
        HashSet<string> conflictTargets = ConflictWorkQueueBuilder.Build(receipt, []).Where(value => value.IsCodeCase && value.IsActionable).Select(value => value.Target).ToHashSet(StringComparer.Ordinal);
        HashSet<string> runtimeTargets = new(StringComparer.Ordinal);
        foreach (SharedStateWriteFinding finding in receipt.SharedStateWrites.Where(value => value.CompetingValues().Any() && conflictTargets.Contains(value.Target)))
        {
            string[] providers = finding.CompetingValues().SelectMany(value => new[] { value.First.Provider, value.Second.Provider }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            Add(requests, new RuntimeProbeRequest(RuntimeProbeKind.PostInitializationTweakValue, finding.Target, providers,
                $"Record the post-initialization snapshot of {finding.Target}, five seconds after CET updates begin.",
                "The observed value at that point, not a guaranteed final winner or a gameplay-failure diagnosis."));
        }
        foreach (InteractionFinding finding in receipt.InteractionFindings.Where(value => value.TweakRuntimeEvidence?.CompetingValues().Any() == true && conflictTargets.Contains(value.Target)))
        {
            runtimeTargets.Add(finding.Target);
            Add(requests, new RuntimeProbeRequest(RuntimeProbeKind.PostInitializationTweakValue, finding.Target, finding.Providers, $"Record the post-initialization snapshot of {finding.Target}, five seconds after CET updates begin, then manually observe its value after the relevant runtime path.", "The snapshot measures only that moment. Later runtime writes require a separate final-value observation; no winner, value equality, or incompatibility is established.") { TweakRuntimeEvidence = finding.TweakRuntimeEvidence });
            Add(requests, new RuntimeProbeRequest(RuntimeProbeKind.SharedStateValue, finding.Target, finding.Providers, $"Manually record the value of {finding.Target} after exercising the relevant runtime write path in this exact profile.", "The observed value at the recorded gameplay point. This remains unresolved until a manual answer is recorded; it does not establish a final winner or incompatibility.") { TweakRuntimeEvidence = finding.TweakRuntimeEvidence });
        }
        foreach (TweakOverlap overlap in receipt.TweakOverlaps.Where(value => conflictTargets.Contains(value.Target) && !runtimeTargets.Contains(value.Target) && value.Kind is TweakOverlapKind.ScalarOverwrite or TweakOverlapKind.MixedArrayOperations or TweakOverlapKind.DuplicateMutation or TweakOverlapKind.OpposingMutation))
        {
            string[] providers = overlap.Operations.Select(value => value.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            Add(requests, new RuntimeProbeRequest(RuntimeProbeKind.PostInitializationTweakValue, overlap.Target, providers, $"Record the post-initialization snapshot of {overlap.Target}, five seconds after CET updates begin.", "The final observed value after TweakXL initialization. This source fact does not establish compatibility, compilation, loader success, or a runtime defect; later gameplay writes remain a separate manual check."));
        }
        foreach (TweakOverlap overlap in receipt.TweakOverlaps.Where(value => value.Kind == TweakOverlapKind.SourceArrayDependency && conflictTargets.Contains(value.Target)))
        {
            string[] providers = overlap.Operations.Select(value => value.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            string[] targets = overlap.Operations.Select(value => value.Target).Concat(overlap.Operations.Where(value => value.Kind is TweakOperationKind.ArrayAppendFrom or TweakOperationKind.ArrayPrependFrom).Select(value => value.Value)).Distinct(StringComparer.Ordinal).ToArray();
            foreach (string target in targets) Add(requests, new RuntimeProbeRequest(RuntimeProbeKind.PostInitializationTweakValue, target, providers, $"Record the post-initialization snapshot of {target}, five seconds after CET updates begin.", "The final observed value after TweakXL initialization. This source fact does not establish compatibility, compilation, loader success, or a runtime defect."));
        }
        return new RuntimeProbeManifest(1, receipt.ProfileName, DateTimeOffset.UtcNow, requests.ToArray(), receipt.InstallationId);
    }

    private static void Add(List<RuntimeProbeRequest> requests, RuntimeProbeRequest request)
    {
        if (!requests.Any(value => value.Kind == request.Kind && value.Target == request.Target && value.Providers.SequenceEqual(request.Providers, StringComparer.OrdinalIgnoreCase))) requests.Add(request);
    }
}

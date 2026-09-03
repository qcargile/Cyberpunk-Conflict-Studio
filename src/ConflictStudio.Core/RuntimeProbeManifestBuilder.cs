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
                $"The automatic check records {finding.Target} once, five seconds after CET starts updating.",
                "This shows the value only at that moment. It does not show which mod sets it last or prove a gameplay bug."));
        }
        foreach (InteractionFinding finding in receipt.InteractionFindings.Where(value => value.TweakRuntimeEvidence?.CompetingValues().Any() == true && conflictTargets.Contains(value.Target)))
        {
            runtimeTargets.Add(finding.Target);
            Add(requests, new RuntimeProbeRequest(RuntimeProbeKind.PostInitializationTweakValue, finding.Target, finding.Providers, $"The automatic check records {finding.Target} once, five seconds after CET starts updating. Ask the mod author how to trigger the later code change and measure this value again.", "This measures only one moment. A later change needs a separate measurement; this check does not show which mod sets the value last, whether the values agree, or whether the mods are incompatible.") { TweakRuntimeEvidence = finding.TweakRuntimeEvidence });
            Add(requests, new RuntimeProbeRequest(RuntimeProbeKind.SharedStateValue, finding.Target, finding.Providers, $"Ask the mod author how to trigger the code that changes {finding.Target} and measure the value afterward in this same profile. This is a technical check: record the measured value, not a guess based on gameplay.", "This records the value at the gameplay moment you tested. The check remains unanswered until a manual result is recorded. It does not show which mod sets the value last or prove that the mods are incompatible.") { TweakRuntimeEvidence = finding.TweakRuntimeEvidence });
        }
        foreach (TweakOverlap overlap in receipt.TweakOverlaps.Where(value => conflictTargets.Contains(value.Target) && !runtimeTargets.Contains(value.Target) && value.Kind is TweakOverlapKind.ScalarOverwrite or TweakOverlapKind.MixedArrayOperations or TweakOverlapKind.DuplicateMutation or TweakOverlapKind.OpposingMutation))
        {
            string[] providers = overlap.Operations.Select(value => value.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            Add(requests, new RuntimeProbeRequest(RuntimeProbeKind.PostInitializationTweakValue, overlap.Target, providers, $"The automatic check records {overlap.Target} once, five seconds after CET starts updating.", "This shows the value only at that moment. It does not prove that the mods work together, pass startup code checks, load successfully, or cause a gameplay bug. Later gameplay changes need a separate manual measurement, with the mod author's help if needed."));
        }
        foreach (TweakOverlap overlap in receipt.TweakOverlaps.Where(value => value.Kind == TweakOverlapKind.SourceArrayDependency && conflictTargets.Contains(value.Target)))
        {
            string[] providers = overlap.Operations.Select(value => value.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            string[] targets = overlap.Operations.Select(value => value.Target).Concat(overlap.Operations.Where(value => value.Kind is TweakOperationKind.ArrayAppendFrom or TweakOperationKind.ArrayPrependFrom).Select(value => value.Value)).Distinct(StringComparer.Ordinal).ToArray();
            foreach (string target in targets) Add(requests, new RuntimeProbeRequest(RuntimeProbeKind.PostInitializationTweakValue, target, providers, $"The automatic check records {target} once, five seconds after CET starts updating.", "This shows the value only at that moment. It does not prove that the mods work together, pass startup code checks, load successfully, or cause a gameplay bug."));
        }
        return new RuntimeProbeManifest(1, receipt.ProfileName, DateTimeOffset.UtcNow, requests.ToArray(), receipt.InstallationId);
    }

    private static void Add(List<RuntimeProbeRequest> requests, RuntimeProbeRequest request)
    {
        if (!requests.Any(value => value.Kind == request.Kind && value.Target == request.Target && value.Providers.SequenceEqual(request.Providers, StringComparer.OrdinalIgnoreCase))) requests.Add(request);
    }
}

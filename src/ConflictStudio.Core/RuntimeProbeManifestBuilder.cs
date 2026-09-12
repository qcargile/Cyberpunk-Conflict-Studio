using System.Text.Json.Serialization;

namespace ConflictStudio.Core;

public enum RuntimeProbeKind { ProviderPresence, PostInitializationTweakValue, CallbackDelivery, SharedStateValue, BehaviorCheck }

public sealed record RuntimeProbeRequest(RuntimeProbeKind Kind, string Target, string[] Providers, string Observation, string Decides)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TweakRuntimeEvidence? TweakRuntimeEvidence { get; init; }
}

public sealed record RuntimeProbeBinding(ModManagerKind ManagerKind, string InstallationId, string ProfileName, ConflictSurface Surface, string Target, string[] Providers, string EvidenceSha256);

public sealed record RuntimeProbeManifest(int SchemaVersion, string ProfileName, DateTimeOffset CreatedAtUtc, RuntimeProbeRequest[] Requests, string? InstallationId = null)
{
    public RuntimeProbeBinding? Binding { get; init; }
}

public static class RuntimeProbeManifestBuilder
{
    public static RuntimeProbeManifest Build(ProfileScanReceipt receipt)
        => BuildAll(receipt, DateTimeOffset.UtcNow);

    public static RuntimeProbeManifest Build(ProfileScanReceipt receipt, ConflictWorkItem selectedItem, DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(selectedItem);
        if (createdAtUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Runtime check timestamps must use UTC.", nameof(createdAtUtc));
        if (string.IsNullOrWhiteSpace(receipt.InstallationId)) throw new InvalidOperationException("The selected profile has no installation identity. Scan the profile again before generating a runtime check.");
        ConflictWorkItem? current = ConflictWorkQueueBuilder.Build(receipt, []).SingleOrDefault(value => SameSelection(value, selectedItem));
        if (current is null) throw new InvalidOperationException("The selected finding no longer matches the current profile evidence. Select the refreshed finding and generate a new run.");
        HashSet<string> targets = [current.Target, .. current.RelatedTargets];
        foreach (TweakOverlap overlap in receipt.TweakOverlaps.Where(value => value.Kind == TweakOverlapKind.SourceArrayDependency && string.Equals(value.Target, current.Target, StringComparison.Ordinal)))
        {
            foreach (TweakOperation operation in overlap.Operations)
            {
                targets.Add(operation.Target);
                if (operation.Kind is TweakOperationKind.ArrayAppendFrom or TweakOperationKind.ArrayPrependFrom) targets.Add(operation.Value);
            }
        }
        RuntimeProbeRequest[] requests = BuildAll(receipt, createdAtUtc).Requests
            .Where(value => targets.Contains(value.Target) && value.Providers.All(provider => current.Providers.Contains(provider, StringComparer.OrdinalIgnoreCase)))
            .ToArray();
        RuntimeProbeBinding binding = new(receipt.ManagerKind, receipt.InstallationId, receipt.ProfileName, current.Surface, current.Target, current.Providers, current.EvidenceSha256);
        return new RuntimeProbeManifest(2, receipt.ProfileName, createdAtUtc, requests, receipt.InstallationId) { Binding = binding };
    }

    private static RuntimeProbeManifest BuildAll(ProfileScanReceipt receipt, DateTimeOffset createdAtUtc)
    {
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
        return new RuntimeProbeManifest(1, receipt.ProfileName, createdAtUtc, requests.ToArray(), receipt.InstallationId);
    }

    private static bool SameSelection(ConflictWorkItem current, ConflictWorkItem selected)
        => current.Surface == selected.Surface
            && string.Equals(current.Target, selected.Target, StringComparison.Ordinal)
            && SameProviders(current.Providers, selected.Providers)
            && string.Equals(current.EvidenceSha256, selected.EvidenceSha256, StringComparison.Ordinal);

    private static bool SameProviders(string[] first, string[] second)
        => first.Length == second.Length && first.All(value => second.Contains(value, StringComparer.OrdinalIgnoreCase));

    private static void Add(List<RuntimeProbeRequest> requests, RuntimeProbeRequest request)
    {
        if (!requests.Any(value => value.Kind == request.Kind && value.Target == request.Target && value.Providers.SequenceEqual(request.Providers, StringComparer.OrdinalIgnoreCase))) requests.Add(request);
    }
}

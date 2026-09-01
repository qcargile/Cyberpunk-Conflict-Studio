namespace ConflictStudio.Core;

public enum RuntimeProbeKind { ProviderPresence, PostInitializationTweakValue, CallbackDelivery, SharedStateValue, BehaviorCheck }

public sealed record RuntimeProbeRequest(RuntimeProbeKind Kind, string Target, string[] Providers, string Observation, string Decides);

public sealed record RuntimeProbeManifest(int SchemaVersion, string ProfileName, DateTimeOffset CreatedAtUtc, RuntimeProbeRequest[] Requests, string? InstallationId = null);

public static class RuntimeProbeManifestBuilder
{
    public static RuntimeProbeManifest Build(ProfileScanReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        List<RuntimeProbeRequest> requests = [];
        foreach (TweakOverlap overlap in receipt.TweakOverlaps.Where(value => value.Kind is TweakOverlapKind.ScalarOverwrite or TweakOverlapKind.MixedArrayOperations or TweakOverlapKind.DuplicateMutation))
        {
            string[] providers = overlap.Operations.Select(value => value.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            Add(requests, new RuntimeProbeRequest(RuntimeProbeKind.PostInitializationTweakValue, overlap.Target, providers, $"Record the post-initialization snapshot of {overlap.Target}, five seconds after CET updates begin.", "The final observed value after TweakXL initialization. This source fact does not establish compatibility, compilation, loader success, or a runtime defect; later gameplay writes remain a separate manual check."));
        }
        foreach (TweakOverlap overlap in receipt.TweakOverlaps.Where(value => value.Kind == TweakOverlapKind.SourceArrayDependency))
        {
            string[] providers = overlap.Operations.Select(value => value.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            string[] targets = overlap.Operations.Select(value => value.Target).Concat(overlap.Operations.Where(value => value.Kind is TweakOperationKind.ArrayAppendFrom or TweakOperationKind.ArrayPrependFrom).Select(value => value.Value)).Distinct(StringComparer.Ordinal).ToArray();
            foreach (string target in targets) Add(requests, new RuntimeProbeRequest(RuntimeProbeKind.PostInitializationTweakValue, target, providers, $"Record the post-initialization snapshot of {target}, five seconds after CET updates begin.", "The final observed value after TweakXL initialization. This source fact does not establish compatibility, compilation, loader success, or a runtime defect."));
        }
        foreach (InteractionFinding finding in receipt.InteractionFindings.Where(value => value.Kind == InteractionFindingKind.Review))
        {
            string methodBase = InteractionReportBuilder.MethodBase(finding.Target);
            LuaCallbackEvidence[] callbacks = receipt.LuaCallbacks.Where(value => (value.Target == finding.Target || value.Target == methodBase) && value.Impact == EvidenceImpact.Review).ToArray();
            if (callbacks.Length > 0) Add(requests, new RuntimeProbeRequest(RuntimeProbeKind.CallbackDelivery, finding.Target, finding.Providers, $"Manually record the named mods' visible behavior when {finding.Target} is invoked in the exact active profile.", "The recorded manual observation, which remains unresolved until an answer is recorded. It does not establish callback order or isolate one provider."));
            RedScriptFlowEvidence[] flows = receipt.RedScriptFlows.Where(value => value.Target == finding.Target && value.Impact == EvidenceImpact.Review).ToArray();
            if (flows.Length > 0) Add(requests, new RuntimeProbeRequest(RuntimeProbeKind.BehaviorCheck, finding.Target, finding.Providers, $"Manually record the exact active-profile behavior when the flagged path reaches {finding.Target}.", "The recorded manual observation, which remains unresolved until an answer is recorded. It does not isolate one provider."));
        }
        return new RuntimeProbeManifest(1, receipt.ProfileName, DateTimeOffset.UtcNow, requests.ToArray(), receipt.InstallationId);
    }

    private static void Add(List<RuntimeProbeRequest> requests, RuntimeProbeRequest request)
    {
        if (!requests.Any(value => value.Kind == request.Kind && value.Target == request.Target && value.Providers.SequenceEqual(request.Providers, StringComparer.OrdinalIgnoreCase))) requests.Add(request);
    }
}

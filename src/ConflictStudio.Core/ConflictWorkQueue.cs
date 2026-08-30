using System.Security.Cryptography;
using System.Text;

namespace ConflictStudio.Core;

public enum ConflictSurface { PackedResource, VirtualFile, ScriptAndTweak, SharedState, ArchiveXl, Diagnostic }

public enum EvidenceClassification { Redundant, Intentional, EffectiveOverwrite, Exclusive, Review, Composable, Unresolved, OrderSensitive, Informational }

public enum ConflictCaseKind { ProvenConflict, FileOverride, OrderSensitive, RuntimeCheck, Composes, SameEvidence, SharedTarget, Unknown, Reviewed }

public enum ConflictWorkState { NeedsAttention = 0, Reviewed = 1, NoActionNeeded = 2, ReviewWhenRelevant = 3 }

public sealed record ConflictWorkItem(
    ConflictSurface Surface,
    string Target,
    EvidenceClassification Classification,
    ConflictWorkState State,
    string Summary,
    string NextAction,
    string? Winner,
    string[] Providers,
    string EvidenceSha256,
    string? ReviewRationale = null,
    string? ResultOverride = null,
    string? ProofOverride = null)
{
    public string[] RelatedTargets { get; init; } = [];
    public string StateLabel => State switch
    {
        ConflictWorkState.NeedsAttention => "Needs attention",
        ConflictWorkState.ReviewWhenRelevant => "Review when relevant",
        ConflictWorkState.Reviewed => "Reviewed",
        _ => "No action needed"
    };
    public string SurfaceLabel => Surface switch
    {
        ConflictSurface.PackedResource => "Packed archive file",
        ConflictSurface.VirtualFile => "Loose file",
        ConflictSurface.ScriptAndTweak => "Script or TweakXL",
        ConflictSurface.SharedState => "Shared runtime state",
        ConflictSurface.ArchiveXl => "ArchiveXL",
        _ => "Setup or scan"
    };
    public string ClassificationLabel => Classification == EvidenceClassification.Intentional ? "Intentional" : ResultOverride ?? (Classification switch
    {
        EvidenceClassification.Redundant => "Same content",
        EvidenceClassification.EffectiveOverwrite => "One file version loads",
        EvidenceClassification.Exclusive => "Proven conflict",
        EvidenceClassification.Review => "Needs one check",
        EvidenceClassification.Composable => "Composes",
        EvidenceClassification.OrderSensitive => "Tweak order changes the result",
        EvidenceClassification.Informational => "Shared target only",
        _ => "Can't determine"
    });
    public ConflictCaseKind CaseKind => Classification switch
    {
        EvidenceClassification.Intentional => ConflictCaseKind.Reviewed,
        EvidenceClassification.Exclusive => ConflictCaseKind.ProvenConflict,
        EvidenceClassification.EffectiveOverwrite => ConflictCaseKind.FileOverride,
        EvidenceClassification.OrderSensitive => ConflictCaseKind.OrderSensitive,
        EvidenceClassification.Review => ConflictCaseKind.RuntimeCheck,
        EvidenceClassification.Composable => ConflictCaseKind.Composes,
        EvidenceClassification.Redundant => ConflictCaseKind.SameEvidence,
        EvidenceClassification.Informational => ConflictCaseKind.SharedTarget,
        _ => ConflictCaseKind.Unknown
    };
    public bool IsActionable => CaseKind is ConflictCaseKind.ProvenConflict or ConflictCaseKind.FileOverride or ConflictCaseKind.OrderSensitive or ConflictCaseKind.RuntimeCheck or ConflictCaseKind.Unknown;
    public string ProviderSummary => string.Join("  ↔  ", Providers);
    public string ProofLabel => CaseKind == ConflictCaseKind.Reviewed ? "Reviewed for this profile" : ProofOverride ?? (CaseKind switch
    {
        ConflictCaseKind.ProvenConflict => "Only one can take effect",
        ConflictCaseKind.FileOverride => "One file wins",
        ConflictCaseKind.OrderSensitive => "Order changes the final result",
        ConflictCaseKind.RuntimeCheck => "Needs an in-game check",
        ConflictCaseKind.Composes => "Both changes can apply",
        ConflictCaseKind.SameEvidence => "Same declaration or content",
        ConflictCaseKind.SharedTarget => "Same target; no conflict found",
        _ => "Needs more information"
    });
}

public static class ConflictWorkQueueBuilder
{
    public static ConflictWorkItem[] Build(ProfileScanReceipt receipt, IReadOnlyList<EvidenceDecision> decisions)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(decisions);
        List<ConflictWorkItem> items = [];
        bool sharedPackedBlocker = receipt.ArchiveFailures.Length > 0 || receipt.ArchiveOrderEvidence?.Kind == ArchiveOrderEvidenceKind.Unresolved;
        foreach (ResourceConflict conflict in receipt.ResourceConflicts)
        {
            if (sharedPackedBlocker && conflict.Kind == ResourceConflictKind.Unresolved) continue;
            string[] providers = conflict.Providers.Select(value => value.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            EvidenceClassification classification = conflict.Kind switch
            {
                ResourceConflictKind.Redundant => EvidenceClassification.Redundant,
                ResourceConflictKind.Divergent or ResourceConflictKind.OrderedOverlap => EvidenceClassification.EffectiveOverwrite,
                _ => EvidenceClassification.Unresolved
            };
            string summary = classification switch
            {
                EvidenceClassification.Redundant => "Every packed provider has the same payload.",
                EvidenceClassification.EffectiveOverwrite => PackedSummary(conflict),
                _ => "The packed provider chain is incomplete, so no winner is claimed."
            };
            string action = classification == EvidenceClassification.Redundant ? "No action is needed." : classification == EvidenceClassification.Unresolved ? "Resolve the named archive diagnostic, then rescan." : "Open Archives → Archive conflicts to see every winning and losing file. If the winner is wrong, change archive load order and preview the exact winner delta.";
            Add(items, receipt, decisions, ConflictSurface.PackedResource, conflict.DisplayName, classification, summary, action, classification == EvidenceClassification.Unresolved ? null : conflict.EngineWinnerArchive, providers, conflict.Providers.Select(value => $"{value.ArchiveName}|{value.PayloadFingerprint}|{value.ResourcePath}|{value.Provider}").ToArray());
        }
        foreach (IGrouping<string, VirtualFileShadow> group in receipt.VirtualFileShadows.GroupBy(value => value.Relation + "|" + value.WinnerProvider + "|" + string.Join("|", value.Providers.Select(provider => provider.Provider)), StringComparer.OrdinalIgnoreCase))
        {
            VirtualFileShadow[] shadows = group.ToArray();
            VirtualFileShadow shadow = shadows[0];
            string[] providers = shadow.Providers.Select(value => value.Provider).ToArray();
            EvidenceClassification classification = shadow.Relation == VirtualFileRelation.Identical ? EvidenceClassification.Redundant : EvidenceClassification.EffectiveOverwrite;
            VirtualFileProvider winner = shadow.Providers[0];
            string priority = receipt.ManagerKind switch { ModManagerKind.Vortex => "the deployed Vortex winner", ModManagerKind.Manual => "the deployed game files", _ => winner.Mo2Priority is int value && value >= 0 && value != int.MaxValue ? $"MO2 priority {value}" : winner.Provider == "Overwrite" ? "the MO2 overwrite directory" : "the highest active provider" };
            string paths = shadows.Length == 1 ? "this path" : $"{shadows.Length} paths";
            string summary = classification == EvidenceClassification.Redundant ? $"{string.Join(" and ", providers)} install identical bytes at {paths}." : $"{winner.Provider} wins at {priority} and replaces {string.Join(", ", providers.Skip(1))} at {paths}. The payload hashes differ.";
            string action = classification == EvidenceClassification.Redundant ? "No action is needed." : receipt.ManagerKind switch { ModManagerKind.Vortex => $"If {winner.Provider} is intended, mark this override intentional. If not, fix the provider conflict rule in Vortex, deploy the profile, then rescan.", ModManagerKind.Manual => $"If {winner.Provider} is intended, mark this override intentional. If not, correct the deployed game files with your installer or mod manager, then rescan.", _ => $"If {winner.Provider} is intended, mark this override intentional. If not, fix the providers' priority in MO2, then rescan; Conflict Studio will not silently reorder MO2 mods." };
            string target = shadows.Length == 1 ? shadow.RelativePath : $"{winner.Provider} overrides {string.Join(", ", providers.Skip(1))} ({shadows.Length} files)";
            Add(items, receipt, decisions, ConflictSurface.VirtualFile, target, classification, summary, action, shadow.WinnerProvider, providers, shadows.SelectMany(value => value.Providers.Select(provider => $"{value.RelativePath}|{provider.Provider}|{provider.Sha256}|{provider.ProfilePosition}")).ToArray(), shadows.Select(value => value.RelativePath).ToArray());
        }
        foreach (InteractionFinding finding in receipt.InteractionFindings)
        {
            EvidenceClassification classification = ClassifyInteraction(receipt, finding);
            (string summary, string action) = DescribeInteraction(receipt, finding, classification);
            (string? result, string? proof) = InteractionLabels(receipt, finding);
            Add(items, receipt, decisions, ConflictSurface.ScriptAndTweak, finding.Target, classification, summary, action, null, finding.Providers, InteractionEvidence(receipt, finding), null, result, proof);
        }
        foreach (SharedStateWriteFinding finding in receipt.SharedStateWrites)
        {
            string[] providers = finding.Writes.Select(value => value.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            string summary = $"Multiple mods write the same {finding.Surface} target. The final in-game state cannot be determined here.";
            Add(items, receipt, decisions, ConflictSurface.SharedState, finding.Target, EvidenceClassification.Informational, summary, "No conflict is proven. Inspect this only when troubleshooting the named runtime state.", null, providers, finding.Writes.Select(value => $"{value.Provider}|{value.FilePath}|{value.Surface}|{value.Target}|{value.Line}|{value.Operation}|{value.Evidence}|{value.SourceHash}").ToArray());
        }
        foreach (ArchiveXlOperationChain chain in receipt.ArchiveXlChains.Where(value => value.Operations.Select(operation => operation.Provider).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
        {
            string[] providers = chain.Operations.Select(value => value.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            EvidenceClassification classification = EvidenceClassification.Informational;
            string summary = "Several mods change the same ArchiveXL target. The operation and keys decide whether they combine; order alone does not prove a winner.";
            string action = "No conflict is confirmed. Inspect the operation only when troubleshooting this target.";
            Add(items, receipt, decisions, ConflictSurface.ArchiveXl, chain.Target, classification, summary, action, null, providers, chain.Operations.Select(value => $"{value.Provider}|{value.FilePath}|{value.Kind}|{value.Target}|{value.Payload}").ToArray());
        }
        foreach (RdarArchiveFailure failure in receipt.ArchiveFailures) Add(items, receipt, decisions, ConflictSurface.Diagnostic, failure.ArchiveName, EvidenceClassification.Unresolved, failure.Message, "Repair or remove the unreadable archive, then rescan.", null, [failure.Provider], [failure.ArchiveName, failure.Message]);
        if (receipt.ArchiveOrderEvidence is { Kind: ArchiveOrderEvidenceKind.Unresolved } unresolvedOrder)
        {
            int affected = receipt.ResourceConflicts.Count(value => value.Kind == ResourceConflictKind.Unresolved);
            string action = receipt.ManagerKind == ModManagerKind.Vortex
                ? unresolvedOrder.ProblemLane switch { ArchiveOrderProblemLane.Redmod => "Deploy the active Vortex profile so the enabled REDmod set and order match, then rescan.", ArchiveOrderProblemLane.Combined => "Repair the named legacy archive order entries, deploy the active Vortex profile, then rescan.", _ => "In Archives, add every named active archive once to the legacy load order, then rescan." }
                : unresolvedOrder.ProblemLane switch { ArchiveOrderProblemLane.Redmod => "Re-deploy REDmods from the active MO2 profile so the enabled REDmod set and order match, then rescan.", ArchiveOrderProblemLane.Combined => "Repair the named legacy archive order entries and re-deploy REDmods from the active MO2 profile, then rescan.", _ => "In Archives, add every named active archive once to the legacy load order, then rescan." };
            Add(items, receipt, decisions, ConflictSurface.Diagnostic, "Archive load order", EvidenceClassification.Unresolved, $"{unresolvedOrder.Message} {affected} packed resource{(affected == 1 ? string.Empty : "s")} are waiting on this one order problem.", action, null, unresolvedOrder.Provider is null ? ["Archive order"] : [unresolvedOrder.Provider], [unresolvedOrder.Message, .. unresolvedOrder.MissingEntries, .. unresolvedOrder.DuplicateEntries]);
        }
        else if (receipt.ArchiveOrderEvidence is { IgnoredEntries.Length: > 0 } maintainedOrder)
        {
            Add(items, receipt, decisions, ConflictSurface.Diagnostic, "Archive order maintenance", EvidenceClassification.Review, maintainedOrder.Message, "Open Archives → Load order and apply the current order when you want to remove the inactive lines from modlist.txt.", null, maintainedOrder.Provider is null ? ["Archive order"] : [maintainedOrder.Provider], [maintainedOrder.Message, .. maintainedOrder.IgnoredEntries]);
        }
        foreach (RdarArchiveWarning warning in receipt.ArchiveWarnings ?? []) Add(items, receipt, decisions, ConflictSurface.Diagnostic, warning.ArchiveName + " path metadata", EvidenceClassification.Unresolved, warning.Message, "The archive resources remain indexed by hash. Restore the game Oodle DLL or repair the archive footer to recover readable custom paths.", null, [warning.Provider], [warning.ArchiveName, warning.Message]);
        if (receipt.ResourcePathIndexEvidence is { State: not ResourcePathIndexState.Resolved } pathEvidence) Add(items, receipt, decisions, ConflictSurface.Diagnostic, "Global resource path index", EvidenceClassification.Unresolved, pathEvidence.Message, "Restore the active CET usedhashes.kark and Cyberpunk Oodle DLL, then rescan.", null, pathEvidence.Provider is null ? ["Resource path resolver"] : [pathEvidence.Provider], [pathEvidence.State.ToString(), pathEvidence.Message]);
        foreach (ArchiveXlSourceFailure failure in receipt.ArchiveXlFailures) Add(items, receipt, decisions, ConflictSurface.Diagnostic, failure.FilePath, EvidenceClassification.Unresolved, failure.Message, "Correct the manifest or update the parser support, then rescan.", null, [failure.Provider], [failure.FilePath, failure.Message]);
        foreach (SourceAnalysisFailure failure in receipt.SourceFailures ?? []) Add(items, receipt, decisions, ConflictSurface.Diagnostic, failure.FilePath, EvidenceClassification.Unresolved, failure.Message, "Correct or restore the source file, then rescan.", null, [failure.Provider], [failure.Surface, failure.FilePath, failure.Message]);
        return items.OrderBy(value => StateOrder(value.State)).ThenBy(value => value.Classification == EvidenceClassification.Unresolved ? 0 : 1).ThenBy(value => value.Surface).ThenBy(value => value.Target, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static int StateOrder(ConflictWorkState state) => state switch
    {
        ConflictWorkState.NeedsAttention => 0,
        ConflictWorkState.ReviewWhenRelevant => 1,
        ConflictWorkState.Reviewed => 2,
        _ => 3
    };

    private static void Add(List<ConflictWorkItem> items, ProfileScanReceipt receipt, IReadOnlyList<EvidenceDecision> decisions, ConflictSurface surface, string target, EvidenceClassification classification, string summary, string action, string? winner, string[] providers, string[] evidence, string[]? relatedTargets = null, string? resultOverride = null, string? proofOverride = null)
    {
        string hash = Hash(surface, target, classification, winner, providers, evidence);
        EvidenceDecision? decision = classification == EvidenceClassification.Unresolved || receipt.InstallationId is null ? null : decisions.Where(value => value.Target == target && value.Providers.SequenceEqual(providers, StringComparer.OrdinalIgnoreCase)).FirstOrDefault(value => EvidenceDecisionStore.Evaluate(value, receipt.InstallationId, receipt.ProfileName, surface, hash) == EvidenceDecisionState.Resolved);
        bool reviewed = decision is not null;
        ConflictWorkState state = reviewed ? ConflictWorkState.Reviewed : classification is EvidenceClassification.Redundant or EvidenceClassification.Composable or EvidenceClassification.Informational ? ConflictWorkState.NoActionNeeded : classification is EvidenceClassification.Exclusive or EvidenceClassification.Unresolved ? ConflictWorkState.NeedsAttention : ConflictWorkState.ReviewWhenRelevant;
        EvidenceClassification effectiveClassification = reviewed ? EvidenceClassification.Intentional : classification;
        items.Add(new ConflictWorkItem(surface, target, effectiveClassification, state, summary, action, winner, providers, hash, reviewed ? decision!.Rationale : null, resultOverride, proofOverride) { RelatedTargets = relatedTargets ?? [target] });
    }

    private static string Hash(ConflictSurface surface, string target, EvidenceClassification classification, string? winner, string[] providers, string[] evidence)
    {
        string canonical = string.Join('\n', [surface.ToString(), target, classification.ToString(), winner ?? string.Empty, .. providers, .. evidence]);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string[] InteractionEvidence(ProfileScanReceipt receipt, InteractionFinding finding)
    {
        List<string> evidence = [finding.Kind.ToString()];
        evidence.AddRange(receipt.TweakOverlaps.Where(value => value.Target == finding.Target).SelectMany(value => value.Operations).Select(value => $"tweak|{value.Provider}|{value.FilePath}|{value.LineNumber}|{value.Kind}|{value.Value}"));
        evidence.AddRange(receipt.RedScriptFlows.Where(value => value.Target == finding.Target).Select(value => $"redscript|{value.Provider}|{value.FilePath}|{value.Line}|{value.Kind}|{value.Continuation}|{value.SourceHash}"));
        string methodBase = InteractionReportBuilder.MethodBase(finding.Target);
        evidence.AddRange(receipt.LuaCallbacks.Where(value => value.Target == finding.Target || value.Target == methodBase).Select(value => $"lua|{value.Kind}|{value.Line}|{value.Continuation}|{value.SourceHash}|{string.Join(',', value.Copies.Select(copy => copy.Provider + ":" + copy.FilePath))}"));
        return evidence.ToArray();
    }

    private static string PackedSummary(ResourceConflict conflict)
    {
        ResourceProvider? winner = conflict.Providers.FirstOrDefault(value => string.Equals(value.ArchiveName, conflict.EngineWinnerArchive, StringComparison.OrdinalIgnoreCase));
        string comparison = conflict.Kind == ResourceConflictKind.OrderedOverlap ? "Payload comparison is unavailable, but the winner is proven." : $"{conflict.Providers.Length - 1} lower archive provider{(conflict.Providers.Length == 2 ? string.Empty : "s")} contain a different payload.";
        return winner is null ? $"{conflict.EngineWinnerArchive} is first in the proven archive order. {comparison}" : $"{winner.Provider} supplies {winner.ArchiveName}, which is first in the proven archive order. {comparison}";
    }

    private static (string Summary, string Action) DescribeInteraction(ProfileScanReceipt receipt, InteractionFinding finding, EvidenceClassification classification)
    {
        RedScriptFlowEvidence[] flows = receipt.RedScriptFlows.Where(value => value.Target == finding.Target).ToArray();
        string methodBase = InteractionReportBuilder.MethodBase(finding.Target);
        LuaCallbackEvidence[] callbacks = receipt.LuaCallbacks.Where(value => value.Target == finding.Target || value.Target == methodBase).ToArray();
        if (flows.Any(value => value.Kind == RedScriptFlowKind.Add) && callbacks.Any(value => value.Kind == LuaCallbackEvidenceKind.Override))
        {
            string summary = string.Join(" ", flows.Select(value => $"{value.Provider} adds the method ({value.FilePath}:{value.Line}).")) + " " + string.Join(" ", callbacks.Where(value => value.Kind == LuaCallbackEvidenceKind.Override).Select(value => $"{string.Join(", ", value.Copies.Select(copy => copy.Provider))} registers a CET override ({string.Join(", ", value.Copies.Select(copy => copy.FilePath))}:{value.Line})."));
            return (summary, "Check the RedScript compiler log first. If it compiles and the feature is wrong, record the exact active-profile behavior before changing either provider.");
        }
        if (flows.Length > 0)
        {
            string summary = string.Join(" ", flows.Select(value => value.Kind switch
            {
                RedScriptFlowKind.Wrap => $"{value.Provider} wraps this method and {(value.Continuation == RedScriptContinuationEvidence.Continues ? "continues to the next implementation" : value.Continuation == RedScriptContinuationEvidence.EarlyReturnBeforeContinuation ? "can return before the next implementation" : "does not call the next implementation")} ({value.FilePath}:{value.Line}).",
                RedScriptFlowKind.Replace => $"{value.Provider} replaces the method ({value.FilePath}:{value.Line}).",
                _ => $"{value.Provider} adds the method ({value.FilePath}:{value.Line})."
            }));
            bool everyWrapperContinues = flows.All(value => value.Kind != RedScriptFlowKind.Wrap || value.Continuation == RedScriptContinuationEvidence.Continues);
            string action = classification == EvidenceClassification.Exclusive ? $"Only one replacement can own this method. Install a compatibility patch or choose the intended provider between {finding.Providers[0]} and {finding.Providers[1]}." : everyWrapperContinues ? "No action is needed unless the combined feature behaves incorrectly in game." : "Reproduce the named path with the exact active profile and record what fails. If you then disable a provider, treat the result as a direction check and rescan before attributing the cause.";
            return (summary, action);
        }

        TweakOverlap? tweak = receipt.TweakOverlaps.FirstOrDefault(value => value.Target == finding.Target);
        if (tweak is not null)
        {
            return DescribeTweak(tweak, classification);
        }

        if (callbacks.Length > 0)
        {
            string summary = string.Join(" ", callbacks.Select(value => $"{string.Join(", ", value.Copies.Select(copy => copy.Provider))} registers {value.Kind}; continuation is {value.Continuation}."));
            return classification == EvidenceClassification.Composable ? (summary, "No action is needed unless a related feature fails.") : (summary, "Run the callback test in the support bundle. If both callbacks arrive and the feature works, mark the interaction intentional.");
        }
        return (finding.Summary, classification == EvidenceClassification.Composable ? "No action is needed unless the feature behaves differently in game." : "Open Technical details for the source locations, then test the named behavior once.");
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length] + "…";

    private static (string Summary, string Action) DescribeTweak(TweakOverlap tweak, EvidenceClassification classification)
    {
        string operations = string.Join(" ", tweak.Operations.Select(value => $"{value.Provider} {OperationVerb(value.Kind)} {Truncate(value.Value, 72)}."));
        if (classification == EvidenceClassification.Composable)
        {
            if (tweak.Kind == TweakOverlapKind.AssignmentThenMutation) return ($"{operations} TweakXL commits the assignment first, then applies every mutation in its documented phase.", "No action is needed unless the resulting array is wrong in game.");
            if (HasAddRemovePair(tweak)) return ($"{operations} TweakXL processes removals first and additions afterward, so a value named by both operations ends present.", "No action is needed unless the final array differs in game.");
            bool additionsOnly = tweak.Operations.All(value => IsTweakAddition(value.Kind));
            bool oneGuardedValue = additionsOnly && tweak.Operations.Select(value => value.Value).Distinct(StringComparer.Ordinal).Count() == 1 && tweak.Operations.All(value => IsUniqueTweakAddition(value.Kind));
            if (oneGuardedValue) return ($"{operations} The uniqueness guard keeps one copy of the value.", "No action is needed unless its array position matters in game.");
            bool hasSourceCopy = tweak.Operations.Any(value => value.Kind is TweakOperationKind.ArrayAppendFrom or TweakOperationKind.ArrayPrependFrom);
            return additionsOnly
                ? hasSourceCopy
                    ? ($"{operations} TweakXL applies the additions; source-copy operations skip values already present in the target.", "No action is needed unless the final array differs in game.")
                    : ($"{operations} TweakXL applies every addition.", "No action is needed unless the final array differs in game.")
                : ($"{operations} The operations affect distinct values, so no operation cancels another.", "No action is needed unless the final array differs in game.");
        }
        if (tweak.Kind == TweakOverlapKind.ScalarOverwrite) return ($"{operations} Only the last assignment remains.", "Open Details to compare the values. If the intended value is unclear, export a support bundle and capture the in-game value after startup.");
        if (tweak.Kind == TweakOverlapKind.DuplicateMutation) return ($"{operations} The same value can be inserted more than once.", "Use an append-once operation or remove the unintended duplicate addition in a compatibility patch.");
        if (tweak.Kind == TweakOverlapKind.RecordDefinitionCollision) return ($"{operations} TweakXL keeps the first construction it encounters; the normal-file discovery order is not established here.", "Keep one record definition or install a compatibility patch that owns the record construction.");
        if (tweak.Kind == TweakOverlapKind.SourceArrayDependency) return ($"{operations} The copied source array is changed by another provider, and source-copy operations discard duplicates already present in the target.", "Capture the final source and target arrays before deciding whether the interaction needs a patch.");
        if (tweak.Operations.Any(value => value.Kind == TweakOperationKind.ArrayReplacement)) return ($"{operations} Different whole-array assignments compete; TweakXL applies mutations after the selected assignment.", "Choose one whole-array owner or replace the competing assignments with compatible mutations.");
        return ($"{operations} The final duplicate count depends on which guarded or unguarded addition TweakXL encounters first.", "Use append-once or prepend-once for the shared value when only one copy is intended.");
    }

    private static string OperationVerb(TweakOperationKind kind) => kind switch
    {
        TweakOperationKind.ArrayAppend or TweakOperationKind.ArrayAppendOnce => "adds",
        TweakOperationKind.ArrayAppendFrom => "adds unique values from",
        TweakOperationKind.ArrayPrepend or TweakOperationKind.ArrayPrependOnce => "adds to the start",
        TweakOperationKind.ArrayPrependFrom => "adds unique values to the start from",
        TweakOperationKind.ArrayRemove => "removes",
        TweakOperationKind.ArrayReplacement => "replaces the whole array with",
        _ => "assigns"
    };

    private static bool IsTweakAddition(TweakOperationKind kind)
        => kind is TweakOperationKind.ArrayAppend or TweakOperationKind.ArrayAppendOnce or TweakOperationKind.ArrayAppendFrom or TweakOperationKind.ArrayPrepend or TweakOperationKind.ArrayPrependOnce or TweakOperationKind.ArrayPrependFrom;

    private static bool IsUniqueTweakAddition(TweakOperationKind kind)
        => kind is TweakOperationKind.ArrayAppendOnce or TweakOperationKind.ArrayAppendFrom or TweakOperationKind.ArrayPrependOnce or TweakOperationKind.ArrayPrependFrom;

    private static bool HasAddRemovePair(TweakOverlap tweak)
        => tweak.Operations.GroupBy(value => value.Value, StringComparer.Ordinal).Any(values => values.Any(value => IsTweakAddition(value.Kind)) && values.Any(value => value.Kind == TweakOperationKind.ArrayRemove));

    private static (string? Result, string? Proof) InteractionLabels(ProfileScanReceipt receipt, InteractionFinding finding)
    {
        TweakOverlap? tweak = receipt.TweakOverlaps.FirstOrDefault(value => value.Target == finding.Target);
        if (tweak is null) return (null, null);
        return tweak.Kind switch
        {
            TweakOverlapKind.ScalarOverwrite => ("Last assigned value wins", "Providers assign different values"),
            TweakOverlapKind.AssignmentThenMutation => ("Assignment then mutations", "TweakXL applies documented phases"),
            TweakOverlapKind.MixedArrayOperations when tweak.Operations.Any(value => value.Kind == TweakOperationKind.ArrayReplacement) => ("Whole-array owner is unresolved", "Different assignments share one array"),
            TweakOverlapKind.MixedArrayOperations => ("Tweak order changes duplicate count", "Guarded and unguarded additions share one value"),
            TweakOverlapKind.DuplicateMutation => ("Same value may be added twice", "Duplicate unguarded additions are proven"),
            TweakOverlapKind.RecordDefinitionCollision => ("Record definition collision", "Providers construct the same record differently"),
            TweakOverlapKind.SourceArrayDependency => ("Source array needs verification", "Copied array contents are unknown"),
            TweakOverlapKind.ComposableMutation when HasAddRemovePair(tweak) => ("Remove first, then add", "Documented TweakXL phases decide membership"),
            TweakOverlapKind.ComposableMutation when tweak.Operations.All(value => IsTweakAddition(value.Kind)) && tweak.Operations.Select(value => value.Value).Distinct(StringComparer.Ordinal).Count() == 1 && tweak.Operations.All(value => IsUniqueTweakAddition(value.Kind)) => ("Duplicate is prevented", "A uniqueness guard keeps one value"),
            TweakOverlapKind.ComposableMutation when tweak.Operations.All(value => IsTweakAddition(value.Kind)) => ("All additions are applied", "Distinct additions compose"),
            TweakOverlapKind.ComposableMutation => ("Array changes compose", "Distinct array values are changed"),
            TweakOverlapKind.Redundant => ("Same declaration", "Every provider declares the same value"),
            _ => (null, null)
        };
    }

    private static EvidenceClassification ClassifyInteraction(ProfileScanReceipt receipt, InteractionFinding finding)
    {
        if (finding.Kind == InteractionFindingKind.Exclusive) return EvidenceClassification.Exclusive;
        if (finding.Kind == InteractionFindingKind.Composable) return EvidenceClassification.Composable;
        TweakOverlap? tweak = receipt.TweakOverlaps.FirstOrDefault(value => value.Target == finding.Target);
        if (tweak is not null) return tweak.Kind is TweakOverlapKind.DuplicateMutation or TweakOverlapKind.RecordDefinitionCollision or TweakOverlapKind.SourceArrayDependency ? EvidenceClassification.Review : EvidenceClassification.OrderSensitive;
        RedScriptFlowEvidence[] flows = receipt.RedScriptFlows.Where(value => value.Target == finding.Target).ToArray();
        string methodBase = InteractionReportBuilder.MethodBase(finding.Target);
        LuaCallbackEvidence[] callbacks = receipt.LuaCallbacks.Where(value => value.Target == finding.Target || value.Target == methodBase).ToArray();
        if (flows.Any(value => value.Kind == RedScriptFlowKind.Add) && callbacks.Any(value => value.Kind == LuaCallbackEvidenceKind.Override)) return EvidenceClassification.Review;
        if (flows.Length > 0 && flows.All(value => value.Kind != RedScriptFlowKind.Wrap || value.Continuation == RedScriptContinuationEvidence.Continues)) return EvidenceClassification.Composable;
        if (callbacks.Length > 0 && callbacks.All(value => value.Kind != LuaCallbackEvidenceKind.Override)) return EvidenceClassification.Composable;
        return EvidenceClassification.Review;
    }
}

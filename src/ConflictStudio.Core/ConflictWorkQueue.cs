using System.Security.Cryptography;
using System.Text;

namespace ConflictStudio.Core;

public enum ConflictSurface { PackedResource, VirtualFile, ScriptAndTweak, SharedState, ArchiveXl, Diagnostic }

public enum EvidenceClassification { Redundant, Intentional, EffectiveOverwrite, Exclusive, Review, Composable, Unresolved, OrderSensitive, Informational, CompetingDeclaration, CompilerEvidence }

public enum ConflictCaseKind { ProvenConflict, FileOverride, OrderSensitive, RuntimeCheck, Composes, SameEvidence, SharedTarget, Unknown, Reviewed, CompetingDeclaration, CompilerEvidence }

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
    string? ProofOverride = null,
    string? MeaningOverride = null,
    string? BoundaryOverride = null)
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
    public string ClassificationLabel => Classification == EvidenceClassification.Intentional ? "Reviewed for this profile" : ResultOverride ?? (Classification switch
    {
        EvidenceClassification.Redundant => "No action: same change",
        EvidenceClassification.EffectiveOverwrite => "One deployed file is selected",
        EvidenceClassification.Exclusive => "Confirmed: different replacements",
        EvidenceClassification.Review => "Review: behavior must be checked",
        EvidenceClassification.CompilerEvidence => "Review: compiler result required",
        EvidenceClassification.Composable => "No action: no competing outcome found",
        EvidenceClassification.OrderSensitive => Providers.Length == 1 ? "Review: one source may stop the next" : "Review: one mod may stop the next",
        EvidenceClassification.Informational => "Information: shared target",
        EvidenceClassification.CompetingDeclaration => "Review: different values assigned",
        _ => "Review: evidence is incomplete"
    });
    public ConflictCaseKind CaseKind => Classification switch
    {
        EvidenceClassification.Intentional => ConflictCaseKind.Reviewed,
        EvidenceClassification.Exclusive => ConflictCaseKind.ProvenConflict,
        EvidenceClassification.EffectiveOverwrite => ConflictCaseKind.FileOverride,
        EvidenceClassification.OrderSensitive => ConflictCaseKind.OrderSensitive,
        EvidenceClassification.CompetingDeclaration => ConflictCaseKind.CompetingDeclaration,
        EvidenceClassification.CompilerEvidence => ConflictCaseKind.CompilerEvidence,
        EvidenceClassification.Review => ConflictCaseKind.RuntimeCheck,
        EvidenceClassification.Composable => ConflictCaseKind.Composes,
        EvidenceClassification.Redundant => ConflictCaseKind.SameEvidence,
        EvidenceClassification.Informational => ConflictCaseKind.SharedTarget,
        _ => ConflictCaseKind.Unknown
    };
    public bool IsActionable => CaseKind is ConflictCaseKind.ProvenConflict or ConflictCaseKind.FileOverride or ConflictCaseKind.OrderSensitive or ConflictCaseKind.CompetingDeclaration or ConflictCaseKind.CompilerEvidence or ConflictCaseKind.RuntimeCheck or ConflictCaseKind.Unknown;
    public bool IsCodeCase => Surface is not ConflictSurface.PackedResource and not ConflictSurface.Diagnostic;
    public string ProviderSummary => string.Join("  ↔  ", Providers);
    public string ProofLabel => CaseKind == ConflictCaseKind.Reviewed ? "You recorded an outcome for this profile" : ProofOverride ?? (CaseKind switch
    {
        ConflictCaseKind.ProvenConflict => "Different replacement bodies target one method",
        ConflictCaseKind.FileOverride => "The deployed order selects one file",
        ConflictCaseKind.OrderSensitive => "At least one path can stop before later code runs",
        ConflictCaseKind.CompetingDeclaration => "Two active sources assign different values to one field",
        ConflictCaseKind.CompilerEvidence => "The active compiler result is needed",
        ConflictCaseKind.RuntimeCheck => "The source cannot decide the in-game behavior",
        ConflictCaseKind.Composes => "The analyzed paths preserve one another",
        ConflictCaseKind.SameEvidence => "The analyzed declarations or file contents are identical",
        ConflictCaseKind.SharedTarget => Providers.Length == 1 ? "The sources share a target, but no competing outcome is shown" : "The mods share a target, but no competing outcome is shown",
        _ => "The available evidence cannot decide this boundary"
    });
    public string MeaningLabel => MeaningOverride ?? (CaseKind switch
    {
        ConflictCaseKind.ProvenConflict => Providers.Length == 1 ? "Active declarations replace the same method with different code. Only one replacement can provide that method." : "Two active mods replace the same method with different code. Only one replacement can provide that method.",
        ConflictCaseKind.FileOverride => Winner is null ? "Several mods install the same file path. The available deployment evidence does not name a selected file." : $"Several mods install the same file path. The deployed profile selects {Winner}.",
        ConflictCaseKind.OrderSensitive => Providers.Length == 1 ? "The sources touch the same method, and at least one path can stop before later code runs. Their conditions may still be separate." : "The mods touch the same method, and at least one path can stop before later code runs. Their conditions may still be separate.",
        ConflictCaseKind.CompetingDeclaration => Providers.Length == 1 ? "Active declarations assign different values to the same field. One final value will exist after loading." : "Two active tweak files assign different values to the same field. One final value will exist after loading.",
        ConflictCaseKind.CompilerEvidence => "Active source declarations overlap in a way only the RedScript compiler result can decide.",
        ConflictCaseKind.RuntimeCheck => "The sources share a boundary, but static inspection cannot determine the visible in-game result.",
        ConflictCaseKind.Composes => "The analyzed changes do not produce competing outcomes at this boundary.",
        ConflictCaseKind.SameEvidence => Providers.Length == 1 ? "The active declarations contain the same analyzed change. There is no competing outcome at this boundary." : "The active mods contain the same analyzed change. There is no competing outcome at this boundary.",
        ConflictCaseKind.SharedTarget => Providers.Length == 1 ? "The sources refer to the same target. Shared use alone is not a conflict." : "The mods refer to the same target. Shared use alone is not a conflict.",
        ConflictCaseKind.Reviewed => "You recorded how this case should be handled for the current profile.",
        _ => "Conflict Studio could not collect enough evidence to classify this boundary."
    });
    public string BoundaryLabel => BoundaryOverride ?? (CaseKind switch
    {
        ConflictCaseKind.ProvenConflict => Providers.Length == 1 ? "This proves competing source ownership. It does not prove the game fails to compile or that these declarations caused a bug." : "This proves competing source ownership. It does not prove the game fails to compile or that either mod caused a bug.",
        ConflictCaseKind.FileOverride => "This proves deployed file ownership. It does not prove the selected file is the version you intended.",
        ConflictCaseKind.OrderSensitive => Providers.Length == 1 ? "This is not a confirmed conflict. It does not prove a feature fails or that declaration order is the runtime call order." : "This is not a confirmed conflict. It does not prove either feature fails or that the listed mod order is the runtime call order.",
        ConflictCaseKind.CompetingDeclaration => "This proves different source values. It does not prove which value is active in game.",
        ConflictCaseKind.CompilerEvidence => "No compiler outcome is claimed until the active compiler log is supplied.",
        ConflictCaseKind.RuntimeCheck => "This is not a confirmed conflict. A targeted in-game observation is still required.",
        ConflictCaseKind.Reviewed => "This records your profile decision. It does not create new technical evidence.",
        ConflictCaseKind.Unknown => "No conflict or compatibility conclusion can be made from the available evidence.",
        _ => Providers.Length == 1 ? "No conflict is shown at this boundary. This does not prove the sources are compatible everywhere else." : "No conflict is shown at this boundary. This does not prove the mods are compatible everywhere else."
    });
}

public static class ConflictWorkQueueBuilder
{
    public static ConflictWorkItem[] Build(ProfileScanReceipt receipt, IReadOnlyList<EvidenceDecision> decisions)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(decisions);
        List<ConflictWorkItem> items = [];
        InteractionLookup interactions = new(receipt);
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
            string action = classification == EvidenceClassification.Redundant ? "No action is needed." : classification == EvidenceClassification.Unresolved ? "Resolve the named archive diagnostic, then rescan." : "Open Archives → Archive conflicts to see every selected and shadowed file. If the selected owner is wrong, change archive load order and preview the exact ownership delta.";
            Add(items, receipt, decisions, ConflictSurface.PackedResource, conflict.DisplayName, classification, summary, action, classification == EvidenceClassification.Unresolved ? null : conflict.EngineWinnerArchive, providers, conflict.Providers.Select(value => $"{value.ArchiveName}|{PayloadIdentity(value)}|{value.ResourcePath}|{value.Provider}").ToArray());
        }
        foreach (IGrouping<string, VirtualFileShadow> group in receipt.VirtualFileShadows.GroupBy(value => value.Relation + "|" + value.WinnerProvider + "|" + string.Join("|", value.Providers.Select(provider => provider.Provider)), StringComparer.OrdinalIgnoreCase))
        {
            VirtualFileShadow[] shadows = group.ToArray();
            VirtualFileShadow shadow = shadows[0];
            string[] providers = shadow.Providers.Select(value => value.Provider).ToArray();
            EvidenceClassification classification = shadow.Relation is VirtualFileRelation.Identical or VirtualFileRelation.Equivalent ? EvidenceClassification.Redundant : EvidenceClassification.EffectiveOverwrite;
            VirtualFileProvider winner = shadow.Providers[0];
            string priority = receipt.ManagerKind switch { ModManagerKind.Vortex => "the deployed Vortex ownership record", ModManagerKind.Manual => "the deployed game files", _ => winner.Mo2Priority is int value && value >= 0 && value != int.MaxValue ? $"MO2 priority {value}" : winner.Provider == "Overwrite" ? "the MO2 overwrite directory" : "the highest active provider" };
            string paths = shadows.Length == 1 ? "this path" : $"{shadows.Length} paths";
            string summary = shadow.Relation == VirtualFileRelation.Identical ? $"{string.Join(" and ", providers)} install identical bytes at {paths}." : shadow.Relation == VirtualFileRelation.Equivalent ? $"{string.Join(" and ", providers)} install JSON files with equivalent parsed values at {paths}. The byte hashes differ." : $"{winner.Provider} is selected by {priority} and shadows {string.Join(", ", providers.Skip(1))} at {paths}. The payload hashes differ.";
            string action = classification == EvidenceClassification.Redundant ? "No action is needed." : receipt.ManagerKind switch { ModManagerKind.Vortex => $"If {winner.Provider} is intended, mark this override intentional. If not, fix the provider conflict rule in Vortex, deploy the profile, then rescan.", ModManagerKind.Manual => $"If {winner.Provider} is intended, mark this override intentional. If not, correct the deployed game files with your installer or mod manager, then rescan.", _ => $"If {winner.Provider} is intended, mark this override intentional. If not, fix the providers' priority in MO2, then rescan; Conflict Studio will not silently reorder MO2 mods." };
            string target = shadows.Length == 1 ? shadow.RelativePath : $"{winner.Provider} overrides {string.Join(", ", providers.Skip(1))} ({shadows.Length} files)";
            string[] evidence = shadows.SelectMany(value => value.Providers.Select((provider, index) => $"{value.RelativePath}|{provider.Provider}|{provider.Sha256}|{index}")).ToArray();
            Add(items, receipt, decisions, ConflictSurface.VirtualFile, target, classification, summary, action, shadow.WinnerProvider, providers, evidence, shadows.Select(value => value.RelativePath).ToArray());
        }
        foreach (InteractionFinding finding in receipt.InteractionFindings)
        {
            EvidenceClassification classification = ClassifyInteraction(finding, interactions);
            (string summary, string action) = DescribeInteraction(finding, classification, interactions);
            (string? result, string? proof, string? meaning, string? boundary) = InteractionLabels(finding, interactions, classification);
            Add(items, receipt, decisions, ConflictSurface.ScriptAndTweak, finding.Target, classification, summary, action, null, InteractionProviders(finding, interactions), InteractionEvidence(finding, interactions), null, result, proof, meaning, boundary);
        }
        foreach (SharedStateWriteFinding finding in receipt.SharedStateWrites)
        {
            string[] providers = finding.Writes.Select(value => value.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            string summary = $"Multiple mods write the same {finding.Surface} target. The final in-game state cannot be determined here.";
            Add(items, receipt, decisions, ConflictSurface.SharedState, finding.Target, EvidenceClassification.Informational, summary, "No conflict is proven. Inspect this only when troubleshooting the named runtime state.", null, providers, finding.Writes.Select(value => $"{value.Provider}|{CanonicalEvidencePath(value.FilePath)}|{value.Surface}|{value.Target}|{value.Operation}|{value.Evidence}" + (value.Surface == SharedStateSurface.TweakDb ? "|" + ScopedEvidence(value.CallSha256, value.SourceHash) : string.Empty)).ToArray());
        }
        foreach (ArchiveXlOperationChain chain in receipt.ArchiveXlChains.Where(value => value.Operations.Select(operation => operation.Provider).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
        {
            string[] providers = chain.Operations.Select(value => value.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            EvidenceClassification classification = EvidenceClassification.Informational;
            string summary = "Several mods change the same ArchiveXL target. The operation and keys decide whether they combine; order alone does not prove a winner.";
            string action = "No conflict is confirmed. Inspect the operation only when troubleshooting this target.";
            Add(items, receipt, decisions, ConflictSurface.ArchiveXl, chain.Target, classification, summary, action, null, providers, chain.Operations.Select(value => $"{value.Provider}|{CanonicalEvidencePath(value.FilePath)}|{value.Kind}|{value.Target}|{value.Payload}").ToArray());
        }
        foreach (RdarArchiveFailure failure in receipt.ArchiveFailures) Add(items, receipt, decisions, ConflictSurface.Diagnostic, failure.ArchiveName, EvidenceClassification.Unresolved, failure.Message, "Repair or remove the unreadable archive, then rescan.", null, [failure.Provider], [failure.ArchiveName, failure.Message]);
        foreach (ArchiveXlSourceFailure failure in receipt.ArchiveXlFailures.Where(value => value.Kind != ArchiveXlFailureKind.Coverage)) Add(items, receipt, decisions, ConflictSurface.Diagnostic, failure.FilePath, EvidenceClassification.Unresolved, failure.Message, "The scan could not read this ArchiveXL provider or file. Fix the named problem, then rescan.", null, [failure.Provider], [failure.FilePath, failure.Message]);
        foreach (SourceAnalysisFailure failure in (receipt.SourceFailures ?? []).Where(value => !IsParserCoverageLimitation(value)))
        {
            if (failure.Surface is "TweakXL RED" or "TweakXL interpretation" or "RedScript registration" or "CET Lua activation" or "CET Lua reachability" or "CET Lua import")
            {
                Add(items, receipt, decisions, ConflictSurface.Diagnostic, failure.FilePath, EvidenceClassification.Informational, failure.Message, "Source interpretation and coverage details are available here when needed.", null, [failure.Provider], [failure.Surface, failure.FilePath, failure.Message], null, "Information: source coverage limit", "The scan records how this source was interpreted or excluded", failure.Message, "This does not establish a conflict or a broken installation.");
                continue;
            }
            bool invalidRedmod = failure.Surface == "REDmod" && failure.Message.StartsWith("This folder will not load as a REDmod", StringComparison.Ordinal);
            string action = invalidRedmod ? "Reinstall or repair the named REDmod so its folder contains a valid info.json, then deploy REDmods again." : "The scan could not use this active file or provider. Fix the named problem, then rescan.";
            Add(items, receipt, decisions, ConflictSurface.Diagnostic, failure.FilePath, EvidenceClassification.Unresolved, failure.Message, action, null, [failure.Provider], [failure.Surface, failure.FilePath, failure.Message], null, invalidRedmod ? "REDmod not loaded" : null, invalidRedmod ? "Required descriptor is missing or invalid" : null);
        }
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
        return items.OrderBy(value => StateOrder(value.State)).ThenBy(value => value.Classification == EvidenceClassification.Unresolved ? 0 : 1).ThenBy(value => value.Surface).ThenBy(value => value.Target, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsParserCoverageLimitation(SourceAnalysisFailure failure)
        => failure.Surface == "RedScript condition" || failure.Surface == "TweakXL" && failure.Message.StartsWith("TweakXL source could not be represented completely:", StringComparison.Ordinal);

    private static int StateOrder(ConflictWorkState state) => state switch
    {
        ConflictWorkState.NeedsAttention => 0,
        ConflictWorkState.ReviewWhenRelevant => 1,
        ConflictWorkState.Reviewed => 2,
        _ => 3
    };

    private static void Add(List<ConflictWorkItem> items, ProfileScanReceipt receipt, IReadOnlyList<EvidenceDecision> decisions, ConflictSurface surface, string target, EvidenceClassification classification, string summary, string action, string? winner, string[] providers, string[] evidence, string[]? relatedTargets = null, string? resultOverride = null, string? proofOverride = null, string? meaningOverride = null, string? boundaryOverride = null)
    {
        string hash = Hash(surface, target, classification, winner, providers, evidence);
        EvidenceDecision? decision = classification == EvidenceClassification.Unresolved || receipt.InstallationId is null ? null : decisions.Where(value => value.Target == target && value.Providers.SequenceEqual(providers, StringComparer.OrdinalIgnoreCase)).FirstOrDefault(value => EvidenceDecisionStore.Evaluate(value, receipt.InstallationId, receipt.ProfileName, surface, hash) == EvidenceDecisionState.Resolved);
        bool reviewed = decision is not null;
        ConflictWorkState state = reviewed ? ConflictWorkState.Reviewed : classification is EvidenceClassification.Redundant or EvidenceClassification.Composable or EvidenceClassification.Informational ? ConflictWorkState.NoActionNeeded : classification is EvidenceClassification.Exclusive or EvidenceClassification.Unresolved ? ConflictWorkState.NeedsAttention : ConflictWorkState.ReviewWhenRelevant;
        EvidenceClassification effectiveClassification = reviewed ? EvidenceClassification.Intentional : classification;
        items.Add(new ConflictWorkItem(surface, target, effectiveClassification, state, summary, action, winner, providers, hash, reviewed ? decision!.Rationale : null, resultOverride, proofOverride, meaningOverride, boundaryOverride) { RelatedTargets = relatedTargets ?? [target] });
    }

    private static string Hash(ConflictSurface surface, string target, EvidenceClassification classification, string? winner, string[] providers, string[] evidence)
    {
        string canonical = string.Join('\n', [surface.ToString(), target, classification.ToString(), winner ?? string.Empty, .. providers, .. evidence]);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string[] InteractionEvidence(InteractionFinding finding, InteractionLookup interactions)
    {
        List<string> evidence = [finding.Kind.ToString()];
        if (finding.TweakRuntimeEvidence is { } runtime)
        {
            evidence.AddRange(runtime.Declarations.Select(value => $"declarative|{value.Provider}|{CanonicalEvidencePath(value.FilePath)}|{value.Kind}|{value.Value}"));
            evidence.AddRange(runtime.Writes.Select(value => $"runtime|{value.Provider}|{CanonicalEvidencePath(value.FilePath)}|{value.Operation}|{ScopedEvidence(value.CallSha256, value.SourceHash)}"));
        }
        evidence.AddRange((finding.DeclarationEvidence ?? []).Select(value => $"field|{value.Provider}|{CanonicalEvidencePath(value.FilePath)}|{RedScriptTarget.NormalizeType(value.Type)}").OrderBy(value => value, StringComparer.Ordinal));
        if (interactions.Tweak(finding.Target) is { } tweak) evidence.AddRange(tweak.Operations.Select(value => $"tweak|{value.Provider}|{CanonicalEvidencePath(value.FilePath)}|{value.Kind}|{value.Value}"));
        if (interactions.Tweak(finding.Target) is { Kind: TweakOverlapKind.BaseRecordDependency } dependency) evidence.AddRange(dependency.Operations.Select(value => "property|" + value.Target));
        evidence.AddRange(interactions.Flows(finding.Target).Select(value => $"redscript|{value.Provider}|{CanonicalEvidencePath(value.FilePath)}|{value.Kind}|{value.Continuation}|{ScopedEvidence(value.BodySha256, value.SourceHash)}"));
        evidence.AddRange(LuaInteractionEvidence(interactions.Callbacks(finding.Target)));
        return evidence.ToArray();
    }

    private static string[] InteractionProviders(InteractionFinding finding, InteractionLookup interactions)
        => finding.Providers
            .Concat(interactions.Flows(finding.Target).Select(value => value.Provider))
            .Concat(interactions.Callbacks(finding.Target).SelectMany(value => value.Copies.Select(copy => copy.Provider)))
            .Concat(interactions.Tweak(finding.Target)?.Operations.Select(value => value.Provider) ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<string> LuaInteractionEvidence(IEnumerable<LuaCallbackEvidence> callbacks)
        => callbacks.SelectMany(value => value.Copies.Length == 0
                ? [$"lua|{value.Target}|{value.Kind}|{value.Continuation}|unknown-provider|{ScopedEvidence(value.CallbackSha256, value.SourceHash)}"]
                : value.Copies.Select(copy => $"lua|{value.Target}|{value.Kind}|{value.Continuation}|{copy.Provider}:{CanonicalEvidencePath(copy.FilePath)}|{ScopedEvidence(value.CallbackSha256, value.SourceHash)}"))
            .OrderBy(value => value, StringComparer.Ordinal);

    private static string ScopedEvidence(string? scopedSha256, string sourceHash)
        => !string.IsNullOrWhiteSpace(scopedSha256) ? "scoped:" + scopedSha256 : "legacy-source:" + (string.IsNullOrWhiteSpace(sourceHash) ? "unavailable" : sourceHash);

    private static string CanonicalEvidencePath(string value)
        => PrivatePathRedactor.RelativeLabel(value).Replace('/', '\\');

    private static string PayloadIdentity(ResourceProvider provider)
        => !string.IsNullOrWhiteSpace(provider.CookedPayloadSha256) ? "cooked-sha256:" + provider.CookedPayloadSha256 : !string.IsNullOrWhiteSpace(provider.PayloadSha1) ? "rdar-sha1:" + provider.PayloadSha1 : "unavailable";

    private static string PackedSummary(ResourceConflict conflict)
    {
        ResourceProvider? winner = conflict.Providers.FirstOrDefault(value => string.Equals(value.ArchiveName, conflict.EngineWinnerArchive, StringComparison.OrdinalIgnoreCase));
        string comparison = conflict.Kind == ResourceConflictKind.OrderedOverlap ? "Payload comparison is unavailable, but the selected archive is established by the static order." : $"{conflict.Providers.Length - 1} lower archive provider{(conflict.Providers.Length == 2 ? string.Empty : "s")} contain a different payload.";
        return winner is null ? $"{conflict.EngineWinnerArchive} is first in the proven archive order. {comparison}" : $"{winner.Provider} supplies {winner.ArchiveName}, which is first in the proven archive order. {comparison}";
    }

    private static (string Summary, string Action) DescribeInteraction(InteractionFinding finding, EvidenceClassification classification, InteractionLookup interactions)
    {
        if (finding.TweakRuntimeEvidence is { } runtime)
        {
            var competing = runtime.CompetingValues().ToArray();
            if (competing.Length > 0)
            {
                string opposition = string.Join(" ", competing.Select(value => $"{value.Declaration.Provider} assigns {value.Declaration.Value} ({value.Declaration.FilePath}:{value.Declaration.LineNumber}); {value.Write.Provider} writes {value.Value} ({value.Write.FilePath}:{value.Write.Line})."));
                return (opposition, "Compare these requested values and choose the behavior you want. The runtime write can replace the declared value when that path runs; no final in-game value is claimed.");
            }
            if (classification is EvidenceClassification.Informational or EvidenceClassification.Composable)
            {
                string initialChanges = string.Join(" ", runtime.Declarations.Select(value => $"{value.Provider} declares {value.Kind} ({value.FilePath}:{value.LineNumber})."));
                string writes = string.Join(" ", runtime.Writes.Select(value => $"{value.Provider} calls {value.Operation} ({value.FilePath}:{value.Line})."));
                return ($"{initialChanges} {writes} No competing value is established by this relationship alone.", "No conflict action follows from this shared target. These source locations are available as context.");
            }
        }
        if (finding.DeclarationEvidence is { Length: > 0 } declarations)
        {
            string summary = string.Join(" ", declarations.Select(value => $"{value.Provider} adds this field as {value.Type} ({value.FilePath}:{value.Line})."));
            return (summary, "Capture the RedScript compiler log for this active profile before deciding which duplicate field declaration needs correction.");
        }
        RedScriptFlowEvidence[] flows = interactions.Flows(finding.Target);
        LuaCallbackEvidence[] callbacks = interactions.Callbacks(finding.Target);
        if (flows.Any(value => value.Kind == RedScriptFlowKind.Add) && callbacks.Any(value => value.Kind == LuaCallbackEvidenceKind.Override))
        {
            string summary = string.Join(" ", flows.Select(value => $"{value.Provider} adds the method ({value.FilePath}:{value.Line}).")) + " " + string.Join(" ", callbacks.Where(value => value.Kind == LuaCallbackEvidenceKind.Override).Select(value => $"{string.Join(", ", value.Copies.Select(copy => copy.Provider))} registers a CET override ({string.Join(", ", value.Copies.Select(copy => copy.FilePath))}:{value.Line})."));
            return (summary, classification == EvidenceClassification.Informational ? "An added method and its extension do not by themselves require conflict investigation." : "The CET override has no forwarding call to the added method. Compare the replacement behavior with the method's intended use.");
        }
        if (flows.Length > 0)
        {
            if (HasIdenticalReplacementBodies(flows))
            {
                string identicalSummary = string.Join(" ", flows.Select(value => $"{value.Provider} contains the same replacement body ({value.FilePath}:{value.Line})."));
                return (identicalSummary, "No action is needed for this method because the analyzed replacement bodies are identical.");
            }
            string summary = string.Join(" ", flows.Select(value => value.Kind switch
            {
                RedScriptFlowKind.Wrap => $"{value.Provider} wraps this method and {(value.Continuation == RedScriptContinuationEvidence.Continues ? "continues to the next implementation" : value.Continuation == RedScriptContinuationEvidence.EarlyReturnBeforeContinuation ? "can return before the next implementation" : "does not call the next implementation")} ({value.FilePath}:{value.Line}).",
                RedScriptFlowKind.Replace => $"{value.Provider} replaces the method ({value.FilePath}:{value.Line}).",
                _ => $"{value.Provider} adds the method ({value.FilePath}:{value.Line})."
            }));
            bool everyWrapperContinues = flows.All(value => value.Kind != RedScriptFlowKind.Wrap || value.Continuation == RedScriptContinuationEvidence.Continues);
            if (classification == EvidenceClassification.Informational) return (summary, "No conflict is proven by these continuation paths. Inspect the combined behavior only when troubleshooting this feature.");
            if (finding.Providers.Length == 1)
            {
                string internalAction = classification == EvidenceClassification.Exclusive
                    ? "Only one replacement can own this method. Compare the source locations and keep the intended declaration or combine them in a compatibility patch."
                    : classification == EvidenceClassification.CompilerEvidence
                        ? "Capture the RedScript compiler log for this active profile before deciding which duplicate declaration needs correction."
                        : classification is EvidenceClassification.Review or EvidenceClassification.OrderSensitive
                            ? "Test the affected feature with this exact profile and record what happens before changing the source declarations."
                            : "No action is needed unless the combined feature behaves incorrectly in game.";
                return (summary, internalAction);
            }
            string action = classification == EvidenceClassification.Exclusive ? $"Only one replacement can own this method. Install a compatibility patch or choose the intended provider between {finding.Providers[0]} and {finding.Providers[1]}." : classification == EvidenceClassification.Review ? "Test the affected feature once with this exact profile and record what happens before changing either mod." : everyWrapperContinues ? "No action is needed unless the combined feature behaves incorrectly in game." : "Test the affected feature once with this exact profile. If it fails, disable one mod only as a direction check, then rescan before blaming either mod.";
            return (summary, action);
        }

        TweakOverlap? tweak = interactions.Tweak(finding.Target);
        if (tweak is not null)
        {
            return DescribeTweak(tweak, classification);
        }

        if (callbacks.Length > 0)
        {
            string summary = string.Join(" ", callbacks.Select(value => $"{string.Join(", ", value.Copies.Select(copy => copy.Provider))} registers {value.Kind}; continuation is {value.Continuation}."));
            return classification == EvidenceClassification.Informational ? (summary, "No conflict is proven. Inspect this only when troubleshooting the affected callback.") : classification == EvidenceClassification.Composable ? (summary, "No action is needed unless a related feature fails.") : (summary, "Run the callback check from the support bundle. If both callbacks arrive and the feature works, mark the interaction intentional.");
        }
        return (finding.Summary, classification == EvidenceClassification.Composable ? "No action is needed unless the feature behaves differently in game." : "Open Technical details for the source locations, then test the affected feature once.");
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length] + "…";

    private static (string Summary, string Action) DescribeTweak(TweakOverlap tweak, EvidenceClassification classification)
    {
        if (tweak.Kind == TweakOverlapKind.InternalContext)
        {
            string definitions = string.Join(" ", tweak.Operations.Select(value => $"{value.Kind}: {Truncate(value.Value, 72)} ({value.FilePath}:{value.LineNumber})."));
            return (definitions, "These definitions are available as source context. No competing active component or required change is established.");
        }
        if (tweak.Kind == TweakOverlapKind.BaseRecordDependency)
        {
            string declarations = string.Join(" ", tweak.Operations.Select(value => $"{value.Provider}: {value.Target} {value.Kind} {Truncate(value.Value, 72)} ({value.FilePath}:{value.LineNumber})."));
            return (declarations, "Use these source locations when investigating the derived record. This relationship alone does not require a patch.");
        }
        string operations = string.Join(" ", tweak.Operations.Select(value => $"{value.Provider} {OperationVerb(value.Kind)} {Truncate(value.Value, 72)}."));
        if (classification == EvidenceClassification.Composable)
        {
            if (tweak.Kind == TweakOverlapKind.Redundant) return ($"{operations} Every active provider declares the same value.", "No action is needed.");
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
        if (tweak.Kind == TweakOverlapKind.ScalarOverwrite) return ($"{operations} The active sources assign different values; the final in-game value is not observed here.", "Open Technical details to compare the values. Then capture the in-game value after startup before deciding which value should apply.");
        if (tweak.Kind == TweakOverlapKind.DuplicateMutation) return ($"{operations} The same value can be inserted more than once.", "Compare the additions and the array's intended use. Repeated values can be intentional; do not remove them or add uniqueness guards without establishing a competing effect.");
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

    private static (string? Result, string? Proof, string? Meaning, string? Boundary) InteractionLabels(InteractionFinding finding, InteractionLookup interactions, EvidenceClassification classification)
    {
        if (finding.TweakRuntimeEvidence is { } runtime)
        {
            if (runtime.CompetingValues().Any())
                return ("Review: different declared and runtime values", "Different providers request different literal values", "The runtime write can replace another provider's declared value.", "This proves differing requested values, not the final in-game value or a gameplay failure.");
            if (classification is EvidenceClassification.Informational or EvidenceClassification.Composable)
                return ("Information: shared TweakDB target", "Declarations and runtime writes refer to the same field", "The source relationship does not establish competing values.", "Settings updates, integrations, and unknown expressions are not conflicts by themselves.");
        }
        RedScriptFlowEvidence[] flows = interactions.Flows(finding.Target);
        LuaCallbackEvidence[] callbacks = interactions.Callbacks(finding.Target);
        if (classification == EvidenceClassification.Review && flows.Any(value => value.Kind == RedScriptFlowKind.Add) && callbacks.Any(value => value.Kind == LuaCallbackEvidenceKind.Override && value.Continuation == LuaContinuationEvidence.Missing))
            return ("Review: extension replaces an added method", "The CET override contains no forwarding call to the added implementation", "One provider can replace a method supplied by another.", "This does not prove a gameplay failure or that the replacement is unwanted.");
        if (classification == EvidenceClassification.Informational && flows.Any(value => value.Kind == RedScriptFlowKind.Add) && callbacks.Any(value => value.Kind == LuaCallbackEvidenceKind.Override))
            return ("Information: added method and extension", "An added RedScript method and a CET override share a target", "This can be ordinary extension or compatibility wiring.", "Their shared target alone does not establish a conflict or a runtime outcome.");
        if (classification == EvidenceClassification.Informational && flows.Any(value => value.Kind == RedScriptFlowKind.Wrap))
            return ("Information: continuation paths found", "Each analyzed wrapper contains a continuation path", "The next implementation is not statically suppressed by these wrappers.", "A continuation path does not prove behavioral compatibility, argument preservation, or an unchanged result.");
        if (finding.Providers.Length == 1 && HasIdenticalReplacementBodies(interactions.Flows(finding.Target)))
            return ("No action: same replacement", "The replacement bodies are identical", "The declarations contain the same replacement body. The analyzed method result is identical in these sources.", "This proves identical replacement source, not a competing method result. It does not prove the sources are compatible elsewhere.");
        if (HasIdenticalReplacementBodies(interactions.Flows(finding.Target))) return ("No action: same replacement", "The replacement bodies are identical", "Both mods declare the same replacement body. The analyzed method result is identical in both sources.", "This proves identical replacement source, not a competing method result. It does not prove the mods are compatible elsewhere.");
        TweakOverlap? tweak = interactions.Tweak(finding.Target);
        if (tweak is null) return (null, null, null, null);
        if (tweak.Kind == TweakOverlapKind.InternalContext)
            return ("Information: internal source definitions", "Differing definitions occur within the same provider", "The source difference alone does not establish competing active components.", "This does not establish a gameplay failure or require a compatibility patch.");
        if (tweak.Kind == TweakOverlapKind.BaseRecordDependency)
            return ("Information: direct base-record relationship", "A $base declaration names a record changed by another provider", "The evidence includes changed base properties and matching explicit derived-property writes.", "This does not resolve final inherited values, recursive dependencies, or prove that the relationship is a conflict.");
        (string? result, string? proof) = tweak.Kind switch
        {
            TweakOverlapKind.ScalarOverwrite => ("Review: different values assigned", "Two active sources assign different values to one field"),
            TweakOverlapKind.AssignmentThenMutation => ("No action: assignment followed by changes", "TweakXL applies the operations in a documented order"),
            TweakOverlapKind.MixedArrayOperations when HasCompetingWholeArrayReplacements(tweak) => ("Review: different arrays assigned", "Two active sources assign different complete arrays"),
            TweakOverlapKind.MixedArrayOperations when tweak.Operations.Any(value => value.Kind == TweakOperationKind.ArrayReplacement) => ("Review: final array needs verification", "A complete array assignment is mixed with other changes"),
            TweakOverlapKind.MixedArrayOperations => ("Review: duplicate count may change", "Guarded and unguarded additions use the same value"),
            TweakOverlapKind.DuplicateMutation => ("Review: one value may be added twice", "Multiple unguarded additions use the same value"),
            TweakOverlapKind.RecordDefinitionCollision => ("Review: same record is built differently", finding.Providers.Length == 1 ? "Several declarations construct the same record with different source" : "Several mods construct the same record with different source"),
            TweakOverlapKind.SourceArrayDependency => ("Review: copied array needs verification", "One mod copies an array that another mod changes"),
            TweakOverlapKind.ComposableMutation when HasAddRemovePair(tweak) => ("No action: remove happens before add", "TweakXL's documented order decides membership"),
            TweakOverlapKind.ComposableMutation when tweak.Operations.All(value => IsTweakAddition(value.Kind)) && tweak.Operations.Select(value => value.Value).Distinct(StringComparer.Ordinal).Count() == 1 && tweak.Operations.All(value => IsUniqueTweakAddition(value.Kind)) => ("No action: duplicate is prevented", "A uniqueness guard keeps one copy of the value"),
            TweakOverlapKind.ComposableMutation when tweak.Operations.All(value => IsTweakAddition(value.Kind)) => ("No action: all additions apply", "The mods add different values"),
            TweakOverlapKind.ComposableMutation => ("No action: array changes can coexist", "The mods change different array values"),
            TweakOverlapKind.Redundant => ("No action: same declaration", "Every provider declares the same value"),
            _ => (null, null)
        };
        return (result, proof, null, null);
    }

    private static EvidenceClassification ClassifyInteraction(InteractionFinding finding, InteractionLookup interactions)
    {
        if (finding.DeclarationEvidence is { Length: > 0 }) return EvidenceClassification.CompilerEvidence;
        if (finding.Kind == InteractionFindingKind.Exclusive)
        {
            RedScriptFlowEvidence[] replacements = interactions.Flows(finding.Target);
            bool competingReplacements = finding.Providers.Length == 1
                ? replacements.Count(value => value.Kind == RedScriptFlowKind.Replace) > 1
                : HasDocumentedExclusiveMechanism(replacements);
            return competingReplacements ? EvidenceClassification.Exclusive : EvidenceClassification.Review;
        }
        if (finding.TweakRuntimeEvidence?.CompetingValues().Any() == true) return EvidenceClassification.CompetingDeclaration;
        if (finding.Kind == InteractionFindingKind.Informational) return EvidenceClassification.Informational;
        TweakOverlap? tweak = interactions.Tweak(finding.Target);
        if (tweak is not null) return tweak.Kind == TweakOverlapKind.BaseRecordDependency ? EvidenceClassification.Informational
            : tweak.Kind == TweakOverlapKind.ScalarOverwrite || HasCompetingWholeArrayReplacements(tweak) ? EvidenceClassification.CompetingDeclaration
            : tweak.Kind is TweakOverlapKind.ComposableMutation or TweakOverlapKind.AssignmentThenMutation or TweakOverlapKind.Redundant ? EvidenceClassification.Composable
            : tweak.Kind is TweakOverlapKind.DuplicateMutation or TweakOverlapKind.RecordDefinitionCollision or TweakOverlapKind.SourceArrayDependency ? EvidenceClassification.Review
            : EvidenceClassification.OrderSensitive;
        RedScriptFlowEvidence[] flows = interactions.Flows(finding.Target);
        LuaCallbackEvidence[] callbacks = interactions.Callbacks(finding.Target);
        if (finding.Providers.Length == 1 && flows.Count(value => value.Kind == RedScriptFlowKind.Add) > 1) return EvidenceClassification.CompilerEvidence;
        if (flows.Any(value => value.Kind == RedScriptFlowKind.Add) && callbacks.Any(value => value.Kind == LuaCallbackEvidenceKind.Override)) return EvidenceClassification.Review;
        if (flows.Any(value => value.Kind == RedScriptFlowKind.Wrap && value.Continuation is RedScriptContinuationEvidence.EarlyReturnBeforeContinuation or RedScriptContinuationEvidence.Missing)) return EvidenceClassification.OrderSensitive;
        if (callbacks.Length > 0 && flows.Length == 0 && callbacks.All(value => value.Kind != LuaCallbackEvidenceKind.Override)) return EvidenceClassification.Informational;
        if (flows.Any(value => value.Kind == RedScriptFlowKind.Wrap) && flows.All(value => value.Kind != RedScriptFlowKind.Wrap || value.Continuation == RedScriptContinuationEvidence.Continues) && callbacks.All(value => value.Kind != LuaCallbackEvidenceKind.Override)) return EvidenceClassification.Informational;
        if (finding.Kind == InteractionFindingKind.Composable) return EvidenceClassification.Composable;
        if (flows.Length > 0 && flows.All(value => value.Kind != RedScriptFlowKind.Wrap || value.Continuation == RedScriptContinuationEvidence.Continues)) return EvidenceClassification.Composable;
        if (callbacks.Length > 0 && callbacks.All(value => value.Kind != LuaCallbackEvidenceKind.Override)) return EvidenceClassification.Composable;
        return EvidenceClassification.Review;
    }

    private static bool HasCompetingWholeArrayReplacements(TweakOverlap tweak)
        => tweak.Operations.Length > 1
            && tweak.Operations.All(value => value.Kind == TweakOperationKind.ArrayReplacement)
            && tweak.Operations.Select(value => value.Value).Distinct(StringComparer.Ordinal).Count() > 1;

    private static bool HasIdenticalReplacementBodies(RedScriptFlowEvidence[] flows)
        => flows.Length > 1
            && flows.All(value => value.Kind == RedScriptFlowKind.Replace && value.BodySha256 is not null)
            && flows.Select(value => value.BodySha256).Distinct(StringComparer.Ordinal).Count() == 1;

    private static bool HasDocumentedExclusiveMechanism(IEnumerable<RedScriptFlowEvidence> flows)
        => flows.Where(value => value.Kind == RedScriptFlowKind.Replace).Select(value => value.Provider).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;

    private sealed class InteractionLookup
    {
        private readonly Dictionary<string, TweakOverlap> _tweaks;
        private readonly ILookup<string, RedScriptFlowEvidence> _flows;
        private readonly ILookup<string, LuaCallbackEvidence> _callbacks;

        public InteractionLookup(ProfileScanReceipt receipt)
        {
            _tweaks = receipt.TweakOverlaps.ToDictionary(value => value.Target, StringComparer.Ordinal);
            _flows = receipt.RedScriptFlows.ToLookup(value => value.Target, StringComparer.Ordinal);
            _callbacks = receipt.LuaCallbacks.ToLookup(value => value.Target, StringComparer.Ordinal);
        }

        public TweakOverlap? Tweak(string target) => _tweaks.GetValueOrDefault(target);

        public RedScriptFlowEvidence[] Flows(string target) => _flows[target].ToArray();

        public LuaCallbackEvidence[] Callbacks(string target)
        {
            string methodBase = InteractionReportBuilder.MethodBase(target);
            return string.Equals(methodBase, target, StringComparison.Ordinal)
                ? _callbacks[target].ToArray()
                : _callbacks[target].Concat(_callbacks[methodBase]).ToArray();
        }
    }
}

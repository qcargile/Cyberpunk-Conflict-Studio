namespace ConflictStudio.Core;

public enum InteractionFindingKind { Exclusive, Review, Composable }

public sealed record InteractionFinding(string Target, InteractionFindingKind Kind, string Summary, string[] Providers);

public static class InteractionReportBuilder
{
    public static InteractionFinding[] Build(ModSourceInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        RedScriptFlowEvidence[] redScriptFlows = RedScriptFlowEvidenceAnalyzer.Analyze(inventory.RedScripts);
        LuaCallbackEvidence[] luaCallbacks = LuaCallbackEvidenceAnalyzer.Analyze(inventory.LuaSources);
        TweakOverlap[] tweakOverlaps = TweakInteractionAnalyzer.Analyze(inventory.TweakSources);
        return Build(inventory, redScriptFlows, luaCallbacks, tweakOverlaps);
    }

    public static InteractionFinding[] Build(ModSourceInventory inventory, IReadOnlyList<RedScriptFlowEvidence> redScriptFlows, IReadOnlyList<LuaCallbackEvidence> luaCallbacks, IReadOnlyList<TweakOverlap> tweakOverlaps)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(redScriptFlows);
        ArgumentNullException.ThrowIfNull(luaCallbacks);
        ArgumentNullException.ThrowIfNull(tweakOverlaps);
        ILookup<string, RedScriptFlowEvidence> flowsByTarget = redScriptFlows.ToLookup(value => value.Target, StringComparer.Ordinal);
        ILookup<string, LuaCallbackEvidence> callbacksByTarget = luaCallbacks.ToLookup(value => value.Target, StringComparer.Ordinal);
        List<InteractionFinding> findings = [];
        findings.AddRange(RedScriptInteractionAnalyzer.Analyze(inventory.RedScripts).Select(value => RedScriptFinding(value, flowsByTarget[value.Target])));
        findings.AddRange(LuaInteractionAnalyzer.Analyze(inventory.LuaSources).Select(value => LuaFinding(value, callbacksByTarget[value.Target])));
        findings.AddRange(CrossLanguageFindings(redScriptFlows, callbacksByTarget));
        findings.AddRange(tweakOverlaps.Select(TweakFinding));
        return findings.GroupBy(value => value.Target, StringComparer.Ordinal)
            .Select(group => new InteractionFinding(group.Key, group.Min(value => value.Kind), string.Join(" ", group.Select(value => value.Summary).Distinct(StringComparer.Ordinal)), group.SelectMany(value => value.Providers).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()))
            .OrderBy(value => value.Kind)
            .ThenBy(value => value.Target, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<InteractionFinding> CrossLanguageFindings(IReadOnlyList<RedScriptFlowEvidence> flows, ILookup<string, LuaCallbackEvidence> callbacksByTarget)
    {
        foreach (RedScriptFlowEvidence flow in flows.Where(value => value.Kind == RedScriptFlowKind.Add))
        {
            string target = MethodBase(flow.Target);
            LuaCallbackEvidence[] overrides = callbacksByTarget[target].Where(value => value.Kind == LuaCallbackEvidenceKind.Override && value.Confidence == EvidenceConfidence.Literal).ToArray();
            string[] providers = overrides.SelectMany(value => value.Copies.Select(copy => copy.Provider)).Append(flow.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (overrides.Length == 0 || providers.Length < 2) continue;
            yield return new InteractionFinding(flow.Target, InteractionFindingKind.Review, "A CET override targets a RedScript method added by another active provider. This is a source overlap; the report does not establish a compiler or runtime outcome.", providers);
        }
    }

    internal static string MethodBase(string target)
    {
        int parameters = target.IndexOf('(');
        return parameters < 0 ? target : target[..parameters];
    }

    private static InteractionFinding RedScriptFinding(RedScriptOverlap overlap, IEnumerable<RedScriptFlowEvidence> flows)
    {
        string[] providers = overlap.Hooks.Select(value => value.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (overlap.Kind == RedScriptOverlapKind.ExclusiveReplacement) return new InteractionFinding(overlap.Target, InteractionFindingKind.Exclusive, "Multiple active replacements declare exclusive source ownership of this method.", providers);
        if (overlap.Kind == RedScriptOverlapKind.RedundantReplacement) return new InteractionFinding(overlap.Target, InteractionFindingKind.Composable, "Multiple active providers contain the same replacement body in analyzed source. This is redundant related source evidence, not a competing source outcome.", providers);
        if (overlap.Kind == RedScriptOverlapKind.AddedMemberCollision) return new InteractionFinding(overlap.Target, InteractionFindingKind.Review, "Multiple active providers add the same RedScript member. This is a source overlap; no compiler outcome is established by this report.", providers);
        if (overlap.Kind == RedScriptOverlapKind.AddedMemberInteraction) return new InteractionFinding(overlap.Target, InteractionFindingKind.Review, "One active provider adds this RedScript member while another wraps or replaces the same target. This is a source overlap; no compiler or runtime outcome is established by this report.", providers);
        RedScriptFlowEvidence[] targetFlows = flows.Where(value => value.Target == overlap.Target).ToArray();
        bool hasMissingContinuation = targetFlows.Any(value => value.Kind == RedScriptFlowKind.Wrap && value.Continuation == RedScriptContinuationEvidence.Missing);
        bool hasEarlyReturn = targetFlows.Any(value => value.Kind == RedScriptFlowKind.Wrap && value.Continuation == RedScriptContinuationEvidence.EarlyReturnBeforeContinuation);
        return hasMissingContinuation
            ? new InteractionFinding(overlap.Target, InteractionFindingKind.Review, "At least one wrapper does not contain a continuation. This unresolved source evidence does not establish a runtime outcome.", providers)
            : hasEarlyReturn
                ? new InteractionFinding(overlap.Target, InteractionFindingKind.Review, "At least one wrapper contains a return before continuation. This is a conditional source interaction, not a runtime-failure verdict.", providers)
            : new InteractionFinding(overlap.Target, InteractionFindingKind.Composable, "Every analyzed wrapper contains a continuation path, so the next implementation is not statically suppressed. This does not establish behavioral compatibility.", providers);
    }

    private static InteractionFinding LuaFinding(LuaOverlap overlap, IEnumerable<LuaCallbackEvidence> callbacks)
    {
        string[] providers = overlap.Hooks.Select(value => value.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return overlap.Kind switch
        {
            _ => new InteractionFinding(overlap.Target, InteractionFindingKind.Review, "Multiple active CET callbacks share this exact target. This is a related source surface; the report does not establish their runtime relationship.", providers)
        };
    }

    private static InteractionFinding TweakFinding(TweakOverlap overlap)
    {
        InteractionFindingKind kind = overlap.Kind is TweakOverlapKind.ComposableMutation or TweakOverlapKind.AssignmentThenMutation or TweakOverlapKind.Redundant ? InteractionFindingKind.Composable : InteractionFindingKind.Review;
        string summary = overlap.Kind == TweakOverlapKind.Redundant ? "All active providers declare the same value."
            : overlap.Kind == TweakOverlapKind.AssignmentThenMutation ? "TweakXL commits the array assignment before applying removals, prepends, and appends."
            : overlap.Kind == TweakOverlapKind.ComposableMutation ? "TweakXL applies these mutations in documented phases; no competing assignment or duplicate insertion is present."
            : overlap.Kind == TweakOverlapKind.DuplicateMutation ? "Multiple providers append the same array element without a uniqueness guard, so the final array may contain duplicates."
            : overlap.Kind == TweakOverlapKind.RecordDefinitionCollision ? "Multiple providers construct the same record differently. TweakXL keeps the first construction it encounters, but normal-file discovery order is not reproduced here."
            : overlap.Kind == TweakOverlapKind.SourceArrayDependency ? "A producer-consumer dependency exists because a provider copies from an array another provider changes. The copied final contents are not resolved statically."
            : overlap.Kind == TweakOverlapKind.ScalarOverwrite ? "Active providers declare competing scalar values. This report does not infer a final winner."
            : "Active providers declare competing array operations. This report preserves the documented operations without resolving a final result.";
        return new InteractionFinding(overlap.Target, kind, summary, overlap.Operations.Select(operation => operation.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }
}

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
        List<InteractionFinding> findings = [];
        findings.AddRange(RedScriptInteractionAnalyzer.Analyze(inventory.RedScripts).Select(value => RedScriptFinding(value, redScriptFlows)));
        findings.AddRange(LuaInteractionAnalyzer.Analyze(inventory.LuaSources).Select(value => LuaFinding(value, luaCallbacks)));
        findings.AddRange(CrossLanguageFindings(redScriptFlows, luaCallbacks));
        findings.AddRange(tweakOverlaps.Select(TweakFinding));
        return findings.GroupBy(value => value.Target, StringComparer.Ordinal)
            .Select(group => new InteractionFinding(group.Key, group.Min(value => value.Kind), string.Join(" ", group.Select(value => value.Summary).Distinct(StringComparer.Ordinal)), group.SelectMany(value => value.Providers).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()))
            .OrderBy(value => value.Kind)
            .ThenBy(value => value.Target, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<InteractionFinding> CrossLanguageFindings(IReadOnlyList<RedScriptFlowEvidence> flows, IReadOnlyList<LuaCallbackEvidence> callbacks)
    {
        foreach (RedScriptFlowEvidence flow in flows.Where(value => value.Kind == RedScriptFlowKind.Add))
        {
            string target = MethodBase(flow.Target);
            LuaCallbackEvidence[] overrides = callbacks.Where(value => value.Kind == LuaCallbackEvidenceKind.Override && value.Confidence == EvidenceConfidence.Literal && value.Target == target).ToArray();
            string[] providers = overrides.SelectMany(value => value.Copies.Select(copy => copy.Provider)).Append(flow.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (overrides.Length == 0 || providers.Length < 2) continue;
            yield return new InteractionFinding(flow.Target, InteractionFindingKind.Review, "A CET override targets a RedScript method added by another active provider. The added method must compile, and the override can stop before the added implementation.", providers);
        }
    }

    internal static string MethodBase(string target)
    {
        int parameters = target.IndexOf('(');
        return parameters < 0 ? target : target[..parameters];
    }

    private static InteractionFinding RedScriptFinding(RedScriptOverlap overlap, IReadOnlyList<RedScriptFlowEvidence> flows)
    {
        string[] providers = overlap.Hooks.Select(value => value.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (overlap.Kind == RedScriptOverlapKind.ExclusiveReplacement) return new InteractionFinding(overlap.Target, InteractionFindingKind.Exclusive, "Multiple active replacements target this method, so only one replacement can take effect.", providers);
        if (overlap.Kind == RedScriptOverlapKind.AddedMemberCollision) return new InteractionFinding(overlap.Target, InteractionFindingKind.Review, "Multiple active providers add the same RedScript member. Check the RedScript compiler log because this tool does not choose an owner for added members.", providers);
        if (overlap.Kind == RedScriptOverlapKind.AddedMemberInteraction) return new InteractionFinding(overlap.Target, InteractionFindingKind.Review, "One active provider adds this RedScript member while another wraps or replaces the same target. Check the compiler log and the resulting chain before treating both behaviors as active.", providers);
        RedScriptFlowEvidence[] targetFlows = flows.Where(value => value.Target == overlap.Target).ToArray();
        bool canSuppress = targetFlows.Any(value => value.Kind == RedScriptFlowKind.Wrap && value.Continuation != RedScriptContinuationEvidence.Continues);
        return canSuppress
            ? new InteractionFinding(overlap.Target, InteractionFindingKind.Review, "At least one wrapper can stop before the next implementation. Runtime behavior depends on the path and wrapper order.", providers)
            : new InteractionFinding(overlap.Target, InteractionFindingKind.Composable, "The active wrappers continue through the RedScript chain. No implementation is statically suppressed.", providers);
    }

    private static InteractionFinding LuaFinding(LuaOverlap overlap, IReadOnlyList<LuaCallbackEvidence> callbacks)
    {
        string[] providers = overlap.Hooks.Select(value => value.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        LuaCallbackEvidence[] targetCallbacks = callbacks.Where(value => value.Target == overlap.Target).ToArray();
        bool overrideCanStop = targetCallbacks.Any(value => value.Kind == LuaCallbackEvidenceKind.Override && value.Continuation != LuaContinuationEvidence.Continues);
        return overlap.Kind switch
        {
            LuaOverlapKind.OverrideReview => new InteractionFinding(overlap.Target, InteractionFindingKind.Review, "Multiple active CET overrides share this target. CET chains them by registration order, but the active mods' cross-mod registration order is not established.", providers),
            LuaOverlapKind.OverrideWithObservers when overrideCanStop => new InteractionFinding(overlap.Target, InteractionFindingKind.Review, "A CET override and observers share this target, and the override can stop before the underlying implementation. The observers still run, but the behavior they observe may change.", providers),
            LuaOverlapKind.OverrideWithObservers => new InteractionFinding(overlap.Target, InteractionFindingKind.Composable, "One continuing CET override and one or more observers share this target. The observers run around the override and underlying implementation.", providers),
            _ => new InteractionFinding(overlap.Target, InteractionFindingKind.Composable, "The active CET hooks are observers. Target sharing alone does not suppress either callback.", providers)
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
            : overlap.Kind == TweakOverlapKind.SourceArrayDependency ? "A provider copies values from a source array that another provider changes; the copied contents are not resolved statically."
            : overlap.Kind == TweakOverlapKind.ScalarOverwrite ? "Active providers assign different final values. TweakXL keeps the assignment it reads last, but normal-file discovery order is not reproduced here."
            : "Active providers replace an array or apply opposing operations to the same element.";
        return new InteractionFinding(overlap.Target, kind, summary, overlap.Operations.Select(operation => operation.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }
}

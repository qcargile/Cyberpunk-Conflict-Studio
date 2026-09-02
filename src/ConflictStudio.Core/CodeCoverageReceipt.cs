namespace ConflictStudio.Core;

public sealed record CodeSourceCoverage(string Surface, int AnalyzedFiles);

public sealed record CodeCoverageReceipt(
    int SchemaVersion,
    CodeSourceCoverage[] Sources,
    int UnsupportedTweakFiles,
    int UnreadableInputs,
    int LiteralCallbacks,
    int DynamicCallbacks,
    string[] Limitations)
{
    internal static CodeCoverageReceipt Build(ModSourceInventory inventory, LuaCallbackEvidence[] callbacks, SourceAnalysisFailure[] failures, int archiveXlSources)
    {
        int unreadable = failures.Where(value => value.Surface is "RedScript" or "CET Lua" or "TweakXL" or "Evidence snapshot" or "Deployment evidence")
            .DistinctBy(value => (value.Provider, value.FilePath)).Count();
        int unsupported = inventory.Failures.Where(value => value.Surface == "TweakXL RED").DistinctBy(value => (value.Provider, value.FilePath)).Count();
        return new(1,
            [new("RedScript", inventory.RedScripts.Length), new("CET Lua", inventory.LuaSources.Length), new("TweakXL YAML", inventory.TweakSources.Length), new("ArchiveXL", archiveXlSources)],
            unsupported, unreadable,
            callbacks.Where(value => value.Confidence != EvidenceConfidence.Dynamic).Sum(value => value.Copies.Length),
            callbacks.Where(value => value.Confidence == EvidenceConfidence.Dynamic).Sum(value => value.Copies.Length),
            ["Partial static coverage: counts are effective source files submitted to analysis, not proof of execution or complete language coverage.",
             "RedScript declaration coverage is annotation-only; literal runtime TweakDB writes are also collected. No complete symbol analysis.",
             "CET Lua reachability follows literal imports from effective init.lua files. Unresolved loading retains the mod's files as candidates; function execution and dynamic callback targets are not established.",
             "RED .tweak files are unsupported. Unreadable inputs count distinct file or directory diagnostics, not conflict findings; parser and activation limitations remain in scan diagnostics.",
             "Native plugin internals are unexamined."]);
    }
}

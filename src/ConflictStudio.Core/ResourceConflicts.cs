namespace ConflictStudio.Core;

public enum ResourcePathConfidence { Unresolved, ResolvedIndex, ArchiveCustomData }

public sealed record ResourceSegmentMetadata(uint InlineBufferSegmentCount, uint Start, uint End);

public sealed record ResourceProvider(
    string ArchiveName,
    ulong ResourceHash,
    string? ResourcePath,
    string? PayloadSha1,
    ResourceSegmentMetadata? SegmentMetadata = null,
    string? ResourceType = null,
    ResourcePathConfidence PathConfidence = ResourcePathConfidence.Unresolved,
    string? ProviderName = null,
    string? CookedPayloadSha256 = null)
{
    public string Provider => string.IsNullOrWhiteSpace(ProviderName) ? ArchiveName : ProviderName;
    public string? PayloadFingerprint => CookedPayloadSha256 ?? PayloadSha1;
}

public enum ResourceConflictKind { Redundant, Divergent, Unresolved, OrderedOverlap }

public sealed record ResourceConflict(ulong ResourceHash, string DisplayName, ResourceConflictKind Kind, string EngineWinnerArchive, ResourceProvider[] Providers);

public static class ResourceConflictAnalyzer
{
    public static ResourceConflict[] Analyze(IReadOnlyList<ResourceProvider> providers, IReadOnlyList<string> archiveOrder)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(archiveOrder);
        Dictionary<string, int> positions = archiveOrder.Select((name, index) => new { name, index }).ToDictionary(value => value.name, value => value.index, StringComparer.OrdinalIgnoreCase);
        return providers.GroupBy(value => value.ResourceHash)
            .Where(group => group.Select(value => value.ArchiveName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(group => Create(group.Key, group.ToArray(), positions))
            .OrderBy(value => value.DisplayName, StringComparer.Ordinal)
            .ToArray();
    }

    private static ResourceConflict Create(ulong resourceHash, ResourceProvider[] providers, Dictionary<string, int> positions)
    {
        bool orderIncomplete = providers.Any(value => !positions.ContainsKey(value.ArchiveName));
        ResourceProvider[] ordered = providers.OrderBy(value => positions.TryGetValue(value.ArchiveName, out int position) ? position : int.MaxValue).ToArray();
        string? path = ordered.Select(value => value.ResourcePath).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        string?[] payloadValues = ordered.Select(value => value.PayloadFingerprint).ToArray();
        string[] payloads = payloadValues.Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        bool ambiguousWinner = !orderIncomplete && RedmodArchiveIdentity.IsAmbiguousProvider(ordered[0], providers);
        bool winnerPayloadUnknown = ambiguousWinner && RedmodArchiveIdentity.ProviderPayloadsDiffer(ordered[0], providers);
        ResourceConflictKind kind = orderIncomplete || winnerPayloadUnknown ? ResourceConflictKind.Unresolved : payloadValues.Any(string.IsNullOrWhiteSpace) ? ResourceConflictKind.OrderedOverlap : payloads.Length == 1 ? ResourceConflictKind.Redundant : ResourceConflictKind.Divergent;
        string winner = kind == ResourceConflictKind.Unresolved ? "unresolved" : ambiguousWinner ? ordered[0].Provider : ordered[0].ArchiveName;
        return new ResourceConflict(resourceHash, path ?? $"resource hash {resourceHash}", kind, winner, ordered);
    }
}

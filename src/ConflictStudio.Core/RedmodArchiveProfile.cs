using System.Security.Cryptography;
using System.Text.Json;

namespace ConflictStudio.Core;

public sealed record RedmodArchiveProfile(Mo2Archive[] Archives, string[] EffectiveOrder, ArchiveOrderEvidence OrderEvidence, SourceAnalysisFailure[] Failures);

public static class RedmodArchiveProfileScanner
{
    private const string OrderRelativePath = "r6\\cache\\modded\\MO_REDmod_load_order.txt";

    public static RedmodArchiveProfile Scan(IReadOnlyList<DeploymentProvider> providers, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(providers);
        Dictionary<string, RedmodCandidate> candidates = new(StringComparer.OrdinalIgnoreCase);
        List<SourceAnalysisFailure> failures = [];
        foreach (DeploymentProvider provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (string directory in CandidateDirectories(provider))
            {
                string folder = Path.GetFileName(directory);
                if (candidates.ContainsKey(folder)) continue;
                string infoPath = Path.Combine(directory, "info.json");
                try
                {
                    RequireValidInfo(infoPath);
                    string archivesRoot = Path.Combine(directory, "archives");
                    string[] archivePaths = Directory.Exists(archivesRoot) ? Directory.EnumerateFiles(archivesRoot, "*.archive", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray() : [];
                    candidates.Add(folder, new RedmodCandidate(folder, provider.Name, archivePaths));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
                {
                    failures.Add(new SourceAnalysisFailure(provider.Name, Path.Combine("mods", folder, "info.json"), "REDmod", exception.Message));
                }
            }
        }

        (string[] folders, ArchiveOrderEvidence evidence) = ResolveOrder(providers, candidates.Keys.ToArray());
        if (failures.Count > 0) evidence = new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.Unresolved, evidence.Provider, evidence.SourcePath, "At least one active REDmod descriptor could not be read, so the REDmod archive set is incomplete.") { ProblemLane = ArchiveOrderProblemLane.Redmod, SourceFingerprints = evidence.SourceFingerprints, AbsentSources = evidence.AbsentSources };
        List<Mo2Archive> archives = [];
        foreach (string folder in folders)
        {
            if (!candidates.TryGetValue(folder, out RedmodCandidate? candidate)) continue;
            foreach (string path in candidate.ArchivePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileInfo file = new(path);
                using FileStream stream = file.OpenRead();
                string logicalName = $"REDmod/{folder}/{Path.GetFileName(path)}";
                archives.Add(new Mo2Archive($"REDmod: {folder} ({candidate.Provider})", logicalName, path, stream.Length, Convert.ToHexStringLower(SHA256.HashData(stream))));
            }
        }
        return new RedmodArchiveProfile(archives.ToArray(), archives.Select(value => value.ArchiveName).ToArray(), evidence, failures.ToArray());
    }

    private static IEnumerable<string> CandidateDirectories(DeploymentProvider provider)
    {
        string modsRoot = Path.Combine(provider.RootPath, "mods");
        if (Directory.Exists(modsRoot)) return Directory.EnumerateDirectories(modsRoot).OrderBy(Path.GetFileName, StringComparer.Ordinal);
        return File.Exists(Path.Combine(provider.RootPath, "info.json")) ? [provider.RootPath] : [];
    }

    private static void RequireValidInfo(string path)
    {
        if (!File.Exists(path)) throw new InvalidDataException("The REDmod folder has no info.json descriptor.");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("name", out JsonElement name) || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString())
            || !document.RootElement.TryGetProperty("version", out JsonElement version) || version.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(version.GetString())) throw new InvalidDataException("The REDmod info.json descriptor must contain non-empty string name and version fields.");
    }

    private static (string[] Folders, ArchiveOrderEvidence Evidence) ResolveOrder(IReadOnlyList<DeploymentProvider> providers, string[] folders)
    {
        string[] alphabetical = folders.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        List<string> absentSources = [];
        foreach (DeploymentProvider provider in providers)
        {
            string path = Path.Combine(provider.RootPath, OrderRelativePath);
            if (!File.Exists(path))
            {
                absentSources.Add(Path.GetFullPath(path));
                continue;
            }
            byte[] orderBytes = File.ReadAllBytes(path);
            string[] order = System.Text.Encoding.UTF8.GetString(orderBytes).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim().TrimStart('\uFEFF')).Where(value => value.Length > 0).ToArray();
            Dictionary<string, string> source = new(StringComparer.OrdinalIgnoreCase) { [Path.GetFullPath(path)] = Convert.ToHexStringLower(SHA256.HashData(orderBytes)) };
            bool complete = order.Length == alphabetical.Length
                && order.Distinct(StringComparer.OrdinalIgnoreCase).Count() == order.Length
                && !order.Except(alphabetical, StringComparer.OrdinalIgnoreCase).Any()
                && !alphabetical.Except(order, StringComparer.OrdinalIgnoreCase).Any();
            if (complete) return (order, new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.ManagedModlist, provider.Name, path, $"REDmod order comes from the active {provider.Name} MO_REDmod_load_order.txt.") { SourceFingerprints = source, AbsentSources = absentSources.ToArray() });
            return (alphabetical, new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.Unresolved, provider.Name, path, "The active REDmod order file is stale or incomplete.") { SourceFingerprints = source, AbsentSources = absentSources.ToArray(), ProblemLane = ArchiveOrderProblemLane.Redmod });
        }
        return (alphabetical, new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.FilenameFallback, null, null, "No active REDmod order file exists. Documented ASCII folder order is used.") { AbsentSources = absentSources.ToArray() });
    }

    private sealed record RedmodCandidate(string Folder, string Provider, string[] ArchivePaths);
}

public static class PackedArchiveTopology
{
    public static Mo2ArchiveProfile Compose(Mo2ArchiveProfile legacy, RedmodArchiveProfile redmods)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        ArgumentNullException.ThrowIfNull(redmods);
        ArchiveOrderEvidence legacyEvidence = legacy.OrderEvidence ?? new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.Unresolved, null, null, "Legacy archive order evidence is unavailable.");
        ArchiveOrderEvidenceKind kind = legacyEvidence.Kind == ArchiveOrderEvidenceKind.Unresolved || redmods.OrderEvidence.Kind == ArchiveOrderEvidenceKind.Unresolved
            ? ArchiveOrderEvidenceKind.Unresolved
            : legacyEvidence.Kind == ArchiveOrderEvidenceKind.ManagedModlist || redmods.OrderEvidence.Kind == ArchiveOrderEvidenceKind.ManagedModlist
                ? ArchiveOrderEvidenceKind.ManagedModlist
                : ArchiveOrderEvidenceKind.FilenameFallback;
        ArchiveOrderEvidence primary = legacyEvidence.Kind == ArchiveOrderEvidenceKind.Unresolved ? legacyEvidence : redmods.OrderEvidence.Kind == ArchiveOrderEvidenceKind.Unresolved ? redmods.OrderEvidence : legacyEvidence.Kind == ArchiveOrderEvidenceKind.ManagedModlist ? legacyEvidence : redmods.OrderEvidence.Kind == ArchiveOrderEvidenceKind.ManagedModlist ? redmods.OrderEvidence : legacyEvidence;
        string[] sources = legacyEvidence.SourcePaths.Concat(redmods.OrderEvidence.SourcePaths).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string[] providers = new[] { legacyEvidence.Provider, redmods.OrderEvidence.Provider }.Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        ArchiveOrderProblemLane problemLane = legacyEvidence.Kind == ArchiveOrderEvidenceKind.Unresolved && redmods.OrderEvidence.Kind == ArchiveOrderEvidenceKind.Unresolved ? ArchiveOrderProblemLane.Combined : legacyEvidence.Kind == ArchiveOrderEvidenceKind.Unresolved ? ArchiveOrderProblemLane.Legacy : redmods.OrderEvidence.Kind == ArchiveOrderEvidenceKind.Unresolved ? ArchiveOrderProblemLane.Redmod : ArchiveOrderProblemLane.None;
        ArchiveOrderEvidence evidence = new(kind, providers.Length == 0 ? null : string.Join(" + ", providers), primary.SourcePath, $"{legacyEvidence.Message} Legacy archives load before REDmods. {redmods.OrderEvidence.Message}")
        {
            SourcePaths = sources,
            IgnoredEntries = legacyEvidence.IgnoredEntries.Concat(redmods.OrderEvidence.IgnoredEntries).ToArray(),
            MissingEntries = legacyEvidence.MissingEntries.Concat(redmods.OrderEvidence.MissingEntries).ToArray(),
            DuplicateEntries = legacyEvidence.DuplicateEntries.Concat(redmods.OrderEvidence.DuplicateEntries).ToArray(),
            SourceFingerprints = legacyEvidence.SourceFingerprints.Concat(redmods.OrderEvidence.SourceFingerprints).ToDictionary(value => value.Key, value => value.Value, StringComparer.OrdinalIgnoreCase),
            AbsentSources = legacyEvidence.AbsentSources.Concat(redmods.OrderEvidence.AbsentSources).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            ProblemLane = problemLane
        };
        return new Mo2ArchiveProfile(legacy.ProfileName, legacy.ProfileModlistPath, [.. legacy.Archives, .. redmods.Archives], [.. legacy.EffectiveOrder, .. redmods.EffectiveOrder], evidence);
    }
}

public static class RedmodArchiveIdentity
{
    public static bool IsAmbiguousProvider(ResourceProvider provider, IReadOnlyList<ResourceProvider> providers)
        => provider.Provider.StartsWith("REDmod:", StringComparison.OrdinalIgnoreCase)
            && providers.Where(value => string.Equals(value.Provider, provider.Provider, StringComparison.OrdinalIgnoreCase)).Select(value => value.ArchiveName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;

    public static bool ProviderPayloadsDiffer(ResourceProvider provider, IReadOnlyList<ResourceProvider> providers)
        => providers.Where(value => string.Equals(value.Provider, provider.Provider, StringComparison.OrdinalIgnoreCase)).Select(value => value.PayloadFingerprint).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any();
}

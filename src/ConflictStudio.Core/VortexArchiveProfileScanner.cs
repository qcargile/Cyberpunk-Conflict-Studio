using System.Security.Cryptography;
using System.Text;

namespace ConflictStudio.Core;

public static class VortexArchiveProfileScanner
{
    public static Mo2ArchiveProfile Scan(VortexManagerContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        Dictionary<string, Mo2Archive> effective = new(StringComparer.OrdinalIgnoreCase);
        foreach (VortexProviderContext provider in context.Providers.OrderBy(value => value.Order)) AddProvider(effective, provider.Name, provider.Id, provider.RootPath, context.DeployedWinners, cancellationToken);
        AddProvider(effective, "Game directory", null, context.GameRoot, context.DeployedWinners, cancellationToken);
        Mo2Archive[] filenameOrder = effective.Values.OrderBy(value => value.ArchiveName, StringComparer.Ordinal).ToArray();
        string orderPath = Path.Combine(context.GameRoot, "archive", "pc", "mod", "modlist.txt");
        (string[] order, ArchiveOrderEvidence evidence) = ResolveOrder(context, filenameOrder, orderPath);
        Dictionary<string, Mo2Archive> byName = filenameOrder.ToDictionary(value => value.ArchiveName, StringComparer.OrdinalIgnoreCase);
        Mo2Archive[] ordered = order.Where(byName.ContainsKey).Select(value => byName[value]).Concat(filenameOrder.Where(value => !order.Contains(value.ArchiveName, StringComparer.OrdinalIgnoreCase))).ToArray();
        return new Mo2ArchiveProfile(context.ProfileName, orderPath, ordered, order, evidence);
    }

    private static void AddProvider(Dictionary<string, Mo2Archive> effective, string providerName, string? providerId, string providerRoot, Dictionary<string, string> winners, CancellationToken cancellationToken)
    {
        string archiveRoot = Path.Combine(providerRoot, "archive", "pc", "mod");
        if (!Directory.Exists(archiveRoot)) return;
        foreach (string path in Directory.EnumerateFiles(archiveRoot, "*.archive", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = Path.GetFileName(path);
            string relative = "archive\\pc\\mod\\" + name;
            if (winners.TryGetValue(relative, out string? winnerId) && !string.Equals(providerId, winnerId, StringComparison.OrdinalIgnoreCase)) continue;
            if (effective.ContainsKey(name) && !winners.ContainsKey(relative)) continue;
            effective[name] = Fingerprint(providerName, path, name);
        }
    }

    private static (string[] Order, ArchiveOrderEvidence Evidence) ResolveOrder(VortexManagerContext context, Mo2Archive[] archives, string orderPath)
    {
        string[] discovered = archives.Select(value => value.ArchiveName).ToArray();
        if (!context.DeploymentFresh) return (discovered, new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.Unresolved, "Vortex", orderPath, "Archive winners cannot be determined until Vortex deploys the active profile.") { ProblemLane = ArchiveOrderProblemLane.Legacy });
        if (!File.Exists(orderPath)) return (discovered, new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.FilenameFallback, "Vortex", null, "No deployed archive modlist.txt exists. Cyberpunk filename order is used."));
        byte[] bytes = File.ReadAllBytes(orderPath);
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Dictionary<string, string> source = new(StringComparer.OrdinalIgnoreCase) { [Path.GetFullPath(orderPath)] = sha256 };
        if (context.ArchiveOrderSha256 is not null && !string.Equals(context.ArchiveOrderSha256, sha256, StringComparison.OrdinalIgnoreCase)) return (discovered, new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.Unresolved, "Vortex", orderPath, "The archive order changed after Vortex exported this profile context. Refresh the Vortex bridge before scanning.") { SourceFingerprints = source, ProblemLane = ArchiveOrderProblemLane.Legacy });
        string[] allEntries = ArchiveOrderText.ArchiveEntries(Encoding.UTF8.GetString(bytes).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        HashSet<string> active = discovered.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] order = allEntries.Where(active.Contains).ToArray();
        string[] ignored = allEntries.Where(value => !active.Contains(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string[] missing = discovered.Where(value => !order.Contains(value, StringComparer.OrdinalIgnoreCase)).ToArray();
        string[] duplicates = order.GroupBy(value => value, StringComparer.OrdinalIgnoreCase).Where(value => value.Count() > 1).Select(value => value.Key).ToArray();
        try
        {
            ArchiveOrderPlanner.RequireComplete(archives.Select(value => new ArchiveFingerprint(value.ArchiveName, value.Size, value.Sha256)).ToArray(), order);
            return (order, new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.ManagedModlist, "Vortex", orderPath, "Archive winners use the active Vortex archive load order.") { IgnoredEntries = ignored, SourceFingerprints = source });
        }
        catch (ArchiveOrderException exception)
        {
            string[] repaired = ArchiveOrderPlanner.CreateRepairOrder(discovered, order);
            return (repaired, new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.Unresolved, "Vortex", orderPath, $"The deployed Vortex archive order is incomplete: {exception.Message}") { IgnoredEntries = ignored, MissingEntries = missing, DuplicateEntries = duplicates, SourceFingerprints = source, ProblemLane = ArchiveOrderProblemLane.Legacy });
        }
    }

    private static Mo2Archive Fingerprint(string provider, string path, string archiveName)
    {
        using FileStream stream = File.OpenRead(path);
        return new Mo2Archive(provider, archiveName, path, stream.Length, Convert.ToHexStringLower(SHA256.HashData(stream)));
    }
}

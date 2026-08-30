using System.Security.Cryptography;

namespace ConflictStudio.Core;

public static class ManualArchiveProfileScanner
{
    public static Mo2ArchiveProfile Scan(string gameRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);
        string root = Path.GetFullPath(gameRoot);
        string archiveRoot = Path.Combine(root, "archive", "pc", "mod");
        if (!Directory.Exists(archiveRoot)) throw new DirectoryNotFoundException("The Cyberpunk archive\\pc\\mod folder does not exist.");
        Mo2Archive[] archives = Directory.EnumerateFiles(archiveRoot, "*.archive", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName, StringComparer.Ordinal).Select(path =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using FileStream stream = File.OpenRead(path);
            return new Mo2Archive("Game directory", Path.GetFileName(path), path, stream.Length, Convert.ToHexStringLower(SHA256.HashData(stream)));
        }).ToArray();
        string[] discovered = archives.Select(value => value.ArchiveName).ToArray();
        string orderPath = Path.Combine(archiveRoot, "modlist.txt");
        if (!File.Exists(orderPath)) return new Mo2ArchiveProfile("Deployed game", orderPath, archives, discovered, new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.FilenameFallback, "Game directory", null, "No deployed archive modlist.txt exists. Cyberpunk filename order is used.") { AbsentSources = [orderPath] });
        byte[] bytes = File.ReadAllBytes(orderPath);
        HashSet<string> active = discovered.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] allEntries = ArchiveOrderText.ArchiveEntries(File.ReadAllLines(orderPath));
        string[] order = allEntries.Where(active.Contains).ToArray();
        string[] ignored = allEntries.Where(value => !active.Contains(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string[] missing = discovered.Where(value => !order.Contains(value, StringComparer.OrdinalIgnoreCase)).ToArray();
        string[] duplicates = order.GroupBy(value => value, StringComparer.OrdinalIgnoreCase).Where(value => value.Count() > 1).Select(value => value.Key).ToArray();
        Dictionary<string, string> source = new(StringComparer.OrdinalIgnoreCase) { [orderPath] = Convert.ToHexStringLower(SHA256.HashData(bytes)) };
        try
        {
            ArchiveOrderPlanner.RequireComplete(archives.Select(value => new ArchiveFingerprint(value.ArchiveName, value.Size, value.Sha256)).ToArray(), order);
            return new Mo2ArchiveProfile("Deployed game", orderPath, archives, order, new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.ManagedModlist, "Game directory", orderPath, "Archive winners use the deployed archive modlist.txt.") { IgnoredEntries = ignored, SourceFingerprints = source });
        }
        catch (ArchiveOrderException)
        {
            List<string> reasons = [];
            if (missing.Length > 0) reasons.Add($"missing active archives: {string.Join(", ", missing)}");
            if (duplicates.Length > 0) reasons.Add($"duplicate active archives: {string.Join(", ", duplicates)}");
            if (ignored.Length > 0) reasons.Add($"inactive entries ignored: {string.Join(", ", ignored)}");
            return new Mo2ArchiveProfile("Deployed game", orderPath, archives, discovered, new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.Unresolved, "Game directory", orderPath, $"Archive winners cannot be determined because the deployed modlist.txt has {string.Join("; ", reasons)}.") { IgnoredEntries = ignored, MissingEntries = missing, DuplicateEntries = duplicates, SourceFingerprints = source, ProblemLane = ArchiveOrderProblemLane.Legacy });
        }
    }
}

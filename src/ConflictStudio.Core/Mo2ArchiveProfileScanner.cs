using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConflictStudio.Core;

public sealed record Mo2Archive(string Provider, string ArchiveName, string PhysicalPath, long Size, string Sha256);

public enum ArchiveOrderEvidenceKind { ManagedModlist, FilenameFallback, Unresolved }

public enum ArchiveOrderProblemLane { None, Legacy, Redmod, Combined }

public sealed record ArchiveOrderEvidence(ArchiveOrderEvidenceKind Kind, string? Provider, string? SourcePath, string Message)
{
    public string[] SourcePaths { get; init; } = SourcePath is null ? [] : [SourcePath];
    public string[] IgnoredEntries { get; init; } = [];
    public string[] MissingEntries { get; init; } = [];
    public string[] DuplicateEntries { get; init; } = [];
    public Dictionary<string, string> SourceFingerprints { get; init; } = [];
    public string[] AbsentSources { get; init; } = [];
    public ArchiveOrderProblemLane ProblemLane { get; init; }
    [JsonIgnore]
    public bool IsRedmodOrder => ProblemLane == ArchiveOrderProblemLane.Redmod;
    [JsonIgnore]
    public bool IsRepairableLegacyOrder => Kind == ArchiveOrderEvidenceKind.Unresolved
        && ProblemLane == ArchiveOrderProblemLane.Legacy
        && SourcePath is not null
        && SourceFingerprints.Count > 0
        && (MissingEntries.Length > 0 || DuplicateEntries.Length > 0);
}

public sealed record Mo2ArchiveProfile(string ProfileName, string ProfileModlistPath, Mo2Archive[] Archives, string[] EffectiveOrder, ArchiveOrderEvidence? OrderEvidence = null);

public static class Mo2ArchiveProfileScanner
{
    private static readonly ConcurrentDictionary<string, CachedFingerprint> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Mo2ArchiveProfile Scan(string modsRoot, string profileModlistPath, string? fingerprintCachePath = null, bool forceFingerprint = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsRoot);
        string mo2Root = Directory.GetParent(Path.GetFullPath(modsRoot))?.FullName ?? throw new DirectoryNotFoundException("The MO2 root could not be resolved.");
        return ScanPaths(Mo2InstancePathResolver.Resolve(mo2Root), modsRoot, profileModlistPath, fingerprintCachePath, forceFingerprint, cancellationToken);
    }

    public static Mo2ArchiveProfile ScanInstance(string mo2Root, string profileModlistPath, string? fingerprintCachePath = null, bool forceFingerprint = false, CancellationToken cancellationToken = default)
    {
        Mo2InstancePaths paths = Mo2InstancePathResolver.Resolve(mo2Root);
        return ScanPaths(paths, paths.ModsRoot, profileModlistPath, fingerprintCachePath, forceFingerprint, cancellationToken);
    }

    public static Mo2ArchiveProfile ScanInstance(string mo2Root, string profileModlistPath, IReadOnlyList<Mo2ActiveProvider> activeProviders, string? fingerprintCachePath = null, bool forceFingerprint = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activeProviders);
        Mo2InstancePaths paths = Mo2InstancePathResolver.Resolve(mo2Root);
        return ScanPaths(paths, paths.ModsRoot, profileModlistPath, fingerprintCachePath, forceFingerprint, cancellationToken, activeProviders);
    }

    private static Mo2ArchiveProfile ScanPaths(Mo2InstancePaths paths, string modsRoot, string profileModlistPath, string? fingerprintCachePath, bool forceFingerprint, CancellationToken cancellationToken, IReadOnlyList<Mo2ActiveProvider>? activeProviders = null)
    {
        Dictionary<string, CachedFingerprint> persistent = Load(fingerprintCachePath);
        bool cacheChanged = false;
        List<ProviderArchives> providers = [];
        AddProvider(providers, "Overwrite", Path.Combine(paths.OverwriteRoot, "archive", "pc", "mod"), persistent, forceFingerprint, ref cacheChanged, cancellationToken);
        foreach (string provider in (activeProviders ?? Mo2ProfileReader.ReadActiveProviderEntries(profileModlistPath)).Select(value => value.Name)) AddProvider(providers, provider, Path.Combine(modsRoot, provider, "archive", "pc", "mod"), persistent, forceFingerprint, ref cacheChanged, cancellationToken);
        if (paths.GameRoot is not null) AddProvider(providers, "Game directory", Path.Combine(paths.GameRoot, "archive", "pc", "mod"), persistent, forceFingerprint, ref cacheChanged, cancellationToken);

        Dictionary<string, Mo2Archive> effective = new(StringComparer.OrdinalIgnoreCase);
        foreach (ProviderArchives provider in providers) foreach (Mo2Archive archive in provider.Archives) effective.TryAdd(archive.ArchiveName, archive);
        Mo2Archive[] filenameOrdered = effective.Values.OrderBy(value => value.ArchiveName, StringComparer.Ordinal).ToArray();
        (string[] order, ArchiveOrderEvidence evidence) = ResolveOrder(providers, filenameOrdered);
        Dictionary<string, Mo2Archive> byName = filenameOrdered.ToDictionary(value => value.ArchiveName, StringComparer.OrdinalIgnoreCase);
        Mo2Archive[] ordered = order.Select(value => byName[value]).ToArray();
        if (cacheChanged && fingerprintCachePath is not null) Save(fingerprintCachePath, persistent);
        return new Mo2ArchiveProfile(Path.GetFileName(Path.GetDirectoryName(profileModlistPath)!), profileModlistPath, ordered, order, evidence);
    }

    private static void AddProvider(List<ProviderArchives> providers, string provider, string directory, Dictionary<string, CachedFingerprint> persistent, bool forceFingerprint, ref bool cacheChanged, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory)) return;
        List<Mo2Archive> archives = [];
        foreach (string path in Directory.EnumerateFiles(directory, "*.archive", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            archives.Add(Fingerprint(provider, path, persistent, forceFingerprint, ref cacheChanged));
        }
        providers.Add(new ProviderArchives(provider, directory, archives.ToArray()));
    }

    private static (string[] Order, ArchiveOrderEvidence Evidence) ResolveOrder(IReadOnlyList<ProviderArchives> providers, Mo2Archive[] archives)
    {
        ArchiveFingerprint[] fingerprints = archives.Select(value => new ArchiveFingerprint(value.ArchiveName, value.Size, value.Sha256)).ToArray();
        string[] discovered = archives.Select(value => value.ArchiveName).ToArray();
        HashSet<string> discoveredSet = discovered.ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<string> absentSources = [];
        foreach (ProviderArchives provider in providers)
        {
            string path = Path.Combine(provider.Directory, "modlist.txt");
            if (!File.Exists(path))
            {
                absentSources.Add(Path.GetFullPath(path));
                continue;
            }
            byte[] orderBytes = File.ReadAllBytes(path);
            string[] order = Encoding.UTF8.GetString(orderBytes).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim().TrimStart('\uFEFF')).Where(value => value.Length > 0).ToArray();
            Dictionary<string, string> source = new(StringComparer.OrdinalIgnoreCase) { [Path.GetFullPath(path)] = Convert.ToHexStringLower(SHA256.HashData(orderBytes)) };
            string[] ignored = order.Where(value => value.EndsWith(".archive", StringComparison.OrdinalIgnoreCase) && !discoveredSet.Contains(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            string[] activeOrder = order.Where(discoveredSet.Contains).ToArray();
            string[] missing = discovered.Where(value => !activeOrder.Contains(value, StringComparer.OrdinalIgnoreCase)).ToArray();
            string[] duplicates = activeOrder.GroupBy(value => value, StringComparer.OrdinalIgnoreCase).Where(value => value.Count() > 1).Select(value => value.Key).ToArray();
            try
            {
                ArchiveOrderPlanner.RequireComplete(fingerprints, activeOrder);
                string maintenance = ignored.Length == 0 ? string.Empty : $" {ignored.Length} inactive entr{(ignored.Length == 1 ? "y was" : "ies were")} ignored without affecting current winners: {string.Join(", ", ignored)}.";
                return (activeOrder, new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.ManagedModlist, provider.Provider, path, $"Archive winners use the active {provider.Provider} modlist.txt.{maintenance}") { IgnoredEntries = ignored, SourceFingerprints = source, AbsentSources = absentSources.ToArray() });
            }
            catch (ArchiveOrderException exception)
            {
                List<string> reasons = [];
                if (missing.Length > 0) reasons.Add($"missing active archives: {string.Join(", ", missing)}");
                if (duplicates.Length > 0) reasons.Add($"duplicate active archives: {string.Join(", ", duplicates)}");
                if (ignored.Length > 0) reasons.Add($"inactive entries ignored: {string.Join(", ", ignored)}");
                if (reasons.Count == 0) reasons.Add(exception.Message);
                string[] repaired = ArchiveOrderPlanner.CreateRepairOrder(discovered, activeOrder);
                return (repaired, new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.Unresolved, provider.Provider, path, $"Archive winners cannot be determined because the active modlist.txt has {string.Join("; ", reasons)}.") { IgnoredEntries = ignored, MissingEntries = missing, DuplicateEntries = duplicates, SourceFingerprints = source, AbsentSources = absentSources.ToArray(), ProblemLane = ArchiveOrderProblemLane.Legacy });
            }
        }
        return (discovered, new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.FilenameFallback, null, null, "No active archive modlist.txt exists. Cyberpunk filename order is used.") { AbsentSources = absentSources.ToArray() });
    }

    private static Mo2Archive Fingerprint(string provider, string path, Dictionary<string, CachedFingerprint> persistent, bool forceFingerprint, ref bool cacheChanged)
    {
        FileInfo file = new(path);
        if (!forceFingerprint && Cache.TryGetValue(path, out CachedFingerprint? cached) && cached.Size == file.Length && cached.LastWriteUtc == file.LastWriteTimeUtc) return new Mo2Archive(provider, Path.GetFileName(path), path, cached.Size, cached.Sha256);
        if (!forceFingerprint && persistent.TryGetValue(path, out cached) && cached.Size == file.Length && cached.LastWriteUtc == file.LastWriteTimeUtc && IsSha256(cached.Sha256))
        {
            Cache[path] = cached;
            return new Mo2Archive(provider, Path.GetFileName(path), path, cached.Size, cached.Sha256);
        }
        using FileStream stream = File.OpenRead(path);
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(stream));
        cached = new CachedFingerprint(stream.Length, file.LastWriteTimeUtc, sha256);
        Cache[path] = cached;
        persistent[path] = cached;
        cacheChanged = true;
        return new Mo2Archive(provider, Path.GetFileName(path), path, stream.Length, sha256);
    }

    private static Dictionary<string, CachedFingerprint> Load(string? path)
    {
        if (path is null || !File.Exists(path)) return new Dictionary<string, CachedFingerprint>(StringComparer.OrdinalIgnoreCase);
        try
        {
            CacheDocument? document = JsonSerializer.Deserialize<CacheDocument>(File.ReadAllText(path));
            return document?.SchemaVersion == 1 && document.Entries is not null ? new Dictionary<string, CachedFingerprint>(document.Entries, StringComparer.OrdinalIgnoreCase) : new Dictionary<string, CachedFingerprint>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Dictionary<string, CachedFingerprint>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void Save(string path, Dictionary<string, CachedFingerprint> entries)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new CacheDocument(1, entries)));
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static bool IsSha256(string value) => value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private sealed record ProviderArchives(string Provider, string Directory, Mo2Archive[] Archives);
    private sealed record CachedFingerprint(long Size, DateTime LastWriteUtc, string Sha256);
    private sealed record CacheDocument(int SchemaVersion, Dictionary<string, CachedFingerprint> Entries);
}

using System.Security.Cryptography;

namespace ConflictStudio.Core;

public enum VirtualFileRelation { Identical, Different }

public sealed record VirtualFileProvider(string Provider, string PhysicalPath, long Size, string Sha256, int ProfilePosition, int? Mo2Priority = null);

public sealed record VirtualFileShadow(string RelativePath, string WinnerProvider, VirtualFileRelation Relation, VirtualFileProvider[] Providers);

public static class VirtualFileShadowScanner
{
    public static VirtualFileShadow[] Scan(string modsRoot, IReadOnlyList<string> activeProviders, CancellationToken cancellationToken = default)
        => ScanProviders(activeProviders.Select(provider => new DeploymentProvider(provider, Path.Combine(modsRoot, provider))).ToArray(), cancellationToken);

    public static VirtualFileShadow[] ScanProviders(IReadOnlyList<DeploymentProvider> providers, CancellationToken cancellationToken = default)
        => ScanProviders(providers, null, cancellationToken);

    public static VirtualFileShadow[] ScanProviders(IReadOnlyList<DeploymentProvider> providers, IReadOnlyDictionary<string, string>? deployedWinners, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(providers);
        Dictionary<string, List<Candidate>> files = new(StringComparer.OrdinalIgnoreCase);
        for (int position = 0; position < providers.Count; position++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeploymentProvider provider = providers[position];
            string providerRoot = provider.RootPath;
            if (!Directory.Exists(providerRoot)) continue;
            foreach (string path in EnumerateDeploymentFiles(providerRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relative = Path.GetRelativePath(providerRoot, path).Replace('/', '\\');
                if (relative.Equals("meta.ini", StringComparison.OrdinalIgnoreCase) || relative.StartsWith(".git\\", StringComparison.OrdinalIgnoreCase) || DeploymentFilePolicy.IsMutableOutput(relative)) continue;
                Candidate file = new(provider.Name, path, position, provider.Mo2Priority, provider.ManagerId);
                if (!files.TryGetValue(relative, out List<Candidate>? fileProviders)) files[relative] = fileProviders = [];
                fileProviders.Add(file);
            }
        }

        return files.Where(value => value.Value.Count > 1)
            .Select(value => Create(value.Key, value.Value, deployedWinners))
            .OrderBy(value => value.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateDeploymentFiles(string providerRoot)
    {
        string[] lanes = ["archive\\pc\\mod", "bin\\x64\\plugins", "engine\\config", "r6\\input", "r6\\scripts", "r6\\tweaks", "red4ext\\plugins"];
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (string lane in lanes)
        {
            string root = Path.Combine(providerRoot, lane);
            if (!Directory.Exists(root)) continue;
            foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (!string.Equals(Path.GetExtension(path), ".archive", StringComparison.OrdinalIgnoreCase) && paths.Add(path)) yield return path;
            }
        }
    }

    private static VirtualFileShadow Create(string relative, List<Candidate> candidates, IReadOnlyDictionary<string, string>? deployedWinners)
    {
        string? winnerId = deployedWinners?.GetValueOrDefault(relative);
        VirtualFileProvider[] ordered = candidates.OrderBy(value => winnerId is not null && string.Equals(value.ManagerId, winnerId, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(value => value.ProfilePosition).Select(Fingerprint).ToArray();
        VirtualFileRelation relation = ordered.Select(value => value.Sha256).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 ? VirtualFileRelation.Identical : VirtualFileRelation.Different;
        return new VirtualFileShadow(relative, ordered[0].Provider, relation, ordered);
    }

    private static VirtualFileProvider Fingerprint(Candidate candidate)
    {
        using FileStream stream = File.OpenRead(candidate.PhysicalPath);
        return new VirtualFileProvider(candidate.Provider, candidate.PhysicalPath, stream.Length, Convert.ToHexStringLower(SHA256.HashData(stream)), candidate.ProfilePosition, candidate.Mo2Priority);
    }

    private sealed record Candidate(string Provider, string PhysicalPath, int ProfilePosition, int? Mo2Priority, string? ManagerId);
}

internal static class DeploymentFilePolicy
{
    private static readonly HashSet<string> MutableExtensions = new([".log", ".tmp", ".dmp", ".bak", ".old", ".db", ".sqlite", ".sqlite3"], StringComparer.OrdinalIgnoreCase);

    public static bool IsMutableOutput(string relativePath)
    {
        string normalized = relativePath.Replace('/', '\\');
        return MutableExtensions.Contains(Path.GetExtension(normalized)) || normalized.Contains("\\logs\\", StringComparison.OrdinalIgnoreCase);
    }
}

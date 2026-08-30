namespace ConflictStudio.Core;

public sealed record ArchiveXlProviderSource(string Provider, string RootPath, string? ManagerId = null);

public sealed record ArchiveXlSourceFailure(string Provider, string FilePath, string Message);

public sealed record ArchiveXlSourceScanResult(ArchiveXlSource[] Sources, ArchiveXlSourceFailure[] Failures);

public static class ArchiveXlSourceScanner
{
    public static ArchiveXlSourceScanResult Scan(IReadOnlyList<ArchiveXlProviderSource> providers, CancellationToken cancellationToken = default)
        => Scan(providers, null, cancellationToken);

    public static ArchiveXlSourceScanResult Scan(IReadOnlyList<ArchiveXlProviderSource> providers, IReadOnlyDictionary<string, string>? deployedWinners, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(providers);
        List<ArchiveXlSource> sources = [];
        List<ArchiveXlSourceFailure> failures = [];
        Dictionary<string, (ArchiveXlProviderSource Provider, string Path)> effective = new(StringComparer.OrdinalIgnoreCase);
        foreach (ArchiveXlProviderSource provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(provider.RootPath))
            {
                failures.Add(new ArchiveXlSourceFailure(provider.Provider, provider.RootPath, "The ArchiveXL provider root does not exist."));
                continue;
            }
            string[] files;
            try
            {
                string archiveLane = Path.Combine(provider.RootPath, "archive", "pc", "mod");
                string scanRoot = Directory.Exists(archiveLane) ? archiveLane : provider.RootPath;
                files = Directory.EnumerateFiles(scanRoot, "*.xl", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failures.Add(new ArchiveXlSourceFailure(provider.Provider, provider.RootPath, exception.Message));
                continue;
            }
            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relative = Path.GetRelativePath(provider.RootPath, file).Replace('/', '\\');
                if (deployedWinners?.TryGetValue(relative, out string? winnerId) == true)
                {
                    if (string.Equals(provider.ManagerId, winnerId, StringComparison.OrdinalIgnoreCase)) effective[relative] = (provider, file);
                }
                else effective.TryAdd(relative, (provider, file));
            }
        }
        foreach ((string relativePath, (ArchiveXlProviderSource provider, string file)) in effective)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { sources.Add(new ArchiveXlSource(provider.Provider, relativePath, File.ReadAllText(file))); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { failures.Add(new ArchiveXlSourceFailure(provider.Provider, relativePath, exception.Message)); }
        }
        return new ArchiveXlSourceScanResult(sources.ToArray(), failures.ToArray());
    }
}

namespace ConflictStudio.Core;

public sealed record SourceAnalysisFailure(string Provider, string FilePath, string Surface, string Message);

public sealed record ModSourceInventory(RedScriptSource[] RedScripts, LuaSource[] LuaSources, TweakSource[] TweakSources, SourceAnalysisFailure[] Failures);

public static class ModSourceScanner
{
    public static ModSourceInventory Scan(string modsRoot, IReadOnlyList<string> activeProviders, CancellationToken cancellationToken = default)
        => ScanProviders(activeProviders.Select(provider => new DeploymentProvider(provider, Path.Combine(modsRoot, provider))).ToArray(), cancellationToken);

    public static ModSourceInventory ScanProviders(IReadOnlyList<DeploymentProvider> providers, CancellationToken cancellationToken = default)
        => ScanProviders(providers, null, cancellationToken);

    public static ModSourceInventory ScanProviders(IReadOnlyList<DeploymentProvider> providers, IReadOnlyDictionary<string, string>? deployedWinners, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(providers);
        Dictionary<string, Candidate> redScripts = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, Candidate> luaSources = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, Candidate> tweakSources = new(StringComparer.OrdinalIgnoreCase);
        List<SourceAnalysisFailure> failures = [];
        foreach (DeploymentProvider provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string providerRoot = provider.RootPath;
            if (!Directory.Exists(providerRoot)) continue;
            AddLane(redScripts, provider, providerRoot, Path.Combine(providerRoot, "r6", "scripts"), "*.reds", "RedScript", failures, deployedWinners, cancellationToken);
            AddLane(luaSources, provider, providerRoot, Path.Combine(providerRoot, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods"), "*.lua", "CET Lua", failures, deployedWinners, cancellationToken);
            string tweaksRoot = Path.Combine(providerRoot, "r6", "tweaks");
            AddLane(tweakSources, provider, providerRoot, tweaksRoot, "*.yaml", "TweakXL", failures, deployedWinners, cancellationToken);
            AddLane(tweakSources, provider, providerRoot, tweaksRoot, "*.yml", "TweakXL", failures, deployedWinners, cancellationToken);
        }

        RedScriptSource[] redScriptInventory = Read(redScripts.Values, "RedScript", value => new RedScriptSource(value.Provider, value.RelativePath, File.ReadAllText(value.PhysicalPath)), failures, cancellationToken);
        LuaSource[] luaInventory = Read(luaSources.Values, "CET Lua", value => new LuaSource(value.Provider, value.RelativePath, File.ReadAllText(value.PhysicalPath)), failures, cancellationToken);
        TweakSource[] tweakInventory = Read(tweakSources.Values, "TweakXL", value => new TweakSource(value.Provider, value.RelativePath, File.ReadAllText(value.PhysicalPath)), failures, cancellationToken);
        failures.AddRange(RedScriptConditionalSourceFilter.Failures(redScriptInventory));
        return new ModSourceInventory(redScriptInventory, luaInventory, tweakInventory, failures.ToArray());
    }

    private static void AddLane(Dictionary<string, Candidate> candidates, DeploymentProvider provider, string providerRoot, string laneRoot, string pattern, string surface, List<SourceAnalysisFailure> failures, IReadOnlyDictionary<string, string>? deployedWinners, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(laneRoot)) return;
        try
        {
            foreach (string path in Directory.EnumerateFiles(laneRoot, pattern, SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Set(candidates, provider, providerRoot, path, deployedWinners);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            failures.Add(new SourceAnalysisFailure(provider.Name, Relative(providerRoot, laneRoot), surface, exception.Message));
        }
    }

    private static T[] Read<T>(IEnumerable<Candidate> candidates, string surface, Func<Candidate, T> read, List<SourceAnalysisFailure> failures, CancellationToken cancellationToken)
    {
        List<T> sources = [];
        foreach (Candidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { sources.Add(read(candidate)); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { failures.Add(new SourceAnalysisFailure(candidate.Provider, candidate.RelativePath, surface, exception.Message)); }
        }
        return sources.ToArray();
    }

    private static void Set(Dictionary<string, Candidate> candidates, DeploymentProvider provider, string providerRoot, string physicalPath, IReadOnlyDictionary<string, string>? deployedWinners)
    {
        string relative = Relative(providerRoot, physicalPath);
        if (deployedWinners?.TryGetValue(relative, out string? winnerId) == true)
        {
            if (!string.Equals(provider.ManagerId, winnerId, StringComparison.OrdinalIgnoreCase)) return;
            candidates[relative] = new Candidate(provider.Name, relative, physicalPath);
            return;
        }
        candidates.TryAdd(relative, new Candidate(provider.Name, relative, physicalPath));
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('/', '\\');

    private sealed record Candidate(string Provider, string RelativePath, string PhysicalPath);
}

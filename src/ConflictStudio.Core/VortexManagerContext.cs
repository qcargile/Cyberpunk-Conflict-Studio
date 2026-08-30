using System.Text.Json;

namespace ConflictStudio.Core;

public enum ModManagerKind { Mo2, Vortex, Manual }

public sealed record VortexProviderContext(string Id, string Name, string RootPath, int Order);

public sealed record VortexManagerContext(
    int SchemaVersion,
    string ContextId,
    DateTimeOffset CapturedAtUtc,
    string ProfileId,
    string ProfileName,
    string GameRoot,
    string StagingRoot,
    bool DeploymentFresh,
    VortexProviderContext[] Providers,
    Dictionary<string, string> DeployedWinners,
    string[] ArchiveOrder,
    string? ArchiveOrderSha256);

public static class VortexManagerContextStore
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static VortexManagerContext Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Read(File.ReadAllBytes(path));
    }

    public static VortexManagerContext Read(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        VortexManagerContext context;
        try { context = JsonSerializer.Deserialize<VortexManagerContext>(content, Options) ?? throw new InvalidDataException("The Vortex context is empty."); }
        catch (JsonException exception) { throw new InvalidDataException("The Vortex context is invalid.", exception); }
        Validate(context);
        string gameRoot = Path.GetFullPath(context.GameRoot);
        string stagingRoot = Path.GetFullPath(context.StagingRoot);
        VortexProviderContext[] providers = context.Providers.OrderBy(value => value.Order).Select(value => value with { RootPath = Path.GetFullPath(value.RootPath) }).ToArray();
        Dictionary<string, string> winners = new(context.DeployedWinners.Select(value => new KeyValuePair<string, string>(Normalize(value.Key), value.Value)), StringComparer.OrdinalIgnoreCase);
        return context with { GameRoot = gameRoot, StagingRoot = stagingRoot, Providers = providers, DeployedWinners = winners };
    }

    private static void Validate(VortexManagerContext context)
    {
        if (context.SchemaVersion != 1 || !IsSha256(context.ContextId) || context.CapturedAtUtc == default || context.CapturedAtUtc.Offset != TimeSpan.Zero || string.IsNullOrWhiteSpace(context.ProfileId) || string.IsNullOrWhiteSpace(context.ProfileName)) throw new InvalidDataException("The Vortex context header is invalid.");
        if (!Path.IsPathRooted(context.GameRoot) || !Path.IsPathRooted(context.StagingRoot) || !Directory.Exists(context.GameRoot) || !Directory.Exists(context.StagingRoot)) throw new InvalidDataException("The Vortex context paths are invalid.");
        if (context.Providers is null || context.DeployedWinners is null || context.ArchiveOrder is null) throw new InvalidDataException("The Vortex context is incomplete.");
        HashSet<string> providerIds = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> providerNames = new(StringComparer.OrdinalIgnoreCase);
        HashSet<int> orders = [];
        string stagingRoot = Path.GetFullPath(context.StagingRoot);
        foreach (VortexProviderContext provider in context.Providers)
        {
            if (string.IsNullOrWhiteSpace(provider.Id) || string.IsNullOrWhiteSpace(provider.Name) || !Path.IsPathRooted(provider.RootPath) || !Directory.Exists(provider.RootPath) || !providerIds.Add(provider.Id) || !providerNames.Add(provider.Name) || !orders.Add(provider.Order) || !IsWithin(stagingRoot, provider.RootPath)) throw new InvalidDataException("The Vortex provider context is invalid.");
        }
        foreach ((string relativePath, string providerId) in context.DeployedWinners)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) || !providerIds.Contains(providerId)) throw new InvalidDataException("The Vortex deployment winner context is invalid.");
        }
        if (context.ArchiveOrder.Any(string.IsNullOrWhiteSpace) || context.ArchiveOrderSha256 is not null && !IsSha256(context.ArchiveOrderSha256)) throw new InvalidDataException("The Vortex archive order context is invalid.");
    }

    private static bool IsWithin(string root, string path)
    {
        string relative = Path.GetRelativePath(root, Path.GetFullPath(path));
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string Normalize(string value) => value.Replace('/', '\\').TrimStart('\\');

    private static bool IsSha256(string value) => value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public static class VortexDeploymentGuard
{
    public static void RequireNoDeployment(string gameRoot, string contextPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(contextPath);
        if (!File.Exists(contextPath)) return;
        VortexManagerContext context;
        try { context = VortexManagerContextStore.Read(contextPath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException) { return; }
        string root = Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(root, context.GameRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) return;
        string prefix = root + Path.DirectorySeparatorChar;
        int deployedFiles = context.DeployedWinners.Keys.Count(relativePath =>
        {
            string path = Path.GetFullPath(Path.Combine(root, relativePath));
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && File.Exists(path);
        });
        if (deployedFiles == 0) return;
        string files = deployedFiles == 1 ? "1 deployed file" : $"{deployedFiles:N0} deployed files";
        throw new CrossManagerDeploymentException($"Vortex profile '{context.ProfileName}' still has {files} in this Cyberpunk installation. Purge the Vortex deployment before scanning an MO2 profile, or switch Conflict Studio to Vortex mode.");
    }
}

public sealed class CrossManagerDeploymentException(string message) : Exception(message);

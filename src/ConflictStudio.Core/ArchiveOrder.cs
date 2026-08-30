using System.Security.Cryptography;
using System.Diagnostics;

namespace ConflictStudio.Core;

public sealed record ArchiveFingerprint(string Name, long Size, string Sha256);

public sealed record ArchiveOrderObservation(string ProfileId, string DirectoryPath, string? OrderFileSha256, ArchiveFingerprint[] Archives, string[] EffectiveOrder);

public sealed record ArchiveOrderPreview(ArchiveOrderObservation Observation, string[] ProposedOrder, string[] ChangedArchives);

public interface IArchiveOrderWriter
{
    ArchiveOrderApplyResult Apply(ArchiveOrderPreview preview, IReadOnlyList<ArchiveFingerprint> currentArchives);
    void RestorePrevious(ArchiveOrderApplyResult result, string targetPath);
}

public static class ArchiveOrderScanner
{
    public static ArchiveOrderObservation Scan(string profileId, string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        string directory = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(directory)) throw new ArchiveOrderException("The archive directory does not exist.");

        ArchiveFingerprint[] archives = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(Path.GetExtension(path), ".archive", StringComparison.OrdinalIgnoreCase))
            .Select(Fingerprint)
            .OrderBy(value => value.Name, StringComparer.Ordinal)
            .ToArray();
        string orderPath = Path.Combine(directory, "modlist.txt");
        if (!File.Exists(orderPath)) return new ArchiveOrderObservation(profileId, directory, null, archives, archives.Select(value => value.Name).ToArray());

        byte[] bytes = File.ReadAllBytes(orderPath);
        string[] order = ArchiveOrderText.ArchiveEntries(File.ReadAllLines(orderPath));
        ArchiveOrderPlanner.RequireComplete(archives, order);
        return new ArchiveOrderObservation(profileId, directory, Hash(bytes), archives, order);
    }

    private static ArchiveFingerprint Fingerprint(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return new ArchiveFingerprint(Path.GetFileName(path), stream.Length, Convert.ToHexStringLower(SHA256.HashData(stream)));
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}

public static class ArchiveOrderPlanner
{
    public static ArchiveOrderPreview CreatePreview(ArchiveOrderObservation observation, IReadOnlyList<string> proposedOrder)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(proposedOrder);
        string[] proposed = proposedOrder.ToArray();
        RequireComplete(observation.Archives, proposed);
        string[] changed = observation.EffectiveOrder.Where((name, index) => !string.Equals(name, proposed[index], StringComparison.OrdinalIgnoreCase)).ToArray();
        return new ArchiveOrderPreview(observation, proposed, changed);
    }

    internal static void RequireComplete(IReadOnlyList<ArchiveFingerprint> archives, IReadOnlyList<string> order)
    {
        if (archives.Count != order.Count) throw new ArchiveOrderException("The archive order must include every discovered archive exactly once.");
        HashSet<string> expected = archives.Select(value => value.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> actual = new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in order)
        {
            if (string.IsNullOrWhiteSpace(name) || !expected.Contains(name) || !actual.Add(name)) throw new ArchiveOrderException("The archive order must include every discovered archive exactly once.");
        }
    }
}

public static class ArchiveOrderText
{
    public static string[] ArchiveEntries(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.Select(value => value.Trim().TrimStart('\uFEFF')).Where(value => value.EndsWith(".archive", StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    public static byte[] Merge(byte[]? existing, IReadOnlyList<string> proposedOrder)
    {
        ArgumentNullException.ThrowIfNull(proposedOrder);
        if (existing is null) return System.Text.Encoding.UTF8.GetBytes(string.Join("\r\n", proposedOrder) + "\r\n");
        string[] lines = System.Text.Encoding.UTF8.GetString(existing).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim().TrimStart('\uFEFF')).Where(value => value.Length > 0).ToArray();
        Queue<string> archives = new(proposedOrder);
        List<string> merged = [];
        foreach (string line in lines)
        {
            if (line.EndsWith(".archive", StringComparison.OrdinalIgnoreCase))
            {
                if (archives.Count > 0) merged.Add(archives.Dequeue());
            }
            else
            {
                merged.Add(line);
            }
        }
        merged.AddRange(archives);
        return System.Text.Encoding.UTF8.GetBytes(string.Join("\r\n", merged) + "\r\n");
    }
}

public static class ManagedArchiveOrderObserver
{
    public static ArchiveOrderObservation Observe(Mo2ArchiveProfile profile, Mo2ArchiveWriteTarget target)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(target);
        ArchiveFingerprint[] archives = profile.Archives.Select(value => new ArchiveFingerprint(value.ArchiveName, value.Size, value.Sha256)).ToArray();
        string[] order = profile.EffectiveOrder;
        string? hash = null;
        if (File.Exists(target.ModlistPath))
        {
            byte[] bytes = File.ReadAllBytes(target.ModlistPath);
            hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            HashSet<string> active = archives.Select(value => value.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            order = ArchiveOrderText.ArchiveEntries(File.ReadAllLines(target.ModlistPath)).Where(active.Contains).ToArray();
            ArchiveOrderPlanner.RequireComplete(archives, order);
        }
        return new ArchiveOrderObservation(profile.ProfileName, Path.GetDirectoryName(target.ModlistPath)!, hash, archives, order);
    }
}

public sealed class ArchiveOrderWriter : IArchiveOrderWriter
{
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<IReadOnlyList<string>> _runningProcesses;
    private readonly Func<string, byte[]> _readAllBytes;

    public ArchiveOrderWriter(Func<DateTimeOffset> clock) : this(clock, RunningProcesses) { }

    public ArchiveOrderWriter(Func<DateTimeOffset> clock, Func<IReadOnlyList<string>> runningProcesses) : this(clock, runningProcesses, File.ReadAllBytes) { }

    public ArchiveOrderWriter(Func<DateTimeOffset> clock, Func<IReadOnlyList<string>> runningProcesses, Func<string, byte[]> readAllBytes)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _runningProcesses = runningProcesses ?? throw new ArgumentNullException(nameof(runningProcesses));
        _readAllBytes = readAllBytes ?? throw new ArgumentNullException(nameof(readAllBytes));
    }

    public ArchiveOrderApplyResult Apply(ArchiveOrderPreview preview) => Apply(preview, preview.Observation.Archives);

    public ArchiveOrderApplyResult Apply(ArchiveOrderPreview preview, IReadOnlyList<ArchiveFingerprint> currentArchives)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(currentArchives);
        if (!SameInventory(preview.Observation.Archives, currentArchives)) throw new ArchiveOrderException("The archive inventory changed after the preview. Scan again before applying.");
        EnsureGameNotRunning();
        string orderPath = Path.Combine(preview.Observation.DirectoryPath, "modlist.txt");
        Directory.CreateDirectory(preview.Observation.DirectoryPath);
        string operationLockPath = orderPath + ".conflictstudio.lock";
        using FileStream operationLock = AcquireOperationLock(operationLockPath);
        byte[]? existing = File.Exists(orderPath) ? _readAllBytes(orderPath) : null;
        string? currentHash = existing is null ? null : Hash(existing);
        if (!string.Equals(currentHash, preview.Observation.OrderFileSha256, StringComparison.Ordinal)) throw new ArchiveOrderException("The archive order file changed after the scan.");

        string? backupPath = existing is null ? null : Backup(orderPath, existing);
        byte[] expected = ArchiveOrderText.Merge(existing, preview.ProposedOrder);
        string temporary = orderPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        bool replaced = false;
        try
        {
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(expected);
                stream.Flush(true);
            }
            File.Move(temporary, orderPath, true);
            replaced = true;
            bool verified = _readAllBytes(orderPath).AsSpan().SequenceEqual(expected);
            if (!verified) throw new ArchiveOrderException("The archive order file did not verify after writing.");
            return new ArchiveOrderApplyResult(backupPath, true, existing is not null, Hash(expected));
        }
        catch (Exception exception) when (replaced)
        {
            try
            {
                if (existing is null) File.Delete(orderPath);
                else File.WriteAllBytes(orderPath, existing);
            }
            catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException)
            {
                throw new ArchiveOrderException($"Archive order verification failed: {exception.Message} Automatic rollback also failed: {rollbackException.Message}");
            }
            throw new ArchiveOrderException($"Archive order verification failed and the previous file was restored: {exception.Message}");
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public void RestorePrevious(ArchiveOrderApplyResult result, string targetPath)
    {
        EnsureGameNotRunning();
        string fullTargetPath = Path.GetFullPath(targetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullTargetPath)!);
        using FileStream operationLock = AcquireOperationLock(fullTargetPath + ".conflictstudio.lock");
        ArchiveOrderRollback.RestorePrevious(result, targetPath);
    }

    private void EnsureGameNotRunning()
    {
        string? running = _runningProcesses().FirstOrDefault(name => string.Equals(name, "Cyberpunk2077", StringComparison.OrdinalIgnoreCase));
        if (running is not null) throw new ArchiveOrderException($"Archive order cannot be written while {running} is running.");
    }

    private static FileStream AcquireOperationLock(string path)
    {
        try { return new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
        catch (IOException) { throw new ArchiveOrderException("Another archive order operation is already in progress."); }
    }

    private string Backup(string orderPath, byte[] bytes)
    {
        string path = orderPath + "." + _clock().ToUniversalTime().ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture) + ".bak";
        if (File.Exists(path)) throw new ArchiveOrderException("The archive order backup path already exists.");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static bool SameInventory(ArchiveFingerprint[] observed, IReadOnlyList<ArchiveFingerprint> current)
    {
        if (observed.Length != current.Count) return false;
        Dictionary<string, ArchiveFingerprint> values = current.ToDictionary(value => value.Name, StringComparer.OrdinalIgnoreCase);
        return observed.All(value => values.TryGetValue(value.Name, out ArchiveFingerprint? now) && now.Size == value.Size && string.Equals(now.Sha256, value.Sha256, StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> RunningProcesses() => Process.GetProcesses().Select(process => process.ProcessName).ToArray();
}

public sealed record ArchiveOrderApplyResult(string? BackupPath, bool Verified, bool TargetPreviouslyExisted, string WrittenSha256);

public sealed class ArchiveOrderException(string message) : Exception(message);

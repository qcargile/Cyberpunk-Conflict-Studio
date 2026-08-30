using System.Security.Cryptography;
using System.Runtime.ExceptionServices;

namespace ConflictStudio.Core;

public sealed record ProfileInputSnapshot(string Path, string Sha256, byte[] Content);
public sealed record ProfileFileSnapshot(string Path, long Length, DateTime LastWriteTimeUtc, string? Sha256);

public static class ProfileInputGuard
{
    public static ProfileInputSnapshot Capture(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] content = File.ReadAllBytes(path);
        return new ProfileInputSnapshot(Path.GetFullPath(path), Convert.ToHexStringLower(SHA256.HashData(content)), content);
    }

    public static void RequireUnchanged(ProfileInputSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ProfileInputSnapshot current = Capture(snapshot.Path);
        if (!string.Equals(current.Sha256, snapshot.Sha256, StringComparison.Ordinal)) throw new ProfileInputChangedException("The active profile changed during the scan. Run the scan again to produce one consistent result.");
    }

    public static void RequireStillAbsent(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (File.Exists(path)) throw new ProfileInputChangedException("An archive order source appeared during the scan. Run the scan again to produce one consistent result.");
    }

    public static ProfileFileSnapshot CaptureFile(string path, bool hashContent)
    {
        FileInfo info = new(Path.GetFullPath(path));
        if (!info.Exists) throw new ProfileInputChangedException("An active evidence file disappeared during the scan.");
        string? hash = hashContent ? HashFile(info.FullName) : null;
        return new ProfileFileSnapshot(info.FullName, info.Length, info.LastWriteTimeUtc, hash);
    }

    public static ProfileFileSnapshot CaptureFile(string path, string expectedSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);
        FileInfo info = new(Path.GetFullPath(path));
        if (!info.Exists) throw new ProfileInputChangedException("An active evidence file disappeared during the scan.");
        return new ProfileFileSnapshot(info.FullName, info.Length, info.LastWriteTimeUtc, expectedSha256);
    }

    public static void RequireUnchanged(ProfileFileSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        FileInfo current = new(snapshot.Path);
        if (!current.Exists || current.Length != snapshot.Length || current.LastWriteTimeUtc != snapshot.LastWriteTimeUtc) throw new ProfileInputChangedException($"An active file changed during the scan: {snapshot.Path}");
        if (snapshot.Sha256 is not null)
        {
            string hash = HashFile(snapshot.Path);
            if (!string.Equals(hash, snapshot.Sha256, StringComparison.Ordinal)) throw new ProfileInputChangedException($"An active source file changed during the scan: {snapshot.Path}");
        }
    }

    public static void RequireAllUnchanged(IReadOnlyList<ProfileFileSnapshot> snapshots, CancellationToken cancellationToken, Action<int>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        Exception? failure = null;
        int completed = 0;
        Parallel.ForEach(snapshots, new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = 4 }, snapshot =>
        {
            if (Volatile.Read(ref failure) is not null) return;
            try { RequireUnchanged(snapshot); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ProfileInputChangedException) { Interlocked.CompareExchange(ref failure, exception, null); }
            progress?.Invoke(Interlocked.Increment(ref completed));
        });
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static string HashFile(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}

public sealed class ProfileInputChangedException(string message) : Exception(message);

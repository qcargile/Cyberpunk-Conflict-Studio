using ConflictStudio.Core;
using System.IO;
using System.Text.Json;

namespace ConflictStudio.App;

public sealed record ProfileScanReceiptHistory(ProfileScanReceipt? Receipt, string? PreservedInvalidPath, bool InvalidHistory)
{
    public bool CanReplaceLatest => !InvalidHistory || PreservedInvalidPath is not null;

    public static ProfileScanReceiptHistory ReadOrPreserveInvalid(string latestPath, string preservedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(latestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(preservedPath);
        try
        {
            return new ProfileScanReceiptHistory(ProfileScanReceiptStore.Read(latestPath), null, false);
        }
        catch (Exception exception) when (exception is ProfileScanReceiptException or IOException or UnauthorizedAccessException)
        {
            return PreserveIncompatible(latestPath, preservedPath);
        }
    }

    public static ProfileScanReceiptHistory PreserveIncompatible(string latestPath, string preservedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(latestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(preservedPath);
        try
        {
            return new ProfileScanReceiptHistory(null, PreserveWithoutOverwrite(latestPath, Path.GetFullPath(preservedPath)), true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ProfileScanReceiptHistory(null, null, true);
        }
    }

    public static Exception? TryPersist(Action persist)
    {
        ArgumentNullException.ThrowIfNull(persist);
        try
        {
            persist();
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return exception;
        }
    }

    private static string? PreserveWithoutOverwrite(string latestPath, string requestedPath)
    {
        string directory = Path.GetDirectoryName(requestedPath)!;
        string name = Path.GetFileNameWithoutExtension(requestedPath);
        string extension = Path.GetExtension(requestedPath);
        Directory.CreateDirectory(directory);
        for (int index = 0; index < 1000; index++)
        {
            string candidate = index == 0 ? requestedPath : Path.Combine(directory, $"{name}-{index}{extension}");
            try
            {
                File.Move(latestPath, candidate, false);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate) || Directory.Exists(candidate)) { continue; }
        }
        return null;
    }
}

public sealed record ProfileScanReceiptPersistenceResult(ProfileScanDrift? Drift, bool InvalidHistory, bool IncompatibleHistory, string? PreservedInvalidPath, string TimestampedScanPath, bool LatestReplaced);

public static class ProfileScanReceiptPersistence
{
    private static readonly JsonSerializerOptions DriftJsonOptions = new() { WriteIndented = true };

    public static ProfileScanReceiptPersistenceResult Save(string directory, ProfileScanReceipt receipt, Func<string, string, ProfileScanReceiptHistory>? readHistory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(receipt);
        string fullDirectory = Path.GetFullPath(directory);
        string latestPath = Path.Combine(fullDirectory, "latest.json");
        string stamp = receipt.ScannedAtUtc.ToString("yyyyMMddTHHmmssfffZ", System.Globalization.CultureInfo.InvariantCulture);
        string timestampedPath = Path.Combine(fullDirectory, "scan-" + stamp + ".json");
        Directory.CreateDirectory(fullDirectory);
        ProfileScanReceiptStore.Write(timestampedPath, receipt);
        ProfileScanDrift? drift = null;
        bool invalidHistory = false;
        bool incompatibleHistory = false;
        string? preservedInvalidPath = null;
        bool replaceLatest = true;
        if (File.Exists(latestPath))
        {
            string preservedPath = Path.Combine(fullDirectory, "invalid-latest-" + stamp + ".json");
            ProfileScanReceiptHistory history = (readHistory ?? ProfileScanReceiptHistory.ReadOrPreserveInvalid)(latestPath, preservedPath);
            invalidHistory = history.InvalidHistory;
            preservedInvalidPath = history.PreservedInvalidPath;
            replaceLatest = history.CanReplaceLatest;
            if (history.Receipt is not null)
            {
                try
                {
                    drift = ProfileScanDriftAnalyzer.Compare(history.Receipt, receipt);
                    File.WriteAllText(Path.Combine(fullDirectory, "drift-" + stamp + ".json"), JsonSerializer.Serialize(drift, DriftJsonOptions));
                }
                catch (ArgumentException)
                {
                    history = ProfileScanReceiptHistory.PreserveIncompatible(latestPath, preservedPath);
                    invalidHistory = true;
                    incompatibleHistory = true;
                    preservedInvalidPath = history.PreservedInvalidPath;
                    replaceLatest = history.CanReplaceLatest;
                }
            }
        }
        if (replaceLatest) ProfileScanReceiptStore.Write(latestPath, receipt);
        RetainNewest(fullDirectory, "scan-*.json", 2);
        RetainNewest(fullDirectory, "drift-*.json", 2);
        return new ProfileScanReceiptPersistenceResult(drift, invalidHistory, incompatibleHistory, preservedInvalidPath, timestampedPath, replaceLatest);
    }

    private static void RetainNewest(string directory, string pattern, int count)
    {
        foreach (string path in Directory.EnumerateFiles(directory, pattern).OrderByDescending(Path.GetFileName, StringComparer.Ordinal).Skip(count)) File.Delete(path);
    }
}

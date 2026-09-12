using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConflictStudio.Core;

public enum RuntimeInvestigationFreshness { Current, Stale }

public sealed record RuntimeInvestigationRun(string PackageDirectory, RuntimeProbeBundleManifest Manifest, RuntimeProbeReceipt? Receipt, DateTimeOffset UpdatedAtUtc);

public sealed record RuntimeInvestigationView(RuntimeInvestigationRun Run, RuntimeInvestigationFreshness Freshness, string? StaleReason);

public sealed class RuntimeInvestigationStore
{
    public const int DefaultRetention = 50;
    private const int SchemaVersion = 1;
    private const int StateBytes = 8_388_608;
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private readonly string _path;
    private readonly int _retention;
    private readonly int _stateBytes;
    private readonly string _mutexName;

    public string? LastRecoveryPath { get; private set; }

    public RuntimeInvestigationStore(string directory, int retention = DefaultRetention)
        : this(directory, retention, StateBytes)
    {
    }

    internal RuntimeInvestigationStore(string directory, int retention, int stateBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (retention is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(retention));
        if (stateBytes is < 1 or > StateBytes) throw new ArgumentOutOfRangeException(nameof(stateBytes));
        _path = Path.Combine(Path.GetFullPath(directory), "runtime-investigations.json");
        _retention = retention;
        _stateBytes = stateBytes;
        _mutexName = "Local\\ConflictStudio.RuntimeInvestigations." + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(_path.ToUpperInvariant())));
    }

    public RuntimeInvestigationRun GeneratePackage(string outputDirectory, ProfileScanReceipt receipt, ConflictWorkItem selectedItem, DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        RuntimeProbeManifest probes = RuntimeProbeManifestBuilder.Build(receipt, selectedItem, createdAtUtc);
        if (probes.Requests.Length == 0) throw new InvalidOperationException("The selected finding has no supported runtime check.");
        string packageDirectory = Path.GetFullPath(outputDirectory);
        RuntimeProbeBundleManifest manifest = RuntimeProbeBundleWriter.Write(packageDirectory, probes);
        RuntimeInvestigationRun run = new(packageDirectory, manifest, null, createdAtUtc);
        return WithLock(() =>
        {
            List<RuntimeInvestigationRun> runs = LoadUnlocked().Where(value => value.Manifest.RunId != manifest.RunId).ToList();
            runs.Add(run);
            SaveUnlocked(runs);
            return run;
        });
    }

    public RuntimeInvestigationRun Import(string manifestPath, string logPath, string? manualAnswersPath, DateTimeOffset importedAtUtc)
    {
        if (importedAtUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Runtime check timestamps must use UTC.", nameof(importedAtUtc));
        RuntimeProbeBundleManifest manifest = RuntimeProbeBundleStore.ReadManifest(manifestPath);
        if (manifest.Binding is null) throw new InvalidDataException("The runtime probe manifest is not bound to one selected finding.");
        string log = RuntimeProbeFileLimits.ReadText(logPath, RuntimeProbeFileLimits.LogBytes);
        Dictionary<string, string>? answers = string.IsNullOrWhiteSpace(manualAnswersPath) ? null : RuntimeProbeBundleStore.ReadManualAnswers(manualAnswersPath);
        if (answers is not null)
        {
            HashSet<string> manualIds = manifest.Requests.Where(value => value.Execution == RuntimeProbeExecution.Manual).Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
            if (answers.Keys.Any(value => !manualIds.Contains(value))) throw new InvalidDataException("The manual answer document contains a foreign or automated request ID.");
        }
        RuntimeProbeReceipt receipt = RuntimeProbeReceiptReader.Read(manifest, log, importedAtUtc, answers);
        return WithLock(() =>
        {
            List<RuntimeInvestigationRun> runs = LoadUnlocked().ToList();
            int index = runs.FindIndex(value => value.Manifest.RunId == manifest.RunId);
            if (index < 0 || !string.Equals(runs[index].Manifest.ManifestId, manifest.ManifestId, StringComparison.Ordinal)) throw new InvalidDataException("The runtime probe manifest does not belong to a generated local run.");
            RuntimeInvestigationRun current = runs[index];
            RuntimeProbeBundleStore.ValidateManifest(current.Manifest);
            if (!string.Equals(current.Manifest.ManifestId, manifest.ManifestId, StringComparison.Ordinal)) throw new InvalidDataException("The runtime probe manifest does not match the generated local run.");
            RuntimeInvestigationRun updated = current with { Receipt = receipt, UpdatedAtUtc = importedAtUtc };
            runs[index] = updated;
            SaveUnlocked(runs);
            return updated;
        });
    }

    public RuntimeInvestigationView[] Load(ProfileScanReceipt receipt, IReadOnlyList<ConflictWorkItem> currentItems)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(currentItems);
        return WithLock(() =>
        {
            LastRecoveryPath = null;
            return LoadUnlocked().OrderByDescending(value => value.UpdatedAtUtc).Select(value => View(value, receipt, currentItems)).ToArray();
        });
    }

    public void Remove(string runId)
    {
        if (!RuntimeProbeFileLimits.IsHex(runId, 32)) throw new ArgumentException("The runtime run identity is invalid.", nameof(runId));
        WithLock(() =>
        {
            List<RuntimeInvestigationRun> runs = LoadUnlocked().Where(value => value.Manifest.RunId != runId).ToList();
            SaveUnlocked(runs);
            return true;
        });
    }

    private RuntimeInvestigationRun[] LoadUnlocked()
    {
        if (!File.Exists(_path)) return [];
        try
        {
            RuntimeInvestigationDocument document = JsonSerializer.Deserialize<RuntimeInvestigationDocument>(RuntimeProbeFileLimits.ReadText(_path, _stateBytes), Options) ?? throw new InvalidDataException("The runtime investigation document is empty.");
            if (document.SchemaVersion != SchemaVersion || document.Runs is null || document.Runs.Length > 500) throw new InvalidDataException("The runtime investigation document is invalid.");
            foreach (RuntimeInvestigationRun run in document.Runs) Validate(run);
            return document.Runs;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            LastRecoveryPath = PreserveInvalidFile();
            return [];
        }
    }

    private void SaveUnlocked(IReadOnlyList<RuntimeInvestigationRun> runs)
    {
        List<RuntimeInvestigationRun> retained = [];
        byte[]? documentBytes = null;
        foreach (RuntimeInvestigationRun run in runs.OrderByDescending(value => value.UpdatedAtUtc).Take(_retention))
        {
            Validate(run);
            RuntimeInvestigationRun[] candidate = [.. retained, run];
            byte[] candidateBytes = JsonSerializer.SerializeToUtf8Bytes(new RuntimeInvestigationDocument(SchemaVersion, candidate), Options);
            if (candidateBytes.Length > _stateBytes)
            {
                if (retained.Count == 0) throw new InvalidOperationException("The newest runtime investigation is too large to save. The previous local state was preserved.");
                break;
            }
            retained.Add(run);
            documentBytes = candidateBytes;
        }
        documentBytes ??= JsonSerializer.SerializeToUtf8Bytes(new RuntimeInvestigationDocument(SchemaVersion, []), Options);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, documentBytes);
            File.Move(temporary, _path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static RuntimeInvestigationView View(RuntimeInvestigationRun run, ProfileScanReceipt receipt, IReadOnlyList<ConflictWorkItem> currentItems)
    {
        RuntimeProbeBinding binding = run.Manifest.Binding!;
        string? staleReason = binding.ManagerKind != receipt.ManagerKind ? "The mod manager changed. Generate a new runtime check for the current setup."
            : !string.Equals(binding.InstallationId, receipt.InstallationId, StringComparison.Ordinal) ? "The manager installation changed. Generate a new runtime check for the current setup."
            : !string.Equals(binding.ProfileName, receipt.ProfileName, StringComparison.Ordinal) ? "The profile changed. Generate a new runtime check for the current profile."
            : CurrentItem(binding, currentItems) is not ConflictWorkItem item ? "The selected finding is no longer present. Keep this observation only as stale context."
            : !SameProviders(item.Providers, binding.Providers) || !string.Equals(item.EvidenceSha256, binding.EvidenceSha256, StringComparison.Ordinal) ? "The providers or source evidence changed. Generate a fresh run before treating an observation as current."
            : null;
        return new RuntimeInvestigationView(run, staleReason is null ? RuntimeInvestigationFreshness.Current : RuntimeInvestigationFreshness.Stale, staleReason);
    }

    private static ConflictWorkItem? CurrentItem(RuntimeProbeBinding binding, IReadOnlyList<ConflictWorkItem> currentItems)
        => currentItems.FirstOrDefault(value => value.Surface == binding.Surface && string.Equals(value.Target, binding.Target, StringComparison.Ordinal));

    private static void Validate(RuntimeInvestigationRun run)
    {
        if (run is null || string.IsNullOrWhiteSpace(run.PackageDirectory) || run.PackageDirectory.Length > 32_767 || run.UpdatedAtUtc.Offset != TimeSpan.Zero) throw new InvalidDataException("A stored runtime investigation is invalid.");
        RuntimeProbeBundleStore.ValidateManifest(run.Manifest);
        if (run.Receipt is null) return;
        RuntimeProbeReceipt receipt = run.Receipt;
        if (receipt.SchemaVersion != 3 || receipt.ImportedAtUtc.Offset != TimeSpan.Zero || receipt.Observations is null || receipt.Observations.Length != run.Manifest.Requests.Length || !string.Equals(receipt.RunId, run.Manifest.RunId, StringComparison.Ordinal) || !string.Equals(receipt.ManifestId, run.Manifest.ManifestId, StringComparison.Ordinal) || !SameBinding(receipt.Binding, run.Manifest.Binding) || receipt.Observations.Any(value => value is null || !RuntimeProbeFileLimits.IsHex(value.Id, 16) || !Enum.IsDefined(value.State) || (value.Value?.Length ?? 0) > RuntimeProbeFileLimits.MaxValueLength || (value.Message?.Length ?? 0) > RuntimeProbeFileLimits.MaxValueLength)) throw new InvalidDataException("A stored runtime receipt is invalid.");
        HashSet<string> requestIds = run.Manifest.Requests.Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
        if (receipt.Observations.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() != receipt.Observations.Length || receipt.Observations.Any(value => !requestIds.Contains(value.Id))) throw new InvalidDataException("A stored runtime receipt is invalid.");
    }

    private static bool SameBinding(RuntimeProbeBinding? first, RuntimeProbeBinding? second)
        => first is not null && second is not null
            && first.ManagerKind == second.ManagerKind
            && string.Equals(first.InstallationId, second.InstallationId, StringComparison.Ordinal)
            && string.Equals(first.ProfileName, second.ProfileName, StringComparison.Ordinal)
            && first.Surface == second.Surface
            && string.Equals(first.Target, second.Target, StringComparison.Ordinal)
            && SameProviders(first.Providers, second.Providers)
            && string.Equals(first.EvidenceSha256, second.EvidenceSha256, StringComparison.Ordinal);

    private static bool SameProviders(string[] first, string[] second)
        => first.Length == second.Length && first.All(value => second.Contains(value, StringComparer.OrdinalIgnoreCase));

    private string PreserveInvalidFile()
    {
        string preserved = Path.Combine(Path.GetDirectoryName(_path)!, $"runtime-investigations.invalid-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.json");
        File.Move(_path, preserved, false);
        return preserved;
    }

    private T WithLock<T>(Func<T> action)
    {
        using Mutex mutex = new(false, _mutexName);
        bool acquired;
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(30)); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new InvalidOperationException("The runtime investigation file is busy. Try again.");
        try { return action(); }
        finally { mutex.ReleaseMutex(); }
    }

    private sealed record RuntimeInvestigationDocument(int SchemaVersion, RuntimeInvestigationRun[] Runs);
}

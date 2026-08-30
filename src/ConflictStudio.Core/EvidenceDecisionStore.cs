using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConflictStudio.Core;

public enum EvidenceDecisionState { Resolved, ReviewExpired }

public sealed record EvidenceDecision(string ProfileName, string Target, string[] Providers, string EvidenceSha256, string Rationale, DateTimeOffset ReviewedAtUtc, string InstallationId = "", ConflictSurface Surface = ConflictSurface.ScriptAndTweak);

public sealed record EvidenceDecisionBatchResult(EvidenceDecision[] Decisions, int ChangedCount);

public sealed class EvidenceDecisionStore
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = true };
    private readonly string _path;

    public EvidenceDecisionStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "evidence-decisions.json");
    }

    public EvidenceDecision[] Load()
    {
        if (!File.Exists(_path)) return [];
        EvidenceDecisionDocument document = JsonSerializer.Deserialize<EvidenceDecisionDocument>(File.ReadAllText(_path), Options) ?? throw new EvidenceDecisionException("The decision document is empty.");
        if (document.SchemaVersion != SchemaVersion || document.Decisions is null) throw new EvidenceDecisionException("The decision document has an unsupported schema.");
        foreach (EvidenceDecision decision in document.Decisions) Validate(decision);
        return document.Decisions;
    }

    public void Save(IReadOnlyList<EvidenceDecision> decisions)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        foreach (EvidenceDecision decision in decisions) Validate(decision);
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new EvidenceDecisionDocument(SchemaVersion, decisions.ToArray()), Options));
            File.Move(temporary, _path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public EvidenceDecision[] Review(string installationId, string profileName, ConflictWorkItem item, string rationale, DateTimeOffset reviewedAtUtc)
        => ReviewMany(installationId, profileName, [item], rationale, reviewedAtUtc).Decisions;

    public EvidenceDecisionBatchResult ReviewMany(string installationId, string profileName, IReadOnlyList<ConflictWorkItem> items, string rationale, DateTimeOffset reviewedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0) throw new EvidenceDecisionException("Select at least one evidence item.");
        if (items.Any(value => value is null || value.Classification == EvidenceClassification.Unresolved)) throw new EvidenceDecisionException("Unresolved evidence cannot be marked intentional.");
        ArgumentException.ThrowIfNullOrWhiteSpace(rationale);
        List<EvidenceDecision> decisions = Load().ToList();
        foreach (ConflictWorkItem item in items)
        {
            decisions.RemoveAll(value => SameTarget(value, installationId, profileName, item.Surface, item.Target, item.Providers));
            decisions.Add(new EvidenceDecision(profileName, item.Target, item.Providers, item.EvidenceSha256, rationale.Trim(), reviewedAtUtc, installationId, item.Surface));
        }
        Save(decisions);
        return new EvidenceDecisionBatchResult(decisions.ToArray(), items.Count);
    }

    public EvidenceDecision[] Reopen(string installationId, string profileName, ConflictWorkItem item)
        => ReopenMany(installationId, profileName, [item]).Decisions;

    public EvidenceDecisionBatchResult ReopenMany(string installationId, string profileName, IReadOnlyList<ConflictWorkItem> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0) throw new EvidenceDecisionException("Select at least one evidence item.");
        EvidenceDecision[] before = Load();
        EvidenceDecision[] decisions = before.Where(value => !items.Any(item => SameTarget(value, installationId, profileName, item.Surface, item.Target, item.Providers))).ToArray();
        Save(decisions);
        return new EvidenceDecisionBatchResult(decisions, before.Length - decisions.Length);
    }

    public static EvidenceDecisionState Evaluate(EvidenceDecision decision, string installationId, string profileName, ConflictSurface surface, string evidenceSha256)
    {
        Validate(decision);
        return string.Equals(decision.InstallationId, installationId, StringComparison.Ordinal) && decision.Surface == surface && string.Equals(decision.ProfileName, profileName, StringComparison.Ordinal) && string.Equals(decision.EvidenceSha256, evidenceSha256, StringComparison.Ordinal)
            ? EvidenceDecisionState.Resolved
            : EvidenceDecisionState.ReviewExpired;
    }

    private static void Validate(EvidenceDecision decision)
    {
        if (decision is null || string.IsNullOrWhiteSpace(decision.ProfileName) || string.IsNullOrWhiteSpace(decision.Target) || decision.Providers is null || decision.Providers.Length == 0 || decision.Providers.Any(string.IsNullOrWhiteSpace) || string.IsNullOrWhiteSpace(decision.Rationale) || decision.ReviewedAtUtc.Offset != TimeSpan.Zero || decision.EvidenceSha256.Length != 64 || decision.EvidenceSha256.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))) throw new EvidenceDecisionException("An evidence decision is invalid.");
    }

    private static bool SameTarget(EvidenceDecision decision, string installationId, string profileName, ConflictSurface surface, string target, string[] providers)
        => string.Equals(decision.InstallationId, installationId, StringComparison.Ordinal) && decision.Surface == surface && string.Equals(decision.ProfileName, profileName, StringComparison.Ordinal) && string.Equals(decision.Target, target, StringComparison.Ordinal) && decision.Providers.SequenceEqual(providers, StringComparer.OrdinalIgnoreCase);

    private sealed record EvidenceDecisionDocument(int SchemaVersion, EvidenceDecision[] Decisions);
}

public sealed class EvidenceDecisionException(string message) : Exception(message);

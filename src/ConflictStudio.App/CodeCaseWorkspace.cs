using ConflictStudio.Core;

namespace ConflictStudio.App;

public sealed record CodeCaseCounts(int ProvenConflicts, int NeedsDecision, int Reviewed, int CompatibleEvidence);

public static class CodeCaseWorkspace
{
    public static ConflictWorkItem[] Filter(IReadOnlyList<ConflictWorkItem> items, string query, string view, string surface, string provider)
    {
        ArgumentNullException.ThrowIfNull(items);
        IEnumerable<ConflictWorkItem> filtered = items;
        filtered = view switch
        {
            "Proven" => filtered.Where(value => value.CaseKind == ConflictCaseKind.ProvenConflict && value.State != ConflictWorkState.Reviewed),
            "NeedsDecision" => filtered.Where(value => value.IsActionable && value.CaseKind != ConflictCaseKind.ProvenConflict && value.State != ConflictWorkState.Reviewed),
            "Reviewed" => filtered.Where(value => value.State == ConflictWorkState.Reviewed),
            "Compatible" => filtered.Where(value => !value.IsActionable && value.State != ConflictWorkState.Reviewed),
            "All" => filtered,
            _ => filtered.Where(value => value.IsActionable && value.State != ConflictWorkState.Reviewed)
        };
        if (surface != "All" && Enum.TryParse(surface, out ConflictSurface parsedSurface)) filtered = filtered.Where(value => value.Surface == parsedSurface);
        if (!string.IsNullOrWhiteSpace(provider) && provider != "All mods") filtered = filtered.Where(value => value.Providers.Contains(provider, StringComparer.OrdinalIgnoreCase));
        string search = query?.Trim() ?? string.Empty;
        if (search.Length > 0) filtered = filtered.Where(value => value.Target.Contains(search, StringComparison.OrdinalIgnoreCase) || value.Summary.Contains(search, StringComparison.OrdinalIgnoreCase) || value.Providers.Any(name => name.Contains(search, StringComparison.OrdinalIgnoreCase)));
        return filtered.OrderBy(CaseOrder).ThenBy(value => value.Surface).ThenBy(value => value.Target, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static CodeCaseCounts Counts(IReadOnlyList<ConflictWorkItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new CodeCaseCounts(
            items.Count(value => value.CaseKind == ConflictCaseKind.ProvenConflict && value.State != ConflictWorkState.Reviewed),
            items.Count(value => value.IsActionable && value.CaseKind != ConflictCaseKind.ProvenConflict && value.State != ConflictWorkState.Reviewed),
            items.Count(value => value.State == ConflictWorkState.Reviewed),
            items.Count(value => !value.IsActionable && value.State != ConflictWorkState.Reviewed));
    }

    public static string[] Providers(IReadOnlyList<ConflictWorkItem> items)
        => ["All mods", .. items.SelectMany(value => value.Providers).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase)];

    public static string ReviewRationale(string outcome, string notes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome);
        string trimmed = notes?.Trim() ?? string.Empty;
        return trimmed.Length == 0 ? outcome : outcome + ": " + trimmed;
    }

    public static string ReviewNotes(string rationale, string outcome)
    {
        if (string.IsNullOrWhiteSpace(rationale) || string.IsNullOrWhiteSpace(outcome)) return string.Empty;
        string prefix = outcome + ": ";
        return rationale.StartsWith(prefix, StringComparison.Ordinal) ? rationale[prefix.Length..] : string.Empty;
    }

    private static int CaseOrder(ConflictWorkItem item) => item.CaseKind switch
    {
        ConflictCaseKind.ProvenConflict => 0,
        ConflictCaseKind.FileOverride => 1,
        ConflictCaseKind.OrderSensitive => 2,
        ConflictCaseKind.RuntimeCheck => 3,
        ConflictCaseKind.Unknown => 4,
        ConflictCaseKind.Reviewed => 5,
        ConflictCaseKind.Composes => 6,
        ConflictCaseKind.SameEvidence => 7,
        _ => 8
    };
}

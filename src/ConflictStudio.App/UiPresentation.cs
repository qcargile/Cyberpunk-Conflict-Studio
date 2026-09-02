using ConflictStudio.Core;

namespace ConflictStudio.App;

public static class CodeCoveragePresentation
{
    public static string Summary(ProfileScanReceipt? receipt)
        => receipt?.CodeCoverage is { } coverage
            ? $"Partial code coverage · {coverage.Sources.Sum(value => value.AnalyzedFiles):N0} effective files submitted to analysis · expand for limits"
            : "Code coverage not recorded · rescan to capture coverage";

    public static string Details(ProfileScanReceipt? receipt)
    {
        List<string> lines = [];
        if (receipt?.CodeCoverage is { } coverage)
        {
            lines.Add(string.Join(" · ", coverage.Sources.Select(value => $"{value.Surface}: {value.AnalyzedFiles:N0}")));
            lines.Add($"Unsupported .tweak: {coverage.UnsupportedTweakFiles:N0} · Unreadable inputs: {coverage.UnreadableInputs:N0} · CET callbacks: {coverage.LiteralCallbacks:N0} literal / {coverage.DynamicCallbacks:N0} dynamic");
            lines.AddRange(coverage.Limitations);
        }
        else lines.Add("Rescan to capture coverage; old receipts do not establish zero limitations.");
        return string.Join(Environment.NewLine, lines);
    }
}

public static class ArchiveScrollbarNavigation
{
    public static double OffsetAtPointer(double pointerCoordinate, double trackLength, double maximum)
    {
        if (trackLength <= 0 || maximum <= 0) return 0;
        return Math.Clamp(pointerCoordinate / trackLength, 0, 1) * maximum;
    }
}

public static class ScanHistoryPresentation
{
    public static string Describe(ProfileScanDrift drift)
    {
        ArgumentNullException.ThrowIfNull(drift);
        return $"Since previous scan: {drift.NewWorkItems.Length:N0} added · {drift.ChangedWorkItems.Length:N0} updated · {drift.RemovedWorkItems.Length:N0} no longer present";
    }
}

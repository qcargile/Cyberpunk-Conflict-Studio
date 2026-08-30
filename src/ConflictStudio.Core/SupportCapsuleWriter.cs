using System.Net;
using System.Text;
using System.Text.Json;

namespace ConflictStudio.Core;

public static class SupportCapsuleWriter
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = false, WriteIndented = true };

    public static void Write(string directory, SupportCapsule capsule)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(capsule);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "conflict-casefile.json"), JsonSerializer.Serialize(capsule, Options));
        File.WriteAllText(Path.Combine(directory, "conflict-casefile.html"), Html(capsule), Encoding.UTF8);
        RuntimeProbeBundleWriter.Write(Path.Combine(directory, "runtime-probe"), capsule.Probes);
    }

    public static SupportCapsule Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return JsonSerializer.Deserialize<SupportCapsule>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("The support casefile is empty.");
    }

    private static string Html(SupportCapsule capsule)
    {
        StringBuilder html = new();
        html.Append("<!doctype html><meta charset=\"utf-8\"><title>Conflict Studio casefile</title><style>body{font:14px system-ui;background:#111;color:#ddd;max-width:1100px;margin:40px auto}h1,h2{color:#fff}.item,details{border:1px solid #444;padding:12px;margin:8px 0}.muted{color:#aaa}code{overflow-wrap:anywhere}table{width:100%;border-collapse:collapse}th,td{border-bottom:1px solid #333;padding:7px;text-align:left}</style>");
        html.Append("<h1>Conflict Studio casefile</h1><p>Manager: ").Append(E(capsule.Evidence.ManagerKind.ToString())).Append(" · Profile: ").Append(E(capsule.Casefile.ProfileName)).Append("</p>");
        html.Append("<p class=\"muted\">Providers: ").Append(capsule.Summary.ActiveProviders).Append(" · Archives: ").Append(capsule.Summary.Archives).Append(" · Resource conflicts: ").Append(capsule.Summary.ResourceConflicts).Append(" · Source findings: ").Append(capsule.Summary.InteractionFindings).Append("</p>");
        html.Append("<h2>Archive order</h2><ol>");
        foreach (string archive in capsule.Casefile.ArchiveOrder) html.Append("<li><code>").Append(E(archive)).Append("</code></li>");
        html.Append("</ol><h2>Scan metrics</h2>");
        if (capsule.Evidence.Metrics is not null)
        {
            html.Append("<p>Total: ").Append(capsule.Evidence.Metrics.TotalElapsedMilliseconds).Append(" ms</p><ul>");
            foreach (ScanPhaseMetric metric in capsule.Evidence.Metrics.Phases) html.Append("<li>").Append(E(metric.Name)).Append(": ").Append(metric.ElapsedMilliseconds).Append(" ms · ").Append(metric.ItemCount).Append(" items</li>");
            html.Append("</ul>");
        }
        html.Append("<h2>Work queue</h2>");
        foreach (ConflictWorkItem item in capsule.WorkQueue) html.Append("<div class=\"item\"><strong>").Append(E(item.Target)).Append("</strong><p>").Append(E(item.State.ToString())).Append(" · ").Append(E(item.Classification.ToString())).Append(" · ").Append(E(item.Summary)).Append("</p><span class=\"muted\">").Append(E(string.Join(", ", item.Providers))).Append("</span><p>").Append(E(item.NextAction)).Append("</p></div>");
        html.Append("<h2>Reviewed decisions</h2>");
        foreach (EvidenceDecision decision in capsule.Decisions) html.Append("<div class=\"item\"><strong>").Append(E(decision.Target)).Append("</strong><p>").Append(E(decision.Rationale)).Append("</p><code>").Append(E(decision.EvidenceSha256)).Append("</code></div>");
        html.Append("<h2>Diagnostics</h2>");
        foreach (RdarArchiveFailure failure in capsule.Evidence.ArchiveFailures) Diagnostic(html, failure.ArchiveName, failure.Provider, failure.Message);
        foreach (RdarArchiveWarning warning in capsule.Evidence.ArchiveWarnings ?? []) Diagnostic(html, warning.ArchiveName, warning.Provider, warning.Message);
        if (capsule.Evidence.ArchiveOrderEvidence is { Kind: ArchiveOrderEvidenceKind.Unresolved } orderEvidence) Diagnostic(html, "Archive order", orderEvidence.Provider ?? "Deployment", orderEvidence.Message);
        if (capsule.Evidence.ResourcePathIndexEvidence is { State: not ResourcePathIndexState.Resolved } pathEvidence) Diagnostic(html, "Resource path index", pathEvidence.Provider ?? "Path resolver", pathEvidence.Message);
        foreach (ArchiveXlSourceFailure failure in capsule.Evidence.ArchiveXlFailures) Diagnostic(html, failure.FilePath, failure.Provider, failure.Message);
        foreach (SourceAnalysisFailure failure in capsule.Evidence.SourceFailures) Diagnostic(html, failure.FilePath, failure.Provider + " · " + failure.Surface, failure.Message);
        html.Append("<h2>Archive overview</h2><table><thead><tr><th>#</th><th>Archive</th><th>Provider</th><th>Winning</th><th>Losing</th><th>Redundant</th><th>Unresolved</th><th>No conflict</th></tr></thead><tbody>");
        foreach (ArchiveConflictSummary archive in capsule.Evidence.ArchiveSummaries ?? []) html.Append("<tr><td>").Append(archive.OrderPosition is int position ? position + 1 : "?").Append("</td><td><code>").Append(E(archive.ArchiveName)).Append("</code></td><td>").Append(E(archive.Provider)).Append("</td><td>").Append(archive.Winning.Length).Append("</td><td>").Append(archive.Losing.Length).Append("</td><td>").Append(archive.Redundant.Length).Append("</td><td>").Append(archive.Unresolved.Length).Append("</td><td>").Append(archive.Unique.Length).Append("</td></tr>");
        html.Append("</tbody></table>");
        html.Append("<h2>Packed resource evidence</h2>");
        foreach (ResourceConflict conflict in capsule.Casefile.ResourceConflicts)
        {
            html.Append("<details><summary>").Append(E(conflict.DisplayName)).Append(" · ").Append(E(conflict.Kind.ToString())).Append(" · winner ").Append(E(conflict.EngineWinnerArchive)).Append("</summary>");
            foreach (ResourceProvider provider in conflict.Providers) html.Append("<p><code>").Append(E(provider.Provider)).Append(" · ").Append(E(provider.ArchiveName)).Append(" · ").Append(E(provider.PayloadFingerprint ?? "payload comparison unavailable")).Append("</code></p>");
            html.Append("</details>");
        }
        html.Append("<h2>Virtual file evidence</h2>");
        foreach (VirtualFileShadow shadow in capsule.Evidence.VirtualFileShadows)
        {
            html.Append("<details><summary>").Append(E(shadow.RelativePath)).Append(" · winner ").Append(E(shadow.WinnerProvider)).Append("</summary>");
            foreach (VirtualFileProvider provider in shadow.Providers) html.Append("<p><code>").Append(E(provider.Provider)).Append(" · ").Append(E(provider.Sha256)).Append("</code></p>");
            html.Append("</details>");
        }
        html.Append("<h2>RedScript flow evidence</h2>");
        foreach (RedScriptFlowEvidence flow in capsule.Evidence.RedScriptFlows) html.Append("<p><code>").Append(E(flow.Target)).Append(" · ").Append(E(flow.Provider)).Append(" · ").Append(E(flow.Kind.ToString())).Append(" · ").Append(E(flow.Continuation.ToString())).Append(" · ").Append(E(flow.SourceHash)).Append("</code></p>");
        html.Append("<h2>Shared state evidence</h2>");
        foreach (SharedStateWriteFinding finding in capsule.Evidence.SharedStateWrites) foreach (SharedStateWrite write in finding.Writes) html.Append("<p><code>").Append(E(finding.Surface.ToString())).Append(" · ").Append(E(finding.Target)).Append(" · ").Append(E(write.Provider)).Append(" · ").Append(E(write.FilePath)).Append(':').Append(write.Line).Append("</code></p>");
        html.Append("<h2>CET Lua evidence</h2>");
        foreach (LuaCallbackEvidence callback in capsule.Evidence.LuaCallbacks) html.Append("<p><code>").Append(E(callback.Target)).Append(" · ").Append(E(callback.Kind.ToString())).Append(" · ").Append(E(callback.Continuation.ToString())).Append(" · ").Append(E(callback.SourceHash)).Append("</code></p>");
        html.Append("<h2>TweakXL evidence</h2>");
        foreach (TweakOverlap overlap in capsule.Evidence.TweakOverlaps)
        {
            html.Append("<details><summary>").Append(E(overlap.Target)).Append(" · ").Append(E(overlap.Kind.ToString())).Append("</summary>");
            foreach (TweakOperation operation in overlap.Operations) html.Append("<p><code>").Append(E(operation.Provider)).Append(" · ").Append(E(operation.Kind.ToString())).Append(" · ").Append(E(operation.Value)).Append(" · ").Append(E(operation.FilePath)).Append(':').Append(operation.LineNumber).Append("</code></p>");
            html.Append("</details>");
        }
        html.Append("<h2>ArchiveXL evidence</h2>");
        foreach (ArchiveXlOperationChain chain in capsule.Evidence.ArchiveXlChains)
        {
            html.Append("<details><summary>").Append(E(chain.Target)).Append(" · ").Append(E(chain.Kind.ToString())).Append("</summary>");
            foreach (ArchiveXlOperation operation in chain.Operations) html.Append("<p><code>").Append(E(operation.Provider)).Append(" · ").Append(E(operation.FilePath)).Append(" · ").Append(E(operation.Payload)).Append("</code></p>");
            html.Append("</details>");
        }
        html.Append("<h2>Runtime requests</h2>");
        foreach (RuntimeProbeRequest request in capsule.Probes.Requests) html.Append("<div class=\"item\"><strong>").Append(E(request.Target)).Append("</strong><p>").Append(E(request.Observation)).Append("</p></div>");
        return html.ToString();
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);

    private static void Diagnostic(StringBuilder html, string target, string provider, string message)
        => html.Append("<div class=\"item\"><strong>").Append(E(target)).Append("</strong><p>").Append(E(message)).Append("</p><span class=\"muted\">").Append(E(provider)).Append("</span></div>");
}

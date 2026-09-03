using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ConflictStudio.Core;

public enum RuntimeProbeExecution { Automated, Manual }

public enum RuntimeProbeObservationState { Observed, Failed, ManualRequired, ManualRecorded, Missing }

public static class RuntimeProbeEvidence
{
    public const string Boundary = "These automatic and manual checks, including failed checks, do not prove that mods work together, pass startup code checks, load successfully, or cause a gameplay bug. They do not show which mod caused a result, the order in which mod code runs, or which mod sets a value last.";
}

public sealed record RuntimeProbeBundleRequest(string Id, RuntimeProbeExecution Execution, RuntimeProbeRequest Request);

public sealed record RuntimeProbeBundleManifest(int SchemaVersion, string ProfileName, string? InstallationId, DateTimeOffset CreatedAtUtc, string RunId, string ManifestId, RuntimeProbeBundleRequest[] Requests);

public sealed record RuntimeProbeObservation(string Id, RuntimeProbeObservationState State, string? Value, string? Message);

public sealed record RuntimeProbeReceipt(int SchemaVersion, string ProfileName, string? InstallationId, string RunId, string ManifestId, DateTimeOffset ImportedAtUtc, bool CompleteRun, RuntimeProbeObservation[] Observations, string EvidenceBoundary = RuntimeProbeEvidence.Boundary);

public static class RuntimeProbeBundleWriter
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static RuntimeProbeBundleManifest Write(string directory, RuntimeProbeManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(manifest);
        string modRoot = Path.Combine(directory, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods", "ConflictStudioProbe");
        Directory.CreateDirectory(modRoot);
        RuntimeProbeBundleRequest[] requests = manifest.Requests.Select(value => new RuntimeProbeBundleRequest(Id(value), value.Kind == RuntimeProbeKind.PostInitializationTweakValue ? RuntimeProbeExecution.Automated : RuntimeProbeExecution.Manual, value)).ToArray();
        string runId = Guid.NewGuid().ToString("N");
        string identity = manifest.InstallationId + "|" + manifest.ProfileName + "|" + manifest.CreatedAtUtc.ToString("O") + "|" + runId + "|" + string.Join("|", requests.Select(value => value.Id));
        string manifestId = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        RuntimeProbeBundleManifest bundle = new(2, manifest.ProfileName, manifest.InstallationId, manifest.CreatedAtUtc, runId, manifestId, requests);
        File.WriteAllText(Path.Combine(directory, "probe-manifest.json"), JsonSerializer.Serialize(bundle, Options), Encoding.UTF8);
        File.WriteAllText(Path.Combine(modRoot, "init.lua"), Lua(bundle), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "README.txt"), Instructions(bundle), Encoding.UTF8);
        File.WriteAllText(Path.Combine(directory, "manual-answers.example.json"), JsonSerializer.Serialize(requests.Where(value => value.Execution == RuntimeProbeExecution.Manual).ToDictionary(value => value.Id, value => string.Empty), Options), Encoding.UTF8);
        return bundle;
    }

    private static string Lua(RuntimeProbeBundleManifest manifest)
    {
        StringBuilder lua = new();
        lua.Append("local elapsed = 0.0\nlocal emitted = false\n\n");
        lua.Append("local function emit(id, state, value)\n  print('[ConflictStudioProbe] RESULT manifest=").Append(manifest.ManifestId).Append(" run=").Append(manifest.RunId).Append(" id=' .. id .. ' state=' .. state .. ' value=' .. value)\nend\n\n");
        lua.Append("local function encode(value)\n  local ok, result = pcall(function() return json.encode(value) end)\n  if ok then return result end\n  return tostring(value)\nend\n\n");
        lua.Append("registerForEvent('onInit', function()\n  print('[ConflictStudioProbe] BEGIN manifest=").Append(manifest.ManifestId).Append(" run=").Append(manifest.RunId).Append(" profile=").Append(LuaString(manifest.ProfileName)).Append("')\nend)\n\n");
        lua.Append("registerForEvent('onUpdate', function(deltaTime)\n  if emitted then return end\n  elapsed = elapsed + deltaTime\n  if elapsed < 5.0 then return end\n  emitted = true\n");
        foreach (RuntimeProbeBundleRequest request in manifest.Requests)
        {
            if (request.Execution == RuntimeProbeExecution.Automated) lua.Append("  do\n    local ok, value = pcall(function() return TweakDB:GetFlat('").Append(LuaString(request.Request.Target)).Append("') end)\n    if ok then emit('").Append(request.Id).Append("', 'observed', encode(value)) else emit('").Append(request.Id).Append("', 'failed', encode(value)) end\n  end\n");
            else lua.Append("  emit('").Append(request.Id).Append("', 'manual', '").Append(LuaString(request.Request.Observation)).Append("')\n");
        }
        lua.Append("  print('[ConflictStudioProbe] END manifest=").Append(manifest.ManifestId).Append(" run=").Append(manifest.RunId).Append("')\nend)\n");
        return lua.ToString();
    }

    private static string Instructions(RuntimeProbeBundleManifest manifest)
    {
        int automated = manifest.Requests.Count(value => value.Execution == RuntimeProbeExecution.Automated);
        int manual = manifest.Requests.Length - automated;
        return $"Conflict Studio in-game check for profile: {manifest.ProfileName}{Environment.NewLine}Manifest: {manifest.ManifestId}{Environment.NewLine}Run: {manifest.RunId}{Environment.NewLine}{Environment.NewLine}This bundle is optional and does not modify game data. Install the bin folder as a separate mod in the same manager profile, deploy or activate it, launch the matching profile, and remain at the main menu for at least 10 seconds before closing the game. The automatic check records selected game database values once, five seconds after CET starts updating and after CET mods finish their startup code (onInit).{Environment.NewLine}{Environment.NewLine}Values recorded automatically: {automated}{Environment.NewLine}Manual checks: {manual}{Environment.NewLine}{Environment.NewLine}The recorded values show only that moment, not later gameplay changes. They do not prove that mods work together or that a bug occurred. Manual checks remain unanswered until a result is recorded. These results do not include startup error logs and cannot confirm that mods passed startup code checks or loaded successfully.{Environment.NewLine}{Environment.NewLine}Manual checks are technical steps that may need the mod author's help. Ask which action to perform and how to measure any hidden value. Copy manual-answers.example.json and edit the copy to replace each empty answer with the measured result, keeping the IDs unchanged. Do not guess from gameplay alone. Keep that file with the ConflictStudioProbe log and casefile when sending the results to the maintainer. Repository maintainers can import these files with the command-line tool built from source (source-built CLI). Remove or disable ConflictStudioProbe after collecting the results.{Environment.NewLine}";
    }

    private static string Id(RuntimeProbeRequest request)
    {
        string source = request.Kind + "|" + request.Target + "|" + string.Join("|", request.Providers);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..16];
    }

    private static string LuaString(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}

public static class RuntimeProbeReceiptReader
{
    private static readonly Regex Result = new("\\[ConflictStudioProbe\\] RESULT manifest=(?<manifest>[0-9a-f]{64}) run=(?<run>[0-9a-f]{32}) id=(?<id>[0-9a-f]{16}) state=(?<state>observed|failed|manual) value=(?<value>.*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    public static RuntimeProbeReceipt Read(RuntimeProbeBundleManifest manifest, string logText, DateTimeOffset importedAtUtc, IReadOnlyDictionary<string, string>? manualAnswers = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(logText);
        if (importedAtUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Receipt timestamps must use UTC.", nameof(importedAtUtc));
        string begin = $"[ConflictStudioProbe] BEGIN manifest={manifest.ManifestId} run={manifest.RunId} profile={manifest.ProfileName}";
        string end = $"[ConflictStudioProbe] END manifest={manifest.ManifestId} run={manifest.RunId}";
        int beginAt = logText.LastIndexOf(begin, StringComparison.Ordinal);
        int endAt = beginAt < 0 ? -1 : logText.IndexOf(end, beginAt + begin.Length, StringComparison.Ordinal);
        bool complete = beginAt >= 0 && endAt >= 0;
        string frame = complete ? logText[beginAt..(endAt + end.Length)] : string.Empty;
        Dictionary<string, Match> matches = Result.Matches(frame).Cast<Match>().Where(value => value.Groups["manifest"].Value == manifest.ManifestId && value.Groups["run"].Value == manifest.RunId).GroupBy(value => value.Groups["id"].Value, StringComparer.Ordinal).ToDictionary(value => value.Key, value => value.Last(), StringComparer.Ordinal);
        RuntimeProbeObservation[] observations = manifest.Requests.Select(request => Observation(request, matches, manualAnswers)).ToArray();
        return new RuntimeProbeReceipt(2, manifest.ProfileName, manifest.InstallationId, manifest.RunId, manifest.ManifestId, importedAtUtc, complete, observations);
    }

    private static RuntimeProbeObservation Observation(RuntimeProbeBundleRequest request, Dictionary<string, Match> matches, IReadOnlyDictionary<string, string>? manualAnswers)
    {
        if (request.Execution == RuntimeProbeExecution.Manual && manualAnswers?.TryGetValue(request.Id, out string? answer) == true && !string.IsNullOrWhiteSpace(answer)) return new RuntimeProbeObservation(request.Id, RuntimeProbeObservationState.ManualRecorded, answer.Trim(), null);
        if (!matches.TryGetValue(request.Id, out Match? match)) return new RuntimeProbeObservation(request.Id, RuntimeProbeObservationState.Missing, null, "No matching result was found inside the complete manifest/run frame.");
        string state = match.Groups["state"].Value;
        string value = match.Groups["value"].Value.Trim();
        return state switch
        {
            "observed" => new RuntimeProbeObservation(request.Id, RuntimeProbeObservationState.Observed, value, null),
            "failed" => new RuntimeProbeObservation(request.Id, RuntimeProbeObservationState.Failed, null, $"Unreadable TweakDB probe result; evidence remains unresolved. {value}"),
            _ => new RuntimeProbeObservation(request.Id, RuntimeProbeObservationState.ManualRequired, null, value)
        };
    }
}

public static class RuntimeProbeBundleStore
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static RuntimeProbeBundleManifest ReadManifest(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        RuntimeProbeBundleManifest manifest = JsonSerializer.Deserialize<RuntimeProbeBundleManifest>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("The runtime probe manifest is empty.");
        if (manifest.SchemaVersion != 2 || string.IsNullOrWhiteSpace(manifest.ProfileName) || manifest.RunId.Length != 32 || manifest.ManifestId.Length != 64 || manifest.Requests is null) throw new InvalidDataException("The runtime probe manifest is invalid.");
        return manifest;
    }

    public static Dictionary<string, string> ReadManualAnswers(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("The manual answer document is empty.");
    }

    public static void WriteReceipt(string path, RuntimeProbeReceipt receipt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(receipt);
        AtomicWrite(path, JsonSerializer.Serialize(receipt, Options));
    }

    public static void WriteCasefileReceipt(string directory, RuntimeProbeReceipt receipt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        SupportCapsule capsule = SupportCapsuleWriter.Read(Path.Combine(directory, "conflict-casefile.json"));
        RuntimeProbeBundleManifest manifest = ReadManifest(Path.Combine(directory, "runtime-probe", "probe-manifest.json"));
        if (string.IsNullOrWhiteSpace(receipt.InstallationId) || !string.Equals(capsule.Evidence.InstallationId, receipt.InstallationId, StringComparison.Ordinal) || !string.Equals(capsule.Casefile.ProfileName, receipt.ProfileName, StringComparison.Ordinal) || !string.Equals(manifest.ManifestId, receipt.ManifestId, StringComparison.Ordinal) || !string.Equals(manifest.RunId, receipt.RunId, StringComparison.Ordinal)) throw new InvalidDataException("The runtime receipt does not belong to this exact casefile probe run.");
        Directory.CreateDirectory(directory);
        WriteReceipt(Path.Combine(directory, "runtime-receipt.json"), receipt);
        StringBuilder html = new("<!doctype html><meta charset=\"utf-8\"><title>Conflict Studio runtime receipt</title><style>body{font:14px system-ui;background:#111;color:#ddd;max-width:1000px;margin:40px auto}.item{border:1px solid #444;padding:12px;margin:8px 0}code{overflow-wrap:anywhere}</style><h1>Runtime receipt</h1>");
        html.Append("<p>Profile: ").Append(WebUtility.HtmlEncode(receipt.ProfileName)).Append(" · Complete run: ").Append(receipt.CompleteRun).Append("</p>");
        html.Append("<p>").Append(WebUtility.HtmlEncode(receipt.EvidenceBoundary)).Append("</p>");
        foreach (RuntimeProbeObservation observation in receipt.Observations) html.Append("<div class=\"item\"><strong>").Append(WebUtility.HtmlEncode(observation.Id)).Append(" · ").Append(WebUtility.HtmlEncode(observation.State.ToString())).Append("</strong><p><code>").Append(WebUtility.HtmlEncode(observation.Value ?? observation.Message ?? string.Empty)).Append("</code></p></div>");
        AtomicWrite(Path.Combine(directory, "runtime-receipt.html"), html.ToString());
    }

    private static void AtomicWrite(string path, string text)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, text, Encoding.UTF8);
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

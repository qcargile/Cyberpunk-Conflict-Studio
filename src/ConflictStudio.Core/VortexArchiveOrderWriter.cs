using System.Text.Json;

namespace ConflictStudio.Core;

public sealed record VortexOrderRequest(int SchemaVersion, string RequestId, string ContextId, string ProfileId, DateTimeOffset RequestedAtUtc, DateTimeOffset ExpiresAtUtc, string? ExpectedOrderSha256, ArchiveFingerprint[] Inventory, string[] ProposedOrder);

public sealed record VortexOrderResponse(int SchemaVersion, string RequestId, bool Applied, string Message, string? BackupPath, string? WrittenSha256, DateTimeOffset CompletedAtUtc, string? ContextId = null);

public sealed class VortexOrderBridgeStore
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    private readonly string _requestPath;
    private readonly string _responsePath;

    public VortexOrderBridgeStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _requestPath = Path.Combine(root, "order-request.json");
        _responsePath = Path.Combine(root, "order-response.json");
    }

    public void WriteRequest(VortexOrderRequest request) => Write(_requestPath, request);

    public VortexOrderRequest? ReadRequest() => Read<VortexOrderRequest>(_requestPath);

    public void WriteResponse(VortexOrderResponse response) => Write(_responsePath, response);

    public VortexOrderResponse? TryReadResponse(string requestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        VortexOrderResponse? response = Read<VortexOrderResponse>(_responsePath);
        return response is not null && string.Equals(response.RequestId, requestId, StringComparison.Ordinal) ? response : null;
    }

    public VortexOrderResponse Exchange(VortexOrderRequest request, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        WriteRequest(request);
        try
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                VortexOrderResponse? response = TryReadResponse(request.RequestId);
                if (response is not null) return response;
                Thread.Sleep(100);
            }
            throw new ArchiveOrderException("Vortex did not confirm the archive order. Keep Vortex open with the Conflict Studio bridge enabled, then try again.");
        }
        finally
        {
            VortexOrderRequest? pending = ReadRequest();
            if (pending is not null && string.Equals(pending.RequestId, request.RequestId, StringComparison.Ordinal)) File.Delete(_requestPath);
        }
    }

    private static T? Read<T>(string path)
    {
        if (!File.Exists(path)) return default;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return default; }
    }

    private static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, Options));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

public sealed class VortexArchiveOrderWriter : IArchiveOrderWriter
{
    private VortexManagerContext _context;
    private readonly Func<VortexOrderRequest, VortexOrderResponse> _exchange;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<IReadOnlyList<string>> _runningProcesses;
    private string[]? _previousOrder;
    private ArchiveFingerprint[]? _inventory;

    public VortexArchiveOrderWriter(VortexManagerContext context, Func<VortexOrderRequest, VortexOrderResponse> exchange, Func<DateTimeOffset> clock) : this(context, exchange, clock, RunningProcesses) { }

    public VortexArchiveOrderWriter(VortexManagerContext context, Func<VortexOrderRequest, VortexOrderResponse> exchange, Func<DateTimeOffset> clock, Func<IReadOnlyList<string>> runningProcesses)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _exchange = exchange ?? throw new ArgumentNullException(nameof(exchange));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _runningProcesses = runningProcesses ?? throw new ArgumentNullException(nameof(runningProcesses));
    }

    public ArchiveOrderApplyResult Apply(ArchiveOrderPreview preview, IReadOnlyList<ArchiveFingerprint> currentArchives)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(currentArchives);
        if (!_context.DeploymentFresh) throw new ArchiveOrderException("Deploy the active Vortex profile before applying an archive order.");
        if (!SameInventory(preview.Observation.Archives, currentArchives)) throw new ArchiveOrderException("The archive inventory changed after the preview. Scan again before applying.");
        if (_runningProcesses().Contains("Cyberpunk2077", StringComparer.OrdinalIgnoreCase)) throw new ArchiveOrderException("Archive order cannot be written while Cyberpunk2077 is running.");
        DateTimeOffset now = _clock().ToUniversalTime();
        TimeSpan contextAge = now - _context.CapturedAtUtc;
        if (contextAge > TimeSpan.FromSeconds(15) || contextAge < TimeSpan.FromSeconds(-5)) throw new ArchiveOrderException("Open Vortex with the Conflict Studio bridge enabled before applying an archive order.");
        string requestId = Guid.NewGuid().ToString("N");
        VortexOrderRequest request = new(1, requestId, _context.ContextId, _context.ProfileId, now, now.AddSeconds(15), preview.Observation.OrderFileSha256, currentArchives.ToArray(), preview.ProposedOrder);
        VortexOrderResponse response = _exchange(request);
        if (response.SchemaVersion != 1 || !string.Equals(response.RequestId, requestId, StringComparison.Ordinal) || !response.Applied || response.WrittenSha256 is null) throw new ArchiveOrderException(string.IsNullOrWhiteSpace(response.Message) ? "Vortex rejected the archive order." : response.Message);
        _previousOrder = preview.Observation.EffectiveOrder.ToArray();
        _inventory = currentArchives.ToArray();
        if (response.ContextId is not null) _context = _context with { ContextId = response.ContextId };
        return new ArchiveOrderApplyResult(response.BackupPath, true, preview.Observation.OrderFileSha256 is not null, response.WrittenSha256);
    }

    public void RestorePrevious(ArchiveOrderApplyResult result, string targetPath)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        if (_runningProcesses().Contains("Cyberpunk2077", StringComparer.OrdinalIgnoreCase)) throw new ArchiveOrderException("Archive order cannot be written while Cyberpunk2077 is running.");
        if (_previousOrder is null || _inventory is null) throw new ArchiveOrderException("There is no Vortex archive order change to undo.");
        string requestId = Guid.NewGuid().ToString("N");
        DateTimeOffset now = _clock().ToUniversalTime();
        VortexOrderRequest request = new(1, requestId, _context.ContextId, _context.ProfileId, now, now.AddSeconds(15), result.WrittenSha256, _inventory, _previousOrder);
        VortexOrderResponse response = _exchange(request);
        if (response.SchemaVersion != 1 || !string.Equals(response.RequestId, requestId, StringComparison.Ordinal) || !response.Applied || response.WrittenSha256 is null) throw new ArchiveOrderException(string.IsNullOrWhiteSpace(response.Message) ? "Vortex rejected the archive order restore." : response.Message);
        _previousOrder = null;
        _inventory = null;
    }

    private static IReadOnlyList<string> RunningProcesses() => System.Diagnostics.Process.GetProcesses().Select(process => process.ProcessName).ToArray();

    private static bool SameInventory(ArchiveFingerprint[] observed, IReadOnlyList<ArchiveFingerprint> current)
    {
        if (observed.Length != current.Count) return false;
        Dictionary<string, ArchiveFingerprint> values = current.ToDictionary(value => value.Name, StringComparer.OrdinalIgnoreCase);
        return observed.All(value => values.TryGetValue(value.Name, out ArchiveFingerprint? now) && now.Size == value.Size && string.Equals(now.Sha256, value.Sha256, StringComparison.Ordinal));
    }
}

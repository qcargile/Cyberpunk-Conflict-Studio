using ConflictStudio.Core;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace ConflictStudio.App;

public sealed class ArchiveOrderWorkspaceViewModel : INotifyPropertyChanged
{
    private readonly Func<Mo2ArchiveWriteTarget?, IArchiveOrderWriter> _writerFactory;
    private readonly Func<string, string, Mo2ArchiveProfile> _profileScanner;
    private ArchiveOrderObservation? _observation;
    private ArchiveOrderPreview? _preview;
    private Mo2ArchiveProfile? _profile;
    private Mo2ArchiveWriteTarget? _profileTarget;
    private string? _profileManagerRoot;
    private Func<Mo2ArchiveProfile>? _profileRefresh;
    private string? _decisionDirectory;
    private string[] _proposedOrder = [];
    private string[] _baselineOrder = [];
    private bool _canApply;
    private bool _canUndo;
    private bool _canReset;
    private ArchiveOrderApplyResult? _lastApply;
    private IArchiveOrderWriter? _lastWriter;
    private string? _lastTargetPath;
    private ResourceProvider[] _resourceProviders = [];
    private string? _resourceEvidenceProfileId;
    private string? _resourceEvidenceInstallationId;
    private string? _profileInstallationId;
    private ArchiveWinnerDelta[] _winnerDeltas = [];
    private string _previewStatus = "Scan an archive folder to prepare an order change.";
    private int _operationActive;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ArchiveOrderWorkspaceViewModel() : this(CreateWriter, ScanManagedProfile) { }

    public ArchiveOrderWorkspaceViewModel(Func<ArchiveOrderWriter> writerFactory) : this(_ => writerFactory(), ScanManagedProfile) { }

    internal ArchiveOrderWorkspaceViewModel(Func<ArchiveOrderWriter> writerFactory, Func<string, string, Mo2ArchiveProfile> profileScanner) : this(_ => writerFactory(), profileScanner) { }

    internal ArchiveOrderWorkspaceViewModel(Func<Mo2ArchiveWriteTarget?, IArchiveOrderWriter> writerFactory, Func<string, string, Mo2ArchiveProfile> profileScanner)
    {
        _writerFactory = writerFactory ?? throw new ArgumentNullException(nameof(writerFactory));
        _profileScanner = profileScanner ?? throw new ArgumentNullException(nameof(profileScanner));
    }

    public bool CanApply
    {
        get => _canApply;
        private set => Set(ref _canApply, value);
    }

    public string PreviewStatus
    {
        get => _previewStatus;
        private set => Set(ref _previewStatus, value);
    }

    public IReadOnlyList<string> ProposedOrder => _proposedOrder;

    public bool CanReset
    {
        get => _canReset;
        private set => Set(ref _canReset, value);
    }

    public void ClearProfileState()
    {
        _observation = null;
        _preview = null;
        _profile = null;
        _profileTarget = null;
        _profileManagerRoot = null;
        _decisionDirectory = null;
        _proposedOrder = [];
        _baselineOrder = [];
        _resourceProviders = [];
        _resourceEvidenceProfileId = null;
        _resourceEvidenceInstallationId = null;
        _profileInstallationId = null;
        _winnerDeltas = [];
        ClearUndo();
        CanApply = false;
        CanReset = false;
        PreviewStatus = "Run the complete profile scan before changing archive order.";
        OnPropertyChanged(nameof(ProposedOrder));
        OnPropertyChanged(nameof(WinnerDeltas));
    }

    public bool CanUndo
    {
        get => _canUndo;
        private set => Set(ref _canUndo, value);
    }

    public IReadOnlyList<ArchiveWinnerDelta> WinnerDeltas => _winnerDeltas;

    public void LoadProfile(Mo2ArchiveProfile profile, Mo2ArchiveWriteTarget target, string mo2Root, Func<Mo2ArchiveProfile>? profileRefresh = null, string? installationId = null, bool preserveUndo = false)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(target);
        bool keepUndo = preserveUndo && CanUndo && _profile is not null && _profileTarget is not null
            && string.Equals(_profile.ProfileName, profile.ProfileName, StringComparison.Ordinal)
            && string.Equals(Path.GetFullPath(_profileTarget.ModlistPath), Path.GetFullPath(target.ModlistPath), StringComparison.OrdinalIgnoreCase)
            && _profileTarget.ManagerKind == target.ManagerKind;
        _profile = profile;
        _profileTarget = target;
        _profileManagerRoot = Path.GetFullPath(mo2Root);
        _profileInstallationId = installationId ?? ProfileInstallationIdentity.Create(mo2Root);
        _profileRefresh = profileRefresh ?? (() => _profileScanner(_profileManagerRoot, profile.ProfileModlistPath));
        if (!keepUndo) ClearUndo();
        _resourceEvidenceProfileId = null;
        _resourceEvidenceInstallationId = null;
        _resourceProviders = [];
        _observation = ManagedArchiveOrderObserver.Observe(profile, target, target.WriteBlockedReason is not null);
        _decisionDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cyberpunk Conflict Studio");
        _baselineOrder = _observation.EffectiveOrder.ToArray();
        _proposedOrder = _baselineOrder.ToArray();
        _preview = null;
        CanApply = false;
        CanReset = false;
        string manager = target.ManagerKind switch { ModManagerKind.Vortex => "Vortex", ModManagerKind.Manual => "deployed game", _ => "MO2" };
        PreviewStatus = $"This is the enabled {manager} profile order. Preview it before applying to {target.Provider}.";
        OnPropertyChanged(nameof(ProposedOrder));
    }

    public void SetProposedOrder(IReadOnlyList<string> order)
    {
        ArgumentNullException.ThrowIfNull(order);
        _proposedOrder = order.ToArray();
        _preview = null;
        _winnerDeltas = [];
        CanApply = false;
        CanReset = _baselineOrder.Length > 0 && !_proposedOrder.SequenceEqual(_baselineOrder, StringComparer.OrdinalIgnoreCase);
        PreviewStatus = CanReset ? "Order changed. Preview it before applying." : "The proposed order matches the scanned order.";
        OnPropertyChanged(nameof(ProposedOrder));
        OnPropertyChanged(nameof(WinnerDeltas));
    }

    public void ResetProposedOrder()
    {
        if (_baselineOrder.Length == 0) return;
        _proposedOrder = _baselineOrder.ToArray();
        _preview = null;
        _winnerDeltas = [];
        CanApply = false;
        CanReset = false;
        PreviewStatus = "Proposed changes were reset to the scanned order.";
        OnPropertyChanged(nameof(ProposedOrder));
        OnPropertyChanged(nameof(WinnerDeltas));
    }

    public void SetResourceProviders(string profileId, IReadOnlyList<ResourceProvider> providers) => SetResourceProviders(null, profileId, providers);

    public void SetResourceProviders(string? installationId, string profileId, IReadOnlyList<ResourceProvider> providers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(providers);
        _resourceEvidenceProfileId = profileId;
        _resourceEvidenceInstallationId = installationId;
        _resourceProviders = providers.ToArray();
    }

    public void Move(int index, int offset)
    {
        int destination = index + offset;
        if (index < 0 || index >= _proposedOrder.Length || destination < 0 || destination >= _proposedOrder.Length) return;
        string[] order = _proposedOrder.ToArray();
        (order[index], order[destination]) = (order[destination], order[index]);
        SetProposedOrder(order);
    }

    public void MoveTo(int sourceIndex, int destinationIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= _proposedOrder.Length || destinationIndex < 0 || destinationIndex >= _proposedOrder.Length || sourceIndex == destinationIndex) return;
        List<string> order = _proposedOrder.ToList();
        string archive = order[sourceIndex];
        order.RemoveAt(sourceIndex);
        order.Insert(destinationIndex, archive);
        SetProposedOrder(order);
    }

    public void MoveMany(IReadOnlyList<string> archives, string target)
    {
        ArgumentNullException.ThrowIfNull(archives);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        HashSet<string> selected = archives.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0 || selected.Contains(target)) return;
        int targetPosition = Array.FindIndex(_proposedOrder, value => string.Equals(value, target, StringComparison.OrdinalIgnoreCase));
        int[] selectedPositions = _proposedOrder.Select((value, index) => (value, index)).Where(value => selected.Contains(value.value)).Select(value => value.index).ToArray();
        if (targetPosition < 0 || selectedPositions.Length == 0) return;
        bool moveDown = selectedPositions.All(value => value < targetPosition);
        bool moveUp = selectedPositions.All(value => value > targetPosition);
        if (!moveDown && !moveUp) return;
        string[] moving = _proposedOrder.Where(selected.Contains).ToArray();
        if (moving.Length == 0) return;
        List<string> remaining = _proposedOrder.Where(value => !selected.Contains(value)).ToList();
        int destination = remaining.FindIndex(value => string.Equals(value, target, StringComparison.OrdinalIgnoreCase));
        if (destination < 0) return;
        if (moveDown) destination++;
        remaining.InsertRange(destination, moving);
        SetProposedOrder(remaining);
    }

    public void PreviewOrder()
    {
        if (_observation is null) throw new ArchiveOrderException("Scan an archive folder before previewing an order.");
        if (!string.Equals(_resourceEvidenceProfileId, _observation.ProfileId, StringComparison.Ordinal) || _profile is not null && !string.Equals(_resourceEvidenceInstallationId, _profileInstallationId, StringComparison.Ordinal))
        {
            _preview = null;
            CanApply = false;
            PreviewStatus = "Packed-resource evidence is unavailable for this profile. Run the complete profile scan before changing archive order.";
            return;
        }
        _preview = ArchiveOrderPlanner.CreatePreview(_observation, _proposedOrder);
        _winnerDeltas = ArchiveOrderImpactAnalyzer.Analyze(_resourceProviders, _observation.EffectiveOrder, _proposedOrder);
        OnPropertyChanged(nameof(WinnerDeltas));
        if (_preview.ChangedArchives.Length == 0)
        {
            _preview = null;
            _winnerDeltas = [];
            CanApply = false;
            CanReset = false;
            PreviewStatus = "The proposed order matches the scanned order.";
            OnPropertyChanged(nameof(WinnerDeltas));
            return;
        }
        if (_profileTarget?.WriteBlockedReason is not null)
        {
            CanApply = false;
            PreviewStatus = _profileTarget.WriteBlockedReason;
            return;
        }
        int changed = _preview.ChangedArchives.Length;
        string positions = changed == 1 ? "1 archive position" : $"{changed} archive positions";
        string winners = _winnerDeltas.Length == 1 ? "1 contested resource winner" : $"{_winnerDeltas.Length} contested resource winners";
        PreviewStatus = $"This changes {positions} and {winners}. Review both before applying.";
        CanApply = true;
    }

    public void BlockPreview(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        _preview = null;
        _winnerDeltas = [];
        CanApply = false;
        PreviewStatus = reason;
        OnPropertyChanged(nameof(WinnerDeltas));
    }

    public void ApplyOrder()
    {
        EnterOperation();
        try { ApplyOrderCore(); }
        finally { ExitOperation(); }
    }

    private void ApplyOrderCore()
    {
        if (_preview is null || _observation is null || _decisionDirectory is null) throw new ArchiveOrderException("Preview an archive order before applying it.");
        if (_profileTarget?.WriteBlockedReason is not null) throw new ArchiveOrderException(_profileTarget.WriteBlockedReason);
        RefreshAndRequireWriteTarget();
        IArchiveOrderWriter writer = _writerFactory(_profileTarget);
        ArchiveOrderApplyResult result = writer.Apply(_preview, CurrentArchiveFingerprints());
        _lastWriter = writer;
        _lastApply = result;
        _lastTargetPath = Path.Combine(_observation.DirectoryPath, "modlist.txt");
        CanUndo = true;
        if (_profile is not null && _profileTarget is not null)
        {
            try
            {
                if (_profileTarget.ManagerKind == ModManagerKind.Vortex)
                {
                    if (_profileRefresh is null) throw new ArchiveOrderException("The manager profile refresh is unavailable for post-write verification.");
                    _profile = _profileRefresh();
                }
                _observation = ManagedArchiveOrderObserver.Observe(_profile, _profileTarget);
                if (!_observation.EffectiveOrder.SequenceEqual(_proposedOrder, StringComparer.OrdinalIgnoreCase)) throw new ArchiveOrderException("The managed archive order changed during post-write verification.");
            }
            catch (Exception exception)
            {
                try { writer.RestorePrevious(result, _profileTarget.ModlistPath); }
                catch (Exception rollbackException) { throw new ArchiveOrderException($"Post-write verification failed: {exception.Message} Automatic rollback also failed: {rollbackException.Message}"); }
                if (_profileRefresh is not null)
                {
                    _profile = _profileRefresh();
                    _observation = ManagedArchiveOrderObserver.Observe(_profile, _profileTarget);
                    _proposedOrder = _observation.EffectiveOrder.ToArray();
                    _baselineOrder = _proposedOrder.ToArray();
                    _preview = null;
                    _winnerDeltas = [];
                    CanApply = false;
                    CanReset = false;
                    PreviewStatus = "Post-write verification failed. The previous archive order was restored.";
                    OnPropertyChanged(nameof(ProposedOrder));
                    OnPropertyChanged(nameof(WinnerDeltas));
                }
                ClearUndo();
                throw new ArchiveOrderException($"Post-write verification failed and the previous archive order was restored: {exception.Message}");
            }
            _preview = null;
            CanApply = false;
            CanReset = false;
            _baselineOrder = _observation.EffectiveOrder.ToArray();
            _winnerDeltas = [];
            OnPropertyChanged(nameof(WinnerDeltas));
            PreviewStatus = $"The managed {_profileTarget.ManagerKind} order was written and verified.";
        }
        else throw new ArchiveOrderException("The managed profile is unavailable after applying the archive order.");
    }

    public void UndoLastApply()
    {
        EnterOperation();
        try { UndoLastApplyCore(); }
        finally { ExitOperation(); }
    }

    private void UndoLastApplyCore()
    {
        if (_lastApply is null || _lastTargetPath is null) throw new ArchiveOrderException("There is no archive order change to undo.");
        if (_lastWriter is null) throw new ArchiveOrderException("The archive order writer is unavailable for undo.");
        RefreshAndRequireWriteTarget();
        _lastWriter.RestorePrevious(_lastApply, _lastTargetPath);
        if (_profile is not null && _profileTarget is not null)
        {
            if (_profileTarget.ManagerKind == ModManagerKind.Vortex)
            {
                if (_profileRefresh is null) throw new ArchiveOrderException("The manager profile refresh is unavailable after undo.");
                _profile = _profileRefresh();
            }
            _observation = ManagedArchiveOrderObserver.Observe(_profile, _profileTarget);
            _proposedOrder = _observation.EffectiveOrder.ToArray();
            _baselineOrder = _proposedOrder.ToArray();
            OnPropertyChanged(nameof(ProposedOrder));
        }
        else throw new ArchiveOrderException("The managed profile is unavailable after restoring the archive order.");
        _lastApply = null;
        _lastTargetPath = null;
        _lastWriter = null;
        CanUndo = false;
        CanApply = false;
        CanReset = false;
        PreviewStatus = "Undo complete. Preview any new order before applying.";
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private ArchiveFingerprint[] CurrentArchiveFingerprints()
    {
        if (_profile is not null && _profileTarget?.ManagerKind == ModManagerKind.Manual)
        {
            string directory = Path.GetDirectoryName(_profileTarget.ModlistPath)!;
            return Directory.EnumerateFiles(directory, "*.archive", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName, StringComparer.Ordinal).Select(path =>
            {
                using FileStream stream = File.OpenRead(path);
                return new ArchiveFingerprint(Path.GetFileName(path), stream.Length, Convert.ToHexStringLower(SHA256.HashData(stream)));
            }).ToArray();
        }
        if (_profile is not null) return _profile.Archives.Select(value => new ArchiveFingerprint(value.ArchiveName, value.Size, value.Sha256)).ToArray();
        if (_observation is null) return [];
        return Directory.EnumerateFiles(_observation.DirectoryPath, "*.archive", SearchOption.TopDirectoryOnly).Select(path =>
        {
            using FileStream stream = File.OpenRead(path);
            return new ArchiveFingerprint(Path.GetFileName(path), stream.Length, Convert.ToHexStringLower(SHA256.HashData(stream)));
        }).ToArray();
    }

    private void RefreshAndRequireWriteTarget()
    {
        if (_profile is null || _profileTarget is null || _profileRefresh is null) return;
        if (_profileTarget.ManagerKind == ModManagerKind.Manual) return;
        if (_profileTarget.ManagerKind == ModManagerKind.Vortex)
        {
            if (_profileTarget.ContextPath is null) throw new ArchiveOrderException("The Vortex manager context is unavailable.");
            VortexManagerContext context = VortexManagerContextStore.Read(_profileTarget.ContextPath);
            Mo2ArchiveWriteTarget vortexTarget = VortexArchiveWriteTargetResolver.Resolve(_profileTarget.ContextPath, context);
            RequireSameWriteTarget(_profileTarget, vortexTarget);
            _profileTarget = vortexTarget;
            return;
        }
        Mo2ArchiveProfile currentProfile = _profileRefresh();
        Mo2ArchiveWriteTarget currentTarget = Mo2ArchiveWriteTargetResolver.Resolve(_profileManagerRoot!, currentProfile.OrderEvidence);
        RequireSameWriteTarget(_profileTarget, currentTarget);
        _profile = currentProfile;
        _profileTarget = currentTarget;
    }

    private static void RequireSameWriteTarget(Mo2ArchiveWriteTarget expectedTarget, Mo2ArchiveWriteTarget currentTarget)
    {
        bool sameOwner = currentTarget.ManagerKind == expectedTarget.ManagerKind
            && string.Equals(Path.GetFullPath(currentTarget.ModlistPath), Path.GetFullPath(expectedTarget.ModlistPath), StringComparison.OrdinalIgnoreCase)
            && string.Equals(currentTarget.Provider, expectedTarget.Provider, StringComparison.OrdinalIgnoreCase)
            && string.Equals(currentTarget.ExpectedContextId, expectedTarget.ExpectedContextId, StringComparison.Ordinal)
            && string.Equals(currentTarget.ExpectedProfileId, expectedTarget.ExpectedProfileId, StringComparison.Ordinal);
        if (!sameOwner) throw new ArchiveOrderException("The active archive-order owner changed after the preview. Scan again before applying or undoing an order.");
        if (currentTarget.WriteBlockedReason is not null) throw new ArchiveOrderException(currentTarget.WriteBlockedReason);
    }

    private void ClearUndo()
    {
        _lastApply = null;
        _lastTargetPath = null;
        _lastWriter = null;
        CanUndo = false;
    }

    private void EnterOperation()
    {
        if (Interlocked.CompareExchange(ref _operationActive, 1, 0) != 0) throw new ArchiveOrderException("Another archive order operation is already in progress.");
    }

    private void ExitOperation() => Volatile.Write(ref _operationActive, 0);

    private static Mo2ArchiveProfile ScanManagedProfile(string mo2Root, string profileModlistPath) => Mo2ArchiveProfileScanner.ScanInstance(mo2Root, profileModlistPath, null, true);

    private static IArchiveOrderWriter CreateWriter(Mo2ArchiveWriteTarget? target)
    {
        if (target is { ManagerKind: ModManagerKind.Vortex, ContextPath: not null })
        {
            VortexManagerContext context = VortexManagerContextStore.Read(target.ContextPath);
            if (!string.Equals(context.ContextId, target.ExpectedContextId, StringComparison.Ordinal) || !string.Equals(context.ProfileId, target.ExpectedProfileId, StringComparison.Ordinal)) throw new ArchiveOrderException("The active Vortex profile or deployment changed after the scan. Scan again before applying.");
            VortexOrderBridgeStore store = new(Path.GetDirectoryName(target.ContextPath)!);
            return new VortexArchiveOrderWriter(context, request => store.Exchange(request, TimeSpan.FromSeconds(15)), () => DateTimeOffset.UtcNow);
        }
        return new ArchiveOrderWriter(() => DateTimeOffset.UtcNow);
    }
}

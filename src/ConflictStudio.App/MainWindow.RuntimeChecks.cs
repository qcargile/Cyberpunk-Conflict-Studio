using ConflictStudio.Core;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ConflictStudio.App;

internal sealed record RuntimeRequestPresentation(string RequestId, string RequestedObservation, string Decides, string Execution, string State, string Result);

public partial class MainWindow
{
    private RuntimeInvestigationStore _runtimeStore = null!;
    private CancellationTokenSource? _runtimeCancellation;
    private RuntimeInvestigationView[] _runtimeViews = [];
    private RuntimeProbeManifest? _preparedRuntimeManifest;
    private ConflictWorkItem? _preparedRuntimeItem;
    private Task _runtimeLoadTask = Task.CompletedTask;
    private Task _runtimeWriteTask = Task.CompletedTask;
    private int _runtimeRevision;
    private bool _runtimeBusy;
    private bool _runtimeLoading;
    private bool _runtimeWriting;

    internal Task RuntimeChecksReady => Task.WhenAll(_runtimeLoadTask, _runtimeWriteTask);

    private void InitializeRuntimeChecks(string applicationData)
    {
        _runtimeStore = new RuntimeInvestigationStore(Path.Combine(applicationData, "runtime"));
    }

    private void BeginRuntimeChecks(ProfileScanReceipt receipt)
    {
        _runtimeRevision++;
        _runtimeCancellation?.Cancel();
        _runtimeLoading = true;
        _runtimeBusy = false;
        _runtimeViews = [];
        _preparedRuntimeManifest = null;
        _preparedRuntimeItem = null;
        RuntimeRunsDataGrid.ItemsSource = null;
        RuntimeRequestsDataGrid.ItemsSource = null;
        CheckInGameButton.IsEnabled = GenerateRuntimePackageButton.IsEnabled = ImportRuntimeResultsButton.IsEnabled = OpenRuntimePackageButton.IsEnabled = OpenRuntimeInstructionsButton.IsEnabled = ForgetRuntimeRunButton.IsEnabled = false;
        RuntimeRunStatusTextBlock.Text = "Reading runtime checks for this profile…";
        CancellationTokenSource cancellation = new();
        _runtimeCancellation = cancellation;
        _runtimeLoadTask = LoadRuntimeChecksAsync(receipt, _runtimeRevision, cancellation);
        UpdateSupportExportAvailability();
    }

    private async Task LoadRuntimeChecksAsync(ProfileScanReceipt receipt, int revision, CancellationTokenSource cancellation)
    {
        ConflictWorkItem[] workItems = _workItems;
        try
        {
            try { await _runtimeWriteTask.WaitAsync(cancellation.Token); }
            catch (Exception) when (_runtimeWriteTask.IsFaulted) { }
            RuntimeInvestigationView[] views = await Task.Run(() => _runtimeStore.Load(receipt, workItems), cancellation.Token);
            if (!IsCurrentRuntimeContext(receipt, revision)) return;
            _runtimeViews = CurrentProfileRuns(views, receipt);
            RuntimeRunsDataGrid.ItemsSource = _runtimeViews;
            RuntimeRunsDataGrid.SelectedIndex = _runtimeViews.Length > 0 ? 0 : -1;
            if (_runtimeStore.LastRecoveryPath is string recovery)
            {
                RuntimeRunStatusTextBlock.Text = $"Unreadable runtime-check state was preserved as {Path.GetFileName(recovery)}.";
                RecordAction("runtime-check-state", "recovered", RuntimeRunStatusTextBlock.Text);
            }
            else if (_runtimeViews.Length == 0) RuntimeRunStatusTextBlock.Text = "No generated runs for this profile.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _diagnostics.TryWrite("runtime-check-load", exception);
            if (IsCurrentRuntimeContext(receipt, revision)) RuntimeRunStatusTextBlock.Text = "Runtime checks could not be loaded. Current scan results are unchanged.";
        }
        finally
        {
            if (ReferenceEquals(_runtimeCancellation, cancellation)) _runtimeCancellation = null;
            cancellation.Dispose();
            if (IsCurrentRuntimeContext(receipt, revision))
            {
                _runtimeLoading = false;
                RuntimeFindingSelectionChanged(WorkQueueDataGrid.SelectedItems.Cast<ConflictWorkItem>().ToArray());
                UpdateSupportExportAvailability();
            }
        }
    }

    private static RuntimeInvestigationView[] CurrentProfileRuns(IEnumerable<RuntimeInvestigationView> views, ProfileScanReceipt receipt)
        => views.Where(value => value.Run.Manifest.Binding is { } binding
            && binding.ManagerKind == receipt.ManagerKind
            && string.Equals(binding.InstallationId, receipt.InstallationId, StringComparison.Ordinal)
            && string.Equals(binding.ProfileName, receipt.ProfileName, StringComparison.Ordinal))
            .ToArray();

    private bool IsCurrentRuntimeContext(ProfileScanReceipt receipt, int revision)
        => !_investigationClosed && revision == _runtimeRevision && ReferenceEquals(receipt, _receipt);

    private void CheckInGameClicked(object sender, RoutedEventArgs e)
    {
        _runtimeLoadTask = PrepareSelectedRuntimeCheckAsync();
    }

    internal async Task PrepareSelectedRuntimeCheckAsync()
    {
        ProfileScanReceipt? receipt = _receipt;
        ConflictWorkItem? selected = WorkQueueDataGrid.SelectedItems.Cast<ConflictWorkItem>().SingleOrDefault();
        if (receipt?.InstallationId is null || selected is null || _runtimeBusy) return;
        int revision = _runtimeRevision;
        SetRuntimeBusy(true);
        MainTabControl.SelectedIndex = 4;
        RuntimeSummaryTextBlock.Text = $"Preparing supported observations for {selected.Target}…";
        try
        {
            RuntimeProbeManifest manifest = await Task.Run(() => RuntimeProbeManifestBuilder.Build(receipt, selected, DateTimeOffset.UtcNow));
            if (!IsCurrentRuntimeContext(receipt, revision) || WorkQueueDataGrid.SelectedItems.Cast<ConflictWorkItem>().SingleOrDefault() is not ConflictWorkItem current || !SameRuntimeFinding(selected, current)) return;
            _preparedRuntimeManifest = manifest;
            _preparedRuntimeItem = selected;
            RuntimeRunsDataGrid.SelectedIndex = -1;
            RuntimeRequestsDataGrid.ItemsSource = manifest.Requests.Select(PreparedRequest).ToArray();
            GenerateRuntimePackageButton.IsEnabled = manifest.Requests.Length > 0;
            RuntimeSummaryTextBlock.Text = manifest.Requests.Length == 0
                ? "This finding has no supported runtime observation. Its source evidence remains available in Code interactions."
                : $"{manifest.Requests.Length:N0} observation{(manifest.Requests.Length == 1 ? string.Empty : "s")} prepared for {selected.Target}. Generate the package only if you want to run this optional check.";
        }
        catch (Exception exception)
        {
            if (IsCurrentRuntimeContext(receipt, revision))
            {
                _preparedRuntimeManifest = null;
                _preparedRuntimeItem = null;
                GenerateRuntimePackageButton.IsEnabled = false;
                RuntimeRequestsDataGrid.ItemsSource = null;
                ShowError("runtime-check-prepare", exception);
            }
        }
        finally { if (IsCurrentRuntimeContext(receipt, revision)) SetRuntimeBusy(false); }
    }

    private async void GenerateRuntimePackageClicked(object sender, RoutedEventArgs e)
    {
        if (_receipt is null) return;
        string profile = SafeRuntimePathPart(_receipt.ProfileName);
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Cyberpunk Conflict Studio Runtime Checks", profile, DateTime.Now.ToString("yyyy-MM-dd HHmmss", System.Globalization.CultureInfo.InvariantCulture) + " " + Guid.NewGuid().ToString("N")[..8]);
        try { await GenerateRuntimePackageAsync(directory); }
        catch (Exception exception) { if (!_investigationClosed) ShowError("runtime-check-generate", exception); }
    }

    internal async Task GenerateRuntimePackageAsync(string packageDirectory)
    {
        ProfileScanReceipt? receipt = _receipt;
        ConflictWorkItem? selected = _preparedRuntimeItem;
        if (receipt?.InstallationId is null || selected is null || _preparedRuntimeManifest?.Requests.Length == 0 || _runtimeBusy) return;
        int revision = _runtimeRevision;
        SetRuntimeBusy(true);
        RuntimeRunStatusTextBlock.Text = "Generating the selected runtime-check package…";
        try
        {
            RuntimeInvestigationRun run = await QueueRuntimeWrite(() => _runtimeStore.GeneratePackage(packageDirectory, receipt, selected, DateTimeOffset.UtcNow));
            if (!IsCurrentRuntimeContext(receipt, revision)) return;
            await ReloadRuntimeViewsAsync(receipt, revision, run.Manifest.RunId);
            RuntimeRunStatusTextBlock.Text = "Package generated. Follow README.txt, import the matching CET log, then remove or disable the separate probe mod.";
        }
        finally { if (IsCurrentRuntimeContext(receipt, revision)) SetRuntimeBusy(false); }
    }

    private async void ImportRuntimeResultsClicked(object sender, RoutedEventArgs e)
    {
        OpenFileDialog logPicker = new() { Title = "Choose the matching ConflictStudioProbe CET log", Filter = "Log files|*.log;*.txt|All files|*.*" };
        if (logPicker.ShowDialog(this) != true) return;
        string? answers = null;
        RuntimeInvestigationView? selected = RuntimeRunsDataGrid.SelectedItem as RuntimeInvestigationView;
        if (selected?.Run.Manifest.Requests.Any(value => value.Execution == RuntimeProbeExecution.Manual) == true && MessageBox.Show(this, "Import a completed manual answers JSON file too?", "Optional manual answers", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            OpenFileDialog answersPicker = new() { Title = "Choose the edited manual answers file", Filter = "JSON files|*.json" };
            if (answersPicker.ShowDialog(this) != true) return;
            answers = answersPicker.FileName;
        }
        try { await ImportRuntimeResultsAsync(logPicker.FileName, answers); }
        catch (Exception exception) { if (!_investigationClosed) ShowError("runtime-check-import", exception); }
    }

    internal async Task ImportRuntimeResultsAsync(string logPath, string? manualAnswersPath)
    {
        ProfileScanReceipt? receipt = _receipt;
        RuntimeInvestigationView? selected = RuntimeRunsDataGrid.SelectedItem as RuntimeInvestigationView;
        if (receipt?.InstallationId is null || selected is null || selected.Freshness != RuntimeInvestigationFreshness.Current || _runtimeBusy) return;
        int revision = _runtimeRevision;
        string manifestPath = Path.Combine(selected.Run.PackageDirectory, "probe-manifest.json");
        SetRuntimeBusy(true);
        RuntimeRunStatusTextBlock.Text = "Importing the matching runtime results…";
        try
        {
            RuntimeInvestigationRun run = await QueueRuntimeWrite(() => _runtimeStore.Import(manifestPath, logPath, manualAnswersPath, DateTimeOffset.UtcNow));
            if (!IsCurrentRuntimeContext(receipt, revision) || run.Manifest.RunId != selected.Run.Manifest.RunId) return;
            await ReloadRuntimeViewsAsync(receipt, revision, run.Manifest.RunId);
            RuntimeRunStatusTextBlock.Text = run.Receipt?.CompleteRun == true
                ? "Matching results imported. These values describe one recorded moment and do not establish later gameplay state or mod compatibility."
                : "The selected log did not contain a complete matching run. Requests remain missing until a complete matching log is imported.";
        }
        finally { if (IsCurrentRuntimeContext(receipt, revision)) SetRuntimeBusy(false); }
    }

    private async void ForgetRuntimeRunClicked(object sender, RoutedEventArgs e)
    {
        try { await ForgetSelectedRuntimeRunAsync(); }
        catch (Exception exception) { if (!_investigationClosed) ShowError("runtime-check-forget", exception); }
    }

    internal async Task ForgetSelectedRuntimeRunAsync()
    {
        ProfileScanReceipt? receipt = _receipt;
        RuntimeInvestigationView? selected = RuntimeRunsDataGrid.SelectedItem as RuntimeInvestigationView;
        if (receipt?.InstallationId is null || selected is null || _runtimeBusy) return;
        int revision = _runtimeRevision;
        SetRuntimeBusy(true);
        try
        {
            await QueueRuntimeWrite(() => { _runtimeStore.Remove(selected.Run.Manifest.RunId); return true; });
            if (!IsCurrentRuntimeContext(receipt, revision)) return;
            await ReloadRuntimeViewsAsync(receipt, revision, null);
            RuntimeRunStatusTextBlock.Text = "The local run record was forgotten. The generated package was not deleted; remove or disable it through your mod manager.";
        }
        finally { if (IsCurrentRuntimeContext(receipt, revision)) SetRuntimeBusy(false); }
    }

    private async Task ReloadRuntimeViewsAsync(ProfileScanReceipt receipt, int revision, string? selectRunId)
    {
        ConflictWorkItem[] workItems = _workItems;
        RuntimeInvestigationView[] views = await Task.Run(() => _runtimeStore.Load(receipt, workItems));
        if (!IsCurrentRuntimeContext(receipt, revision)) return;
        _runtimeViews = CurrentProfileRuns(views, receipt);
        RuntimeRunsDataGrid.ItemsSource = _runtimeViews;
        RuntimeRunsDataGrid.SelectedItem = selectRunId is null ? null : _runtimeViews.FirstOrDefault(value => value.Run.Manifest.RunId == selectRunId);
        if (RuntimeRunsDataGrid.SelectedItem is null && _runtimeViews.Length > 0) RuntimeRunsDataGrid.SelectedIndex = 0;
        if (_runtimeViews.Length == 0) RuntimeRequestsDataGrid.ItemsSource = null;
    }

    private async Task<T> QueueRuntimeWrite<T>(Func<T> write)
    {
        Task previous = _runtimeWriteTask;
        async Task<T> ExecuteWrite()
        {
            try { await previous; }
            catch (Exception) when (previous.IsFaulted) { }
            return await Task.Run(write);
        }
        Task<T> current = ExecuteWrite();
        _runtimeWriteTask = current;
        _runtimeWriting = true;
        UpdateSupportExportAvailability();
        try { return await current; }
        finally
        {
            if (ReferenceEquals(_runtimeWriteTask, current)) _runtimeWriting = false;
            UpdateSupportExportAvailability();
        }
    }

    private void RuntimeRunSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RuntimeInvestigationView? selected = RuntimeRunsDataGrid.SelectedItem as RuntimeInvestigationView;
        UpdateRuntimeRunActions(selected);
    }

    private void UpdateRuntimeRunActions(RuntimeInvestigationView? selected)
    {
        if (selected is null)
        {
            ImportRuntimeResultsButton.IsEnabled = OpenRuntimePackageButton.IsEnabled = OpenRuntimeInstructionsButton.IsEnabled = ForgetRuntimeRunButton.IsEnabled = false;
            if (_preparedRuntimeManifest is null) RuntimeRequestsDataGrid.ItemsSource = null;
            return;
        }
        _preparedRuntimeManifest = null;
        _preparedRuntimeItem = null;
        GenerateRuntimePackageButton.IsEnabled = false;
        ImportRuntimeResultsButton.IsEnabled = !_runtimeBusy && selected.Freshness == RuntimeInvestigationFreshness.Current;
        RuntimeSummaryTextBlock.Text = selected.Run.Manifest.Binding!.Target;
        OpenRuntimePackageButton.IsEnabled = !_runtimeBusy && Directory.Exists(selected.Run.PackageDirectory);
        OpenRuntimeInstructionsButton.IsEnabled = !_runtimeBusy && File.Exists(Path.Combine(selected.Run.PackageDirectory, "README.txt"));
        ForgetRuntimeRunButton.IsEnabled = !_runtimeBusy;
        RuntimeRequestsDataGrid.ItemsSource = selected.Run.Manifest.Requests.Select(request => PresentRequest(request, selected.Run.Receipt)).ToArray();
        RuntimeRunStatusTextBlock.Text = selected.Freshness == RuntimeInvestigationFreshness.Stale
            ? selected.StaleReason ?? "This run is stale. Generate a new check for the current source evidence."
            : selected.Run.Receipt is null ? "Package generated. No results have been imported yet." : selected.Run.Receipt.EvidenceBoundary;
    }

    private static RuntimeRequestPresentation PreparedRequest(RuntimeProbeRequest request)
        => new(string.Empty, request.Observation, request.Decides, request.Kind == RuntimeProbeKind.PostInitializationTweakValue ? "Automatic" : "Manual", "Ready to generate", string.Empty);

    private static RuntimeRequestPresentation PresentRequest(RuntimeProbeBundleRequest request, RuntimeProbeReceipt? receipt)
    {
        RuntimeProbeObservation? observation = receipt?.Observations.FirstOrDefault(value => value.Id == request.Id);
        string state = observation?.State switch
        {
            RuntimeProbeObservationState.Observed => "Observed",
            RuntimeProbeObservationState.Failed => "Failed",
            RuntimeProbeObservationState.ManualRequired => "Manual required",
            RuntimeProbeObservationState.ManualRecorded => "Manual recorded",
            RuntimeProbeObservationState.Missing => "Missing",
            _ => "Not imported"
        };
        return new RuntimeRequestPresentation(request.Id, request.Request.Observation, request.Request.Decides, request.Execution == RuntimeProbeExecution.Automated ? "Automatic" : "Manual", state, observation?.Value ?? observation?.Message ?? string.Empty);
    }

    private void SetRuntimeBusy(bool busy)
    {
        _runtimeBusy = busy;
        RuntimeRunsDataGrid.IsEnabled = !busy;
        RuntimeRequestsDataGrid.IsEnabled = !busy;
        CheckInGameButton.IsEnabled = !busy && WorkQueueDataGrid.SelectedItems.Count == 1;
        GenerateRuntimePackageButton.IsEnabled = !busy && _preparedRuntimeManifest?.Requests.Length > 0;
        UpdateRuntimeRunActions(RuntimeRunsDataGrid.SelectedItem as RuntimeInvestigationView);
        UpdateSupportExportAvailability();
    }

    private void RuntimeFindingSelectionChanged(ConflictWorkItem[] selected)
    {
        CheckInGameButton.IsEnabled = !_runtimeBusy && selected.Length == 1;
        if (_preparedRuntimeItem is not null && (selected.Length != 1 || !SameRuntimeFinding(_preparedRuntimeItem, selected[0])))
        {
            _preparedRuntimeManifest = null;
            _preparedRuntimeItem = null;
            GenerateRuntimePackageButton.IsEnabled = false;
            if (RuntimeRunsDataGrid.SelectedItem is null) RuntimeRequestsDataGrid.ItemsSource = null;
        }
    }

    private void OpenRuntimePackageClicked(object sender, RoutedEventArgs e)
    {
        Execute("open-runtime-package", () =>
        {
            if (RuntimeRunsDataGrid.SelectedItem is not RuntimeInvestigationView selected || !Directory.Exists(selected.Run.PackageDirectory)) throw new DirectoryNotFoundException("The generated runtime-check package folder is no longer available.");
            Process.Start(new ProcessStartInfo("explorer.exe", selected.Run.PackageDirectory) { UseShellExecute = true });
        });
    }

    private void OpenRuntimeInstructionsClicked(object sender, RoutedEventArgs e)
    {
        Execute("open-runtime-instructions", () =>
        {
            if (RuntimeRunsDataGrid.SelectedItem is not RuntimeInvestigationView selected) return;
            string path = Path.Combine(selected.Run.PackageDirectory, "README.txt");
            if (!File.Exists(path)) throw new FileNotFoundException("The generated runtime-check instructions are no longer available.", path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        });
    }

    private void ClearRuntimeChecks()
    {
        _runtimeRevision++;
        _runtimeCancellation?.Cancel();
        _runtimeLoading = false;
        _runtimeBusy = false;
        _runtimeViews = [];
        _preparedRuntimeManifest = null;
        _preparedRuntimeItem = null;
        if (RuntimeRunsDataGrid is null) return;
        RuntimeRunsDataGrid.ItemsSource = null;
        RuntimeRequestsDataGrid.ItemsSource = null;
        CheckInGameButton.IsEnabled = GenerateRuntimePackageButton.IsEnabled = ImportRuntimeResultsButton.IsEnabled = OpenRuntimePackageButton.IsEnabled = OpenRuntimeInstructionsButton.IsEnabled = ForgetRuntimeRunButton.IsEnabled = false;
        RuntimeSummaryTextBlock.Text = "Select one code finding and choose Check in game. Nothing is installed or launched automatically.";
        RuntimeRunStatusTextBlock.Text = "No generated runs for this profile.";
    }

    private void DisposeRuntimeChecks()
    {
        _runtimeRevision++;
        _runtimeCancellation?.Cancel();
    }

    private void UpdateSupportExportAvailability()
    {
        if (ExportButton is null) return;
        ExportButton.IsEnabled = _receipt is not null && !_historyBusy && _noteSaveTask.IsCompleted && !_runtimeLoading && !_runtimeWriting && !_runtimeBusy;
    }

    private static bool SameRuntimeFinding(ConflictWorkItem first, ConflictWorkItem second)
        => first.Surface == second.Surface && string.Equals(first.Target, second.Target, StringComparison.Ordinal) && string.Equals(first.EvidenceSha256, second.EvidenceSha256, StringComparison.Ordinal);

    private static string SafeRuntimePathPart(string value)
        => string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
}

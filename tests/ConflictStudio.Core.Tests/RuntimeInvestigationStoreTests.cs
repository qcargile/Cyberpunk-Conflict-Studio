using ConflictStudio.Core;
using System.Text.Json.Nodes;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class RuntimeInvestigationStoreTests
{
    [TestMethod]
    public void GenerateImportAndLoadKeepObservedManualAndMissingStatesSeparate()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-runtime-" + Guid.NewGuid().ToString("N"));
        try
        {
            (ProfileScanReceipt receipt, ConflictWorkItem selected) = Fixture();
            RuntimeInvestigationStore store = new(Path.Combine(root, "state"));
            RuntimeInvestigationRun run = store.GeneratePackage(Path.Combine(root, "package"), receipt, selected, new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero));
            RuntimeProbeBundleRequest automated = run.Manifest.Requests.Single(value => value.Execution == RuntimeProbeExecution.Automated);
            RuntimeProbeBundleRequest manual = run.Manifest.Requests.Single(value => value.Execution == RuntimeProbeExecution.Manual);
            string logPath = Path.Combine(root, "probe.log");
            File.WriteAllText(logPath, $"[ConflictStudioProbe] BEGIN manifest={run.Manifest.ManifestId} run={run.Manifest.RunId} profile={run.Manifest.ProfileName}\n[ConflictStudioProbe] RESULT manifest={run.Manifest.ManifestId} run={run.Manifest.RunId} id={automated.Id} state=observed value=8\n[ConflictStudioProbe] RESULT manifest={run.Manifest.ManifestId} run={run.Manifest.RunId} id={manual.Id} state=manual value=measure\n[ConflictStudioProbe] END manifest={run.Manifest.ManifestId} run={run.Manifest.RunId}\n");
            string answersPath = Path.Combine(root, "answers.json");
            File.WriteAllText(answersPath, $"{{\"{manual.Id}\":\"Measured after combat\"}}");

            RuntimeInvestigationRun imported = store.Import(Path.Combine(root, "package", "probe-manifest.json"), logPath, answersPath, new DateTimeOffset(2026, 9, 12, 13, 0, 0, TimeSpan.Zero));
            RuntimeInvestigationView view = store.Load(receipt, [selected]).Single();

            Assert.AreEqual(RuntimeProbeObservationState.Observed, imported.Receipt!.Observations.Single(value => value.Id == automated.Id).State);
            Assert.AreEqual(RuntimeProbeObservationState.ManualRecorded, imported.Receipt.Observations.Single(value => value.Id == manual.Id).State);
            Assert.AreEqual(RuntimeInvestigationFreshness.Current, view.Freshness);
            Assert.IsNull(view.StaleReason);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ImportRejectsForeignManifestAndTruncatedRunRemainsMissing()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-runtime-foreign-" + Guid.NewGuid().ToString("N"));
        try
        {
            (ProfileScanReceipt receipt, ConflictWorkItem selected) = Fixture();
            RuntimeInvestigationStore store = new(Path.Combine(root, "state"));
            RuntimeInvestigationRun run = store.GeneratePackage(Path.Combine(root, "package"), receipt, selected, DateTimeOffset.UtcNow);
            RuntimeInvestigationRun foreign = new RuntimeInvestigationStore(Path.Combine(root, "foreign-state")).GeneratePackage(Path.Combine(root, "foreign-package"), receipt, selected, DateTimeOffset.UtcNow);
            string logPath = Path.Combine(root, "probe.log");
            File.WriteAllText(logPath, $"[ConflictStudioProbe] BEGIN manifest={run.Manifest.ManifestId} run={run.Manifest.RunId} profile={run.Manifest.ProfileName}\n");

            Assert.ThrowsExactly<InvalidDataException>(() => store.Import(Path.Combine(root, "foreign-package", "probe-manifest.json"), logPath, null, DateTimeOffset.UtcNow));

            RuntimeInvestigationRun imported = store.Import(Path.Combine(root, "package", "probe-manifest.json"), logPath, null, DateTimeOffset.UtcNow);
            Assert.IsFalse(imported.Receipt!.CompleteRun);
            Assert.IsTrue(imported.Receipt.Observations.All(value => value.State == RuntimeProbeObservationState.Missing));
            Assert.AreNotEqual(run.Manifest.RunId, foreign.Manifest.RunId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void LoadMarksChangedEvidenceOrProvidersStaleWithoutDiscardingObservations(bool changeProviders, bool changeEvidence)
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-runtime-stale-" + Guid.NewGuid().ToString("N"));
        try
        {
            (ProfileScanReceipt receipt, ConflictWorkItem selected) = Fixture();
            RuntimeInvestigationStore store = new(root);
            RuntimeInvestigationRun run = store.GeneratePackage(Path.Combine(root, "package"), receipt, selected, DateTimeOffset.UtcNow);
            ConflictWorkItem changed = selected with
            {
                Providers = changeProviders ? ["Alpha", "Gamma"] : selected.Providers,
                EvidenceSha256 = changeEvidence ? new string('b', 64) : selected.EvidenceSha256
            };
            ConflictWorkItem reordered = selected with { Providers = selected.Providers.Reverse().ToArray() };

            RuntimeInvestigationView view = store.Load(receipt, [changed]).Single();
            RuntimeInvestigationView current = store.Load(receipt, [reordered]).Single();

            Assert.AreEqual(RuntimeInvestigationFreshness.Stale, view.Freshness);
            StringAssert.Contains(view.StaleReason!, "providers or source evidence changed");
            Assert.AreEqual(run.Manifest.RunId, view.Run.Manifest.RunId);
            Assert.AreEqual(RuntimeInvestigationFreshness.Current, current.Freshness);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void LoadPreservesCorruptStateAndStartsEmpty()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-runtime-corrupt-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "runtime-investigations.json"), "{bad json");
            RuntimeInvestigationStore store = new(root);
            (ProfileScanReceipt receipt, ConflictWorkItem selected) = Fixture();

            RuntimeInvestigationView[] views = store.Load(receipt, [selected]);

            Assert.IsEmpty(views);
            Assert.IsNotNull(store.LastRecoveryPath);
            Assert.IsTrue(File.Exists(store.LastRecoveryPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void RetentionKeepsOnlyTheNewestRuns()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-runtime-retention-" + Guid.NewGuid().ToString("N"));
        try
        {
            (ProfileScanReceipt receipt, ConflictWorkItem selected) = Fixture();
            RuntimeInvestigationStore store = new(root, 2);
            RuntimeInvestigationRun first = store.GeneratePackage(Path.Combine(root, "one"), receipt, selected, new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.Zero));
            RuntimeInvestigationRun second = store.GeneratePackage(Path.Combine(root, "two"), receipt, selected, new DateTimeOffset(2026, 9, 12, 11, 0, 0, TimeSpan.Zero));
            RuntimeInvestigationRun third = store.GeneratePackage(Path.Combine(root, "three"), receipt, selected, new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero));

            RuntimeInvestigationView[] views = store.Load(receipt, [selected]);

            Assert.HasCount(2, views);
            Assert.IsFalse(views.Any(value => value.Run.Manifest.RunId == first.Manifest.RunId));
            Assert.IsTrue(views.Any(value => value.Run.Manifest.RunId == second.Manifest.RunId));
            Assert.IsTrue(views.Any(value => value.Run.Manifest.RunId == third.Manifest.RunId));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void LoadMarksManagerInstallationAndProfileChangesStale()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-runtime-context-" + Guid.NewGuid().ToString("N"));
        try
        {
            (ProfileScanReceipt receipt, ConflictWorkItem selected) = Fixture();
            RuntimeInvestigationStore store = new(root);
            store.GeneratePackage(Path.Combine(root, "package"), receipt, selected, DateTimeOffset.UtcNow);

            RuntimeInvestigationView manager = store.Load(receipt with { ManagerKind = ModManagerKind.Vortex }, [selected]).Single();
            RuntimeInvestigationView installation = store.Load(receipt with { InstallationId = new string('9', 64) }, [selected]).Single();
            RuntimeInvestigationView profile = store.Load(receipt with { ProfileName = "Other" }, [selected]).Single();

            Assert.AreEqual(RuntimeInvestigationFreshness.Stale, manager.Freshness);
            StringAssert.Contains(manager.StaleReason!, "manager changed");
            StringAssert.Contains(installation.StaleReason!, "installation changed");
            StringAssert.Contains(profile.StaleReason!, "profile changed");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ImportRejectsAnswersForAutomatedOrForeignRequests()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-runtime-answers-" + Guid.NewGuid().ToString("N"));
        try
        {
            (ProfileScanReceipt receipt, ConflictWorkItem selected) = Fixture();
            RuntimeInvestigationStore store = new(Path.Combine(root, "state"));
            RuntimeInvestigationRun run = store.GeneratePackage(Path.Combine(root, "package"), receipt, selected, DateTimeOffset.UtcNow);
            RuntimeProbeBundleRequest automated = run.Manifest.Requests.Single(value => value.Execution == RuntimeProbeExecution.Automated);
            string logPath = Path.Combine(root, "probe.log");
            File.WriteAllText(logPath, string.Empty);
            string answersPath = Path.Combine(root, "answers.json");
            File.WriteAllText(answersPath, $"{{\"{automated.Id}\":\"not a manual answer\"}}");

            Assert.ThrowsExactly<InvalidDataException>(() => store.Import(Path.Combine(root, "package", "probe-manifest.json"), logPath, answersPath, DateTimeOffset.UtcNow));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ImportAllowsAnUnansweredManualEntryToRemainManualRequired()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-runtime-unanswered-" + Guid.NewGuid().ToString("N"));
        try
        {
            (ProfileScanReceipt receipt, ConflictWorkItem selected) = Fixture();
            RuntimeInvestigationStore store = new(Path.Combine(root, "state"));
            RuntimeInvestigationRun run = store.GeneratePackage(Path.Combine(root, "package"), receipt, selected, DateTimeOffset.UtcNow);
            RuntimeProbeBundleRequest manual = run.Manifest.Requests.Single(value => value.Execution == RuntimeProbeExecution.Manual);
            string logPath = Path.Combine(root, "probe.log");
            File.WriteAllText(logPath, $"[ConflictStudioProbe] BEGIN manifest={run.Manifest.ManifestId} run={run.Manifest.RunId} profile={run.Manifest.ProfileName}\n[ConflictStudioProbe] RESULT manifest={run.Manifest.ManifestId} run={run.Manifest.RunId} id={manual.Id} state=manual value=measure\n[ConflictStudioProbe] END manifest={run.Manifest.ManifestId} run={run.Manifest.RunId}\n");

            RuntimeInvestigationRun imported = store.Import(Path.Combine(root, "package", "probe-manifest.json"), logPath, Path.Combine(root, "package", "manual-answers.example.json"), DateTimeOffset.UtcNow);

            Assert.AreEqual(RuntimeProbeObservationState.ManualRequired, imported.Receipt!.Observations.Single(value => value.Id == manual.Id).State);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    [DataRow("binding")]
    [DataRow("request-null")]
    [DataRow("observation-id")]
    [DataRow("observation-state")]
    public void LoadPreservesStateWithInvalidBindingOrObservationMetadata(string mutation)
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-runtime-invalid-metadata-" + Guid.NewGuid().ToString("N"));
        try
        {
            (ProfileScanReceipt receipt, ConflictWorkItem selected) = Fixture();
            RuntimeInvestigationStore store = new(root);
            RuntimeInvestigationRun run = store.GeneratePackage(Path.Combine(root, "package"), receipt, selected, DateTimeOffset.UtcNow);
            string logPath = Path.Combine(root, "probe.log");
            string results = string.Join('\n', run.Manifest.Requests.Select(value => $"[ConflictStudioProbe] RESULT manifest={run.Manifest.ManifestId} run={run.Manifest.RunId} id={value.Id} state={(value.Execution == RuntimeProbeExecution.Automated ? "observed" : "manual")} value=8"));
            File.WriteAllText(logPath, $"[ConflictStudioProbe] BEGIN manifest={run.Manifest.ManifestId} run={run.Manifest.RunId} profile={run.Manifest.ProfileName}\n{results}\n[ConflictStudioProbe] END manifest={run.Manifest.ManifestId} run={run.Manifest.RunId}\n");
            store.Import(Path.Combine(root, "package", "probe-manifest.json"), logPath, null, DateTimeOffset.UtcNow);
            string statePath = Path.Combine(root, "runtime-investigations.json");
            JsonNode document = JsonNode.Parse(File.ReadAllText(statePath))!;
            JsonNode storedRun = document["runs"]![0]!;
            if (mutation == "binding") storedRun["manifest"]!["binding"] = null;
            else if (mutation == "request-null") storedRun["manifest"]!["requests"]![0] = null;
            else if (mutation == "observation-id") storedRun["receipt"]!["observations"]![0]!["id"] = new string('f', 16);
            else storedRun["receipt"]!["observations"]![0]!["state"] = 999;
            File.WriteAllText(statePath, document.ToJsonString());

            RuntimeInvestigationStore restored = new(root);
            RuntimeInvestigationView[] views = restored.Load(receipt, [selected]);

            Assert.IsEmpty(views);
            Assert.IsNotNull(restored.LastRecoveryPath);
            Assert.IsTrue(File.Exists(restored.LastRecoveryPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void SaveRetainsTheNewestWholeRunsWithinTheStateByteLimit()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-runtime-state-cap-" + Guid.NewGuid().ToString("N"));
        try
        {
            (ProfileScanReceipt receipt, ConflictWorkItem selected) = Fixture();
            RuntimeInvestigationStore initial = new(root);
            initial.GeneratePackage(Path.Combine(root, "first"), receipt, selected, new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.Zero));
            string statePath = Path.Combine(root, "runtime-investigations.json");
            int stateLimit = checked((int)new FileInfo(statePath).Length + 32);
            RuntimeInvestigationStore bounded = new(root, RuntimeInvestigationStore.DefaultRetention, stateLimit);

            RuntimeInvestigationRun newest = bounded.GeneratePackage(Path.Combine(root, "second"), receipt, selected, new DateTimeOffset(2026, 9, 12, 11, 0, 0, TimeSpan.Zero));
            RuntimeInvestigationView[] retained = bounded.Load(receipt, [selected]);

            Assert.IsTrue(new FileInfo(statePath).Length <= stateLimit);
            Assert.HasCount(1, retained);
            Assert.AreEqual(newest.Manifest.RunId, retained[0].Run.Manifest.RunId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void OversizedReceiptUpdatePreservesThePreviousStateFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-runtime-state-update-" + Guid.NewGuid().ToString("N"));
        try
        {
            (ProfileScanReceipt receipt, ConflictWorkItem selected) = Fixture();
            RuntimeInvestigationStore initial = new(root);
            RuntimeInvestigationRun run = initial.GeneratePackage(Path.Combine(root, "package"), receipt, selected, DateTimeOffset.UtcNow);
            string statePath = Path.Combine(root, "runtime-investigations.json");
            byte[] before = File.ReadAllBytes(statePath);
            RuntimeInvestigationStore bounded = new(root, RuntimeInvestigationStore.DefaultRetention, before.Length + 32);
            RuntimeProbeBundleRequest automated = run.Manifest.Requests.Single(value => value.Execution == RuntimeProbeExecution.Automated);
            string logPath = Path.Combine(root, "probe.log");
            File.WriteAllText(logPath, $"[ConflictStudioProbe] BEGIN manifest={run.Manifest.ManifestId} run={run.Manifest.RunId} profile={run.Manifest.ProfileName}\n[ConflictStudioProbe] RESULT manifest={run.Manifest.ManifestId} run={run.Manifest.RunId} id={automated.Id} state=observed value={new string('x', 1_000)}\n[ConflictStudioProbe] END manifest={run.Manifest.ManifestId} run={run.Manifest.RunId}\n");

            Assert.ThrowsExactly<InvalidOperationException>(() => bounded.Import(Path.Combine(root, "package", "probe-manifest.json"), logPath, null, DateTimeOffset.UtcNow));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(statePath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static (ProfileScanReceipt Receipt, ConflictWorkItem Selected) Fixture()
    {
        ModSourceInventory inventory = new([], [new("Beta", "runtime.lua", "TweakDB:SetFlat('Items.Test.value', 8)")], [new("Alpha", "alpha.yaml", "Items.Test.value: 4")], []);
        TweakAnalysisResult tweakAnalysis = TweakInteractionAnalyzer.AnalyzeDetailed(inventory.TweakSources);
        SharedStateWrite[] writes = SharedStateWriteAnalyzer.Collect(inventory.RedScripts, inventory.LuaSources);
        ProfileScanReceipt receipt = new(2, "Standard", DateTimeOffset.UtcNow, ["Alpha", "Beta"], [], [], [], [], InteractionReportBuilder.Build(inventory, [], [], tweakAnalysis.Overlaps, tweakAnalysis.Operations, writes), [], [], [], tweakAnalysis.Overlaps, [], [])
        { InstallationId = new string('1', 64), ManagerKind = ModManagerKind.Mo2 };
        return (receipt, ConflictWorkQueueBuilder.Build(receipt, []).Single());
    }
}

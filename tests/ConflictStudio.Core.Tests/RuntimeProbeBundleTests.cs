using ConflictStudio.Core;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class RuntimeProbeBundleTests
{
    private static readonly JsonSerializerOptions LegacyOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    [TestMethod]
    public void WriteCreatesOptInCetBundleAndReceiptReaderPreservesUnknowns()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            RuntimeProbeManifest manifest = new(1, "Standard", DateTimeOffset.UtcNow, [new RuntimeProbeRequest(RuntimeProbeKind.PostInitializationTweakValue, "Items.Test.value", ["Alpha", "Beta"], "Read post-init value.", "Post-init value"), new RuntimeProbeRequest(RuntimeProbeKind.CallbackDelivery, "PlayerPuppet.OnAction", ["Alpha"], "Perform one action.", "Callback delivery")], "install");

            RuntimeProbeBundleManifest bundle = RuntimeProbeBundleWriter.Write(root, manifest);
            string automatedId = bundle.Requests.Single(value => value.Execution == RuntimeProbeExecution.Automated).Id;
            string manualId = bundle.Requests.Single(value => value.Execution == RuntimeProbeExecution.Manual).Id;
            string log = $"[ConflictStudioProbe] BEGIN manifest={bundle.ManifestId} run={bundle.RunId} profile={bundle.ProfileName}\n[ConflictStudioProbe] RESULT manifest={bundle.ManifestId} run={bundle.RunId} id={automatedId} state=observed value=2.0\n[ConflictStudioProbe] RESULT manifest={bundle.ManifestId} run={bundle.RunId} id={manualId} state=manual value=Perform one action.\n[ConflictStudioProbe] END manifest={bundle.ManifestId} run={bundle.RunId}\n";
            RuntimeProbeReceipt receipt = RuntimeProbeReceiptReader.Read(bundle, log, DateTimeOffset.UtcNow, new Dictionary<string, string> { [manualId] = "Both callbacks fired once." });

            Assert.IsTrue(File.Exists(Path.Combine(root, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods", "ConflictStudioProbe", "init.lua")));
            string instructions = File.ReadAllText(Path.Combine(root, "README.txt"));
            Assert.IsFalse(instructions.Contains("probe-import", StringComparison.Ordinal));
            Assert.IsFalse(instructions.Contains("source-built CLI", StringComparison.Ordinal));
            StringAssert.Contains(instructions, "open Runtime checks");
            StringAssert.Contains(instructions, "Remove or disable the separate ConflictStudioProbe mod");
            Assert.IsTrue(receipt.CompleteRun);
            Assert.AreEqual(RuntimeProbeObservationState.Observed, receipt.Observations.Single(value => value.Id == automatedId).State);
            Assert.AreEqual(RuntimeProbeObservationState.ManualRecorded, receipt.Observations.Single(value => value.Id == manualId).State);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ReaderRejectsUnframedAndStaleRunResults()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-probe-frame-" + Guid.NewGuid().ToString("N"));
        try
        {
            RuntimeProbeBundleManifest bundle = RuntimeProbeBundleWriter.Write(root, new RuntimeProbeManifest(1, "Standard", DateTimeOffset.UtcNow, [new RuntimeProbeRequest(RuntimeProbeKind.PostInitializationTweakValue, "Items.Test.value", ["Alpha"], "Read value.", "Post-init value")], "install"));
            string id = bundle.Requests.Single().Id;
            string stale = $"[ConflictStudioProbe] BEGIN manifest={bundle.ManifestId} run={new string('a', 32)} profile=Standard\n[ConflictStudioProbe] RESULT manifest={bundle.ManifestId} run={new string('a', 32)} id={id} state=observed value=9\n[ConflictStudioProbe] END manifest={bundle.ManifestId} run={new string('a', 32)}\n";

            RuntimeProbeReceipt receipt = RuntimeProbeReceiptReader.Read(bundle, stale, DateTimeOffset.UtcNow);

            Assert.IsFalse(receipt.CompleteRun);
            Assert.AreEqual(RuntimeProbeObservationState.Missing, receipt.Observations.Single().State);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ReaderSelectsTheMatchingRunFromMultipleCompleteFrames()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-probe-multiple-" + Guid.NewGuid().ToString("N"));
        try
        {
            (RuntimeProbeBundleManifest bundle, string id) = BoundBundle(root);
            string foreignRun = new string('a', 32);
            string log = $"[ConflictStudioProbe] BEGIN manifest={bundle.ManifestId} run={foreignRun} profile=Standard\n[ConflictStudioProbe] RESULT manifest={bundle.ManifestId} run={foreignRun} id={id} state=observed value=1\n[ConflictStudioProbe] END manifest={bundle.ManifestId} run={foreignRun}\n" +
                $"[ConflictStudioProbe] BEGIN manifest={bundle.ManifestId} run={bundle.RunId} profile=Standard\n[ConflictStudioProbe] RESULT manifest={bundle.ManifestId} run={bundle.RunId} id={id} state=observed value=8\n[ConflictStudioProbe] END manifest={bundle.ManifestId} run={bundle.RunId}\n";

            RuntimeProbeReceipt receipt = RuntimeProbeReceiptReader.Read(bundle, log, DateTimeOffset.UtcNow);

            Assert.IsTrue(receipt.CompleteRun);
            Assert.AreEqual("8", receipt.Observations.Single().Value);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ManifestReaderRejectsChangedBindingMetadata()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-probe-binding-" + Guid.NewGuid().ToString("N"));
        try
        {
            BoundBundle(root);
            string path = Path.Combine(root, "probe-manifest.json");
            string json = File.ReadAllText(path).Replace("Items.Test.value", "Items.Other.value", StringComparison.Ordinal);
            File.WriteAllText(path, json);

            Assert.ThrowsExactly<InvalidDataException>(() => RuntimeProbeBundleStore.ReadManifest(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ReaderKeepsFailedMissingAndUnansweredManualResultsDistinct()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-probe-states-" + Guid.NewGuid().ToString("N"));
        try
        {
            RuntimeProbeBinding binding = new(ModManagerKind.Mo2, new string('1', 64), "Standard", ConflictSurface.ScriptAndTweak, "Items.Target.tags <- Items.Source.tags", ["Alpha", "Beta"], new string('2', 64));
            RuntimeProbeManifest manifest = new RuntimeProbeManifest(2, "Standard", DateTimeOffset.UtcNow,
                [new RuntimeProbeRequest(RuntimeProbeKind.PostInitializationTweakValue, "Items.Target.tags", binding.Providers, "Read target.", "Target value"),
                 new RuntimeProbeRequest(RuntimeProbeKind.PostInitializationTweakValue, "Items.Source.tags", binding.Providers, "Read source.", "Source value"),
                 new RuntimeProbeRequest(RuntimeProbeKind.SharedStateValue, "Items.Target.tags", binding.Providers, "Measure later.", "Later value")], binding.InstallationId) { Binding = binding };
            RuntimeProbeBundleManifest bundle = RuntimeProbeBundleWriter.Write(root, manifest);
            RuntimeProbeBundleRequest failed = bundle.Requests[0];
            RuntimeProbeBundleRequest missing = bundle.Requests[1];
            RuntimeProbeBundleRequest manual = bundle.Requests[2];
            string log = $"[ConflictStudioProbe] BEGIN manifest={bundle.ManifestId} run={bundle.RunId} profile=Standard\n[ConflictStudioProbe] RESULT manifest={bundle.ManifestId} run={bundle.RunId} id={failed.Id} state=failed value=lookup failed\n[ConflictStudioProbe] RESULT manifest={bundle.ManifestId} run={bundle.RunId} id={manual.Id} state=manual value=measure\n[ConflictStudioProbe] END manifest={bundle.ManifestId} run={bundle.RunId}\n";

            RuntimeProbeReceipt receipt = RuntimeProbeReceiptReader.Read(bundle, log, DateTimeOffset.UtcNow);

            Assert.AreEqual(RuntimeProbeObservationState.Failed, receipt.Observations.Single(value => value.Id == failed.Id).State);
            Assert.AreEqual(RuntimeProbeObservationState.Missing, receipt.Observations.Single(value => value.Id == missing.Id).State);
            Assert.AreEqual(RuntimeProbeObservationState.ManualRequired, receipt.Observations.Single(value => value.Id == manual.Id).State);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ManifestValidationRejectsANullRequestAsInvalidData()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-probe-null-request-" + Guid.NewGuid().ToString("N"));
        try
        {
            RuntimeProbeBundleManifest valid = BoundBundle(root).Bundle;
            RuntimeProbeBundleManifest invalid = valid with { Requests = [null!] };
            invalid = invalid with { ManifestId = RuntimeProbeBundleWriter.ManifestId(invalid with { ManifestId = string.Empty }) };

            Assert.ThrowsExactly<InvalidDataException>(() => RuntimeProbeBundleStore.ValidateManifest(invalid));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    [DataRow("execution")]
    [DataRow("kind")]
    [DataRow("manager")]
    [DataRow("surface")]
    public void ManifestValidationRejectsUndefinedEnumsEvenWithAMatchingIdentity(string mutation)
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-probe-enum-" + Guid.NewGuid().ToString("N"));
        try
        {
            RuntimeProbeBundleManifest valid = BoundBundle(root).Bundle;
            RuntimeProbeBundleRequest request = valid.Requests[0];
            RuntimeProbeBundleManifest invalid = mutation switch
            {
                "execution" => valid with { Requests = [request with { Execution = (RuntimeProbeExecution)999 }] },
                "kind" => valid with { Requests = [request with { Request = request.Request with { Kind = (RuntimeProbeKind)999 } }] },
                "manager" => valid with { Binding = valid.Binding! with { ManagerKind = (ModManagerKind)999 } },
                _ => valid with { Binding = valid.Binding! with { Surface = (ConflictSurface)999 } }
            };
            invalid = invalid with { ManifestId = RuntimeProbeBundleWriter.ManifestId(invalid with { ManifestId = string.Empty }) };

            Assert.ThrowsExactly<InvalidDataException>(() => RuntimeProbeBundleStore.ValidateManifest(invalid));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ExistingSchemaTwoBundleRemainsReadableAndWritesItsSupportReceipt()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-probe-schema-two-" + Guid.NewGuid().ToString("N"));
        try
        {
            string installationId = "install";
            DateTimeOffset createdAtUtc = new(2026, 8, 25, 16, 0, 0, TimeSpan.Zero);
            string runId = new string('a', 32);
            RuntimeProbeRequest request = new(RuntimeProbeKind.PostInitializationTweakValue, "Items.Test.value", ["Alpha", "Beta"], "Read value.", "Post-init value");
            string requestSource = request.Kind + "|" + request.Target + "|" + string.Join("|", request.Providers);
            string requestId = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(requestSource)))[..16];
            RuntimeProbeBundleRequest bundleRequest = new(requestId, RuntimeProbeExecution.Automated, request);
            string identity = installationId + "|Standard|" + createdAtUtc.ToString("O") + "|" + runId + "|" + requestId;
            string manifestId = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
            RuntimeProbeBundleManifest legacy = new(2, "Standard", installationId, createdAtUtc, runId, manifestId, [bundleRequest]);
            ConflictCasefile casefile = new(1, "Standard", createdAtUtc, [], [], [], []);
            SupportEvidence evidence = new([], [], [], [], [], [], [], [], [], null, installationId);
            SupportCapsuleWriter.Write(root, new SupportCapsule(5, casefile, [], evidence, [], new RuntimeProbeManifest(1, "Standard", createdAtUtc, [], installationId), new SupportCapsuleSummary(0, 0, 0, 0, 0, 0, 0, 0)));
            string manifestPath = Path.Combine(root, "runtime-probe", "probe-manifest.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(legacy, LegacyOptions));

            RuntimeProbeBundleManifest restored = RuntimeProbeBundleStore.ReadManifest(manifestPath);
            string log = $"[ConflictStudioProbe] BEGIN manifest={manifestId} run={runId} profile=Standard\n[ConflictStudioProbe] RESULT manifest={manifestId} run={runId} id={requestId} state=observed value=8\n[ConflictStudioProbe] END manifest={manifestId} run={runId}\n";
            RuntimeProbeReceipt receipt = RuntimeProbeReceiptReader.Read(restored, log, DateTimeOffset.UtcNow);
            RuntimeProbeBundleStore.WriteCasefileReceipt(root, receipt);

            Assert.AreEqual(2, restored.SchemaVersion);
            Assert.AreEqual(2, receipt.SchemaVersion);
            Assert.AreEqual(RuntimeProbeObservationState.Observed, receipt.Observations.Single().State);
            Assert.IsTrue(File.Exists(Path.Combine(root, "runtime-receipt.json")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "runtime-receipt.html")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static (RuntimeProbeBundleManifest Bundle, string RequestId) BoundBundle(string root)
    {
        RuntimeProbeBinding binding = new(ModManagerKind.Mo2, new string('1', 64), "Standard", ConflictSurface.ScriptAndTweak, "Items.Test.value", ["Alpha", "Beta"], new string('2', 64));
        RuntimeProbeManifest manifest = new RuntimeProbeManifest(2, "Standard", DateTimeOffset.UtcNow, [new RuntimeProbeRequest(RuntimeProbeKind.PostInitializationTweakValue, "Items.Test.value", ["Alpha", "Beta"], "Read value.", "Post-init value")], binding.InstallationId) { Binding = binding };
        RuntimeProbeBundleManifest bundle = RuntimeProbeBundleWriter.Write(root, manifest);
        return (bundle, bundle.Requests.Single().Id);
    }
}

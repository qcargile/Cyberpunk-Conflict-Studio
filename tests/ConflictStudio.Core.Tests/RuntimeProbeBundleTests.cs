using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class RuntimeProbeBundleTests
{
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
            StringAssert.Contains(instructions, "source-built CLI");
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
}

using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class SupportCapsuleBuilderTests
{
    [TestMethod]
    public void BuildProducesPrivacySafeCasefileAndRuntimeRequests()
    {
        ProfileScanReceipt receipt = new ProfileScanReceipt(1, "Standard", new DateTimeOffset(2026, 8, 25, 16, 0, 0, TimeSpan.Zero), ["Alpha", "Beta"], ["Alpha.archive"], [new RdarArchiveFailure("Alpha", "Alpha.archive", @"Failed to open C:\private\Alpha.archive")], [], [new VirtualFileShadow("r6\\scripts\\shared.reds", "Beta", VirtualFileRelation.Different, [new VirtualFileProvider("Alpha", @"C:\private\Alpha\shared.reds", 1, new string('a', 64), 0)])], [new InteractionFinding("DamageSystem.ProcessHit()", InteractionFindingKind.Exclusive, "suppressed", ["Alpha", "Beta"])], [], [], [], [], [], []) with { InstallationId = "install", ArchiveSummaries = [new ArchiveConflictSummary("Alpha.archive", "Alpha", 0, [], [], [], [], [], @"C:\private\Alpha.archive")], ArchiveOrderEvidence = new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.ManagedModlist, "Alpha", @"C:\private\modlist.txt", "managed"), ResourcePathIndexEvidence = new ResourcePathIndexEvidence(ResourcePathIndexState.Resolved, "CET", @"C:\private\usedhashes.kark", 1, 1, "resolved"), ArchiveWarnings = [new RdarArchiveWarning("Alpha", "Alpha.archive", @"Oodle failed at C:\private\oo2ext_7_win64.dll")], ManagerKind = ModManagerKind.Vortex, DeploymentFresh = false };

        receipt = receipt with { RedScriptFlows = RedScriptFlowEvidenceAnalyzer.Analyze([
            new("Alpha", "a.reds", "@replaceMethod(DamageSystem)\npublic func ProcessHit() -> Void { First(); }"),
            new("Beta", "b.reds", "@replaceMethod(DamageSystem)\npublic func ProcessHit() -> Void { Second(); }")]) };
        EvidenceDecision otherProfile = new("Other", "target", ["Alpha"], new string('a', 64), "Other profile decision.", DateTimeOffset.UtcNow);
        EvidenceDecision privateDecision = new("Standard", "DamageSystem.ProcessHit()", ["Alpha", "Beta"], ConflictWorkQueueBuilder.Build(receipt, []).Single(value => value.Target == "DamageSystem.ProcessHit()").EvidenceSha256, "Verified with C:/private/runtime.log", DateTimeOffset.UtcNow, "install", ConflictSurface.ScriptAndTweak);
        SupportCapsule capsule = SupportCapsuleBuilder.Build(receipt, [otherProfile, privateDecision]);

        Assert.AreEqual("Standard", capsule.Casefile.ProfileName);
        Assert.AreEqual(ModManagerKind.Vortex, capsule.Evidence.ManagerKind);
        Assert.IsFalse(capsule.Evidence.DeploymentFresh);
        Assert.IsFalse(capsule.Probes.Requests.Any(value => value.Target == "DamageSystem.ProcessHit()"));
        Assert.IsTrue(capsule.WorkQueue.Any(value => value.Target == "DamageSystem.ProcessHit()" && value.State == ConflictWorkState.Reviewed));
        Assert.IsTrue(capsule.WorkQueue.Any(value => value.Target == "r6\\scripts\\shared.reds"));
        Assert.AreEqual(1, capsule.Decisions.Length);
        Assert.AreEqual("Verified with [private path]", capsule.Decisions.Single().Rationale);
        Assert.IsTrue(capsule.Evidence.VirtualFileShadows.SelectMany(value => value.Providers).All(value => value.PhysicalPath.Length == 0));
        Assert.IsNull(capsule.Evidence.ArchiveSummaries!.Single().PhysicalPath);
        Assert.IsNull(capsule.Evidence.ArchiveOrderEvidence!.SourcePath);
        Assert.AreEqual(0, capsule.Evidence.ArchiveOrderEvidence.SourcePaths.Length);
        Assert.IsNull(capsule.Evidence.ResourcePathIndexEvidence!.SourcePath);
        Assert.IsFalse(capsule.Evidence.ArchiveFailures.Single().Message.Contains(@"C:\private", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(capsule.Evidence.ArchiveWarnings!.Single().Message.Contains(@"C:\private", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(capsule.WorkQueue.Any(value => value.Summary.Contains(@"C:\private", StringComparison.OrdinalIgnoreCase)));
        string serialized = System.Text.Json.JsonSerializer.Serialize(capsule);
        Assert.IsFalse(serialized.Contains(@"C:\private", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serialized.Contains("C:/private", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(capsule.Casefile.Findings.Any(value => Path.IsPathRooted(value.Target)));
    }

    [TestMethod]
    public void BuildExcludesExpiredDecisionsFromTheCurrentSupportCapsule()
    {
        InteractionFinding finding = new("DamageSystem.ProcessHit()", InteractionFindingKind.Review, "review", ["Alpha", "Beta"]);
        ProfileScanReceipt receipt = new ProfileScanReceipt(1, "Standard", DateTimeOffset.UtcNow, ["Alpha", "Beta"], [], [], [], [], [finding], [], [], [], [], [], []) with { InstallationId = "install" };
        EvidenceDecision expired = new("Standard", finding.Target, finding.Providers, new string('a', 64), "Old result", DateTimeOffset.UtcNow, "install", ConflictSurface.ScriptAndTweak);

        SupportCapsule capsule = SupportCapsuleBuilder.Build(receipt, [expired]);

        Assert.AreEqual(0, capsule.Decisions.Length);
        Assert.AreEqual(0, capsule.Summary.ReviewDecisions);
    }

    [TestMethod]
    public void BuildExportsOnlyRelevantPrivacySafeNotes()
    {
        TweakOperation alpha = new("Alpha", "alpha.yaml", "Items.Pistol.damage", "10", false);
        TweakOperation beta = new("Beta", "beta.yaml", "Items.Pistol.damage", "20", false);
        InteractionFinding finding = new(alpha.Target, InteractionFindingKind.Review, "review", ["Alpha", "Beta"]);
        ProfileScanReceipt receipt = new ProfileScanReceipt(1, "Standard", DateTimeOffset.UtcNow, ["Alpha", "Beta"], [], [], [], [], [finding], [], [], [], [new TweakOverlap(finding.Target, TweakOverlapKind.ScalarOverwrite, [alpha, beta])], [], []) with { InstallationId = "install" };
        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single();
        EvidenceNote relevant = new("Standard", "install", item.Surface, item.Target, item.Providers, item.EvidenceSha256, "Check C:/private/runtime.log", DateTimeOffset.UtcNow);
        EvidenceNote otherProfile = relevant with { ProfileName = "Other", Text = "Other profile note." };
        EvidenceNote otherTarget = relevant with { Target = "Other.Target", Text = "Other target note." };

        SupportCapsule capsule = SupportCapsuleBuilder.Build(receipt, [], [otherProfile, otherTarget, relevant]);

        Assert.AreEqual(1, capsule.Notes.Length);
        Assert.AreEqual("Check [private path]", capsule.Notes.Single().Text);
        Assert.AreEqual("Check [private path]", capsule.WorkQueue.Single().OpenNote!.Text);
        string serialized = System.Text.Json.JsonSerializer.Serialize(capsule);
        Assert.IsFalse(serialized.Contains("C:/private", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serialized.Contains("Other profile note.", StringComparison.Ordinal));
        Assert.IsFalse(serialized.Contains("Other target note.", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BuildExportsPrivacySafeCurrentAndStaleRuntimeEvidenceForTheProfile()
    {
        TweakOperation[] operations = [new("Alpha", "alpha.yaml", "Items.Test.value", "1", false), new("Beta", "beta.yaml", "Items.Test.value", "2", false)];
        ProfileScanReceipt receipt = new(2, "Standard", DateTimeOffset.UtcNow, ["Alpha", "Beta"], [], [], [], [], [new InteractionFinding("Items.Test.value", InteractionFindingKind.Review, "review", ["Alpha", "Beta"])], [], [], [], [new TweakOverlap("Items.Test.value", TweakOverlapKind.ScalarOverwrite, operations)], [], [])
        { InstallationId = new string('1', 64), ManagerKind = ModManagerKind.Mo2 };
        ConflictWorkItem item = ConflictWorkQueueBuilder.Build(receipt, []).Single();
        RuntimeProbeBinding binding = new(receipt.ManagerKind, receipt.InstallationId!, receipt.ProfileName, item.Surface, item.Target, item.Providers, item.EvidenceSha256);
        RuntimeProbeBundleManifest manifest = new(3, receipt.ProfileName, receipt.InstallationId, DateTimeOffset.UtcNow, new string('2', 32), new string('3', 64), [new RuntimeProbeBundleRequest(new string('4', 16), RuntimeProbeExecution.Automated, new RuntimeProbeRequest(RuntimeProbeKind.PostInitializationTweakValue, item.Target, item.Providers, "Read", "Decide"))]) { Binding = binding };
        RuntimeProbeReceipt probeReceipt = new(3, receipt.ProfileName, receipt.InstallationId, manifest.RunId, manifest.ManifestId, DateTimeOffset.UtcNow, true, [new RuntimeProbeObservation(manifest.Requests[0].Id, RuntimeProbeObservationState.Observed, @"C:\private\runtime.log", null)]) { Binding = binding };
        RuntimeInvestigationView current = new(new RuntimeInvestigationRun(@"C:\private\package", manifest, probeReceipt, DateTimeOffset.UtcNow), RuntimeInvestigationFreshness.Current, null);
        RuntimeProbeBinding foreignBinding = binding with { ProfileName = "Other" };
        RuntimeInvestigationView foreign = current with { Run = current.Run with { Manifest = manifest with { ProfileName = "Other", Binding = foreignBinding }, Receipt = probeReceipt with { ProfileName = "Other", Binding = foreignBinding } } };
        RuntimeInvestigationView stale = current with { Freshness = RuntimeInvestigationFreshness.Stale, StaleReason = "The source evidence changed." };

        SupportCapsule capsule = SupportCapsuleBuilder.Build(receipt, [], null, [foreign, current, stale]);

        Assert.HasCount(2, capsule.RuntimeEvidence);
        Assert.IsTrue(capsule.RuntimeEvidence.Any(value => value.Freshness == RuntimeInvestigationFreshness.Stale));
        Assert.IsTrue(capsule.RuntimeEvidence.SelectMany(value => value.Receipt.Observations).All(value => value.Value == "[private path]"));
        string serialized = System.Text.Json.JsonSerializer.Serialize(capsule);
        Assert.IsFalse(serialized.Contains(@"C:\private", StringComparison.OrdinalIgnoreCase));
    }
}

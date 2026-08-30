using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class SupportCapsuleBuilderTests
{
    [TestMethod]
    public void BuildProducesPrivacySafeCasefileAndRuntimeRequests()
    {
        ProfileScanReceipt receipt = new ProfileScanReceipt(1, "Standard", new DateTimeOffset(2026, 8, 25, 16, 0, 0, TimeSpan.Zero), ["Alpha", "Beta"], ["Alpha.archive"], [new RdarArchiveFailure("Alpha", "Alpha.archive", @"Failed to open C:\private\Alpha.archive")], [], [new VirtualFileShadow("r6\\scripts\\shared.reds", "Beta", VirtualFileRelation.Different, [new VirtualFileProvider("Alpha", @"C:\private\Alpha\shared.reds", 1, new string('a', 64), 0)])], [new InteractionFinding("DamageSystem.ProcessHit()", InteractionFindingKind.Exclusive, "suppressed", ["Alpha", "Beta"])], [], [], [], [], [], []) with { InstallationId = "install", ArchiveSummaries = [new ArchiveConflictSummary("Alpha.archive", "Alpha", 0, [], [], [], [], [], @"C:\private\Alpha.archive")], ArchiveOrderEvidence = new ArchiveOrderEvidence(ArchiveOrderEvidenceKind.ManagedModlist, "Alpha", @"C:\private\modlist.txt", "managed"), ResourcePathIndexEvidence = new ResourcePathIndexEvidence(ResourcePathIndexState.Resolved, "CET", @"C:\private\usedhashes.kark", 1, 1, "resolved"), ArchiveWarnings = [new RdarArchiveWarning("Alpha", "Alpha.archive", @"Oodle failed at C:\private\oo2ext_7_win64.dll")], ManagerKind = ModManagerKind.Vortex, DeploymentFresh = false };

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
}

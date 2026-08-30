using ConflictStudio.Core;
using System.IO;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class EvidenceDecisionStoreTests
{
    [TestMethod]
    public void EvaluateReopensDecisionWhenEvidenceChanges()
    {
        EvidenceDecision decision = new("Standard", "DamageSystem.ProcessHit()", ["Alpha", "Beta"], new string('a', 64), "Alpha intentionally owns this hook.", new DateTimeOffset(2026, 8, 25, 16, 0, 0, TimeSpan.Zero), "install", ConflictSurface.ScriptAndTweak);

        EvidenceDecisionState state = EvidenceDecisionStore.Evaluate(decision, "install", "Standard", ConflictSurface.ScriptAndTweak, new string('b', 64));

        Assert.AreEqual(EvidenceDecisionState.ReviewExpired, state);
    }

    [TestMethod]
    public void ReviewReplacesAnOlderTargetDecisionAndReopenRemovesIt()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-decisions-" + Guid.NewGuid().ToString("N"));
        try
        {
            EvidenceDecisionStore store = new(root);
            ConflictWorkItem first = new(ConflictSurface.ScriptAndTweak, "Target", EvidenceClassification.Review, ConflictWorkState.NeedsAttention, "summary", "action", null, ["Alpha", "Beta"], new string('a', 64));
            ConflictWorkItem second = first with { EvidenceSha256 = new string('b', 64) };

            store.Review("install", "Standard", first, "First review.", DateTimeOffset.UtcNow.AddDays(-1));
            EvidenceDecision[] decisions = store.Review("install", "Standard", second, "Updated review.", DateTimeOffset.UtcNow);

            Assert.AreEqual(1, decisions.Length);
            Assert.AreEqual(second.EvidenceSha256, decisions.Single().EvidenceSha256);
            Assert.AreEqual(0, store.Reopen("install", "Standard", second).Length);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void BatchReviewValidatesBeforeOneWriteAndReopenReportsActualChanges()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-decisions-batch-" + Guid.NewGuid().ToString("N"));
        try
        {
            EvidenceDecisionStore store = new(root);
            ConflictWorkItem first = new(ConflictSurface.ScriptAndTweak, "First", EvidenceClassification.Review, ConflictWorkState.ReviewWhenRelevant, "summary", "action", null, ["Alpha", "Beta"], new string('a', 64));
            ConflictWorkItem second = first with { Target = "Second", EvidenceSha256 = new string('b', 64) };
            ConflictWorkItem unresolved = first with { Target = "Unknown", Classification = EvidenceClassification.Unresolved, EvidenceSha256 = new string('c', 64) };

            Assert.Throws<EvidenceDecisionException>(() => store.ReviewMany("install", "Standard", [first, unresolved], "review", DateTimeOffset.UtcNow));
            Assert.AreEqual(0, store.Load().Length);

            EvidenceDecisionBatchResult reviewed = store.ReviewMany("install", "Standard", [first, second], "review", DateTimeOffset.UtcNow);

            Assert.AreEqual(2, reviewed.ChangedCount);
            Assert.AreEqual(2, reviewed.Decisions.Length);

            EvidenceDecisionBatchResult reopened = store.ReopenMany("install", "Standard", [first, unresolved]);

            Assert.AreEqual(1, reopened.ChangedCount);
            Assert.AreEqual(1, reopened.Decisions.Length);
            Assert.AreEqual("Second", reopened.Decisions.Single().Target);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}

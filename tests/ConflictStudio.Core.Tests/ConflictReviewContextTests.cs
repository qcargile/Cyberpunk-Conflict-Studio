using ConflictStudio.Core;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class ConflictReviewContextTests
{
    [TestMethod]
    public void AcceptedReviewKeepsRawAnalysisLabelSeparateFromReviewedPresentation()
    {
        ProfileScanReceipt receipt = Receipt(["Alpha", "Beta"]);
        ConflictWorkItem original = ConflictWorkQueueBuilder.Build(receipt, []).Single();
        EvidenceDecision decision = new("Standard", original.Target, original.Providers, original.EvidenceSha256, "Accepted result.", DateTimeOffset.UtcNow, "install", original.Surface);

        ConflictWorkItem reviewed = ConflictWorkQueueBuilder.Build(receipt, [decision]).Single();

        Assert.AreEqual(ConflictWorkState.Reviewed, reviewed.State);
        Assert.AreEqual("Reviewed", reviewed.ReviewStatusLabel);
        Assert.AreEqual("Review: different values assigned", reviewed.AnalysisLabel);
        Assert.AreEqual("Reviewed for this profile", reviewed.ClassificationLabel);
        Assert.IsNull(reviewed.PreviousReview);
    }

    [TestMethod]
    public void NewestPriorReviewExpiresForProviderOrEvidenceChangesWithoutAcceptingCase()
    {
        ProfileScanReceipt originalReceipt = Receipt(["Alpha", "Beta"]);
        ConflictWorkItem original = ConflictWorkQueueBuilder.Build(originalReceipt, []).Single();
        EvidenceDecision evidenceChanged = new("Standard", original.Target, original.Providers, new string('a', 64), "Older evidence.", DateTimeOffset.UtcNow.AddMinutes(-2), "install", original.Surface);
        EvidenceDecision providersChanged = new("Standard", original.Target, ["Alpha"], original.EvidenceSha256, "Older providers.", DateTimeOffset.UtcNow.AddMinutes(-1), "install", original.Surface);

        ConflictWorkItem providerResult = ConflictWorkQueueBuilder.Build(originalReceipt, [evidenceChanged, providersChanged]).Single();
        ConflictWorkItem evidenceResult = ConflictWorkQueueBuilder.Build(originalReceipt, [evidenceChanged]).Single();

        Assert.AreEqual(ConflictWorkState.ReviewWhenRelevant, providerResult.State);
        Assert.AreSame(providersChanged, providerResult.PreviousReview);
        StringAssert.Contains(providerResult.PreviousReviewReason!, "providers changed");
        Assert.AreEqual("Review expired", providerResult.ReviewStatusLabel);
        Assert.AreSame(evidenceChanged, evidenceResult.PreviousReview);
        StringAssert.Contains(evidenceResult.PreviousReviewReason!, "evidence changed");
    }

    [TestMethod]
    public void QueueUsesCurrentNoteAndLabelsOlderEvidenceWithoutChangingReviewState()
    {
        ProfileScanReceipt receipt = Receipt(["Alpha", "Beta"]);
        ConflictWorkItem original = ConflictWorkQueueBuilder.Build(receipt, []).Single();
        EvidenceNote current = new("Standard", "install", original.Surface, original.Target, original.Providers, original.EvidenceSha256, "Current note.", DateTimeOffset.UtcNow.AddMinutes(-1));
        EvidenceNote stale = current with { EvidenceSha256 = new string('a', 64), Text = "Older note.", UpdatedAtUtc = DateTimeOffset.UtcNow };

        ConflictWorkItem currentResult = ConflictWorkQueueBuilder.Build(receipt, [], [stale, current]).Single();
        ConflictWorkItem staleResult = ConflictWorkQueueBuilder.Build(receipt, [], [stale]).Single();

        Assert.AreSame(current, currentResult.OpenNote);
        Assert.IsFalse(currentResult.NoteIsStale);
        Assert.AreEqual(ConflictWorkState.ReviewWhenRelevant, currentResult.State);
        Assert.AreSame(stale, staleResult.OpenNote);
        Assert.IsTrue(staleResult.NoteIsStale);
        Assert.AreEqual(ConflictWorkState.ReviewWhenRelevant, staleResult.State);
    }

    private static ProfileScanReceipt Receipt(string[] providers)
    {
        TweakOperation alpha = new("Alpha", "alpha.yaml", "Items.Pistol.damage", "10", false);
        TweakOperation beta = new("Beta", "beta.yaml", "Items.Pistol.damage", "20", false);
        InteractionFinding finding = new(alpha.Target, InteractionFindingKind.Review, "review", providers);
        return new ProfileScanReceipt(1, "Standard", DateTimeOffset.UtcNow, providers, [], [], [], [], [finding], [], [], [], [new TweakOverlap(finding.Target, TweakOverlapKind.ScalarOverwrite, [alpha, beta])], [], []) with { InstallationId = "install" };
    }
}

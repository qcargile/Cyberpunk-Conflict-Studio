using ConflictStudio.App;
using ConflictStudio.Core;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class CodeCaseWorkspaceTests
{
    private static readonly string[] ExpectedActionableTargets = ["blocked", "check"];

    [TestMethod]
    public void ActionableViewHidesCompositionAndSharedTargetNoise()
    {
        ConflictWorkItem[] items = [Item("blocked", EvidenceClassification.Exclusive, "Alpha", "Beta"), Item("check", EvidenceClassification.Review, "Alpha", "Gamma"), Item("compose", EvidenceClassification.Composable, "Beta", "Gamma"), Item("shared", EvidenceClassification.Informational, "Alpha", "Delta")];

        ConflictWorkItem[] filtered = CodeCaseWorkspace.Filter(items, string.Empty, "Actionable", "All", "All mods");

        CollectionAssert.AreEqual(ExpectedActionableTargets, filtered.Select(value => value.Target).ToArray());
    }

    [TestMethod]
    public void ProviderLensKeepsOnlyCasesInvolvingTheSelectedMod()
    {
        ConflictWorkItem[] items = [Item("alpha-beta", EvidenceClassification.Review, "Alpha", "Beta"), Item("gamma-delta", EvidenceClassification.Review, "Gamma", "Delta")];

        ConflictWorkItem[] filtered = CodeCaseWorkspace.Filter(items, string.Empty, "Actionable", "All", "Beta");

        Assert.AreEqual("alpha-beta", filtered.Single().Target);
    }

    [TestMethod]
    public void CountsSeparateProvenDecisionReviewedAndCompatibleEvidence()
    {
        ConflictWorkItem reviewed = Item("reviewed", EvidenceClassification.Intentional, "Alpha", "Beta");
        ConflictWorkItem[] items = [Item("blocked", EvidenceClassification.Exclusive, "Alpha", "Beta"), Item("order", EvidenceClassification.OrderSensitive, "Alpha", "Gamma"), reviewed, Item("compose", EvidenceClassification.Composable, "Beta", "Gamma")];

        CodeCaseCounts counts = CodeCaseWorkspace.Counts(items);

        Assert.AreEqual(1, counts.ProvenConflicts);
        Assert.AreEqual(1, counts.NeedsDecision);
        Assert.AreEqual(1, counts.Reviewed);
        Assert.AreEqual(1, counts.CompatibleEvidence);
    }

    [TestMethod]
    public void DefaultCodeViewAndCountsExcludeSetupDiagnostics()
    {
        ConflictWorkItem diagnostic = Item("missing info.json", EvidenceClassification.Unresolved, "Broken REDmod") with { Surface = ConflictSurface.Diagnostic };
        ConflictWorkItem[] items = [Item("check", EvidenceClassification.Review, "Alpha", "Beta"), diagnostic];

        ConflictWorkItem[] filtered = CodeCaseWorkspace.Filter(items, string.Empty, "Actionable", "All", "All mods");
        CodeCaseCounts counts = CodeCaseWorkspace.Counts(items);

        Assert.AreEqual("check", filtered.Single().Target);
        Assert.AreEqual(1, counts.NeedsDecision);
    }

    [TestMethod]
    public void ScanProblemsRemainAvailableThroughTheirExplicitSurface()
    {
        ConflictWorkItem diagnostic = Item("missing info.json", EvidenceClassification.Unresolved, "Broken REDmod") with { Surface = ConflictSurface.Diagnostic };

        ConflictWorkItem[] filtered = CodeCaseWorkspace.Filter([diagnostic], string.Empty, "Actionable", "Diagnostic", "All mods");

        Assert.AreEqual("missing info.json", filtered.Single().Target);
    }

    [TestMethod]
    public void StructuredReviewRoundTripsOutcomeAndNotesWithoutDuplicatingTheOutcome()
    {
        string rationale = CodeCaseWorkspace.ReviewRationale("Works as intended", "Both features passed.");

        Assert.AreEqual("Works as intended: Both features passed.", rationale);
        Assert.AreEqual("Both features passed.", CodeCaseWorkspace.ReviewNotes(rationale, "Works as intended"));
    }

    private static ConflictWorkItem Item(string target, EvidenceClassification classification, params string[] providers)
    {
        ConflictWorkState state = classification == EvidenceClassification.Intentional ? ConflictWorkState.Reviewed
            : classification is EvidenceClassification.Composable or EvidenceClassification.Informational ? ConflictWorkState.NoActionNeeded
            : classification == EvidenceClassification.Exclusive ? ConflictWorkState.NeedsAttention
            : ConflictWorkState.ReviewWhenRelevant;
        return new ConflictWorkItem(ConflictSurface.ScriptAndTweak, target, classification, state, target, "action", null, providers, new string('a', 64));
    }
}

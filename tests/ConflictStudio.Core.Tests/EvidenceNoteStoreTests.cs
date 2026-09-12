using ConflictStudio.Core;
using System.IO;

namespace ConflictStudio.Core.Tests;

[TestClass]
public sealed class EvidenceNoteStoreTests
{
    [TestMethod]
    public void SaveManyReplacesAndClearsOnlySelectedCaseNotesWithoutChangingDecisions()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-notes-" + Guid.NewGuid().ToString("N"));
        try
        {
            ConflictWorkItem first = Item("First", 'a');
            ConflictWorkItem second = Item("Second", 'b');
            EvidenceDecisionStore decisions = new(root);
            decisions.Review("install", "Standard", first, "Accepted result.", DateTimeOffset.UtcNow);
            EvidenceNoteStore notes = new(root);

            notes.SaveMany("install", "Standard", [first, second], "Check both in game.", DateTimeOffset.UtcNow.AddMinutes(-1));
            EvidenceNote[] saved = notes.SaveMany("install", "Standard", [first], "Updated first check.", DateTimeOffset.UtcNow);

            Assert.AreEqual(2, saved.Length);
            Assert.AreEqual("Updated first check.", saved.Single(value => value.Target == "First").Text);
            EvidenceNote[] remaining = notes.SaveMany("install", "Standard", [first], "  ", DateTimeOffset.UtcNow);
            Assert.AreEqual("Second", remaining.Single().Target);
            Assert.AreEqual(1, decisions.Load().Length);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void NotesRemainIsolatedByInstallationProfileAndSurface()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-note-scope-" + Guid.NewGuid().ToString("N"));
        try
        {
            EvidenceNoteStore store = new(root);
            ConflictWorkItem item = Item("Target", 'a');
            store.SaveMany("first-install", "Standard", [item], "First installation.", DateTimeOffset.UtcNow.AddMinutes(-3));
            store.SaveMany("second-install", "Standard", [item], "Second installation.", DateTimeOffset.UtcNow.AddMinutes(-2));
            store.SaveMany("first-install", "Other", [item], "Other profile.", DateTimeOffset.UtcNow.AddMinutes(-1));
            store.SaveMany("first-install", "Standard", [item with { Surface = ConflictSurface.SharedState }], "Other surface.", DateTimeOffset.UtcNow);

            EvidenceNote[] notes = store.Load();

            Assert.AreEqual(4, notes.Length);
            Assert.IsTrue(notes.Any(value => value.InstallationId == "first-install" && value.ProfileName == "Standard" && value.Surface == ConflictSurface.ScriptAndTweak && value.Text == "First installation."));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static ConflictWorkItem Item(string target, char hash)
        => new(ConflictSurface.ScriptAndTweak, target, EvidenceClassification.Review, ConflictWorkState.ReviewWhenRelevant, "summary", "action", null, ["Alpha", "Beta"], new string(hash, 64));

    [TestMethod]
    public void UpdatingOrClearingChangedEvidenceDoesNotResurrectTheDisplayedNote()
    {
        string root = Path.Combine(Path.GetTempPath(), "conflict-studio-note-renewal-" + Guid.NewGuid().ToString("N"));
        try
        {
            EvidenceNoteStore store = new(root);
            ConflictWorkItem original = Item("Target", 'a');
            EvidenceNote old = store.SaveMany("install", "Standard", [original], "Old investigation", DateTimeOffset.UtcNow).Single();
            ConflictWorkItem changed = original with { EvidenceSha256 = new string('b', 64), OpenNote = old };
            EvidenceNote renewed = store.SaveMany("install", "Standard", [changed], "Current investigation", DateTimeOffset.UtcNow).Single();
            Assert.AreEqual(changed.EvidenceSha256, renewed.EvidenceSha256);
            ConflictWorkItem providersChanged = changed with { Providers = ["Gamma"], OpenNote = renewed };
            Assert.IsEmpty(store.SaveMany("install", "Standard", [providersChanged], string.Empty, DateTimeOffset.UtcNow));
            Assert.IsEmpty(store.Load());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}

using ConflictStudio.App;
using ConflictStudio.Core;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class ArchiveOrderGuidanceTests
{
    [TestMethod]
    [DataRow(ArchiveOrderProblemLane.Legacy, "Show repair steps", "Add every named active archive once to the legacy load order, then re-check conflicts.")]
    [DataRow(ArchiveOrderProblemLane.Redmod, "Show REDmod deployment steps", "Re-deploy REDmods from this active MO2 profile, then re-check conflicts.")]
    [DataRow(ArchiveOrderProblemLane.Combined, "Show repair steps", "Repair the named legacy archive-order entries and re-deploy REDmods, then re-check conflicts.")]
    public void ActionLabelNamesTheBlockingOwner(ArchiveOrderProblemLane lane, string expectedLabel, string expectedInstruction)
    {
        ArchiveOrderEvidence evidence = new(ArchiveOrderEvidenceKind.Unresolved, null, null, "blocked") { ProblemLane = lane };

        Assert.AreEqual(expectedLabel, ArchiveOrderGuidance.ActionLabel(evidence));
        Assert.AreEqual(expectedInstruction, ArchiveOrderGuidance.Instruction(evidence));
    }

    [TestMethod]
    public void VortexGuidanceUsesDeploymentLanguage()
    {
        ArchiveOrderEvidence evidence = new(ArchiveOrderEvidenceKind.Unresolved, "Vortex", null, "blocked") { ProblemLane = ArchiveOrderProblemLane.Redmod };

        Assert.AreEqual("Deploy the active Vortex profile, then re-check conflicts.", ArchiveOrderGuidance.Instruction(evidence, ModManagerKind.Vortex));
    }

    [TestMethod]
    public void ManualGuidanceUsesRedmodDeploymentLanguage()
    {
        ArchiveOrderEvidence evidence = new(ArchiveOrderEvidenceKind.Unresolved, "Game directory", "MO_REDmod_load_order.txt", "blocked") { ProblemLane = ArchiveOrderProblemLane.Redmod };

        Assert.AreEqual("Deploy REDmods with REDlauncher or REDmod, then re-check conflicts.", ArchiveOrderGuidance.Instruction(evidence, ModManagerKind.Manual));
    }

    [TestMethod]
    public void PreparedLegacyRepairOpensTheApplyStep()
    {
        ArchiveOrderEvidence evidence = new(ArchiveOrderEvidenceKind.Unresolved, "Alpha", "modlist.txt", "blocked")
        {
            ProblemLane = ArchiveOrderProblemLane.Legacy,
            MissingEntries = ["Alpha.archive"],
            SourceFingerprints = new Dictionary<string, string> { ["modlist.txt"] = new string('a', 64) }
        };

        Assert.IsTrue(ArchiveOrderGuidance.OpensPreparedOrder(evidence));
        Assert.AreEqual("Review repair draft", ArchiveOrderGuidance.ActionLabel(evidence));
    }

    [TestMethod]
    public void UnreadableLegacyOrderShowsInstructionsInsteadOfPromisingARepair()
    {
        ArchiveOrderEvidence evidence = new(ArchiveOrderEvidenceKind.Unresolved, "Alpha", "modlist.txt", "blocked") { ProblemLane = ArchiveOrderProblemLane.Legacy };

        Assert.IsFalse(ArchiveOrderGuidance.OpensPreparedOrder(evidence));
        Assert.AreEqual("Show repair steps", ArchiveOrderGuidance.ActionLabel(evidence));
    }
}

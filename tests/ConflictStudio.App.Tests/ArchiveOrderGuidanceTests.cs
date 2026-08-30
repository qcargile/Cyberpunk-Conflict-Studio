using ConflictStudio.App;
using ConflictStudio.Core;

namespace ConflictStudio.App.Tests;

[TestClass]
public sealed class ArchiveOrderGuidanceTests
{
    [TestMethod]
    [DataRow(ArchiveOrderProblemLane.Legacy, "Repair legacy load order", "Add every named active archive once to the legacy load order, then re-check conflicts.")]
    [DataRow(ArchiveOrderProblemLane.Redmod, "REDmod repair help", "Re-deploy REDmods from this active MO2 profile, then re-check conflicts.")]
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
}

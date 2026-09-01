using ConflictStudio.Core;

namespace ConflictStudio.App;

public static class ArchiveOrderGuidance
{
    public static string ActionLabel(ArchiveOrderEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.Kind != ArchiveOrderEvidenceKind.Unresolved) return evidence.IgnoredEntries.Length > 0 ? "Clean inactive entry" : "View load order";
        if (evidence.IsRepairableLegacyOrder) return "Review repair draft";
        return evidence.ProblemLane switch
        {
            ArchiveOrderProblemLane.Redmod => "Re-deploy REDmods",
            ArchiveOrderProblemLane.Combined => "Show repair steps",
            _ => "Repair legacy load order"
        };
    }

    public static string Instruction(ArchiveOrderEvidence evidence, ModManagerKind managerKind = ModManagerKind.Mo2)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.IsRepairableLegacyOrder) return "Review the complete repair draft, adjust it if needed, then apply and verify it.";
        if (managerKind == ModManagerKind.Vortex && evidence.ProblemLane is ArchiveOrderProblemLane.Redmod or ArchiveOrderProblemLane.Combined) return evidence.ProblemLane == ArchiveOrderProblemLane.Combined ? "Repair the named legacy archive-order entries, deploy the active Vortex profile, then re-check conflicts." : "Deploy the active Vortex profile, then re-check conflicts.";
        if (managerKind == ModManagerKind.Manual && evidence.ProblemLane is ArchiveOrderProblemLane.Redmod or ArchiveOrderProblemLane.Combined) return evidence.ProblemLane == ArchiveOrderProblemLane.Combined ? "Repair the named legacy archive-order entries, deploy REDmods with REDlauncher or REDmod, then re-check conflicts." : "Deploy REDmods with REDlauncher or REDmod, then re-check conflicts.";
        return evidence.ProblemLane switch
        {
            ArchiveOrderProblemLane.Redmod => "Re-deploy REDmods from this active MO2 profile, then re-check conflicts.",
            ArchiveOrderProblemLane.Combined => "Repair the named legacy archive-order entries and re-deploy REDmods, then re-check conflicts.",
            _ => "Add every named active archive once to the legacy load order, then re-check conflicts."
        };
    }
}

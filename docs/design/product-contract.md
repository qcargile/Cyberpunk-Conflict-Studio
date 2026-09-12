# Product contract

Conflict Studio inspects one Cyberpunk 2077 profile and explains archive precedence, file overwrites and supported code interactions. It shows the evidence and source locations, lets the user retain reviews and notes, and exports a local support report. It does not generate mods, import game measurements or decide compatibility.

## Current ownership

| Surface | Owner | State and boundary |
|---|---|---|
| Scan and coverage | ProfileScanCoordinator and MainWindow.LoadReceipt | Captured evidence for the selected manager installation/profile; replaced on rescan or invalidation. |
| Archives and loose files | Existing archive-order and virtual-file analysis | Precedence evidence. Archive edits retain preview, backup and Undo. |
| Source investigation | CodeComparisonWindow and CodeSourceReader | Verified excerpts, contributor/reference navigation and editor handoff. Source loading is canceled on close or replacement. |
| Reviews and notes | EvidenceDecisionStore and EvidenceNoteStore | User-authored profile decisions. They do not prove compatibility. |
| Changes and view preferences | MainWindow.History, MainWindow.Views and their stores | Saved scan comparison, baseline and presentation state. Pending writes are included in close flushing. |
| Support reports | SupportCapsuleBuilder and SupportCapsuleWriter | Local privacy-filtered reports of existing evidence and decisions. No generated executable code or automatic upload. |

The manager owns mod activation and deployment. Game execution and diagnosis remain outside the application. Source analysis of literal runtime writes remains part of conflict inspection; it is not an in-game monitoring feature.

## Runtime-feature removal

The runtime-check subsystem introduced after baseline `b214bd42d8fbaecd93b7fe5bb248467af6cc5cb5` is withdrawn at the user's request. Its tab, preparation actions, package generator, importer, run store and support-export integration are removed, including the older support-owned probe generator.

The remaining App history/view/review integration returns to that baseline. Existing saved runtime files and previously exported packages are not erased or loaded. No migration or background cleanup service is added. An obsolete fifth-tab preference uses the existing invalid-tab fallback.

| Invariant | Deciding check |
|---|---|
| Support export creates reports only | SupportCapsuleWriter tests assert report files and no probe/mod output. |
| Runtime UI and state ownership are gone | No RuntimeProbe or RuntimeInvestigation production types/call sites; four existing tabs remain. |
| Source comparison clarity survives removal | CodeComparisonWindow tests retain primary evidence and optional blue surrounding-text differences. |
| Reviews, notes and Changes remain usable | Existing profile-investigation, view-state and full App tests. |
| Unsupported RED files are not treated as analyzed | Existing coverage diagnostics remain; no RED parser was introduced. |

## Product direction

The next work should improve conflict accuracy, explanations, source navigation, responsiveness and error recovery within the existing inspector. A roadmap milestone does not justify another subsystem. New workflows require a concrete user problem and a proportionate solution.

This is a removal contract, not a public-release certification. The executable version and public 0.5.0 ZIP are not changed by the removal.

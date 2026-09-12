# Profile investigation

This implements the approved 0.6 roadmap stage. It adds continuity between scans using recorded evidence, personal notes and profile view preferences. The executable version remains 0.5.0 until release preparation.

## Scan changes

Changes compares the current receipt with the previous scan or one explicitly pinned baseline. Rows distinguish new, changed, no longer detected and unchanged findings. Before and Now show recorded summaries and values. Open current finding navigates to the current code case or archive filter. Removed findings cannot open current files as historical evidence.

Tool and analyzer versions are recorded on new receipts. Older receipts remain readable; missing versions are described as unrecorded. Coverage differences can account for disappearing findings, so disappearance is never described as a fix.

Each installation/profile retains two rolling scans and two drift files. The independently retained baseline survives those limits until Pin/Replace or Clear. Invalid or foreign baselines remain untouched until an explicit action. The existing receipt store validates and atomically replaces baseline files.

## Reviews and notes

Accepted reviews retain their existing schema and exact evidence binding. A changed finding shows the previous reason and whether evidence or providers changed. That context does not accept the new finding. The analysis category remains visible beside a separate review status.

Save note keeps a finding open. Notes use a separate evidence-notes.json under the existing local decisions directory. Updating or clearing a stale note replaces or removes the displayed note without reviving older text. Notes from other installations or profiles do not apply. Support exports include relevant notes through the existing private-path redactor.

## Presentation and ownership

Coverage is visible beneath the scan controls, with expandable input counts and limitations. Columns, sorting, code/archive filters, detail split, summary expansion, tab and history selection are saved per manager, installation and profile. Reset view restores defaults without touching reviews, baselines or archive order.

MainWindow owns current selection and cancellation. History and note I/O run off the dispatcher. Receipt loads reject stale completion after a profile change or closing. View writes are debounced and serialized; closing waits for pending user writes. No recurring disk work runs while the view is idle.

Local data remains under the existing application-data directory: receipts for history/baselines, decisions for reviews/notes, and views for presentation. This stage adds no source editing, automatic compatibility decisions, new parsers, runtime import or historical source snapshots.

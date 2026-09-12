# Product contract

## Scope

Conflict Studio helps a user inspect one Cyberpunk 2077 profile, trace the evidence behind an overlap, retain their own decisions, and collect an optional measurement when source inspection leaves a question unanswered. It does not decide compatibility or identify the cause of a gameplay bug from an overlap or a single measurement.

This contract records the runtime-check subsystem addition against baseline `b214bd42d8fbaecd93b7fe5bb248467af6cc5cb5`. The runtime-check implementation, tests and supporting documentation in the same change are the architecture delta. Existing scan, archive and source-inspection capabilities are mapped here to identify their interfaces; this document does not recertify their entire implementation or authorize a release. The package version remains 0.5.0.

Product depth is PD2 within profile investigation: several useful workflows share the current scan, profile identity and local persistence. Adding another shared state owner, a source editor, automatic installation or a compatibility decision system requires a new composition decision.

## Owners and interfaces

| Subsystem | Owner and inputs | Output and lifetime | Connection to runtime checks |
|---|---|---|---|
| Profile scan and coverage | `ProfileScanCoordinator` reads the selected manager profile and effective sources. `MainWindow.LoadReceipt` owns the current receipt. | A captured receipt and work queue; replaced or invalidated when the selected setup changes. | Supplies manager, installation, profile, target, providers and source-evidence identity. A loaded observation cannot rewrite a scan. |
| Archive and loose-file precedence | Existing archive-order and virtual-file analysis, exposed by the Archives and Code interactions views. | Ownership evidence; existing archive edits retain their preview/backup/undo workflow. | No automatic runtime check is invented for a file overwrite. Runtime package generation does not change archive order or deployed files. |
| Source investigation | `CodeComparisonWindow`, source reader, contributor/reference navigation and editor handoff. | Verified source excerpts; canceled and released when the view closes or selection changes. | Shows source declarations and writes, while runtime checks show a separately recorded value. Blue surrounding-text differences do not become new findings. See [source investigation](source-investigation.md). |
| Reviews, notes, history and views | Existing decision/note/history/view stores and MainWindow partials. | Profile-bound local state; pending user writes are included in close flushing. | Runtime results never accept a review, delete a note or replace a baseline. Runtime writes join the same pending-write wait. See [profile investigation](profile-investigation.md). |
| Runtime investigation | `RuntimeProbeManifestBuilder`, `RuntimeInvestigationStore` and `MainWindow.RuntimeChecks`. | Selected-finding package, saved run and imported receipt. Current/stale status is derived against the current scan. | Owns its run file and UI state only. Package generation and import are explicit user actions. See [runtime checks](runtime-checks.md). |
| Support export | `SupportCapsuleBuilder` and `SupportCapsuleWriter`, invoked by `MainWindow.ExportClicked`. | Local privacy-filtered report; no automatic transmission. | Includes imported observations for the current manager/install/profile and retains stale labels. Export waits for runtime loading/writing. |

The mod manager owns activation, deployment and removal of an installed probe. CET owns callbacks and the game's TweakDB supplies observed values. Windows owns the selected editor and folder/file launch. Conflict Studio does not substitute for any of those owners.

## Composition and event order

| Pair or cluster | Rule | Deciding implementation and checks |
|---|---|---|
| Scan + runtime state | COMPOSE through the captured receipt. Loading a different profile clears prior runs immediately; old completions cannot populate the new view. | `BeginRuntimeChecks`, `IsCurrentRuntimeContext`; `ProfileSwitchClearsThePreviousRuntimeRunBeforeTheNextReadCompletes`. |
| Finding + request builder | COMPOSE through one current work item. Request providers must belong to its full bound contributor set; order changes alone do not expire it. | Selected `RuntimeProbeManifestBuilder.Build`; selected-provider tests and the independent provider/hash freshness cases in `RuntimeInvestigationStoreTests`. |
| Runtime write + profile load + close | COMPOSE through `_runtimeWriteTask` and `PendingUserStateWrites`. Reads wait for writes; close waits for pending user state. UI completions require the original receipt/revision. | `QueueRuntimeWrite`, `LoadRuntimeChecksAsync`, `FlushInvestigationBeforeClose`; pending-write/profile/export/closing tests in `ProfileInvestigationWindowTests`. |
| Runtime + review/note/baseline | NO-CONTACT with decision mutation. They share the profile boundary and close wait, not meaning or file ownership. | Runtime methods only call their store; their generated/imported types are not `EvidenceDecision` or `EvidenceNote`. Existing review paths remain in `MainWindow.ReviewNotes` and history/view partials. |
| Runtime + support export | COMPOSE through `RuntimeInvestigationView[]`. Do not export another profile's observations or present stale evidence as current. | `UpdateSupportExportAvailability`, `SupportCapsuleBuilder.Build`; runtime export tests cover filtering, stale labels and path redaction. |
| Package + manager + CET | YIELD installation and execution to the user and manager. CET performs one scheduled read batch; later values require a separate measurement. | Generated `RuntimeProbeBundleWriter.Lua` contains GetFlat reads, no database setters or installer action. Writer tests check instructions and framing; actual game execution is deferred below. |
| Imported file + saved run | COMPOSE only for the exact local run and manifest. Invalid identities or oversized inputs cannot replace the saved record. | `RuntimeProbeBundleStore.ValidateManifest`, `RuntimeInvestigationStore.Import`; foreign/tampered/legacy/size/state recovery tests. |

## User loops and recovery

| Loop | Signal and choice | Result and recovery | Status |
|---|---|---|---|
| Inspect sources | Select a finding, inspect its supporting pair, optionally compare surrounding text. | Amber evidence retains its meaning; blue differences are context only. Disable the option without changing the finding. | PROVED by comparison tests. |
| Prepare a check | Choose Check in game for one finding and read its requested observations. | Generate a package only when supported requests exist; unsupported findings remain available for source inspection. | PROVED by explicit-preparation WPF test. |
| Collect and import | Install the separate package, collect its log, optionally supply measured manual answers. | Observed, failed, missing and manual states remain distinct. An incomplete log can be replaced with a complete matching log. | Import handling PROVED by fixtures; actual game collection DEFERRED. |
| Revisit after change | Rescan after changing providers or source evidence. | Older runs become stale and cannot accept new imports as current. Generate a new package. | PROVED by independent provider-only, source-only and combined-change cases. |
| Finish or report | Disable/remove the installed probe; export observations or forget the local run. | Forget leaves package files intact. Export is local and privacy-filtered. | Local handling PROVED; manager uninstall is externally owned. |

## Workload and lifecycle

| Surface | Trigger, ownership and bound | Idle, close and recovery |
|---|---|---|
| Run-state read | Once per loaded receipt and after an explicit mutation, on a background task. At most 8 MiB is read; the mutex wait is bounded to 30 seconds. | No polling. Profile replacement cancels pending waits and rejects old completions. Invalid state is preserved separately and the run list recovers empty. |
| Request preparation | Explicit action for one selected finding; builder scans the captured analysis on a background task. | No per-frame or per-keystroke preparation. A changed selection invalidates prepared requests. |
| Package and import writes | Serialized user actions. At most 256 requests, 64 providers per request, 1 MiB manifests/manual-answer files, 4 MiB logs and 16,384 characters per result. | Atomic state replacement retains at most 50 newest whole runs within 8 MiB. A newest update that cannot fit is rejected before replacement. Close waits for the write. |
| UI retention | Current-profile run metadata and selected request rows; requests/results are wrapped in scrollable tables. | Invalidating the receipt clears the views; closing cancels read completion. No autonomous runtime monitoring is added. |
| Generated CET probe | One batch after five seconds of update time, at most one GetFlat per automatic request. | Subsequent callbacks return immediately after the batch. Removing/disabling the separate mod removes its callbacks on the next appropriate manager/game lifecycle. No claim of zero callback cost while it remains installed. |

The structural bounds are verified by code and negative tests, not a universal latency claim. Normal and minimum WPF renders were checked at 1660x950 and 1180x720 using a real two-request camera finding and a controlled imported-value fixture. The tested profile's package remained outside the game and was not executed.

## Invariants and deciding evidence

| Invariant | Oracle or falsifier | Status |
|---|---|---|
| Changed source evidence expires a saved observation independently of provider changes. | The identical freshness oracle passes all three control cases; removing only the source-hash comparison fails the source-only case (expected Stale, actual Current). | PROVED |
| Foreign, malformed or oversized inputs cannot be displayed as valid current results. | Manifest tampering, foreign-run, null request/binding, invalid enum/observation ID, byte-limit and corrupt-state fixtures reject or preserve/recover the input. | PROVED for the supported input paths |
| Current and legacy identities retain their contracts. | Exact pre-change schema-2 fixture reads and writes its support receipt; selected schema 3 validates the full binding/payload identity. | PROVED |
| A callback from an earlier profile cannot populate the current run view. | Blocked-write/profile-switch fixture checks immediate clearing and current-profile filtering after completion. | PROVED |
| Closing or exporting cannot outrun pending runtime writes. | WPF close/export tests hold a pending write and assert the required wait/disabled state. | PROVED |
| Forgetting a run does not uninstall or delete the exported package. | End-to-end generation/import/forget WPF fixture asserts package preservation. | PROVED |
| Runtime evidence does not establish compatibility or accept reviews. | Separate runtime store, support boundary text and unchanged decision consumers; failed/missing/manual observations remain explicit. | PROVED for local state behavior |
| Actual CET output matches the generated protocol in a running user profile. | Requires installing the opt-in package, collecting the matching log and observing the requested moment. Offline synthetic logs cannot establish this. | DEFERRED to user-run validation |

## Shortfalls and delivery boundary

RED `.tweak` parsing remains deferred for the concrete grammar/value-resolution gaps in [runtime checks](runtime-checks.md). The bounded current-profile inventory does not prove those files conflict-free. Arbitrary dynamic Lua interpretation, automatic installation, database writes, a patch editor and automated compatibility judgments remain excluded.

The completed static and WPF checks support a development test handoff. They do not certify a public release or replace an actual game-run receipt. Release preparation must reconcile the game observation, version, package and deployment evidence separately; this change does not alter the public 0.5.0 ZIP.

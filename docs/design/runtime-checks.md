# Runtime checks

Runtime checks implement the optional investigation loop from the 0.8 roadmap stage. They connect a selected source finding to a generated CET check and an imported observation. They do not change review decisions or resolve a finding automatically.

## Evidence and ownership

Each generated run records its manager, installation, profile, finding target, providers and source-evidence hash. Imports must match a locally generated run and its manifest. Changed evidence makes an earlier observation stale. Observations remain separate from the source analysis and cannot establish execution order, which mod ran last, compatibility or a gameplay cause.

The generated CET mod only reads the requested TweakDB fields. It records them once, five seconds after CET starts updating. That moment does not establish later gameplay values. Failed reads, missing output and unanswered manual checks remain visible. A manual answer is the user's measurement, not an automatic result.

Packages remain outside the game installation until the user installs one as a separate mod. The application neither installs nor runs them. Package instructions include removal or disabling after collection. Forgetting a local run removes the application's record; it does not uninstall the probe or delete exported files.

Local run history retains at most 50 runs. File reads have explicit size limits, state replacement is atomic, and malformed state is preserved separately for recovery. Support reports include observations for the selected installation and profile, with stale status and private paths filtered.

## RED format decision

The bounded RED `.tweak` spike is closed with parsing deferred. Existing per-file unsupported diagnostics remain. The inspected Standard Profile contained 37 physical RED files and 31 effective files after manager precedence. Its direct-record-field subset contained no cross-provider target overlap; comparison of top-level record names also found no YAML/RED intersection. These checks exclude nested and semantic interactions and do not establish that all RED files are conflict-free.

A useful parser needs grounded package and `using` resolution, cross-format value normalization, an explicit treatment of anonymous inline records and conditional sections, and equal/different YAML+RED acceptance fixtures. A partial parser would not meet those requirements. No new RED coverage claim is made.

## Source comparison presentation

Compare surrounding text remains off by default. Its blue marks indicate ordinary differences between the visible excerpts. Amber marks retain the selected finding's supporting evidence. More context may change blue marks without changing the finding.

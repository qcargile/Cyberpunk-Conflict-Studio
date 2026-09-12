# Source investigation

This implements the approved 0.7 roadmap stage. Source comparisons retain their typed supporting pairs while exposing the other recorded contributors to the target. A contextual statement cannot replace a supporting participant merely because it changes the same property.

## Investigation flow

The code list can filter by two mods. Matching findings retain every provider, including third-party contributors. The pair survives rescans and is saved in the existing profile view. Previous/next finding follows the current filtered and sorted list. If a comparison is open, navigation opens the next finding's comparison when one is available.

The comparison overview identifies each operation, value preview, provider, source line and relationship to the selected issue. Context rows can be inspected, but only supporting operations can be selected for comparison. Display order does not imply execution order. The overview collapses when entering the compact layout so the two code panes and their actions remain visible.

Inspect / find opens one verified source document. Literal search covers that file and records line and column. Previous/next match and recorded-location actions move within it. Search results are labeled as context, and matches beyond shortened lines direct the user to the editor. Text sizing applies to the rendered document without rereading the file.

## Literal references

TweakXL array member additions/removals and base declarations can lead to exact local record definitions. Array-copy operands are flat paths, not record IDs, and are excluded. Aliases, generated records and dynamic values do not become guessed literal definitions. Exact duplicate definitions are all available up to the declared bounds; no row is described as the winner.

Definitions are captured from the existing parser's record boundaries and manifest hashes. Following a reference shows the exact definition block first. Back returns to the original source; more context and whole-file search remain available. The displayed fields are declarations, not computed gameplay results.

The index admits at most 4,096 source operations, 16 definitions per reference and 2,048 definition blocks. Overflow reports its limits explicitly. The representative Standard Profile had 1,401 eligible operations, 898 references and 830 definitions, requiring 844,122 bytes of serialized metadata without truncation. It included both armor definitions from the accepted walkthrough. This measurement is a capacity check, not a performance guarantee for other profiles.

Code cache schema 9 carries the index. Admission validates its arrays, identities, ranges, hashes, counts and limit flags, and checks reference bindings against captured source values. It then derives the bounded index again from current effective TweakXL sources and requires an exact match, including missing definitions and ambiguities. This reuses the same winner/exclusion selection, parser and capture factory as a fresh scan. RedScript and CET analysis can still use the code cache. Invalid metadata rebuilds; resolution also rejects a mismatched source/reference binding. Historical receipts omit this current-source index.

## Ownership and limits

The comparison and inspector share the existing two-read gate. Every preview uses CodeSourceReader's hash, 8 MiB and 200,000-line checks. A comparison retains only its selected documents; an inspector retains one document and up to 32 metadata-only Back entries. Closing or replacing a source cancels work and rejects stale completion.

Previews show at most 400 lines and shorten lines beyond 1,600 characters. Search retains at most 500 matches and accepts at most 512 query characters. It runs only when requested, with cancellation. No idle polling or source-file mutation is added.

[Editor integration](../editor-integration.md) uses explicitly chosen normal Windows, VS Code or Notepad++ opening. Known argument forms support line selection, and launch failures retain normal file opening with an explicit fallback status. No shell templates are stored or executed.

This stage does not add a general reference graph, runtime importer, RED .tweak parser, patch editor, version bump or publication.

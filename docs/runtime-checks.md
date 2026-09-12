# Optional in-game checks

Some source findings leave a question that a game database measurement can help answer. Runtime checks generate a small, read-only CET mod for those findings and let you import its output. They do not mark a conflict resolved.

1. Select a code finding and open its runtime check. Read the requested observations before generating a package. Unsupported findings cannot generate a check.
2. Generate the package, then read its `README.txt`. Install the package's `bin` folder as a separate mod in the same manager profile. Conflict Studio does not install it for you.
3. Launch that profile with CET enabled and remain at the main menu for at least 10 seconds. The probe reads the requested fields once, five seconds after CET starts updating.
4. Import the matching ConflictStudioProbe log from Runtime checks. If the package asks for a later manual measurement, copy `manual-answers.example.json`, fill in the measured answers without changing their IDs, and import that file with the log. Leave answers empty when you have no measurement.
5. Remove or disable the separate ConflictStudioProbe mod after collecting the results.

An observed value describes that moment. It does not identify which mod wrote it last or establish its value throughout gameplay. A failed read or missing result leaves the question unanswered. Manual checks may need the mod author's help to identify a trigger and measure a hidden value.

Runs belong to one installation, profile and source finding. Rescanning changed sources can mark older results stale. Generate a new package for the new evidence; stale results remain historical context. Forgetting a local run removes its saved record only, so exported files and any installed probe still need separate cleanup.

Saved runs live in `runtime/runtime-investigations.json` under Conflict Studio's application-data folder. The application retains the latest 50 runs. Support exports include this profile's imported observations and their stale status, with private paths filtered. Nothing is uploaded automatically.

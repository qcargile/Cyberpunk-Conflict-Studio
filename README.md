# Cyberpunk Conflict Studio

Conflict Studio is a Windows desktop tool that shows which Cyberpunk 2077 mods overwrite one another in the active MO2 or Vortex profile.

This is a beta. It separates definite conflicts from ordinary overwrites, compatible changes, and cases that still need an in-game check.

## Credit

Conflict Studio was heavily inspired by rfuzzo's [Archive Conflict Checker](https://www.nexusmods.com/cyberpunk2077/mods/11126). Please endorse the original tool rather than this one. rfuzzo's source is available in [Cyberpunk-utility](https://github.com/rfuzzo/Cyberpunk-utility).

Conflict Studio is a separate implementation and contains no copied code or assets from the original tool.

## Features

- Profile-aware archive conflicts for MO2 and Vortex.
- Winning, losing, identical, unresolved, and unique packed resources.
- Multi-selection archive reordering with live winner preview, accelerated edge scrolling, and wheel scrolling during drag.
- Verified archive-order writes with backup and undo.
- Active RedScript, CET Lua, TweakXL, ArchiveXL, and loose-file interaction analysis.
- Clear reasons, next steps, saved reviews, and privacy-filtered support reports.

## Installation

Download the release ZIP manually.

### MO2

1. Extract the ZIP outside the MO2 instance's `mods` directory.
2. Add `Conflict Studio\ConflictStudio.exe` as an executable.
3. Set **Start in** to the MO2 instance directory containing `ModOrganizer.ini`.
4. Set arguments to `--manager mo2 --profile current`.
5. Add `ConflictStudio.exe` to **Settings > Workarounds > Executables Blacklist**, restart MO2, and choose **Continue** when launching it.

MO2's program folder, instance folder, profiles, mods, and overwrite may all be on different drives. **Start in** must point to the instance folder containing `ModOrganizer.ini`. Conflict Studio reads MO2's `mod_directory`, `profiles_directory`, and `overwrite_directory` settings from that file.

### Vortex

1. Open **Extensions** in advanced mode.
2. Drop the release ZIP onto the extension installer.
3. Restart Vortex and deploy the active Cyberpunk profile.
4. Launch Conflict Studio from **Cyberpunk 2077 > Tools**.

## Troubleshooting

- A first scan can take several minutes on very large profiles because archive contents are fingerprinted. Later scans reuse cached fingerprints. If a cached fingerprint no longer matches, Conflict Studio rebuilds the archive fingerprints once and repeats the scan. A second mismatch is reported with the fresh expected and final hashes.
- Conflict Studio is intentionally excluded from MO2's VFS. It reads the physical provider folders and rebuilds the effective order so it can name the winning mod.
- If the configured MO2 mods directory is missing, the scan stops with the resolved path and points back to **MO2 Settings > Paths**.
- An incomplete archive `modlist.txt` opens as a repair draft. Valid listed entries keep their order, duplicate or inactive entries are removed, and missing active archives are added for review before Apply.
- ArchiveXL, TweakXL, or source files that cannot be fully parsed are listed under Support as scan limitations; they are not automatically mod conflicts.

## Antivirus warnings

Conflict Studio is unsigned and self-contained, so SmartScreen or antivirus software may warn about a new executable. Download releases only from this repository or the official Nexus page and compare the published SHA-256 checksum.

## Build

The repository uses the .NET SDK selected by `global.json`.

```powershell
dotnet test --project tests\ConflictStudio.Core.Tests\ConflictStudio.Core.Tests.csproj --configuration Release
dotnet test --project tests\ConflictStudio.App.Tests\ConflictStudio.App.Tests.csproj --configuration Release
node --test integrations\vortex\bridge.test.js integrations\vortex\index.test.js
.\scripts\publish-win-x64.ps1 -Version 0.1.8
```

The public release contains one self-contained `ConflictStudio.exe`; users do not need to install .NET separately.

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

### Vortex

1. Open **Extensions** in advanced mode.
2. Drop the release ZIP onto the extension installer.
3. Restart Vortex and deploy the active Cyberpunk profile.
4. Launch Conflict Studio from **Cyberpunk 2077 > Tools**.

## Antivirus warnings

Conflict Studio is unsigned and self-contained, so SmartScreen or antivirus software may warn about a new executable. Download releases only from this repository or the official Nexus page and compare the published SHA-256 checksum.

## Build

The repository uses the .NET SDK selected by `global.json`.

```powershell
dotnet test --project tests\ConflictStudio.Core.Tests\ConflictStudio.Core.Tests.csproj --configuration Release
dotnet test --project tests\ConflictStudio.App.Tests\ConflictStudio.App.Tests.csproj --configuration Release
node --test integrations\vortex\bridge.test.js integrations\vortex\index.test.js
.\scripts\publish-win-x64.ps1 -Version 0.1.7
```

The public release contains one self-contained `ConflictStudio.exe`; users do not need to install .NET separately.

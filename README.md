# Cyberpunk Conflict Studio

Conflict Studio shows what is happening inside one Cyberpunk 2077 mod profile. It reads MO2, Vortex, or the deployed game folder and traces archive order, loose files, REDmods, RedScript, CET hooks, TweakXL changes, and ArchiveXL manifests.

This is a beta. Keep a backup and read the preview before changing archive order.

## What it does

- Shows which archive wins each packed file.
- Finds loose files installed by more than one active mod.
- Separates replaced methods, wrappers, CET callbacks, and TweakXL changes instead of calling every shared target a conflict.
- Previews archive-order changes before writing anything.
- Backs up and verifies `modlist.txt`, then offers Undo.
- Exports a privacy-filtered support report when something goes wrong.

Conflict Studio does not decide that two mods are compatible or incompatible. A shared method or record is only a place to inspect. Compile results, the loaded game state, and an in-game reproduction still matter.

## How to read code results

- **Confirmed conflicts** identify one exact boundary where active changes cannot coexist.
- **Needs review** identifies competing changes and the specific outcome that remains unresolved. It is not a compatibility verdict.
- **Identical overlaps** contains loose files with identical bytes or equivalent parsed JSON.
- File ownership proves which deployed file is selected. It does not prove that the selected file is the version you intended.
- Competing values prove that active sources assign different values. They do not prove the final in-game value without a runtime observation.

Conflict Studio can read literal CET callbacks and deployed DLL ownership. It does not resolve dynamically constructed CET callbacks or inspect native DLL hooks and internal behavior.

A default followed by the same mod's settings update is not a conflict. Literal-value checks cover different numeric or boolean values requested by different providers, including two runtime writers. They also cover literal array clears that oppose another provider's additions. Separate source components adding and removing the same entries are opposing changes, even when their execution order is known.

Method warnings cover competing replacements and duplicate added members. Shared wrappers, early returns, and CET callbacks remain technical evidence: deciding whether their conditions and side effects conflict requires source inspection. No warning does not mean compatible.

Base-record links, ordinary settings updates, and other background relationships also remain in technical evidence rather than standalone cases. They do not establish a conflict or prove compatibility.

Support reports record which files were checked and which could not be read or analyzed. RED `.tweak` files are listed but not parsed. RedScript checks cover annotated declarations and supported TweakDB writes with literal targets, not every symbol in a script.

CET checks follow literal `require`, `dofile`, and `loadfile` paths from the selected `init.lua`. If loading cannot be resolved, the mod's other files stay in the scan as possible inputs. This does not prove that a function runs. Scans do not read framework logs or determine whether a native plugin can load.

See [GitHub releases](https://github.com/qcargile/Cyberpunk-Conflict-Studio/releases) for downloads and version history.

## Install

Download `Cyberpunk-Conflict-Studio-0.4.1-Nexus.zip` from Nexus Mods.

### Vortex

1. Switch Vortex to advanced mode and open Extensions.
2. Drop the ZIP onto the extension installer.
3. Restart Vortex, activate your Cyberpunk profile, and deploy it.
4. Launch Conflict Studio from Cyberpunk 2077 > Tools.

### Mod Organizer 2

1. Extract the ZIP outside the instance's `mods` folder.
2. Add `ConflictStudio.exe` from the extracted folder as an MO2 executable.
3. Set **Start in** to the folder containing `ModOrganizer.ini`.
4. Use `--manager mo2 --profile current` as the arguments.
5. Add `ConflictStudio.exe` to MO2's executable blacklist so it reads the real files instead of the virtual filesystem.

If updating from the older package layout, update MO2's executable path to the copy now at the top level of the extracted folder.

Do not scan the same game through MO2 while Vortex files are still deployed. Purge Vortex first or use the Vortex profile.

### Manual

Run `ConflictStudio.exe` from the extracted folder, choose Manual, and select the Cyberpunk 2077 folder.

## Build

The public package is a self-contained Windows x64 executable:

```powershell
.\scripts\publish-win-x64.ps1 -Version 0.4.1
```

The script writes the release outside the repository and verifies the package manifest and ZIP contents. Release settings live in `release/0.4.1.json`.

## Credits

Conflict Studio was inspired by [rfuzzo's Archive Conflict Checker](https://www.nexusmods.com/cyberpunk2077/mods/11126). Please endorse the original tool. Conflict Studio is a separate implementation and does not copy its code or assets.

Cyberpunk 2077 belongs to CD Projekt Red. Thanks to psiberx and the Cyberpunk modding community for the frameworks and documentation that make tools like this possible.

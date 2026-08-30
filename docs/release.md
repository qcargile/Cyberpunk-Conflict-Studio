# Release workflow

Cyberpunk Conflict Studio ships as a self-contained Windows x64 desktop package. The repository remains the source tree; release output is staged under `%LOCALAPPDATA%\Cyberpunk Conflict Studio\releases` unless an external `-OutputRoot` is supplied.

## Prerequisites

- Windows x64
- .NET SDK version selected by `global.json`
- A clean enough working tree to identify the release commit

## Publish

From the repository root:

```powershell
.\scripts\publish-win-x64.ps1 -Version 0.1.7
```

The command performs these checks in order:

1. Reads `docs\release\0.1.7.json`.
2. Restores the solution.
3. Builds the WPF application in Release configuration for `win-x64`.
4. Runs the Core and App test projects.
5. Publishes a self-contained `win-x64` application to an external staging directory.
6. Runs the Vortex bridge behavior and syntax tests.
7. Places the Vortex extension entry points beside the single application payload.
8. Writes `package-manifest.json`, including relative path, byte length, and SHA-256 for each package file.
9. Rejects PDBs, version drift, missing notices, nested ZIPs, or manager-entry-point drift.
10. Produces and hash-verifies `Cyberpunk-Conflict-Studio-0.1.7-Nexus.zip` against the complete package directory.

The default output is `%LOCALAPPDATA%\Cyberpunk Conflict Studio\releases\0.1.7\win-x64`, with the complete Nexus archive beside it. The script rejects an output path inside the repository and refuses to delete an existing directory or archive unless `-Force` is supplied.

The Nexus archive contains only the public runtime payload:

- `Conflict Studio\ConflictStudio.exe` is one self-contained Windows executable used by both managers.
- `info.json`, `index.js`, and `bridge.js` make the same archive installable through Vortex Extensions.
- `ConflictStudio.png` supplies the automatically registered Vortex Tools entry icon.
- `Licenses` contains the MIT project license, dependency notices, and the self-contained runtime terms.

There are no nested ZIPs, loose runtime DLLs, or second self-contained CLI/runtime copy.

For MO2, manually extract the package outside the instance's `mods` directory and register `ConflictStudio.exe` as a custom executable. MO2 rewrites every executable stored under `mods` into its virtual game path regardless of whether the mod is enabled. For Vortex, install the same manually downloaded ZIP through Extensions; the bridge registers the bundled executable in Cyberpunk 2077 Tools.

## Verify

```powershell
.\scripts\verify-package.ps1 -PackageRoot '%LOCALAPPDATA%\Cyberpunk Conflict Studio\releases\0.1.7\win-x64' -ArchivePath '%LOCALAPPDATA%\Cyberpunk Conflict Studio\releases\0.1.7\Cyberpunk-Conflict-Studio-0.1.7-Nexus.zip'
```

Verification uses the manifest beside the unpacked release directory, checks every listed file and hash, and confirms that the downloadable ZIP contains only those public files. The publisher also writes a `.sha256` file beside the ZIP. A successful run prints `PACKAGE PASS`; any mismatch exits non-zero.

## Release checklist

- Run the publish script with the intended version.
- Inspect the generated manifest and package file list.
- Run verification independently from the publish command.
- Launch the published executable from the package directory.
- Keep the package directory and manifest together when handing off the release.

The application can still be run from Visual Studio or `dotnet run` for development. A development build is not a release artifact.

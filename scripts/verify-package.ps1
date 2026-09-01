[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackageRoot,
    [string]$MetadataPath,
    [string]$ArchivePath,
    [switch]$DirectoryOnly
)

$ErrorActionPreference = 'Stop'
$resolvedPackage = (Resolve-Path -LiteralPath $PackageRoot -ErrorAction Stop).Path
$manifestPath = Join-Path $resolvedPackage 'package-manifest.json'
if (-not $MetadataPath) { $MetadataPath = Join-Path (Split-Path -Parent $PSScriptRoot) "release\$([IO.Path]::GetFileName((Split-Path -Parent $resolvedPackage))).json" }
if (-not (Test-Path -LiteralPath $MetadataPath -PathType Leaf)) { throw "Release metadata was not found: $MetadataPath" }
$metadata = Get-Content -Raw -LiteralPath $MetadataPath | ConvertFrom-Json
if ($metadata.packageFormat -eq 'zip' -and -not $ArchivePath -and -not $DirectoryOnly) { throw 'ArchivePath is required to verify the downloadable ZIP. Use DirectoryOnly only for an intermediate staging check.' }
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Package manifest was not found: $manifestPath" }
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
if ($manifest.version -ne $metadata.version) { throw "Manifest version '$($manifest.version)' does not match metadata '$($metadata.version)'." }
if ($manifest.product -ne $metadata.product -or $manifest.runtime -ne $metadata.runtime -or $manifest.selfContained -ne $metadata.selfContained -or $manifest.channel -ne $metadata.channel -or $manifest.entryPoint -ne $metadata.entryPoint -or $manifest.packageFormat -ne $metadata.packageFormat -or $manifest.minimumOs -ne $metadata.minimumOs) { throw 'Package identity metadata does not match the release contract.' }
if ($manifest.sourceCommit -notmatch '^[0-9a-f]{40}$') { throw 'Package source commit is invalid.' }
if (-not (Test-Path -LiteralPath (Join-Path $resolvedPackage $manifest.entryPoint) -PathType Leaf)) { throw "Package entry point is missing: $($manifest.entryPoint)" }
$requiredManagerFiles = @('ConflictStudio.png', 'Licenses\Conflict-Studio-LICENSE.txt', 'Licenses\THIRD-PARTY-NOTICES.txt', 'Licenses\DOTNET-LICENSE.txt', 'Licenses\DOTNET-THIRD-PARTY-NOTICES.txt', 'info.json', 'index.js', 'bridge.js')
foreach ($relativePath in $requiredManagerFiles) { if (-not (Test-Path -LiteralPath (Join-Path $resolvedPackage $relativePath) -PathType Leaf)) { throw "Manager integration file is missing: $relativePath" } }
$bridgeInfo = Get-Content -Raw -LiteralPath (Join-Path $resolvedPackage 'info.json') | ConvertFrom-Json
if ($bridgeInfo.version -ne $metadata.version -or $bridgeInfo.id -ne 'conflict-studio-bridge') { throw 'The Vortex bridge identity does not match the release contract.' }
$actualFiles = @(Get-ChildItem -LiteralPath $resolvedPackage -File -Recurse | Where-Object Name -ne 'package-manifest.json' | ForEach-Object { $_.FullName.Substring($resolvedPackage.Length).TrimStart('\').Replace('\','/') } | Sort-Object)
$expectedFiles = @($manifest.files | ForEach-Object path | Sort-Object)
if ((ConvertTo-Json $actualFiles -Compress) -ne (ConvertTo-Json $expectedFiles -Compress)) { throw 'Package file inventory does not match the manifest.' }
$allowedFiles = @('Conflict Studio/ConflictStudio.exe', 'ConflictStudio.png', 'Licenses/Conflict-Studio-LICENSE.txt', 'Licenses/DOTNET-LICENSE.txt', 'Licenses/DOTNET-THIRD-PARTY-NOTICES.txt', 'Licenses/THIRD-PARTY-NOTICES.txt', 'bridge.js', 'index.js', 'info.json') | Sort-Object
if ((ConvertTo-Json $actualFiles -Compress) -ne (ConvertTo-Json $allowedFiles -Compress)) { throw 'The Nexus package does not match the one-executable public layout.' }
foreach ($entry in $manifest.files) {
    $filePath = Join-Path $resolvedPackage ($entry.path -replace '/', '\')
    $item = Get-Item -LiteralPath $filePath
    if ($item.Length -ne $entry.bytes) { throw "Byte length mismatch: $($entry.path)" }
    $hash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $entry.sha256) { throw "SHA-256 mismatch: $($entry.path)" }
}
if (Get-ChildItem -LiteralPath $resolvedPackage -Filter '*.pdb' -File -Recurse) { throw 'Public packages must not contain PDB files.' }
$applicationFiles = @(Get-ChildItem -LiteralPath $resolvedPackage -Filter '*.exe' -File -Recurse)
if ($applicationFiles.Count -ne 1) { throw 'The Nexus package must contain exactly one executable.' }
$applicationPath = $applicationFiles[0].FullName.Substring($resolvedPackage.Length).TrimStart('\').Replace('\','/')
if ($applicationPath -ne $manifest.entryPoint) { throw 'The sole application executable does not match the release entry point.' }
if (Get-ChildItem -LiteralPath $resolvedPackage -Filter '*.dll' -File -Recurse) { throw 'The public application must be bundled as one executable instead of loose runtime DLLs.' }
$appVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $resolvedPackage $manifest.entryPoint)).FileVersion
if (-not $appVersion.StartsWith($metadata.version + '.', [StringComparison]::Ordinal)) { throw "Application version does not match the release: $appVersion" }
if (Get-ChildItem -LiteralPath $resolvedPackage -Filter '*.zip' -File -Recurse) { throw 'The Nexus package must not contain nested ZIP archives.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
if ($ArchivePath) {
    $resolvedArchive = (Resolve-Path -LiteralPath $ArchivePath -ErrorAction Stop).Path
    $outer = [IO.Compression.ZipFile]::OpenRead($resolvedArchive)
    try {
        $archiveEntries = @($outer.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) } | ForEach-Object { $_.FullName.Replace('\','/') } | Sort-Object)
        $expectedArchiveEntries = @($actualFiles | Sort-Object)
        if ((ConvertTo-Json $archiveEntries -Compress) -ne (ConvertTo-Json $expectedArchiveEntries -Compress)) { throw 'The Nexus archive does not contain the exact verified package inventory.' }
        foreach ($entry in $outer.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) }) {
            $normalizedEntry = $entry.FullName.Replace('\','/')
            $sourcePath = Join-Path $resolvedPackage ($normalizedEntry -replace '/', '\')
            $sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
            $stream = $entry.Open()
            $archiveHasher = [Security.Cryptography.SHA256]::Create()
            try { $archiveHash = ([BitConverter]::ToString($archiveHasher.ComputeHash($stream))).Replace('-','').ToLowerInvariant() }
            finally { $archiveHasher.Dispose(); $stream.Dispose() }
            if ($archiveHash -ne $sourceHash) { throw "Nexus archive content hash mismatch: $normalizedEntry" }
        }
    }
    finally { $outer.Dispose() }
}
Write-Output "PACKAGE PASS version=$($manifest.version) files=$($manifest.files.Count) sha256=verified"

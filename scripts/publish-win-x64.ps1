[CmdletBinding()]
param(
    [string]$Version = '0.1.7',
    [string]$OutputRoot = (Join-Path $env:LOCALAPPDATA 'Cyberpunk Conflict Studio\releases'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$metadataPath = Join-Path $repositoryRoot "docs\release\$Version.json"
$appProject = Join-Path $repositoryRoot 'src\ConflictStudio.App\ConflictStudio.App.csproj'
$cliProject = Join-Path $repositoryRoot 'src\ConflictStudio.Cli\ConflictStudio.Cli.csproj'
$packageRoot = Join-Path (Join-Path $OutputRoot $Version) 'win-x64'

if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) { throw "Release metadata was not found: $metadataPath" }
if (-not (Test-Path -LiteralPath $appProject -PathType Leaf)) { throw "Application project was not found: $appProject" }
if (-not (Test-Path -LiteralPath $cliProject -PathType Leaf)) { throw "CLI project was not found: $cliProject" }
$dirtyPaths = @(git -C $repositoryRoot status --porcelain --untracked-files=all)
if ($LASTEXITCODE -ne 0) { throw 'Repository status could not be resolved.' }
if ($dirtyPaths.Count -gt 0) { throw 'Release packages require a clean repository so sourceCommit identifies the packaged source exactly.' }
$resolvedRepository = (Resolve-Path -LiteralPath $repositoryRoot).Path.TrimEnd('\') + '\'
$resolvedOutput = [IO.Path]::GetFullPath($OutputRoot).TrimEnd('\') + '\'
if ($resolvedOutput.StartsWith($resolvedRepository, [StringComparison]::OrdinalIgnoreCase)) { throw 'OutputRoot must be outside the repository.' }
if ((Test-Path -LiteralPath $packageRoot) -and -not $Force) { throw "Package directory already exists. Use -Force to replace it: $packageRoot" }
$stageRoot = Join-Path ([IO.Path]::GetTempPath()) "cyberpunk-conflict-studio-$Version-$([Guid]::NewGuid().ToString('N'))"

try {
    New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
    $appStage = Join-Path $stageRoot 'Conflict Studio'
    dotnet restore $appProject --runtime win-x64
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
    dotnet restore $cliProject --runtime win-x64
    if ($LASTEXITCODE -ne 0) { throw 'CLI restore failed.' }
    dotnet restore (Join-Path $repositoryRoot 'tests\ConflictStudio.Core.Tests\ConflictStudio.Core.Tests.csproj')
    if ($LASTEXITCODE -ne 0) { throw 'Core test restore failed.' }
    dotnet restore (Join-Path $repositoryRoot 'tests\ConflictStudio.App.Tests\ConflictStudio.App.Tests.csproj')
    if ($LASTEXITCODE -ne 0) { throw 'App test restore failed.' }
    dotnet restore $appProject --runtime win-x64
    if ($LASTEXITCODE -ne 0) { throw 'Final App runtime restore failed.' }
    dotnet restore $cliProject --runtime win-x64
    if ($LASTEXITCODE -ne 0) { throw 'Final CLI runtime restore failed.' }
    dotnet build $appProject --configuration Release --runtime win-x64 --self-contained true --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
    dotnet build $cliProject --configuration Release --runtime win-x64 --self-contained true --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'CLI build failed.' }
    dotnet test --project (Join-Path $repositoryRoot 'tests\ConflictStudio.Core.Tests\ConflictStudio.Core.Tests.csproj') --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    dotnet test --project (Join-Path $repositoryRoot 'tests\ConflictStudio.App.Tests\ConflictStudio.App.Tests.csproj') --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'App tests failed.' }
    node --test (Join-Path $repositoryRoot 'integrations\vortex\bridge.test.js') (Join-Path $repositoryRoot 'integrations\vortex\index.test.js')
    if ($LASTEXITCODE -ne 0) { throw 'Vortex bridge tests failed.' }
    node --check (Join-Path $repositoryRoot 'integrations\vortex\index.js')
    if ($LASTEXITCODE -ne 0) { throw 'Vortex bridge syntax validation failed.' }
    $integrationRoot = Join-Path $repositoryRoot 'integrations'
    dotnet publish $appProject --configuration Release --runtime win-x64 --self-contained true --no-restore --output $appStage -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
    $licenseStage = Join-Path $stageRoot 'Licenses'
    New-Item -ItemType Directory -Path $licenseStage -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $licenseStage 'Conflict-Studio-LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.txt') -Destination (Join-Path $licenseStage 'THIRD-PARTY-NOTICES.txt')
    $dotnetRoot = Split-Path -Parent (Get-Command dotnet -ErrorAction Stop).Source
    Copy-Item -LiteralPath (Join-Path $dotnetRoot 'LICENSE.txt') -Destination (Join-Path $licenseStage 'DOTNET-LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $dotnetRoot 'ThirdPartyNotices.txt') -Destination (Join-Path $licenseStage 'DOTNET-THIRD-PARTY-NOTICES.txt')
    Get-ChildItem -LiteralPath $appStage -Filter '*.pdb' -File -Recurse | Remove-Item -Force
    Copy-Item -LiteralPath (Join-Path $integrationRoot 'vortex\info.json') -Destination $stageRoot
    Copy-Item -LiteralPath (Join-Path $integrationRoot 'vortex\index.js') -Destination $stageRoot
    Copy-Item -LiteralPath (Join-Path $integrationRoot 'vortex\bridge.js') -Destination $stageRoot
    Copy-Item -LiteralPath (Join-Path $integrationRoot 'vortex\ConflictStudio.png') -Destination $stageRoot
    $metadata = Get-Content -Raw -LiteralPath $metadataPath | ConvertFrom-Json
    $sourceCommit = (git -C $repositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-f]{40}$') { throw 'Source commit could not be resolved.' }
    $entries = @(Get-ChildItem -LiteralPath $stageRoot -File -Recurse | ForEach-Object {
        [ordered]@{
            path = $_.FullName.Substring($stageRoot.Length).TrimStart('\').Replace('\','/')
            bytes = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    } | Sort-Object path)
    $manifest = [ordered]@{
        product = $metadata.product
        version = $metadata.version
        runtime = $metadata.runtime
        selfContained = $metadata.selfContained
        channel = $metadata.channel
        sourceCommit = $sourceCommit
        entryPoint = $metadata.entryPoint
        packageFormat = $metadata.packageFormat
        minimumOs = $metadata.minimumOs
        generatedUtc = [DateTime]::UtcNow.ToString('o')
        files = $entries
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stageRoot 'package-manifest.json') -Encoding utf8
    & (Join-Path $PSScriptRoot 'verify-package.ps1') -PackageRoot $stageRoot -MetadataPath $metadataPath -DirectoryOnly
    if ($LASTEXITCODE -ne 0) { throw 'Staged package verification failed.' }
    if (Test-Path -LiteralPath $packageRoot) { Remove-Item -LiteralPath $packageRoot -Recurse -Force }
    New-Item -ItemType Directory -Path (Split-Path -Parent $packageRoot) -Force | Out-Null
    Move-Item -LiteralPath $stageRoot -Destination $packageRoot
    & (Join-Path $PSScriptRoot 'verify-package.ps1') -PackageRoot $packageRoot -MetadataPath $metadataPath -DirectoryOnly
    if ($LASTEXITCODE -ne 0) { throw 'Package verification failed.' }
    $nexusArchive = Join-Path (Split-Path -Parent $packageRoot) "Cyberpunk-Conflict-Studio-$Version-Nexus.zip"
    if (Test-Path -LiteralPath $nexusArchive) { if (-not $Force) { throw "Nexus archive already exists. Use -Force to replace it: $nexusArchive" }; Remove-Item -LiteralPath $nexusArchive -Force }
    $archiveInputs = @('Conflict Studio', 'Licenses', 'ConflictStudio.png', 'bridge.js', 'index.js', 'info.json') | ForEach-Object { Join-Path $packageRoot $_ }
    Compress-Archive -Path $archiveInputs -DestinationPath $nexusArchive -CompressionLevel Optimal
    & (Join-Path $PSScriptRoot 'verify-package.ps1') -PackageRoot $packageRoot -MetadataPath $metadataPath -ArchivePath $nexusArchive
    if ($LASTEXITCODE -ne 0) { throw 'Nexus archive verification failed.' }
    $checksumPath = $nexusArchive + '.sha256'
    $checksum = (Get-FileHash -LiteralPath $nexusArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$checksum  $([IO.Path]::GetFileName($nexusArchive))" | Set-Content -LiteralPath $checksumPath -Encoding ascii
    Write-Output "PUBLISHED $packageRoot"
    Write-Output "NEXUS $nexusArchive"
    Write-Output "CHECKSUM $checksumPath"
}
finally {
    if (Test-Path -LiteralPath $stageRoot) { Remove-Item -LiteralPath $stageRoot -Recurse -Force }
}

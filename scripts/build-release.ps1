# SPDX-License-Identifier: GPL-3.0-only
# Copyright (C) 2026 mostdak1ng

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifacts = [System.IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts"))
$staging = [System.IO.Path]::GetFullPath((Join-Path $projectRoot ".release-staging"))
$publish = Join-Path $staging "publish"
$binaryStage = Join-Path $staging "TheWarriorsFreecam-v0.1.5-windows-x64"
$sourceStage = Join-Path $staging "TheWarriorsFreecam-v0.1.5-source"
$appProject = Join-Path $projectRoot "src\TheWarriorsFreecam\TheWarriorsFreecam.csproj"
$testProject = Join-Path $projectRoot "tests\TheWarriorsFreecam.Tests\TheWarriorsFreecam.Tests.csproj"

function Assert-ProjectChild([string] $path) {
    $resolved = [System.IO.Path]::GetFullPath($path)
    $prefix = $projectRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the Freecam project: $resolved"
    }
}

function Invoke-DotNet([string[]] $arguments) {
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code ${LASTEXITCODE}: $($arguments -join ' ')"
    }
}

function Copy-SourceTree([string] $relativeRoot) {
    $sourceRoot = Join-Path $projectRoot $relativeRoot
    Get-ChildItem -LiteralPath $sourceRoot -Recurse -File |
        Where-Object {
            $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
        } |
        ForEach-Object {
            $relative = $_.FullName.Substring($projectRoot.Length).TrimStart('\', '/')
            $destination = Join-Path $sourceStage $relative
            $destinationDirectory = Split-Path -Parent $destination
            New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $destination
        }
}

Assert-ProjectChild $artifacts
Assert-ProjectChild $staging
if (Test-Path -LiteralPath $staging) {
    Remove-Item -LiteralPath $staging -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $artifacts, $publish, $binaryStage, $sourceStage |
    Out-Null

Write-Host "Running Freecam tests..."
Invoke-DotNet @("run", "--project", $testProject, "-c", "Release")

Write-Host "Restoring the pinned Win64 runtime..."
Invoke-DotNet @(
    "restore", $appProject,
    "-r", "win-x64",
    "-p:RuntimeFrameworkVersion=8.0.15",
    "-p:TargetLatestRuntimePatch=false"
)

Write-Host "Publishing the self-contained single-file executable..."
Invoke-DotNet @(
    "publish", $appProject,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "--no-restore",
    "-p:RuntimeFrameworkVersion=8.0.15",
    "-p:TargetLatestRuntimePatch=false",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:PublishReadyToRun=false",
    "-p:DebugType=embedded",
    "-o", $publish
)

$publishedExe = Join-Path $publish "TheWarriorsFreecam.exe"
if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf)) {
    throw "Publish completed without TheWarriorsFreecam.exe."
}

foreach ($file in @(
    "README.md",
    "CHANGELOG.md",
    "LICENSE",
    "THIRD-PARTY-NOTICES.txt",
    "SOURCE.txt"
)) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $binaryStage
}
Copy-Item -LiteralPath $publishedExe -Destination $binaryStage

$exeHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $publishedExe).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $binaryStage "SHA256SUMS.txt") -Encoding Ascii -Value (
    "$exeHash  TheWarriorsFreecam.exe"
)

foreach ($tree in @("src", "tests", "scripts")) {
    Copy-SourceTree $tree
}
foreach ($file in @(
    "Directory.Build.props",
    ".gitignore",
    "README.md",
    "CHANGELOG.md",
    "LICENSE",
    "THIRD-PARTY-NOTICES.txt",
    "SOURCE.txt"
)) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $sourceStage
}

$binaryZip = Join-Path $artifacts "TheWarriorsFreecam-v0.1.5-windows-x64.zip"
$sourceZip = Join-Path $artifacts "TheWarriorsFreecam-v0.1.5-source.zip"
foreach ($archive in @($binaryZip, $sourceZip)) {
    if (Test-Path -LiteralPath $archive) {
        Remove-Item -LiteralPath $archive -Force
    }
}
Compress-Archive -LiteralPath $binaryStage -DestinationPath $binaryZip -CompressionLevel Optimal
Compress-Archive -LiteralPath $sourceStage -DestinationPath $sourceZip -CompressionLevel Optimal

$checksums = foreach ($archive in @($binaryZip, $sourceZip)) {
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash.ToLowerInvariant()
    "$hash  $(Split-Path -Leaf $archive)"
}
Set-Content -LiteralPath (Join-Path $artifacts "SHA256SUMS.txt") -Encoding Ascii -Value $checksums

Write-Host "Release artifacts created in $artifacts"
Get-ChildItem -LiteralPath $artifacts | Select-Object Name, Length

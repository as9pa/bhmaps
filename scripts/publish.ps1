# Builds the two release files into dist\ from the <Version> in BhMaps.App.csproj. The names carry no version: the
# release tag does, and the app updates itself from these exact names.
# Run from the repo root with PowerShell 7: pwsh -File scripts\publish.ps1
# -Compat also writes the versioned names that installs older than 3.5 look for, so a release they should still be
# able to update from carries both. Drop the switch once no such install is left.
param([switch]$Compat)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo "src\BhMaps.App"
$csproj = Join-Path $project "BhMaps.App.csproj"

$found = Select-String -Path $csproj -Pattern '<Version>(.+?)</Version>' | Select-Object -First 1
if (-not $found) { throw "No <Version> element in $csproj" }
$version = $found.Matches[0].Groups[1].Value

$dist = Join-Path $repo "dist"
New-Item -ItemType Directory -Force $dist | Out-Null

$exeOut = Join-Path $dist "bhmaps.exe"
$zipOut = Join-Path $dist "bhmaps-dotnet.zip"
$sumsOut = Join-Path $dist "SHA256SUMS.txt"
$compatExeOut = Join-Path $dist "bhmaps-v$version-win-x64.exe"
$compatZipOut = Join-Path $dist "bhmaps-v$version-win-x64-dotnet.zip"
# The versioned copies go too, with or without -Compat, so dist\ never holds a mixed set.
foreach ($f in $exeOut, $zipOut, $sumsOut, $compatExeOut, $compatZipOut) {
    if (Test-Path $f) { Remove-Item -Force $f }
}

# Both publishes go to throwaway folders, so dist\ holds only the release files.
$selfContained = Join-Path $env:TEMP "bhmaps-publish-selfcontained"
$frameworkDependent = Join-Path $env:TEMP "bhmaps-publish-framework"
foreach ($d in $selfContained, $frameworkDependent) {
    if (Test-Path $d) { Remove-Item -Recurse -Force $d }
}

Write-Host "Publishing BhMaps $version"

try {
    dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $selfContained
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish --self-contained true failed with exit code $LASTEXITCODE" }
    Copy-Item (Join-Path $selfContained "BhMaps.exe") $exeOut -Force

    dotnet publish $project -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o $frameworkDependent
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish --self-contained false failed with exit code $LASTEXITCODE" }
    Compress-Archive -Path (Join-Path $frameworkDependent "BhMaps.exe") -DestinationPath $zipOut
}
finally {
    foreach ($d in $selfContained, $frameworkDependent) {
        if (Test-Path $d) { Remove-Item -Recurse -Force $d }
    }
}

$assets = @($exeOut, $zipOut)
if ($Compat) {
    Copy-Item $exeOut $compatExeOut -Force
    Copy-Item $zipOut $compatZipOut -Force
    $assets += $compatExeOut, $compatZipOut
}

# The app verifies its download against this file, so it is a release asset like the others. sha256sum's own
# format: lowercase hex, two spaces, the file name with no path.
$lines = foreach ($f in $assets) {
    $hash = (Get-FileHash -Algorithm SHA256 -Path $f).Hash.ToLowerInvariant()
    "$hash  $([System.IO.Path]::GetFileName($f))"
}
Set-Content -Path $sumsOut -Value $lines -Encoding ascii

foreach ($f in $assets + $sumsOut) {
    $mb = [math]::Round((Get-Item $f).Length / 1MB, 1)
    Write-Host "$f  $mb MB"
}

Write-Host ""
Write-Host "Upload every file in dist\ to the GitHub release. The update check needs SHA256SUMS.txt and a public repo."

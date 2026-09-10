# Builds the two release files into dist\, named after the <Version> in BhMaps.App.csproj.
# Run from the repo root with PowerShell 7: pwsh -File scripts\publish.ps1
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo "src\BhMaps.App"
$csproj = Join-Path $project "BhMaps.App.csproj"

$found = Select-String -Path $csproj -Pattern '<Version>(.+?)</Version>' | Select-Object -First 1
if (-not $found) { throw "No <Version> element in $csproj" }
$version = $found.Matches[0].Groups[1].Value

$dist = Join-Path $repo "dist"
New-Item -ItemType Directory -Force $dist | Out-Null

$exeOut = Join-Path $dist "bhmaps-v$version-win-x64.exe"
$zipOut = Join-Path $dist "bhmaps-v$version-win-x64-dotnet.zip"
foreach ($f in $exeOut, $zipOut) {
    if (Test-Path $f) { Remove-Item -Force $f }
}

# Both publishes go to throwaway folders, so dist\ holds only the two release files.
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

foreach ($f in $exeOut, $zipOut) {
    $mb = [math]::Round((Get-Item $f).Length / 1MB, 1)
    Write-Host "$f  $mb MB"
}

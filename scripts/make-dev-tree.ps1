# Builds a small fake mapArt tree plus two packs for manual testing.
# Reads the real game folder and, with -RealArt, the real library. Never writes to either.
# The app is only ever launched against $Dest.
param(
    [string]$Dest = (Join-Path $env:TEMP "bhmaps-dev"),
    [string]$GameRoot = "C:\Program Files (x86)\Steam\steamapps\common\Brawlhalla",
    [string]$Library = "C:\Users\alexa\files\bh",
    [switch]$RealArt
)

$game = Join-Path $GameRoot "mapArt"
$devRoot = Join-Path $Dest "game"
$devMapArt = Join-Path $devRoot "mapArt"
$devLib = Join-Path $Dest "lib"
$devAppData = Join-Path $Dest "appdata"

if (Test-Path $Dest) { Remove-Item -Recurse -Force $Dest }
New-Item -ItemType Directory -Force $devMapArt, $devAppData | Out-Null

foreach ($folder in "BloodMoon", "Swamp", "Backgrounds", "BP8", "Tekken") {
    Copy-Item (Join-Path $game $folder) (Join-Path $devMapArt $folder) -Recurse -Force
}

# Pack "demo": exact copies of BloodMoon and one background, so those folders show as Applied.
$demo = Join-Path $devLib "packs\demo"
New-Item -ItemType Directory -Force (Join-Path $demo "BloodMoon"), (Join-Path $demo "Backgrounds") | Out-Null
Copy-Item (Join-Path $devMapArt "BloodMoon\*") (Join-Path $demo "BloodMoon") -Force
Copy-Item (Join-Path $devMapArt "Backgrounds\BG_Sewer.jpg") (Join-Path $demo "Backgrounds\BG_Sewer.jpg") -Force

# Pack "dark": a different image under the BG_Sewer.jpg name, so applying it visibly changes status.
$dark = Join-Path $devLib "packs\dark"
New-Item -ItemType Directory -Force (Join-Path $dark "Backgrounds") | Out-Null
Copy-Item (Join-Path $devMapArt "Backgrounds\BG_Space.jpg") (Join-Path $dark "Backgrounds\BG_Sewer.jpg") -Force

# Loose files for Import testing: a flat folder outside packs\.
$loose = Join-Path $devLib "loose stuff"
New-Item -ItemType Directory -Force $loose | Out-Null
Copy-Item (Join-Path $devMapArt "Backgrounds\BG_Space.jpg") (Join-Path $loose "BG_Space.jpg") -Force
Copy-Item (Join-Path $devMapArt "BP8\MainPlat.png") (Join-Path $loose "MainPlat.png") -Force
Copy-Item (Join-Path $devMapArt "BP8\MainPlat.png") (Join-Path $loose "Whatever.png") -Force

# -RealArt: the real Default pack, the four game data files, and the other real packs, so
# manual walkthroughs show real maps. Every path below is read only.
if ($RealArt) {
    $defaultPack = Join-Path $Library "packs\Default"
    if (Test-Path $defaultPack) {
        Copy-Item (Join-Path $defaultPack "*") $devMapArt -Recurse -Force
    } else {
        Write-Warning "No Default pack at $defaultPack; dev mapArt keeps only the sample folders."
    }

    foreach ($f in "BrawlhallaAir.swf", "Dynamic.swz", "Init.swz", "Game.swz") {
        $src = Join-Path $GameRoot $f
        if (Test-Path $src) { Copy-Item $src (Join-Path $devRoot $f) -Force }
        else { Write-Warning "Missing game data file: $src" }
    }

    $srcPacks = Join-Path $Library "packs"
    if (Test-Path $srcPacks) {
        Get-ChildItem $srcPacks -Directory |
            Where-Object { $_.Name -ne "Default" } |
            ForEach-Object { Copy-Item $_.FullName (Join-Path $devLib "packs") -Recurse -Force }
    }
}

Write-Host "Dev tree ready at $Dest"
Write-Host "Run:"
Write-Host "  dotnet run --project src\BhMaps.App -- --game `"$devMapArt`" --library `"$devLib`" --appdata `"$devAppData`""

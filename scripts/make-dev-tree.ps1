# Builds a small fake mapArt tree plus two packs for manual testing. Reads the real game folder; never writes to it.
param([string]$Dest = (Join-Path $env:TEMP "bhmaps-dev"))

$game = "C:\Program Files (x86)\Steam\steamapps\common\Brawlhalla\mapArt"
$devGame = Join-Path $Dest "game"
$devLib = Join-Path $Dest "lib"
$devAppData = Join-Path $Dest "appdata"

if (Test-Path $Dest) { Remove-Item -Recurse -Force $Dest }
New-Item -ItemType Directory -Force $devGame, $devAppData | Out-Null

foreach ($folder in "BloodMoon", "Swamp", "Backgrounds", "BP8", "Tekken") {
    Copy-Item (Join-Path $game $folder) (Join-Path $devGame $folder) -Recurse -Force
}

# Pack "demo": exact copies of BloodMoon and one background, so those folders show as Applied.
$demo = Join-Path $devLib "packs\demo"
New-Item -ItemType Directory -Force (Join-Path $demo "BloodMoon"), (Join-Path $demo "Backgrounds") | Out-Null
Copy-Item (Join-Path $devGame "BloodMoon\*") (Join-Path $demo "BloodMoon") -Force
Copy-Item (Join-Path $devGame "Backgrounds\BG_Sewer.jpg") (Join-Path $demo "Backgrounds\BG_Sewer.jpg") -Force

# Pack "dark": a different image under the BG_Sewer.jpg name, so applying it visibly changes status.
$dark = Join-Path $devLib "packs\dark"
New-Item -ItemType Directory -Force (Join-Path $dark "Backgrounds") | Out-Null
Copy-Item (Join-Path $devGame "Backgrounds\BG_Space.jpg") (Join-Path $dark "Backgrounds\BG_Sewer.jpg") -Force

# Loose files for Import testing: a flat folder outside packs\.
$loose = Join-Path $devLib "loose stuff"
New-Item -ItemType Directory -Force $loose | Out-Null
Copy-Item (Join-Path $devGame "Backgrounds\BG_Space.jpg") (Join-Path $loose "BG_Space.jpg") -Force
Copy-Item (Join-Path $devGame "BP8\MainPlat.png") (Join-Path $loose "MainPlat.png") -Force
Copy-Item (Join-Path $devGame "BP8\MainPlat.png") (Join-Path $loose "Whatever.png") -Force

Write-Host "Dev tree ready at $Dest"
Write-Host "Run:"
Write-Host "  dotnet run --project src\BhMaps.App -- --game `"$devGame`" --library `"$devLib`" --appdata `"$devAppData`""

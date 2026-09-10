# BhMaps

Brawlhalla map art manager for Windows. It keeps packs of map art in a library, shows every map
under its in-game name with a preview composed from the game's own level data, and copies packs into
the game with one click, with an Undo for the last write.

## Download

Requires Windows 10 or 11, 64-bit, and Brawlhalla installed through Steam.

| [Releases](https://github.com/as9pa/bhmaps/releases) | Size | Needs .NET? |
|---|---|---|
| `bhmaps-v2.0.0-win-x64.exe` | ~135 MB | No, the runtime is inside |
| `bhmaps-v2.0.0-win-x64-dotnet.zip` | ~0.7 MB | Yes, [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |

Take the `.exe` unless you already have .NET 10 installed. Windows may warn on first run because the
exe is unsigned: choose "More info", then "Run anyway".

## First run

Double-click `BhMaps.exe`. The Welcome window finds the game folder through Steam, asks where to keep
the pack library (`Documents\BhMaps` by default), and offers to capture the game's current art as the
`Default` pack so any map can be reset later. Changes show up in the game after it restarts; with
Brawlhalla running, a write offers "Restart and apply".

## What it does

- Home is a grid of every map with a composed preview and a status tag: Default, the packs it
  matches, Custom or Missing.
- The Backgrounds and Platforms pages apply one picture or one platform set to every map you tick.
- Packs imports folders as packs, applies them, exports them, and captures the game's current art as
  a Default pack.
- Add pictures fits any image to the background size and writes it into a pack.
- Undo puts back whatever the last write into the game folder overwrote or deleted.
- Only `.png` and `.jpg` files inside `mapArt` are ever changed; the game's data files are read only.

## Files

Game art lives in `<Steam>\steamapps\common\Brawlhalla\mapArt`. The pack library is
`Documents\BhMaps` by default, holding packs at `packs\<name>\<GameFolder>\<file>`. Settings and
caches are in `%APPDATA%\BhMaps`.

## Build

```
dotnet build BhMaps.slnx
dotnet test
dotnet run --project src\BhMaps.App
pwsh -File scripts\publish.ps1
```

Needs the .NET 10 SDK. `scripts\publish.ps1` writes both release files into `dist\`.
See [docs/manual.md](docs/manual.md) for every page, how status and previews are computed,
development against a throwaway copy of the game folder, and known limitations.

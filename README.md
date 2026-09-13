# BhMaps

Brawlhalla map art manager for Windows. It keeps packs of map art in a library, shows every map
under its in-game name with a preview composed from the game's own level data, and copies packs into
the game with one click, with an Undo for the last write.

## Download

Requires Windows 10 or 11, 64-bit, and Brawlhalla installed through Steam.

| [Releases](https://github.com/as9pa/bhmaps/releases) | Size | Needs .NET? |
|---|---|---|
| `bhmaps-v2.4.0-win-x64.exe` | ~135 MB | No, the runtime is inside |
| `bhmaps-v2.4.0-win-x64-dotnet.zip` | ~0.7 MB | Yes, [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |

Take the `.exe` unless you already have .NET 10 installed. Windows may warn on first run because the
exe is unsigned: choose "More info", then "Run anyway".

## First run

Double-click `BhMaps.exe`. The Welcome window finds the game folder through Steam, asks where to keep
the pack library (`Documents\BhMaps` by default), and offers to capture the game's current art as the
`Default` pack so any map can be reset later. Changes are written straight into the game folder, with
Brawlhalla open or closed, and show on the next match load.

## What it does

- Maps is a grid of every map with a composed preview and a tag saying what art is on it: the pack it
  matches, an any-map picture's name, or Missing. Clicking a map opens a panel with everything that one
  map can do.
- Backgrounds and Platforms are one row per map, with every background or platform set that map could
  have laid out along it. One click applies the one you want.
- Packs imports folders as packs, applies them, exports them, and captures the game's current art as
  a Default pack.
- Add Image fits any image to the background size and writes it into a pack, and the editor
  crops, pans and darkens one picture into a map's background slot, for one map or for all of them.
- The platform editor fades a map's platforms with an Opacity slider and recolours them with a Hue
  slider, over a live preview that can show only the ticked pieces, and saves the result into a pack.
- Right-click any picture in a pack to put it on one map, the selected maps, a map picked from a
  list, or every map; right-click any map card to apply a pack or picture, edit it or reset it.
- The My Backgrounds switch on Backgrounds shows your own pictures on every row at once, or hides
  them all while you compare packs.
- Select maps on Maps to apply a pack or a picture to all of them at once, from the selection bar or
  from any choice's menu.
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

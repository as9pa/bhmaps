# BhMaps

![The Maps grid, every map with its composed preview](docs/images/maps.png)

Brawlhalla map art manager for Windows. It keeps packs of map art in a library, shows every map
under its in-game name with a preview composed from the game's own level data, and copies packs into
the game with one click, with an Undo for the last write.

## Download

Requires Windows 10 or 11, 64-bit, and Brawlhalla installed through Steam.

| [Releases](https://github.com/as9pa/bhmaps/releases) | Size    | Needs .NET?                                                                       |
| ---------------------------------------------------- | ------- | --------------------------------------------------------------------------------- |
| `bhmaps.exe`                                         | ~135 MB | No, the runtime is inside                                                         |
| `bhmaps-dotnet.zip`                                  | ~0.8 MB | Yes, [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |

Take the `.exe` unless you already have .NET 10 installed. Windows may warn on first run because the
exe is unsigned: choose "More info", then "Run anyway".

From 2.6 the app checks this page for a newer release once a day and offers to update itself. It is
one small request to github.com, nothing about you or your library is sent, and the check can be
turned off in Settings under Updates. Both builds update in place: press **Update** in Settings and
BhMaps downloads the new version, checks it, replaces itself and restarts, with nothing to download
by hand. Keep BhMaps in a folder it can write to, such as Documents or Desktop, for that to work.

## First run

Double-click `BhMaps.exe`. The Welcome window finds the game folder through Steam, asks where to keep
the pack library (`Documents\BhMaps` by default), and offers to capture the game's current art as the
`Default` pack so any map can be reset later. Changes are written straight into the game folder, with
Brawlhalla open or closed, and show on the next match load.

## What it does

- **Map grid.** Every map under its in-game name, with a preview composed from the game's own level
  data and a tag saying what art is on it: the pack it matches, a picture's name, or Missing. Click a
  map to open a panel with everything that map can do, or right-click it to apply a pack or picture,
  edit it or reset it.
- **Backgrounds and Platforms.** One row per map, with every set that map could have laid out along
  it. The My Backgrounds switch shows your own pictures on every row at once, or hides them while you
  compare packs. One search box is shared by Maps, Backgrounds and Platforms.
- **Packs.** Folders of map art, kept in a library. Import, export, duplicate, rename (F2) and apply
  them: one background or platform set to a map in one click, or a whole pack to every map at once.
  Each row leads with a thumbnail of the pack's art. The eye on a row hides a pack from the other
  pages without removing it. The game's own art is captured as a Default pack so any map can be
  reset.
- **Moving art between packs.** Copy to pack, Move to pack, or Ctrl+C, Ctrl+X and Ctrl+V. Import from
  pack copies another pack's maps into the one you are looking at. Delete takes a picture, a platform
  set or a map out of a pack and puts the default back on any map showing it.
- **Custom images.** Add any `.png` or `.jpg` and it is fitted to the map's background size and
  written into a pack. Right-click a picture to put it on one map, a map picked from a list, or every
  map.
- **Platform art.** Put a custom image on platforms as well, per piece or laid across the whole set
  and dragged into place.
- **Background editor.** Pan and zoom, with Hue, Saturation, Contrast, Blur and Darken sliders over a
  live preview, for one map or for all of them. Drag the preview to move the picture and scroll to
  zoom; the sliders follow.
- **Platform editor.** The same Pan X, Pan Y and Zoom on the preview, plus Opacity, Hue, Saturation,
  Contrast, Darken and Blur per piece. The preview can show only the selected pieces.
- **Editors remember.** Reopen an editor on the same map and pack and its settings come back, with a
  Start fresh link to drop them.
- **Map-select thumbnails.** Each map's art also replaces its thumbnail on the game's map select
  screen, keeping the original for a reset.
- **Refresh.** Next to Launch. Applies again what changed at the source, restores missing files and
  rewrites the thumbnails, in one write with one Undo. After a game update, a new map's own art goes
  into the Default pack by itself.
- **Safe writes and Undo.** Only `.png` and `.jpg` files inside `mapArt` and the map-select thumbnails
  are ever changed; the game's data files are only read. Undo puts back whatever the last write
  overwrote or deleted.
- **Keyboard.** F5 rescans, Ctrl+Z undoes, Escape cancels a running write, Ctrl+1 to Ctrl+5 switch
  pages, Ctrl+K jumps to the Maps search, and Ctrl+plus, Ctrl+minus and Ctrl+0 change the zoom.
- **Updates.** Checks for a new release once a day and can update itself in place. One line in
  Settings turns it off.

![The Backgrounds page, one row per map with every background it could have](docs/images/backgrounds.png)

![The Platforms page, one row per map with every platform set it could have](docs/images/platforms.png)

![The Packs page, one row per pack with thumbnails and Apply all](docs/images/packs.png)

| Background editor | Platform editor |
| --- | --- |
| ![Background editor with fit, pan, zoom, hue, saturation, contrast, darken and blur](docs/images/editor-background.png) | ![Platform editor with pan, zoom and per-piece tone sliders over a live preview](docs/images/editor-platform.png) |

## Files

Game art lives in `<Steam>\steamapps\common\Brawlhalla\mapArt`. The pack library is
`Documents\BhMaps` by default, holding packs at `packs\<name>\<GameFolder>\<file>`. A folder elsewhere
in the library with a `mapArt` folder inside it, such as `<name>\mapArt\<GameFolder>\<file>`, is found
as a pack too, as long as `mapArt` is at most three folders below the library. Those packs are read
only: they can be viewed and applied but not deleted or changed. Settings and caches are in
`%APPDATA%\BhMaps`.

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

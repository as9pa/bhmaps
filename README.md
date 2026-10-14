# BhMaps

![The Maps grid, every map with its composed preview](docs/images/maps.png)

Brawlhalla map art manager for Windows. It keeps packs of map art in a library, shows every map
under its in-game name with a preview composed from the game's own level data, and copies packs into
the game with one click, with an Undo for the last write.

Made by **aexax**. Discord: `aexax`. GitHub: [as9pa](https://github.com/as9pa). This repository is the official home of BhMaps, and its [Releases](https://github.com/as9pa/bhmaps/releases) page is the official download.

**[Download bhmaps.exe](https://github.com/as9pa/bhmaps/releases/latest/download/bhmaps.exe)** (Windows 10 or 11, 64-bit)

## Download

Requires Windows 10 or 11, 64-bit, and Brawlhalla installed through Steam.

| [Releases](https://github.com/as9pa/bhmaps/releases) | Size    | Needs .NET?                                                                       |
| ---------------------------------------------------- | ------- | --------------------------------------------------------------------------------- |
| `bhmaps.exe`                                         | ~135 MB | No, the runtime is inside                                                         |
| `bhmaps-dotnet.zip`                                  | ~0.8 MB | Yes, [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |

Take the `.exe` unless you already have .NET 10 installed. Windows may warn on first run because the
exe is unsigned: choose "More info", then "Run anyway".

## First run

Double-click `BhMaps.exe`. The Welcome window finds the game folder through Steam, asks where to keep
the pack library (`Documents\BhMaps` by default), and offers to capture the game's current art as the
`Default` pack so any map can be reset later. Changes are written straight into the game folder, with
Brawlhalla open or closed, and show on the next match load.

## What it does

- **Map grid:** every map by its in-game name, with a live preview and what art is on it.
- **Packs:** keep map art in packs and apply one map or a whole pack in one click.
- **Backgrounds and Platforms:** every option for each map, side by side.
- **Custom images:** add any `.png`, `.jpg`, `.bmp` or `.webp` as a background or on the platforms.
- **Editors:** pan, zoom, hue, saturation, contrast, darken and blur, with a live preview.
- **Map-select thumbnails:** match the art you picked.
- **Undo and Reset:** put back the last change, or the game's own art on any map.
- **Refresh:** reapplies your art after a game update.
- **Updates itself:** checks once a day, can be turned off.
- **Safe:** only map art and thumbnails are changed; game data is never written.

Every menu, shortcut and option is in the [manual](docs/manual.md).

![The Backgrounds page, one row per map with every background it could have](docs/images/backgrounds.png)

![The Platforms page, one row per map with every platform set it could have](docs/images/platforms.png)

![The Packs page, one row per pack with thumbnails and Apply all](docs/images/packs.png)

| Background editor | Platform editor |
| --- | --- |
| ![Background editor with fit, pan, zoom, hue, saturation, contrast, darken and blur](docs/images/editor-background.png) | ![Platform editor with pan, zoom and per-piece tone sliders over a live preview](docs/images/editor-platform.png) |

## Updates

BhMaps checks for a new release once a day: one small request to github.com, with nothing about
you or your library sent. Press **Update** in Settings and it downloads the new version, checks it,
replaces itself and restarts. Keep it in a folder it can write to, such as Documents or Desktop.
The check can be turned off in Settings under Updates.

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

## Contributing

Bug reports, ideas and pull requests are welcome here on GitHub, or message `aexax` on Discord.

## License

BhMaps is free software under the [GNU General Public License v3.0](LICENSE), and it stays free for everyone.
You may use, study, change and share it. If you share BhMaps, a modified version of it, or a program that
includes its code, you must release that program's source under the same license and keep the copyright
and author notices.

Additional term under section 7(b) of the GPL: any copy or modified version must keep the author
attribution "BhMaps by aexax" with a link to https://github.com/as9pa/bhmaps, in its documentation and in
any about or credits screen it shows.

Copyright (C) 2026 aexax

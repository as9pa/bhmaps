# BhMaps manual

BhMaps is a Windows desktop app for managing Brawlhalla map art. It is map-first: every map is
listed under the name the game gives it, above a preview composed the way the game composes it, from
the game's own level data. It keeps a library of *packs*, folders of images that mirror the game's
`mapArt` tree, and copies them into the game folder on demand. The only files it changes are `.png`
and `.jpg` files inside the `mapArt` folder and inside its own library, plus its own files in
`%APPDATA%\BhMaps`. The game's data files are read and never written, and nothing else in the game
install is touched.

## Where things live

| What | Where |
| --- | --- |
| Game map art, read and written | `C:\Program Files (x86)\Steam\steamapps\common\Brawlhalla\mapArt` |
| Game data files, read only | `BrawlhallaAir.swf`, `Dynamic.swz`, `Init.swz` and `Game.swz`, in the folder above `mapArt` |
| Pack library | `Documents\BhMaps` by default, changeable in Settings |
| Packs inside the library | `<library>\packs\<pack name>\<GameFolder>\<file>` |
| Settings | `%APPDATA%\BhMaps\settings.json` |
| Hash cache | `%APPDATA%\BhMaps\hashcache.json` |
| Level data cache | `%APPDATA%\BhMaps\leveldata.json` |
| Composed previews | `%APPDATA%\BhMaps\previews\` |
| Undo of the last game write | `%APPDATA%\BhMaps\undo\` |

The map art folder and the library are chosen in the welcome window and editable in Settings; the
data files are wherever the game folder is. Only `<library>\packs` is read and written, so anything
else kept in the library folder is left alone. The settings file holds the two paths, the two zoom
levels, what to do while the game runs, and whether the welcome window has been finished. The three
caches are speed-ups only: deleting any of them costs one slower start, and preview files unused for
30 days are deleted at startup anyway. The undo folder holds one set at a time, the files the last
write into the game folder was about to overwrite or delete. The game folder can be any folder shaped like `mapArt`, one level of subfolders holding `.png` and `.jpg` files. Real map names, level sets and composed previews also need the game's four data files in the folder above it; without them the app still runs, with folder names and single-file thumbnails.

## Pages

The sidebar lists the pages, with the map list and its search box between them, and Launch at the
bottom, which asks Steam to start Brawlhalla and reads "Brawlhalla running" while it is up. Ticking
maps in the list is how the pages that write to several maps at once know which ones. F5 rescans;
**Cancel** in the page header stops a long operation.

- **Home** is the grid of maps. Chips above it filter: All, one per level set, and Changed, which is
  every map that is not entirely default. The set chips are Ranked 1v1, Ranked 2v2 and Tournament
  when the game's data has the ranked sets, and the standard ones when it does not; the other sets
  the game defines are read but not shown. A slider at the right end of the chip row sets 2 to 5
  columns. Each card is a composed preview, the map's name and a state tag reading Default, the packs
  it matches, Custom or Missing. Clicking a card opens a panel on the right: a larger preview, the
  map's background with the candidates from every pack beside it and **Add picture**, the platform
  sets on offer with **Use** on the one you want, the map's platform files with thumbnails and where
  each came from, **Reset to default (this map)** and **Open folder**. The header carries **Reset all
  to default**.
- **Backgrounds** is every picture in any pack's `Backgrounds` folder plus the game's own current
  backgrounds, on one grid, searchable by file name or pack and with the same kind of zoom.
  **Add pictures** is in the header. A tile offers **Apply**, which writes it into the background
  slots of every ticked map, and **Edit**, which opens the background editor on that slot. A tile is
  ticked when it is the picture a selected map is already showing. With maps ticked, a bar along the
  bottom says how many they are; applying to more than one asks first and names them.
- **Platforms** shows one map or all of them. For one map: every platform set on offer, each drawn
  over that map's current background, with a tick and a ring on the set the game is showing and
  **Use** on the others, then the map's platform files with thumbnails and their source. **All maps**
  is one row per map with the same tiles, smaller. **Reset to default (this map)** is in the header.
  The map shown is the first ticked one, else the map last opened on Home, else the first in the
  list.
- **Packs** is one row per pack with what it holds, as in "3 maps, 12 backgrounds", and **Apply all**,
  **Remove**, **Export** and **Open folder** beside it. The header has **Import folder**, **Capture
  defaults** and **Open library**. Clicking a row opens the pack.
- **Pack detail** shows one pack whole: its maps put together with the game's art, its own
  backgrounds, and the same maps with the background dropped so its platform art shows on its own.
  When the pack holds fully transparent PNGs, a line says how many of its files change nothing in
  game and offers **Remove** for them. **Apply all**, **Open folder** and **Back to packs** are in
  the header.
- **Settings** is one line per setting: the game folder and the library, each with **Change** and
  **Open**; the game data, with the date it was last read and **Refresh now**; **Capture defaults**;
  what happens while the game runs, **Restart and apply** or **Apply live**; and the version. There
  is no OK button, so every row saves as it is changed.

These windows open on top of the pages:

- **Welcome** opens on the first run and asks for the three things the app needs: the game folder,
  found through Steam when it can be, the library folder, and whether to capture the game's current
  art as the `Default` pack. It comes back if the saved game folder later stops working.
- **Add pictures**, from the Backgrounds header or a map's panel, takes any number of images, dropped
  on the window or picked, fits them all one way (**Stretch**, **Center**, **Fill** or **Fit**) and
  writes each one into an existing or new pack as a 2048x1151 JPEG. It can apply them to the ticked
  maps in the same step, in order, starting again from the first picture when there are more maps
  than pictures. The pictures it reads are never changed.
- **Import folder** takes several folders at once and makes one pack of each. Every folder's name is
  filled in as its pack name and can be edited; a name that matches a pack already in the library
  adds to that pack after one confirm. The button reads "Import N packs". Inside each folder, every
  `.png` and `.jpg` at any depth is routed to a game folder, and the whole routing table is shown
  before anything is copied.
- The **background editor**, from a background tile's **Edit**, fits one picture to a background slot
  and saves it into a pack, optionally writing it into the game at the same time.

**Undo** appears in the page header beside the line saying what the last write did, and only after a
write into the game folder. It puts back the files that write overwrote or deleted. One set is kept,
so the next game write replaces it.

Confirmations, errors and the list of files an operation could not touch are the app's own windows in
the app's own theme rather than Windows message boxes. Two failures before the main window exists are
the exception and use a message box: an error the app did not expect, and a development run given
`--game` or `--library` without `--appdata`.

## How it works

- **Level data.** The app reads, and never writes, four files in the folder above `mapArt`:
  `BrawlhallaAir.swf`, `Dynamic.swz`, `Init.swz` and `Game.swz`. They hold the levels, their real
  names and the sets they belong to. The `.swz` files are encrypted with a key that is found by
  scanning the compiled ActionScript in `BrawlhallaAir.swf` for the number that unlocks them; it is
  never hard-coded, because it changes with every game update. The parsed result is cached in
  `leveldata.json` next to the size and timestamp of all four files, so an unchanged game starts from
  the cache and a game update re-reads in the background. When the files are not there, the key is
  not found, or an entry fails its checksum, the app falls back to folder names for map names, hides
  the set chips, and shows a single-file thumbnail in place of each composed preview. Settings says
  which of those happened.
- A **map** is a `mapArt` folder that at least one level points at, named after the level the game
  names it after. Folders no level points at, such as the shared `Backgrounds` folder and the
  seasonal ones, are not maps: they stay out of the grid and the sidebar, and are still covered by
  Reset all to default and by pack operations.
- A **preview** is drawn the way the game draws the level: the level's first background stretched
  over its camera bounds, then every platform asset at its own position, scale, rotation and flip.
  Seasonal platforms and the further parallax background layers are left out. Rendering happens on
  its own thread, never the UI thread, and each result is kept as a JPEG named after the hash of
  every file that went into it, so a preview is drawn once and reused until one of its inputs
  changes.
- A **pack** is a folder under `<library>\packs`. Inside it are folders named exactly like the game's
  folders, holding the images that replace the game's. A pack may hold one file or every folder.
- **Status** is decided by SHA-256, file by file. A file that matches only the `Default` pack is
  default; one that matches other packs is labelled with them; one that matches nothing is custom; a
  file the `Default` pack has and the game folder lacks is missing. A map is summarised from its
  files, and missing is the only state that gets a colour. Hashes are cached by full path, size and
  last-write time, so a rescan only rehashes what changed.
- **Apply all** copies every file a pack holds into the game folder, overwriting what is there. It
  never deletes a game file the pack does not have, so packs stack: applying one changes only the
  slots it carries.
- **Reset to default** copies the `Default` pack's files for that map back, including its copies of
  the map's backgrounds. With no `Default` pack it falls back to deleting the map's `.png` and `.jpg`
  files so Brawlhalla writes its own art back the next time it launches, which leaves the folder
  looking empty until then.
- **Applying a background** writes the picture into every background slot the map's levels name,
  under the file name the game expects, fitted to 2048x1151 when the source is a different size. The
  picture it came from is not touched. **Using a platform set** copies every file that pack holds for
  the map's folder, transparent files included; the game then shows its own art through them, which
  is why the app counts them as changing nothing.
- **Import** walks each source folder recursively and routes every `.png` and `.jpg` it finds. A file
  whose parent folder is named like a game folder goes to that folder. Otherwise, if its filename
  exists in exactly one game folder, it goes there. If the filename exists in several, the file is
  ambiguous and you pick the folder. If it matches nothing, it is unmatched and is left out until you
  assign a folder. Two files that would land on the same path conflict, and only the first is
  included. Sources are only read, never moved or deleted.
- **Undo** works by copying the files a write is about to overwrite or delete into
  `%APPDATA%\BhMaps\undo\` first, before the write starts. Restoring puts those files back and
  deletes the ones that were not there before. Only writes into the game folder get a snapshot;
  changes to the library, including deleting a pack, cannot be undone.

Changes only show up in the game after it restarts. With Brawlhalla running, a write reads "Restart
and apply": the app closes the game, makes the change and starts it again through Steam. Settings can
switch that to writing while the game runs, which is untested.

## Development

Building from source needs the .NET 10 SDK. From the repo root:

```
dotnet build BhMaps.slnx
dotnet test
dotnet run --project src\BhMaps.App
```

With no arguments the app uses the saved settings. A first run has none, so it opens the welcome
window instead.

```
pwsh -File scripts\publish.ps1
```

That writes both release files into `dist\`: a self-contained
`bhmaps-v<version>-win-x64.exe` that carries its own runtime, and
`bhmaps-v<version>-win-x64-dotnet.zip`, a framework-dependent build that needs the .NET 10 Desktop
Runtime on whatever machine runs it.

Never point a development run at the real game folder. Build a throwaway copy instead:

```
pwsh -File scripts\make-dev-tree.ps1
```

That writes a small fake tree, two packs, and a folder of loose images under `%TEMP%\bhmaps-dev`. It
reads the real game folder to get the images and never writes to it. It also writes
`<dest>\appdata\settings.json` with `welcomeDone` set, so the run opens straight onto the map grid.
Two switches change what it builds:

- `-RealArt` also copies the library's `Default` pack into the dev `mapArt`, the game's four data
  files into the dev game root, and the library's other packs into the dev library, so a walkthrough
  shows real maps under their real names. Every one of those paths is read only.
- `-Welcome` leaves the settings file out, which is how the first-run flow is tested.

`-Dest`, `-GameRoot` and `-Library` override the three paths the script uses. Then run against the
copy:

```
dotnet run --project src\BhMaps.App -- --game "$env:TEMP\bhmaps-dev\game\mapArt" --library "$env:TEMP\bhmaps-dev\lib" --appdata "$env:TEMP\bhmaps-dev\appdata"
```

`--game`, `--library`, and `--appdata` each take a path. The first two override the saved settings
for that run only and are never written back; `--appdata` chooses the folder holding `settings.json`,
`hashcache.json`, `leveldata.json`, `previews\` and `undo\`. Passing `--game` or `--library` without
`--appdata` is refused at startup with an error dialog and a non-zero exit code, because only
`--appdata` moves the settings file: such a run would otherwise still write the real
`%APPDATA%\BhMaps\settings.json`. Give all three or none. The script prints the command with the
paths already filled in. Note that `--game` points one level deeper than the dev tree's game root:
the data files sit beside `mapArt`, not inside it.

The tests in `RealGameTests` read the real install, never write to it, and are skipped unless
`BHMAPS_REAL_GAME` names the Brawlhalla folder:

```powershell
$env:BHMAPS_REAL_GAME = "C:\Program Files (x86)\Steam\steamapps\common\Brawlhalla"
dotnet test tests\BhMaps.Core.Tests --filter "FullyQualifiedName~RealGameTests"
Remove-Item Env:\BHMAPS_REAL_GAME
```

Formatting and layout:

- `dotnet format BhMaps.slnx` before every commit, and
  `dotnet format BhMaps.slnx --verify-no-changes` to check. `.editorconfig` sets CRLF everywhere,
  four-space indents in C#, and two in the markup and project files.
- All three projects build with `TreatWarningsAsErrors`, so a warning fails the build.
- `System.IO` is a project-level global using in every `.csproj`; do not add `using System.IO;` to a
  file.
- `dotnet format BhMaps.slnx` is the only formatter this repo uses. csharpier must not be added.
  `.csharpierignore` at the repo root ignores every file, and exists only to switch off a csharpier
  formatting hook installed outside this repo.
- `BhMaps.Core` holds all the logic and is unit tested in `tests\BhMaps.Core.Tests`. The WPF layer
  in `BhMaps.App` has no unit tests and is checked by hand against the dev tree.

## Known limitations

- A subfolder inside the game's `mapArt` that cannot be listed, because it is locked or the ACL
  denies access, fails the whole scan with an error dialog instead of being skipped.
- Cancel interrupts only the operations that check for it: the scan, Reset all to default, applying a
  pack, applying a background, using a platform set, exporting a pack, capturing defaults, importing
  a folder and importing pictures. Resetting one map, deleting a pack and Undo run to the end.
- Cancelling an import or an apply leaves whatever was already copied on disk. The rescan that
  follows shows it.
- The import dialog's text boxes stay editable while a scan is running, and editing the source path
  after a scan does not invalidate the plan. Retyping the source and pressing Import without pressing
  Scan again imports the previous folder's plan.
- The thumbnail cache never evicts. Every thumbnail decoded during a session stays in memory until
  the app closes. The composed previews on disk are swept instead, at startup.
- The guard on deleting a pack compares resolved paths as text. A directory junction placed inside
  `packs\` is treated as a pack folder, so deleting it removes the link rather than refusing.
- A `BrawlhallaAir.swf` compressed with LZMA, which a `ZWS` signature marks, is not read. Only `FWS`
  and `CWS` are. The key cannot be found in that case, so the app runs without level data.
- A level whose XML will not parse is skipped rather than reported. The names of the skipped levels
  are collected, but they are only shown when the read failed outright, so a map that is missing for
  that reason is indistinguishable from one the game does not have.
- Writing while Brawlhalla runs is off by default: `whileRunning` is `restart`, so a write closes the
  game and starts it again. Settings offers `Apply live`, but writing into a running game has not
  been tested.

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
else kept in the library folder is left alone. The settings file holds the two paths, the zoom level
each page remembers, and whether the welcome window has been finished. The three caches are speed-ups
only: deleting any of them costs one slower start, and preview files unused for 30 days are deleted
at startup anyway. The undo folder holds one set at a time, the files the last
write into the game folder was about to overwrite or delete. The game folder can be any folder shaped like `mapArt`, one level of subfolders holding `.png` and `.jpg` files. Real map names, level sets and composed previews also need the game's four data files in the folder above it; without them the app still runs, with folder names and single-file thumbnails.

## Pages

The top bar carries the app's name, then the five tabs: Maps, Backgrounds, Platforms, Packs and
Settings. Ctrl+1 to Ctrl+5 switch between them, and Ctrl+K goes to Maps and puts the cursor in its
search box. On the right is the game line, reading "Brawlhalla running", or "Brawlhalla not running"
beside a Launch button that asks Steam to start the game. When the game folder cannot be found the
line turns red and offers Choose folder, which opens Settings. F5 rescans; Cancel in a page header
stops a long operation.

- **Maps** is the grid of maps, and the only page where maps are selected. Chips above the grid
  filter: All, one per level set, and Selected, which appears once anything is selected. The set
  chips are Ranked 1v1, Ranked 2v2 and
  Tournament when the game's data has the ranked sets, and the standard ones when it does not; the
  other sets the game defines are read but not shown. At the right end of the chip row is Select all,
  which selects every map the chips and the search currently show. The zoom slider in the header sets
  2 to 10 columns, 6 to begin with. Each card is a composed preview, the map's name, and a tag only
  when there is something to say: the pack the map matches, the name of the any-map picture on it,
  or Missing. A map that is entirely default draws no tag. Wide cards keep the tag on the name
  row; narrower ones draw it over the bottom-left corner of the picture instead, so every card in the
  grid is the same height. From 7 columns on the name is smaller and the tag is hidden; at 9 and 10
  the name row goes altogether, the name and tag become the card's tooltip, and a missing map is
  marked on the picture.
- Clicking a card opens the map panel and leaves the selection alone. Ctrl+click adds a map to the
  selection or takes it out, Shift+click takes a run of maps, Space toggles the card the keyboard is
  on, and Ctrl+A selects everything shown. Selected cards are outlined. With at least one map
  selected, a bar floats at the bottom of the grid saying how many, as in "3 of 67 maps selected",
  with Apply pack, Apply picture, Reset to default, Select all and Clear on it. Apply pack opens a
  menu of packs and gives each selected map that pack's platform art and background. Apply picture
  opens a menu of any-map pictures with Add Image at the end. A write to more than one map asks
  first and names the count. The selection clears after a write that worked, and stays after one that
  failed or was cancelled. Escape closes the panel when one is open, and clears the selection when
  none is.
- The **map panel** opens on the right and holds everything one map can do: its name and the sets it
  belongs to, a larger preview, a line saying in words what is in game, Reset this map and Open
  folder, and then a two-part switch, Background and Platforms. Background lists the choices two
  across: Default, one per pack that has a picture for this map, then the any-map pictures, one
  strip per pack that holds one and a last strip of the pictures only the game has, all folding open
  together. Platforms lists Default and each pack that has a set for this map, drawn
  over the map's current background. The choice the game is showing carries a check. Hovering a
  choice shows Apply and a dots button, and the dots or a right-click opens its menu: apply to this
  map, apply to the selected maps, apply to all maps, then Edit and Show in folder for a picture, or
  Show files and Open folder for a platform set. Reset all to default is in the page header.
- **Backgrounds** is one row per map: the map's name and its tag on the left, then a strip of every
  background that map could have. The picture the game is showing comes first, with a check and a
  border; then Default, then one thumbnail for each pack that has a picture for that map, captioned
  with the pack's name. Any-map pictures are folded behind a single tile reading "My Backgrounds" and a count,
  and when more choices exist than fit the row, a tile reading "+" and a number stands for the rest.
  Clicking either unfolds that row; rows fold back when the row is clicked again or the page is left.
  One click on a thumbnail applies it to that map, with no confirmation to answer. Hovering shows
  Apply and a dots button, and the menu offers to apply the picture to this map, to the selected
  maps or to all maps, then Edit and Show in folder, and for a picture in a pack Remove from that
  pack, or Save to My Backgrounds for a picture the game is showing that no pack holds. Search
  matches map names, pack names and file names. The chips are the ones Maps has, without Selected.
  The zoom slider sets the row height in five steps, the second to begin with. Add Image is in the
  header. Maps are not selected on this page: it has
  no selection boxes and no selection bar, and "apply to the selected maps" means the maps
  selected on Maps. Up and Down move between rows, Left and Right along a strip, and Enter applies
  the thumbnail the keyboard is on.
- **Platforms** is the same kind of row for the other half of a map's look. A platform set fits only
  the map it was drawn for, so a row holds exactly the packs that have a set for that map: the set in
  game first with a check, then Default, then one thumbnail per pack, captioned with the pack's name.
  Each thumbnail is cropped to the platforms themselves and drawn over the map's current background,
  because a whole level shrunk to row height shows the platforms as slivers. Every thumbnail in a row
  uses the same crop, so they can be compared. Clicking one applies it to that map. The menu offers
  to apply to this map or to the selected maps, then Show files and Open folder. Search matches map
  and pack names, and the zoom slider has the same five steps, the third to begin with.
- **Packs** is one row per pack: a composed thumbnail of the pack's first map, the pack's name and
  what it holds, as in "3 maps, 12 backgrounds", and a strip of previews of the maps it touches, as
  many as the width allows, with a count for the rest. Apply all is a button on the row; a dots
  button beside it holds Export, Open folder and Remove. The header has Import folder, Capture
  defaults and Open library. Clicking a row opens the pack.
- **Pack detail** shows one pack as a single grid of its maps, each composed with the game's art
  where the pack has nothing of its own, with zoom setting the column count. Clicking a tile opens a
  drawer listing that map's files, its background and its platforms, and where each came from. When
  the pack holds fully transparent PNGs, a line above the grid says how many of its files change
  nothing in game and offers to remove them. Apply all, Open folder and the way back to the pack list
  are in the header.
- **Settings** is one line per setting: the game folder and the library, each with Change and Open;
  the game data, with the date it was last read and Refresh now; Capture defaults; one line about
  applying, which says that changes are written straight into the game folder with Brawlhalla open or
  closed and show on the next match load, and that Undo puts back the files of the last write; and
  the version. There is no OK button, so every row saves as it is changed.

These windows open on top of the pages:

- **Welcome** opens on the first run and asks for the three things the app needs: the game folder,
  found through Steam when it can be, the library folder, and whether to capture the game's current
  art as the `Default` pack. It comes back if the saved game folder later stops working.
- **Add Image**, from the Backgrounds header, the selection bar or a map's panel, takes any
  number of images, dropped on the window or picked, fits them all one way (**Stretch**, **Center**,
  **Fill** or **Fit**) and writes each one into an existing or new pack as a 2048x1151 JPEG. A block
  of choices headed "Then" decides what happens after that: add them to the library and stop; add and
  apply to the map the window was opened from; add and apply to the selected maps; or add and apply
  to every map. Applying goes in order and starts again from the first picture when there are more
  maps than pictures. The pictures it reads are never changed.
- **Import folder** takes several folders at once and makes one pack of each. Every folder's name is
  filled in as its pack name and can be edited; a name that matches a pack already in the library
  adds to that pack after one confirm. The button reads "Import N packs". Inside each folder, every
  `.png` and `.jpg` at any depth is routed to a game folder, and the whole routing table is shown
  before anything is copied.
- The **background editor**, from a background tile's **Edit**, fits one picture to a background slot
  and saves it into a pack. Its title names the file. The source row shows the picture, its name, its
  pack and its size, with **Replace** beside it, and another image can be dropped on it. Under that:
  which map the picture is for, a slot several maps share naming them all; the fit, **Fill**, **Fit**
  or **Stretch**, with two pan sliders that apply only to Fill; and how far to darken it. It saves
  into a pack, the picture's own to begin with, under a note saying which file it replaces and that
  choosing another pack keeps the original, and it can write the result into the game in the same
  step. Opened from Add Image with no picture, the source row is where a picture is dropped or
  browsed for.

Every change is written straight into the game folder, whether Brawlhalla is open or closed, and
nothing is restarted. Each write reports in the page header: what it did, then "Shows on the next
match load." while the game is running or "Shows when Brawlhalla starts." while it is not, then
**Undo**. Undo puts back the files that write overwrote or deleted. One set is kept, so the next
write into the game folder replaces it.

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
  seasonal ones, are not maps: they stay out of the grid and the rows pages, and are still covered by
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
  default; one that matches other packs is labelled with them; one that matches nothing is in game
  only; a
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
  picture it came from is not touched. **Applying a platform set** copies every file that pack holds for
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

A write goes into the game folder whether Brawlhalla is running or not; the app never closes or
starts the game to make a change. A change made while the game is up shows on the next match load,
and one made while it is down shows when the game starts.

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
- `Throttler` lives in `BhMaps.Core\Threading` and is tested from `tests\BhMaps.Core.Tests` like
  everything else in Core, so the WPF layer still has no test project of its own.

## Known limitations

- A subfolder inside the game's `mapArt` that cannot be listed, because it is locked or the ACL
  denies access, fails the whole scan with an error dialog instead of being skipped.
- Cancel interrupts only the operations that check for it: the scan, Reset all to default, applying a
  pack, applying a background, applying a platform set, exporting a pack, capturing defaults,
  importing a folder and adding images. Resetting one map, deleting a pack and Undo run to the end.
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

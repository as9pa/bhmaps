# BhMaps v2 design

Date: 2026-09-10. Supersedes the UI sections of `2026-09-08-bhmaps-design.md`; the core sections of that spec (scan, hashing, status, apply, reset, import routing, background fit, settings storage, safety rules, testing tiers) stay in force except where this document changes them.

The visual reference is the design canvas "BhMaps Redesign" v4 (14 boards: Main, HomeOpen, HomeOpenFiles, Backgrounds, Platforms, PlatformsAll, Packs, PackDetail, Settings, Welcome, Editor, ImportPhotos, ImportPack, Components) plus the owner's final comments recorded in section 2. Where the canvas and section 2 disagree, section 2 wins.

## 1. Goal and scope

BhMaps becomes a map-first manager:

- Every map is shown as an exact composed preview built from the game's own level data, under its real in-game name, filterable by the game's own level sets.
- Backgrounds form one universal library; platforms are per map.
- A captured `Default` pack makes reset exact and instant.
- Packs stack: applying a pack touches only the slots the pack has.
- The UI is rebuilt on the WPF Fluent dark theme with the canvas tokens and the Geist typefaces.

Out of scope (recorded as Later): per-file Edit on platform files, custom maps generated from photos, a separate home page, presets and mashups, favourites, a per-pack pastel accent.

## 2. Decisions from the review

All seven assumptions from the build plan were accepted on 2026-09-10:

- A1 Reset to default applies the captured `Default` pack; delete-and-regenerate is the fallback only when no `Default` pack exists.
- A2 Apply all from a pack writes only the slots that pack has, nothing else; packs stack in application order.
- A3 Applying a background to a map copies the image into that map's background slot under the file name the game expects; the source pack is untouched.
- A4 New installs default the library to `Documents\BhMaps`; an existing library is chosen in the welcome flow; nothing is moved.
- A5 While Brawlhalla runs, write actions become "Restart and apply". Live writes to a running game are not attempted until the owner's manual test says they are safe.
- A6 A fully transparent platform PNG counts as "changes nothing". Removing such files is an offer, never automatic.
- A7 The v1 main window, folder detail view and settings dialog are replaced. The background editor is restyled in place.

Final comments, applied:

- Home: no summary bar. No composition bars under map cards or beside sidebar names. Header holds the search box and "Reset all to default" only. Search autocompletes map names. Zoom is a compact slider at the right end of the chip row; its default will be tuned by hand later, so it persists in settings.
- Backgrounds: header on one line: title, search, zoom, one button "Add pictures". No separate "New background" button; the editor opens from a background's Edit action. Grid rows are uniform.
- Platforms: a pack that has no platform files for the selected map is not listed, not even as a placeholder.
- Settings: one line per setting, no explanatory subtext. Game data refreshes on its own when the game's data files change; the page shows one sentence, the date last read, and a "Refresh now" button.
- Packs: the pack detail page shows every row; nothing is truncated.
- Delivery: the owner allowed creating and merging pull requests for this build without further review, and asked to be contacted only when the build is complete and merged. The running-game test (section 6.7) is the one step the owner performs.

## 3. Game level data

### 3.1 Sources

The game root is the parent of the configured `mapArt` folder. The files read, never written:

| File | Holds |
| --- | --- |
| `BrawlhallaAir.swf` | The decryption key, as a uint constant in the ActionScript bytecode |
| `Dynamic.swz` | One `LevelDesc_<Level>.xml` entry per level (about 120) |
| `Init.swz` | `LevelTypes.xml`: level name to display name and flags |
| `Game.swz` | `LevelSetTypes.xml`: named sets of level names |

### 3.2 Container format

Reference implementation: `docs/superpowers/reference/swz.py` (the project's own Python port, verified on 2026-09-09 against the live game). The C# implementation is written from this description and that file; third-party source is not copied.

- PRNG: WELL512 seeded as in `swz.py` (`SwzRandom`).
- Header: bytes 0-3 big-endian u32 `expected`; bytes 4-7 big-endian u32 `seedWord`. `seed = seedWord ^ key`.
- Key check: `cs = 0x2DF4A1CD`; repeat `(key % 31) + 5` times `cs ^= rng.Next()`; the key is right when `cs == expected`. The check needs only the 8-byte header and the PRNG, so scanning candidates is cheap.
- Entries start at offset 8 and repeat while more than 12 bytes remain: `csize = u32be ^ rng.Next()`, `usize = u32be ^ rng.Next()`, `entryChecksum = u32be`; then `cs = rng.Next()` and for each of `csize` bytes: `rnd = rng.Next()`, `bi = i & 0xF`, `b = data[i] ^ (((0xFF << bi) & rnd) >> bi)`, `cs = b ^ RotateRight(cs, (i % 7) + 1)`. The entry is valid when `cs == entryChecksum`. The bytes are a zlib stream (with header) inflating to exactly `usize` bytes of UTF-8 XML.
- Entry identity: the root element name (`LevelDesc`, `LevelTypes`, `LevelSetTypes`); other entries are ignored.

### 3.3 Key discovery

Parse the SWF body (`FWS` raw, `CWS` zlib, `ZWS` lzma), walk the tags, and for every `DoABC` tag (code 82) read the ABC constant pool's uint entries. Every uint is a candidate; the first one that passes the key check against `Dynamic.swz` is the key. The key found on 2026-09-09 was 826351010; it changes with game updates and is never hard-coded. When no candidate passes, level data is unavailable (section 3.6).

### 3.4 Data model

`LevelDesc`: `LevelName`, `AssetDir`, `CameraBounds` (X Y W H), `Background` elements (`AssetName`, optional H W), and a tree of `Platform` elements (attributes `X`, `Y`, `Scale`, `ScaleX`, `ScaleY`, `Rotation`, `Theme`, any of which may be absent) containing `Asset` elements (`AssetName`, `X`, `Y`, `W`, `H`). A negative `W` or `H` means a flip on that axis. A `Platform` with a `Theme` attribute is seasonal and skipped. `AssetName` values starting with `../` point at another folder under `mapArt` (for example `../Snow/Snow1.png`).

One malformed file is known: `LevelDesc_ThreeShips.xml` has `Initial="true"X=` (a missing space). Before parsing, insert a space between `"` and a following attribute name via the regex `"([A-Za-z]+=")` to `" $1`.

`LevelTypes`: for each `LevelType`, `LevelName`, `DisplayName`, `DevOnly`, `TestLevel`. `DevOnly="true"` or `TestLevel="true"` levels are excluded.

`LevelSetTypes`: for each `LevelSetType`, `LevelSetName` and a comma-separated `LevelTypes` list. The UI uses `Ranked1v1`, `Ranked2v2` and `Tournament1v1` when present, otherwise `Standard1v1`, `Standard2v2`, `Tournament1v1`; other sets are read but unused.

### 3.5 Cache and refresh

`%APPDATA%\BhMaps\leveldata.json` stores the parsed model plus a stamp: for each of the four source files, size and last-write ticks, plus the key. At startup the reader compares the stamp with the files; a match loads the cache, a mismatch re-reads in the background and then refreshes the model, and Settings shows "Read on <date>". "Refresh now" forces a re-read. Reading runs off the UI thread; the cache write is atomic.

### 3.6 Fallback

When the game root lacks the files, the key is not found, or an entry fails its checksum, the app runs without level data: display names fall back to folder names, sets are empty (chips other than All and Changed are hidden), and previews fall back to the v1 file-tile thumbnail. Nothing throws to the UI; the state is shown as one sentence in Settings.

## 4. Map model

`MapCatalog` folds level data onto the game tree:

- A map is a game folder (`AssetDir`) that at least one included level points at. Folders no level points at (theme folders such as `Halloween`, `Snow`, and `Test`) are not maps: hidden from Home and the sidebar, still covered by reset-all and pack operations.
- Base level per folder: the level whose `LevelName` equals the folder name when present; otherwise the level with the shortest display name that does not start with `Small `, `Big ` or `Tutorial` and is not a mini-game (`Catch Bombs`, `Color Platforms`, `Demon Island CTF`, `Beachbrawl Arena`).
- `DisplayName` is the base level's display name.
- `Sets`: a map is in a set when any of its levels is in the set.
- `BackgroundSlots`: the distinct `Background` asset names used by the map's levels (files under `Backgrounds\`).
- `PlatformFiles`: the distinct platform asset names under the map's own folder, plus the referenced `../` files (attributed to their own folder).

## 5. Compositor

`MapCompositor.Render(level, size, sources)` draws a preview:

1. Fill with the tile colour `#2F2D2B`.
2. Scale = min(size.W / camera.W, size.H / camera.H); centre the camera rectangle.
3. Draw the background stretched to the camera bounds.
4. Walk platforms depth-first with a transform stack (translate X Y, scale, rotation), skipping `Theme` platforms; draw each asset at its X Y W H, flipping on negative W or H.
5. Sources decide where each asset comes from: the game folder, or a pack's folder, or a specific background image. A fully transparent PNG from a pack falls back to the game file for that slot.

Rendering runs on a dedicated STA worker thread using `DrawingVisual` and `RenderTargetBitmap`, never on the UI thread. Sizes: card 640x360 (shown at zoom), panel 1280x720. Results are JPEG files under `%APPDATA%\BhMaps\previews\<sha256 of level name + size + every input file hash>.jpg`; a cache hit skips rendering. Unused cache files older than 30 days are deleted at startup.

Golden tests use synthetic level XML and synthetic PNGs written by the tests: flips, nested scale, rotation, theme skipping, `../` references, transparent fallback. An opt-in test (section 9) renders the real `Grove`, `Blackguard` and `Enigma` and checks that the platform layer is non-empty.

## 6. Operations

### 6.1 Default pack and reset

The pack named `Default` (case-insensitive) is the reference. `Capture defaults` imports the game folder into `packs\Default` (confirm when it already exists; the import replaces it). `Reset to default` for a map applies the Default pack's files for that folder and the map's background slots; with no Default pack it deletes the map's PNG and JPG files so the game regenerates them (v1 behaviour). `Reset all to default` does the same for every folder.

### 6.2 Status

`MapStatusDetector` replaces the folder-level status for the UI. For each file in a map's folder and each of its background slots, hash and compare against every pack: matches Default only => Default; matches one or more other packs => those packs; matches nothing => Custom; a file the Default pack has and the game folder lacks => Missing. Map status is the summary: Default when everything is default; otherwise the pack names (at most two shown, then "+N"), Custom when any file is custom, Missing when any file is missing. Missing is the only coloured state.

### 6.3 Background library and apply

The library is every JPG under any pack's `Backgrounds\` folder plus the game's current backgrounds. A background is identified by pack and file name. Applying one to a set of maps writes it into each map's background slots: `Backgrounds\<slot>.jpg`, fitted with Cover to 2048x1151 when the source size differs. A confirm dialog names the maps before more than one map is written.

### 6.4 Platform sets and apply

For a map, a platform set is the Default pack or any other pack that has at least one file for that folder. Applying a set copies every file the pack has for that folder. Files that are fully transparent are copied like any other (the game then shows its own art), and the UI marks them "changes nothing".

### 6.5 Apply all, remove, export

Apply all from a pack applies every folder and every background the pack has, in one busy operation. Remove deletes the pack from the library after confirm. Export copies the pack folder to a folder the user picks. Remove transparent files (pack detail) deletes the flagged PNGs from that pack after confirm.

### 6.6 Undo

Before any write into the game folder, the files about to be overwritten or deleted are copied to `%APPDATA%\BhMaps\undo\<yyyyMMdd-HHmmss>\<Folder>\`. The last operation can be undone from the done line in the header; only the most recent undo set is kept.

### 6.7 Game running

`GameProcess` (v1) detects Brawlhalla. While it runs, write actions read "Restart and apply": close the game (main window close, then terminate after ten seconds), perform the write, relaunch through Steam (`steam://rungameid/291550`). The owner's manual test after the build decides whether live writes can be allowed; a setting `whileRunning` (`restart` default, `live`) exists so the change is one line.

### 6.8 Add pictures

The user drops or picks any number of images, chooses one fit mode (Stretch, Center, Fill, Fit, mapped onto the v1 fitter's Stretch, Contain-centred, Cover, Contain), and a target pack (existing or new name). Each image becomes `packs\<pack>\Backgrounds\<name>.jpg` at 2048x1151. Optionally the dialog assigns the pictures to selected maps straight away.

### 6.9 Import folder

The v1 import routing (plan and execute) stays, restyled.

## 7. UI

### 7.1 Shell and theme

- WPF, `ThemeMode="Dark"` (Fluent). Geist and Geist Mono (OFL) embedded as resources with `FontFamily` resources `Sans` and `Mono`.
- Tokens as resources: `Bg #161514`, `Surface #1C1B1A`, `Surface2 #242220`, `Line #2A2827`, `Line2 #3A3734`, `Text #F1EFEA`, `Text2 #9C9891`, `Text3 #6B675F`, `Tile #2F2D2B`, `Missing #3A1A1C` on `#E57A7A`. Radius 6. One solid button style (Text on Bg) for the page's primary action; every other button is outlined or plain.
- Window 1280x800, minimum 1000x640. Sidebar 240 px; right panel 360 px.
- Sidebar: app name; Home, Backgrounds, Platforms; divider; the map list (name plus a small state tag, filtered by the search box, multi-select with a checkbox that appears on hover or when selected); divider; Packs, Settings; game block at the bottom (Brawlhalla running, or Launch).
- Header per page: title left; page actions right. A single busy operation at a time (`RunBusyAsync`); progress and the done line with Undo appear in the header.
- Search box: filters the sidebar and the current page, with an autocomplete list of map display names; Enter opens the first match on Home.

### 7.2 Home

Chips: All (default), Ranked 1v1, Ranked 2v2, Tournament, Changed; zoom slider at the row's right end (2 to 5 columns, persisted as `homeZoom`). Grid of map cards: composed preview, display name, state tag. Header actions: search, Reset all to default. Clicking a card opens the right panel: large composed preview; Background row with the current background and clickable candidates from every pack plus "Add picture"; Platforms row with the available sets (hover shows Use; tick and subtle ring on the one in game); "Platform files, N" expands into a scrolling list of files with thumbnails and their source; Reset to default (this map); Open folder.

### 7.3 Backgrounds

Header: title, search, zoom (`backgroundsZoom`), Add pictures. Grid of the library; each tile shows the image, its name and pack, and a tick when it is in use by a selected map. Hover: Apply, Edit. With maps selected in the sidebar, a bottom selection bar reads "Apply to N maps" with Clear.

### 7.4 Platforms

Segmented control: the selected map, or All maps. Per map: the available sets drawn over the map's current background, the in-game set with tick and ring, then the file list with thumbnails. All maps: one row per map with its available sets. Reset lives in the header actions.

### 7.5 Packs and pack detail

List rows: name, "N maps, N backgrounds", Apply all, Remove, Export, Open folder. Header: Import folder, Capture defaults, Open library. Pack detail: Put together (maps composed with the pack's files), Backgrounds, Platforms (platform-only composites), and when the pack has transparent files, a line "N files change nothing in game" with Remove.

### 7.6 Settings

One line each: Game folder (path, Change, Open); Library (path, Change, Open); Game data ("Maps and names come from the game's files and refresh after a game update. Read on <date>.", Refresh now); Defaults (Capture defaults); While the game runs (Restart and apply); version.

### 7.7 Welcome

Shown when `welcomeDone` is false: 1 Game folder (auto-detected through Steam, Change); 2 Library (default `Documents\BhMaps`, Change; an existing library is accepted as is); 3 Capture defaults now? (Yes runs the capture; the text explains verifying game files through Steam first). Replaces the v1 backup prompt; `firstRunDone` is kept in settings for compatibility and no longer read.

### 7.8 Dialogs and states

Confirm (multi-map apply, remove, capture over an existing Default), Add pictures, Import folder, Background editor (restyled), Error summary (v1). States: empty library (Packs invites Capture defaults or Import), no results for a chip or search, Missing, game folder missing (header notice, write actions disabled), game running, busy, done line with Undo, visible keyboard focus on tiles and buttons, Space toggles selection.

## 8. Settings

`settings.json` gains `homeZoom` (int, default 3), `backgroundsZoom` (int, default 4), `whileRunning` (`restart`), `welcomeDone` (bool). Unknown fields are preserved on save.

## 9. Project layout and testing

New in `BhMaps.Core`: `LevelData/` (`SwzRandom`, `SwzReader`, `SwfKeyFinder`, `LevelDataReader`, `LevelDataCache`, models), `Maps/` (`MapCatalog`, `MapStatusDetector`), `Imaging/MapCompositor.cs`, `Imaging/TransparentPng.cs`, `Operations/` (`DefaultPack`, `BackgroundApplier`, `PlatformSetApplier`, `PackExporter`, `UndoStore`, `PictureImporter`). New in `BhMaps.App`: `Theme/` (tokens, control styles, fonts), one view and view model per page, `WelcomeWindow`, `AddPicturesWindow`, `Services/GameLauncher`. Removed: `FolderDetailView`, `SettingsWindow`, the v1 main window content.

Testing tiers stay: Core is unit tested; the WPF layer is checked by hand on the dev tree. Required Core tests: SWZ round trip on synthetic containers (encrypt in the test with the same algorithm, then read), key check, ABC uint scan on a synthetic SWF, LevelDesc parsing including the ThreeShips fix and `../` assets, catalog folding and base-level ranking, set membership, compositor goldens, status summary, Default reset and fallback, background apply into slots with fit, platform set apply, apply-all stacking (only the pack's slots change), undo restore, transparent detection, picture import fit modes, settings round trip with new fields and unknown-field preservation.

Opt-in tests read the real install and never write: they run only when `BHMAPS_REAL_GAME` is set, and check that the key is found, about 120 levels parse, `Grove` reads as `Twilight Grove`, and three real composites have a non-empty platform layer.

`scripts\make-dev-tree.ps1` gains `-RealArt`: copies `packs\Default` from the library into the dev `mapArt`, copies the four data files into the dev game root, and copies the other packs into the dev library, so manual walkthroughs show real maps. The app is still launched only with `--game`, `--library` and `--appdata` pointing at the dev tree.

## 10. Delivery

Work continues on `feature/bhmaps-v1`. When every step is verified: README refreshed, `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` clean, the open pull request updated and merged into `main` under the `as9pa` account (the owner allowed merging on 2026-09-10).

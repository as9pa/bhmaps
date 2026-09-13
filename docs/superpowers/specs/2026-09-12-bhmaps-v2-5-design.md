# BhMaps 2.5 design: editor memory, spanning Replace, many maps, background memory, map-select thumbnails

Date: 2026-09-12
Base: main 717a36e (BhMaps 2.4.0)
Author: Claude Fable 5.1 with the owner's answers
Review pages: proposal (7 questions, all answered) and the follow-up page 19447cc3-d3aa-498f-a7b4-85ff0ad6a1e5 version 2, whose items 1 to 8 and build order this document binds.

## 1. What 2.5 is

| Item | Change | Owner's decision |
| --- | --- | --- |
| 1 | Platform editor Save writes a record beside the pictures | In the pack |
| 2 | Opening loads the record and starts from the original art | Hash match picks the pack; the Default pack is the original store |
| 3 | Replace lays one picture across the platforms | Fitted to the platform box; Across is the default |
| 4 | Reset to default clears the memory too | As proposed |
| 5 | Edit platforms works on a selection, and on every map shown | As proposed |
| 6 | Background editor remembers crop, pan and darken | Now, same build |
| 7 | Edits reach the game's map-select picture, opt in | Fine, off by default |
| 8 | Bug: a map looked unedited after switching maps | Repro first, fix last in the editor work |

Nothing here removes a 2.4 behaviour. Save still writes plain PNGs and JPGs the game reads. The records are extra and never required: a pack without them behaves as in 2.4.

## 2. Vocabulary

- **Record**: a json file in a pack root. `platforms.bhmaps.json` holds platform entries, `backgrounds.bhmaps.json` holds background entries. One file per pack per kind, the latest save wins, no history.
- **Entry**: the saved values for one written file, keyed by the file's path relative to the pack root, backslash separated, as `MapEntry.PlatformFiles` and `AssetPath.Background` produce them (for example `BloodMoon\Platform_BM1.png`, `Backgrounds\BG_Sewer.jpg`).
- **Art kind**: what the saved piece was made from. `own` (the piece's own art through opacity and hue), `eachPiece` (a picture cover fitted on the piece, 2.4 Replace), `across` (a window cut from a picture laid across the platforms), `workingCopy` (edited in another app; the file is the truth and the entry carries no values).
- **Original**: the untouched art of a piece. The Default pack's copy first, the game's file second.
- **Source pack**: the pack whose record the editor loaded. The Target pack (Save into pack) defaults to it.
- **Hash match**: the SHA-256 match `MapStatusDetector` already makes between a game file and pack files, shown on cards as "matches pack X". Item 2 reuses it to choose the source pack.
- **Platform box**: `PlatformBounds.For(level, pad: 0, aspect)`, the union of every placed, unthemed platform asset in camera space, without the 0.12 padding the Ticked only preview adds.
- **Placed rectangle**: one drawn instance of a piece: its asset rectangle under the node chain's scale, flip and rotation, in camera space, as `PlatformBounds.Union` computes it.
- **Map set**: the maps one platform editor session edits. One map in 2.4; one or many in 2.5.
- **Game write**: any operation that goes through `MainViewModel.RunGameWriteAsync`: Apply pack, Apply picture, the editors' Apply to game now, Reset to default, Undo.
- **Thumbnail file**: the jpg in `<GameRoot>\images\thumbnails\` that a level's `ThumbnailPNGFile` names, 290 by 164 pixels, drawn on the game's map-select screen.

## 3. Records (items 1 and 6)

### 3.1 Files and shape

Both records use one reader and writer, `EditRecordFile`, in `BhMaps.Core\Packs\`. System.Text.Json, indented, camelCase, UTF-8 without BOM, written to a temporary file in the same folder and moved over the old one so a crash never leaves a half file.

`platforms.bhmaps.json`:

```json
{
  "version": 1,
  "maps": {
    "BloodMoon": {
      "savedAt": "2026-09-12T22:01:07-04:00",
      "pieces": {
        "BloodMoon\\Platform_BM1.png": {
          "opacity": 60,
          "hue": 180,
          "art": "across",
          "picture": "C:\\Users\\alexa\\Pictures\\sunset.jpg",
          "panX": 0.5,
          "panY": 0.35,
          "hash": "3f9c..."
        }
      }
    }
  }
}
```

`backgrounds.bhmaps.json`:

```json
{
  "version": 1,
  "slots": {
    "Backgrounds\\BG_Sewer.jpg": {
      "savedAt": "2026-09-12T22:03:11-04:00",
      "picture": "C:\\Users\\alexa\\Pictures\\sunset.jpg",
      "mode": "cover",
      "panX": 0.5,
      "panY": 0.5,
      "darken": 20,
      "hash": "a1b2..."
    }
  }
}
```

Rules:

- `picture` is the full path of the picture the owner picked, so it can be reloaded. The UI shows only its file name.
- `panX` and `panY` are 0 to 1 as the background editor already uses them. `darken` is the percent the slider shows. `opacity` and `hue` are the slider integers. `mode` is `cover`, `contain` or `stretch`.
- `hash` is `FileHasher.Hash` of the file the save wrote, taken after the write.
- An entry with `art` = `workingCopy` carries only `art` and `hash`; other fields are absent.
- Unknown properties on any object are kept through a read and write round trip (`[JsonExtensionData]`), so a newer BhMaps can add fields without an older one dropping them.
- A missing file reads as an empty record. A file that does not parse reads as an empty record too and the app never throws for it; the next save overwrites it.
- Map folder names and entry keys compare ordinal, ignoring case.

### 3.2 Operations on records

- `PlatformEditRecord.Load(packRoot)` and `Save(packRoot)`; `Entry(mapFolder, relativePath)`; `Set(mapFolder, savedAt, relativePath, entry)`; `RemoveMap(mapFolder)` returns whether anything was removed; `Maps` and `Pieces` read access.
- `BackgroundEditRecord.Load(packRoot)` and `Save(packRoot)`; `Entry(relativePath)`; `Set(relativePath, entry)`; `Remove(relativePath)`.
- Export (`PackExporter.Export`) copies both record files when present. Import (`ImportRouter`) copies image rows only, as today: a pack without records imports and works as in 2.4. Applying a pack to the game never copies a record; `PackApplier` walks `Pack.Folders` and the records live in the root, outside them.

## 4. Platform editor Save (item 1)

- Save writes, for every row, into the Target pack's record: the row's relative path, opacity, hue, art kind, and for `eachPiece` and `across` the picture path; for `across` also `panX` and `panY`. `hash` is taken from the written pack file after it is written. `savedAt` is the time of the save, one value per map.
- Cancel writes nothing, including the record.
- A row that is a working copy (edited in another app) writes `art` = `workingCopy` and `hash` only.
- A row that is untouched and not a Replacement is still written (its values are 100 and 0 and `art` = `own`) so the map's entry set is complete and a later open shows Values from for every row.
- A map's earlier entries in that pack are replaced as a whole by the new save, so a row deleted from the map does not linger.

## 5. Platform editor open (item 2)

### 5.1 Which record

1. Opened with a pack (`PlatformEditorRequest.Pack` not null): that pack's record.
2. Opened with no pack, from a card or a panel row: the source pack is the first pack, in the order `MapStatus.Files[i].PackNames` lists them for the map's platform files with state `Pack`, whose platform record has an entry set for the map. `MainViewModel.OpenPlatformEditorAsync` resolves this from the current `ScanSnapshot.MapStatuses` and passes the pack on the request as `SourcePack`.
3. Otherwise none: the editor opens as in 2.4.

### 5.2 Which pixels

For a row with an entry whose `art` is not `workingCopy`:

- `OriginalPath` is `<PacksRoot>\Default\<relativePath>` when that file exists, else the game's file `<GamePath>\<relativePath>`, and the row carries the note **Original missing** (5.4).
- The row's sliders start from the entry. `art` = `own`: the row shows its original art through the values. `art` = `eachPiece`: the picture is reloaded from `picture` and fitted on each piece as 2.4 Replace does. `art` = `across`: the picture is reloaded and cut through `SpanFitter` (section 6) with the entry's pan.
- When `picture` does not exist: the row's source is the saved pack file, the sliders show 100 and 0, and the row carries the note **Picture gone**: "sunset.jpg, missing. Showing the saved file." Replace picks a new one. The values are not loaded for this row because the saved file already has them baked in and loading them would stack.

For a row with no entry, or an entry with `art` = `workingCopy`: exactly 2.4. `OriginalPath` resolves through `AssetSources(GamePath, Pack.FullPath)`, pack file first, and the sliders start at 100 and 0.

### 5.3 The Values from line

Above the Files rows, shown only when a record was loaded for the current map:

- "Values from Default, saved 12 Sep 22:01." with **Start fresh** as a link at its end. The time is `savedAt` in local time, `d MMM HH:mm`.
- **Start fresh** drops the loaded values for this editor session only: every row returns to 2.4 resolution (pack file, 100 and 0, `own`), the line disappears, the notes clear. The record is not touched until Save.
- The Target pack defaults to the source pack. When no record was loaded the 2.4 default stands.

### 5.4 Row notes

Shown under the row's readout, one at a time, in this priority: Picture gone, Original missing, Changed outside, then the item 3 notes (drawn many times, not on this stage).

- **Original missing**: "The untouched art was not found, so the preview starts from the saved file." Start fresh clears it.
- **Changed outside**: "The file changed since, so the preview may differ from the game." Shown when `FileHasher.Hash` of the pack file no longer equals the entry's `hash`. Values still load.
- **Picture gone**: as 5.2.

## 6. Replace across the platforms (item 3)

### 6.1 SpanFitter (Core)

`BhMaps.Core\Imaging\SpanFitter.cs`:

- `SpanFitter.Placements(LevelDesc level, string relativePath)` returns every placed rectangle of the piece as `(Matrix Transform, Rect Local)` in camera space, walking `level.Platforms` with the same matrix chain `PlatformBounds.Union` uses (scale, then rotate, then translate, child times parent), skipping themed nodes, matching `AssetPath.Resolve(level.AssetDir, asset.AssetName)` to `relativePath` ordinal ignoring case.
- `SpanFitter.Box(LevelDesc level)` is the platform box: `SpanFitter.Box(level)`, the plain union of the unthemed platform rectangles with no padding and no aspect growth (`PlatformBounds.For` returns null for aspect 0, so the box has its own method sharing the same matrix walk), no aspect fitting.
- `SpanFitter.Cut(BitmapSource picture, Rect box, FitOptions pan, Matrix placement, Rect local, BitmapSource piece)` returns a frozen Bgra32 bitmap the size of `piece`: the picture is cover fitted into `box` with `BackgroundFitter.DestinationRect(picture.PixelWidth, picture.PixelHeight, pan, box.Width, box.Height)`; every pixel of the piece maps through the placement matrix into camera space, then into picture space, and samples the picture (bilinear through a `DrawingVisual` with the inverse transform, no hand loops); the result is multiplied by the piece's alpha as `PieceFitter.Fit` already does.
- `SpanFitter.Largest(placements)` picks the instance with the largest transformed area.
- When a piece has no placement it is not SpanFitter's job: the caller falls back to `PieceFitter.Fit`.

Tests (`tests\BhMaps.Core.Tests\SpanFitterTests.cs`) on a synthetic level with two pieces side by side: the right edge column of the left cut equals the left edge column of the right cut within one gradient step; a piece placed with `ScaleX` = -1 comes back mirrored; a piece placed with `Rotation` = 90 comes back rotated so that its cut, drawn through the placement again, reproduces the picture window; a piece placed twice is cut from the larger instance.

### 6.2 Editor behaviour

- Under Image, a two-way switch with `AutomationProperties.Name` "Fit": **Across the platforms** (default) and **On each piece**. The switch is enabled when a picture is loaded or when Replace can be used. Switching re-cuts the ticked rows from the loaded picture without asking for it again.
- Across: the picture is fitted to the platform box of the current map's `BaseLevel`. Dragging in the preview moves the picture (pan 0 to 1 on each axis, as the background editor's pan). The preview draws the joined result on the stage: pieces render from their cut bitmaps through opacity and hue, so the picture continues from one platform to the next.
- Each ticked row receives its cut, masked by alpha. Opacity and hue apply after the cut. Save writes the cut through `PlatformRecolor.Apply` as today and records `art` = `across`, `picture`, `panX`, `panY`.
- **Drawn many times**: a piece with more than one placement is cut from the largest and its row note says "Platform_Tile.png drawn 4 times, cut from the largest."
- **Not placed**: a piece with no placement on the map's base level is fitted on its own (`PieceFitter.Fit`, 2.4) and its row note says "Platform_Extra.png not on this stage, fitted on its own." Its entry records `art` = `eachPiece`.
- On each piece: 2.4 behaviour, `art` = `eachPiece`.

## 7. Reset clears the memory (item 4)

- Reset to default on a map (card menu, ticked reset, panel button) removes the map's entry set from `platforms.bhmaps.json` and the map's background slots from `backgrounds.bhmaps.json` in every pack the map's files currently match (state `Pack`, `PackNames`). Packs the map does not match are not touched.
- The removal happens inside the same write as the game reset and is undoable: the undo session captures the record files before the write, and Undo restores them with the game files.
- `UndoSession` gains a library side: `CaptureLibrary(libraryPath, relativePaths)` stores copies under `<session>\library\` and absent paths in `_library_absent.txt`; `UndoStore.Restore(session, gamePath, libraryPath)` restores both sides. A session with no library side restores as today.
- Applying a different pack to a map touches no record.

## 8. Background editor memory (item 6)

- Save writes into the Target pack's `backgrounds.bhmaps.json` the entry for the written file's relative path (`Backgrounds\<slot>` for a slot save, `Backgrounds\<name>.jpg` or `Backgrounds\<name> all maps.jpg` for the All maps save): `picture`, `mode`, `panX`, `panY`, `darken`, `hash`, `savedAt`.
- Opening: source pack is `BackgroundEditorRequest.PackName` when given, else the pack whose file the game's background slot matches by hash (`MapStatus.Files` for the slot, first `PackNames` entry with an entry in its background record), else none. When an entry is found and `picture` exists: `SourcePath`, `Mode`, `PanX`, `PanY`, `DarkenPercent` load from it; the line "Values from <pack>, saved <time>." with **Start fresh** shows above the Fit controls; the Target pack defaults to the source pack.
- **Picture gone**: `picture` does not exist: the controls load, the preview shows the saved pack file untouched, and the line reads "sunset.jpg, missing. Showing the saved file." Save is disabled until a new picture is picked (as today with no source).
- **Start fresh** clears the loaded values (source empty, Cover, 0.5, 0.5, 0) for this session only.
- Reset to default removes the slot's entry with undo, per section 7.

## 9. Many maps (item 5)

- `PlatformEditorRequest` carries `IReadOnlyList<MapEntry> Maps` (one or more). The card menu's Edit platforms row is enabled for any selection size and passes the selection. Select all shown then Edit platforms is the all maps route.
- Map strip: shown above the preview only when the set has more than one map: "Brawlhaven", "2 of 3 maps", previous and next buttons (`AutomationProperties.Name` "Previous map" and "Next map"). The preview, the Source line, the Values from line and the Files rows show the current map.
- Opacity, Hue and Replace act on every map's ticked rows at once. Ticks are per map. A picture laid across is fitted to each map's own platform box, so every map gets its own cut of the same picture. The Fit switch and the pan are shared by the set.
- Mixed: the sliders show "Mixed" when the ticked rows across the set differ, until the slider moves.
- Save loops the maps into one pack: a progress line at the bottom of the editor, "Saving 3 of 59 maps into Default", with a Cancel button that stops after the current map (maps already written stay written, their entries saved). Each map gets its own entry set. One undo entry for the whole run when Apply to game now is ticked: the game write applies every map through `PlatformSetApplier.Apply` with a single undo capture over all their target paths, done line "Default applied to 3 maps".
- `PlatformSave` carries `IReadOnlyList<MapEntry> Maps` written.

## 10. Map-select thumbnails (item 7)

### 10.1 Level data

- `LevelType` gains `string? ThumbnailFile` from `ThumbnailPNGFile` (attribute or child, as `Read` already handles); `LevelDataCache.SchemaVersion` becomes 3.
- `MapEntry` gains `string? ThumbnailFile`: the file named by its included levels when every level of the map names the same file and no included level of a different map names it. Shared across maps, no file, or differing files inside the map: null. `MapCatalog.Build` computes it.

### 10.2 Setting

- `AppSettings.WriteGameThumbnails` bool, default false, key `writeGameThumbnails`. Settings page row "Map-select thumbnails" with a CheckBox "Also update the game's map-select thumbnails" (`AutomationProperties.Name` the same) and one line under it: "When on, a game write that changes a map's art also writes the map's picture over its thumbnail in the game's images\thumbnails folder. The original is kept and comes back with Reset or when this is turned off."
- Turning the switch off restores every copied original in one undoable game write, "Restoring map-select thumbnails", done line "Map-select thumbnails restored". When there are none, nothing is written.

### 10.3 ThumbnailWriter (Core)

`BhMaps.Core\Operations\ThumbnailWriter.cs`:

- `ThumbnailWriter.Plan(MapEntry map, string gameRoot)` returns `ThumbnailTarget?`: null with a `SkipReason` when `map.ThumbnailFile` is null (`Shared` when another map names the file, `NoFile` when no file is named) or the jpg is missing in `<gameRoot>\images\thumbnails` (`Missing`). The public shape is `ThumbnailPlan(ThumbnailTarget? Target, ThumbnailSkip Skip, string? OtherMap)`.
- `ThumbnailWriter.Write(BitmapSource composite, string targetPath)` scales the composite to 290 by 164 (the composite is already 16:9, so a plain scale) and writes JPEG quality 88.
- `ThumbnailWriter.KeepOriginal(string targetPath, string originalsDir)` copies the jpg to `<originalsDir>\<file>` only when no copy exists yet.
- `ThumbnailWriter.RestoreAll(string originalsDir, string thumbnailsDir)` copies every kept original back and deletes the copy; returns the paths written.
- Composite: `MapCompositor.Render(map.BaseLevel, 580, 328, new AssetSources(gamePath), viewport: null, focus: null)` after the map's files are written, so the picture is the game's new state, then scaled to 290 by 164.

### 10.4 In the game write

- When the setting is on, every game write that changes a map's art (Apply pack, Apply picture, the editors' Apply to game now, Reset to default) adds each written map's thumbnail path to the undo paths and, after its own files are written and inside the same `RunGameWriteAsync`, calls `KeepOriginal` then renders and `Write`s. Reset to default instead copies the kept original back (when one exists) and does not render.
- Done line: the operation's text followed by " Map-select thumbnail updated." (one map) or " Map-select thumbnails updated." (many).
- Skipped maps carry a panel note under the map name, `MapPanelViewModel.ThumbnailNote`: "Map-select thumbnail not written: Twilight Grove shares its picture with Grid." / "Map-select thumbnail not written: no file is named for Brawlhaven." / "Map-select thumbnail not written: the file is missing from the game folder." The note is shown once, after the write, and clears at the next write of that map.
- Never on its own: no thumbnail is written unless the map's art is written in the same operation. The originals folder is `<AppDataDir>\thumbnails-original\`.

## 11. Bug 8

- Step 1 of the build reproduces the owner's steps in the dev tree twice: Apply to game now ticked, and pack only. The repro note goes into the plan's ledger.
- Fix (last step of the editor work): after a game write, `MapsViewModel.Refresh` reloads the written maps' cards first, then the rest in list order; a card's `Preview` is cleared before the reload so nothing from before the write is shown. `RunGameWriteAsync` passes the written folder names to the rescan (`RescanAsync(IReadOnlyList<string>? writtenFolders)`), and `MapsViewModel.LoadPreviewsAsync` takes a priority list.
- A unit-level test in `tests\BhMaps.Core.Tests` is not possible for the view model (App project tests are not part of the suite); the order logic lives in a small Core helper `LoadOrder.Prioritise(IReadOnlyList<string> all, IReadOnlyList<string> first)` with a test, and the view model uses it.
- Pack only case: the card correctly shows the game. The wording fix is item 2's Values from line.

## 12. Global constraints

- .NET 10, WPF, CommunityToolkit.Mvvm. `dotnet format BhMaps.slnx` only. Builds and tests with `--artifacts-path`. TreatWarningsAsErrors on.
- Tests only in `tests\BhMaps.Core.Tests` (xunit 2.9.3). Every new Core class has tests. No App project tests.
- New .cs and .xaml files CRLF. `docs/manual.md` and `README.md` CRLF UTF-8 without BOM. No em-dashes and no emoji in prose, UI text, docs or commit messages.
- WPF rules from 2.4: `FocusVisualStyle` as a local attribute on every focusable control, no bare `x:Static` const int into a double property, ObservableProperty setters run their `OnXChanged` during construction.
- Never run the app or tests against the real game folder, the real library or the real `%APPDATA%\BhMaps`. Verification uses the dev tree (`scripts\make-dev-tree.ps1`), which needs a fake `images\thumbnails` folder under the dev GameRoot for item 7.
- The Default pack is always in the library (owner's answer 3). When it is missing the fallback is the game file with the Original missing note, never a hidden copy.
- The records are never required: every path works when the file is absent.
- Version 2.5.0 in `src\BhMaps.App\BhMaps.App.csproj`. Manual gets "What is new in 2.5". README rows for the records, the Fit switch, many maps, and the thumbnails switch.

## 13. Open questions settled here

- Where the picture path lives: in the record, as a full path. The owner picks pictures from anywhere; a name alone cannot be reloaded.
- Picture gone and values: not loaded (5.2), to avoid stacking. The page said the saved file stays; the sliders show what that file is, 100 and 0.
- Record undo: a library side on the existing undo session (section 7), not a second undo store, so one Undo restores both the game and the pack.
- Bug 8 order test: a Core helper carries the order so the test can live where tests are allowed.

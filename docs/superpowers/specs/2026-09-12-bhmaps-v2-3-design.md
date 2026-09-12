# BhMaps 2.3 design: platform pieces, one file at a time

Date: 2026-09-12. Base: main b4296b8 (v2.2.2). Author: the session, on the owner's go in chat ("okay looks good.
you can build") and delegated judgment ("autopilot without me, use your best interest, think about the user").
Review pages: https://claude.ai/code/artifact/37b09a0a-3631-42d0-9e87-9a27cdb8c450 (page 1, the owner's answers)
and https://claude.ai/code/artifact/55f92d8e-767b-4a8d-9182-fcb965a9838b (page 2, every item as built here; the
"Keep the piece's shape" toggle was removed from it on the owner's ruling). This spec is binding where the pages
and the spec differ; section 12 lists every ruling made after the pages.

## 1. What 2.3 is

The 2.2 platform editor treats a map's set as one thing: two sliders move every piece at once. 2.3 makes the
editor work one piece at a time and lets a piece be replaced with a picture or edited in another program, so a
set can be built rather than only tinted. Nothing on disk changes shape: a set is still `packs\<pack>\<map
folder>\*.png`, one PNG per piece at the piece's own size, and the game folder is untouched by everything here
except the existing "Apply to game now".

Seven changes, all in the editor and the map panel:

| Item | Change |
|---|---|
| 1.1 | A Files block lists the map's pieces with a tick, a thumbnail, the file name and a readout |
| 1.2 | The sliders act on the ticked files only; each file keeps its own values; "Mixed" when they differ |
| 1.3 | Save writes the whole set: untouched files as plain copies, changed files as their result |
| 1.4 | All and None in the Files header |
| 1.5 | An Image block: what the piece shows today, Reset, Replace, Edit in another app |
| 1.6 | Replace fits a picture into the piece's exact size, keeping the piece's shape |
| 1.7 | Edit in another app opens the pack's copy in the program of the user's choice and previews every save |
| 2.1 | Edit on every row of the map panel's Platform files list, opening the editor on that one file |

The owner's answers on page 1, all applied: files listed with ticks (q1), per-file values with Mixed (q2), Save
writes the whole set (q3), All/None (q4), Edit on the panel rows (q5). The owner's ruling in chat on page 2: no
"Keep the piece's shape" toggle; a replacement always takes the piece's alpha ("replace only the pixels of the
platform only").

## 2. Vocabulary

- **Piece**: one PNG of the map's own platform art (`MapEntry.PlatformFiles`), named by its path relative to the
  map art root, for example `WarShuttle\Platform_WarShuttle1.png`.
- **Row**: the piece's line in the editor's Files block. A row is **ticked** or not.
- **Original art**: the piece as the editor found it: the pack's copy when the editor was opened from a pack's
  set and the pack has the file, otherwise the game's copy (`AssetSources.ResolveAsset`, unchanged).
- **Replacement**: a picture the user chose through Replace, fitted to the piece and held in memory until Save.
- **Working copy**: the piece's file inside `packs\<pack>\<map folder>\` after Edit in another app wrote it
  there. From then on it is the row's source and other programs write to it.
- **Result**: what a row produces: its source (original art, replacement or working copy) with the row's opacity
  and hue applied. The preview draws results; Save writes results.
- **Mixed**: the reading of a shared control when the ticked rows do not agree.

## 3. Item 1.1 to 1.4: the Files block, per-file values, All and None

**Basis.** A set is several files that play different parts on the map: a main deck, a side ledge, a floating
piece. Fading all of them equally is the rare case; the common one is fading the piece that hides the
background while the others stay solid. The block makes the file the unit the sliders act on, in the same window,
without a mode switch. Ticking is the app's existing selection idiom (the map cards on Maps), so the block reads
as "tick what you mean, then use the controls" with nothing new to learn.

Layout, right column of `PlatformEditorWindow`, between Source and the Image block (section 4):

- Header line: label "Files, 1 of 2" (ticked count of total, `FieldLabel` style) on the left; "All" and "None"
  on the right as `ResetLink` words. "All" is disabled when every row is ticked, "None" when none is.
- One row per piece, ordered by relative path (OrdinalIgnoreCase, as today): a CheckBox (`CardTick` style,
  `AutomationProperties.Name` = the file name, `FocusVisualStyle` `DialogFocusRing`) holding a 48 x 27 thumbnail
  (`PictureTile` border, `ImageBrush` `UniformToFill`), the file name (`MonoText`, ellipsis, tooltip the relative
  path) and, right-aligned, the row's readout in `Readout` style: "100%, 0" for defaults, otherwise the row's own
  values, for example "60%, +140".
- The thumbnail is the row's current source decoded through `AppServices.Thumbnails` for a file (original art or
  working copy) and a 240-wide `TransformedBitmap` of the fitted bitmap for a replacement, so a replaced row shows
  the new picture at once.
- Every row opens ticked at 100% and 0, except when the editor was opened for one file (item 2.1): then only
  that row is ticked.

Per-file values (1.2):

- Each row has its own `Opacity` (0..100) and `Hue` (-180..180).
- The Opacity slider shows the ticked rows' value when they agree, and its readout shows "60%". When they differ
  the readout says "Mixed" and the thumb sits at the first ticked row's value. Same for Hue.
- Moving a slider sets that value on every ticked row. Reset beside a slider sets the default on every ticked
  row.
- Ticking or unticking rows re-reads the sliders from the new ticked set; it never changes a row's values.
- With no row ticked the sliders, both Resets, Replace and Edit in another app are disabled and the Image line
  reads "Tick a file to edit it." (`HintText` style).

Save (1.3): writes every piece to `packs\<pack>\<map folder>\<file>`; a row at defaults with original art is a
plain copy of its source; every other row is its result (`PlatformRecolor.Apply` from the source, or from the
fitted bitmap for a replacement). A working copy row at defaults is left alone: its file is already what Save
would write. The "Replace platforms?" confirm fires only when the destination holds a file the editor did not
write in this window (a working copy of its own does not count), same title and body as 2.2.

Preview: unchanged mechanism (results written into the temp set, `MapCompositor.Render` over the map's current
background), now one result per row from that row's own source and values. Untouched rows are copied into the
temp set, not re-encoded, so a two-file set costs one recolour per moved row.

Files: `ViewModels\PlatformEditorViewModel.cs` (rows), new `ViewModels\PlatformPieceViewModel.cs` (one row),
`Views\PlatformEditorWindow.xaml`.

## 4. Item 1.5: the Image block

**Basis.** The sliders answer "how does the piece look"; the picture the piece is made of is a separate question
and gets a separate block, in the same shape as the slider blocks (label with a Reset word, a value line, the
control) so the column stays one rhythm. The value line always says where the piece's art is coming from, so a
piece replaced in memory, a piece being edited in Photoshop and an untouched piece are told apart without opening
anything.

Between the Files block and Opacity:

- Label "Image" with "Reset" as a `ResetLink` word (disabled when no ticked row has a replacement or working
  copy).
- Value line, one of: "The piece's own art"; "sunset.jpg, fitted to 1500 by 685" (replacement: the picked file's
  name and the piece's size); "sunset.jpg, fitted to each piece" (every ticked row replaced from the same file at
  different sizes); "in My Backgrounds" (working copy, the pack it lives in); "Mixed" (the ticked rows do not
  share one of the above); "Tick a file to edit it." in `HintText` style when nothing is ticked.
- Two buttons in a row, `PlainButton` style, `DialogFocusRing`: "Replace" and "Edit in another app". Both are
  disabled with no ticked row; Edit in another app is also disabled while the pack name is invalid
  (`PackNameError` non-empty), because it needs a pack to write into.
- Error line under the buttons (`ErrorText` style), for example "Could not read sunset.heic. Use a PNG, JPG, BMP,
  GIF or WebP."; cleared by the next successful Replace or Reset.

Reset on the Image line: a replaced row drops its replacement (memory only, nothing asked). A working copy row
asks first, once for the whole click: title "Put the original art back?", body "<file> in <pack> was changed in
another app. Reset replaces it with the piece's original art." (several rows: "<n> files in <pack> were changed in
another app. Reset replaces them with the pieces' original art."). On yes the original art is copied over the
working copy and the row returns to the original-art state with its source back on the original piece; the file
stays in the pack as a plain copy, which is what Save would write for it anyway. Reset never touches the row's
opacity or hue.

Tab order in the column: Source, All, None, the row ticks in order, Image Reset, Replace, Edit in another app,
Opacity Reset, Opacity slider, Hue Reset, Hue slider, Save into pack, New pack name, then the bottom row.

## 5. Item 1.6: Replace

**Basis.** A platform is a shaped cut-out: transparent corners, a ledge silhouette. A picture dropped on it as a
rectangle would draw a box on the map where the game draws nothing, which is the one outcome the owner ruled out.
So the picture is fitted to the piece's rectangle the way a background is fitted to the screen (cover, centred),
and then cut to the piece's shape by multiplying in the piece's alpha. The result is the piece with a new skin.

Rules:

- Replace opens `IDialogs.PickImageFile("Replace <file name>")` (one ticked row) or `("Replace 2 files")`; the
  filter is the app's image filter. Cancel changes nothing.
- The picture is decoded once (`BackgroundFitter.LoadSource`) and fitted to every ticked row's piece with
  `PieceFitter.Fit(source, piece)`: cover fit through `BackgroundFitter.DestinationRect(srcW, srcH, new
  FitOptions(), pieceW, pieceH)` (Cover, PanX 0.5, PanY 0.5: the largest scale that covers, centred crop), drawn
  with HighQuality scaling into a bitmap exactly `pieceW x pieceH` at the piece's DPI, Bgra32, then for every
  pixel `A = round(pieceA * sourceA / 255)`, colour from the fitted picture. No pan, no zoom, no toggle.
- The fitted bitmap is held in memory on the row until Save, Reset or Cancel. Opacity and Hue apply on top of it
  exactly as on original art, so the preview is the same bytes Save writes.
- A picture that cannot be decoded (HEIC, a corrupt file) sets the Image error line to "Could not read <name>.
  Use a PNG, JPG, BMP, GIF or WebP." and changes no row.
- Replace on a working copy row replaces the working copy's art in memory the same way (the working copy is the
  piece whose alpha is used); Save then writes the result over the working copy.

Core: `src\BhMaps.Core\Imaging\PieceFitter.cs`, `public static class PieceFitter` with
`public static BitmapSource Fit(BitmapSource source, BitmapSource piece)` (frozen Bgra32, `piece.PixelWidth x
piece.PixelHeight`, `piece.DpiX`/`DpiY`) and `public static BitmapSource Fit(string sourcePath, string
piecePath)`. `PlatformRecolor` gains `public static void Apply(BitmapSource source, string destPng, double
opacity, double hueDegrees)`; the file overload decodes and calls it. Tests pin: exact output size for a source
wider than the piece and one taller; the centred crop (which quadrant of a quadrant picture lands where); alpha as
the product (0 stays 0, 255 x 128 gives 128, 200 x 128 gives 100); a fully transparent piece gives a fully
transparent result; the bitmap overload of `Apply` writes the same bytes as the file overload for the same
picture.

## 6. Item 1.7: Edit in another app

**Basis.** Two sliders and a fit cover recolouring and reskinning; drawing on a piece needs a paint program, and
the app should hand the piece over and take the result back rather than pretend to be one. The hand-over goes
through the pack, not the game folder: the game's file is never opened for editing, so nothing the other program
saves reaches the game until the user presses Save with Apply ticked, and Undo covers it as one apply. The file is
opened with the Windows "Open with" dialog because the PNG default on most PCs is a viewer with no edit verb.

Rules:

- Enabled when at least one row is ticked and the pack name is valid. If "Save into pack" is New pack, the pack is
  created first (`PackCreator.TryCreate(libraryPath, name, out error)`; on failure the Image error line shows the
  error and nothing opens).
- For every ticked row, the working copy path is `packs\<pack>\<map folder>\<file name>`. The row's result is
  written there when the file is absent, or when the row has a replacement or non-default values (the user changed
  it in this window, so that is what they want to open). An existing file with an untouched row is opened as it
  is. After the write the row's source becomes the working copy, its opacity and hue return to 100 and 0 (they
  are baked into the file now), and its Image state is "in <pack>".
- Each working copy is opened with `EditorLauncher.OpenWith(path)`: `Process.Start(new ProcessStartInfo(path) {
  UseShellExecute = true, Verb = "openas" })`, returning a reason string on `Win32Exception` or
  `InvalidOperationException` and null on success. A failure goes to the Image error line; the working copy stays.
- A `FileSystemWatcher` per library set folder the rows read from (`*.png`, Changed, Created, Renamed; the
  pack's set folder when the editor was opened from a pack's tile, and every working copy's folder; never the game
  folder) re-renders the preview and the row's thumbnail through a `Debouncer` of 250 ms. A file still locked by
  the other program (`IOException` on read) is retried every 250 ms up to 12 times, then the Image error line says
  "Could not read <file>: <message>". Watchers are created when the folder first becomes a source and disposed in
  `Cleanup`.
- Save: a working copy row at defaults is left alone; a working copy row with values is recoloured in place (the
  decode completes before the write, so source and destination may be the same file). A working copy written in
  pack A while Save now targets pack B is written into B as a result; A keeps its file.
- Cancel keeps every working copy. The shell then rescans (the pack has a set the last scan never saw) and sets
  the status line, not undoable: "Platform_WarShuttle1.png stays in My Backgrounds." for one file, "3 files stay in
  My Backgrounds." for several in one pack, "3 files stay in the library." across packs. The view model exposes
  `WorkingCopies` (`IReadOnlyList<(string Path, string PackName)>`) for this.
- Reset over a working copy: section 4.

Files: new `Services\EditorLauncher.cs`, `PlatformEditorViewModel.cs`, `PlatformPieceViewModel.cs`,
`MainViewModel.OpenPlatformEditorAsync` (the Cancel line and rescan).

## 7. Item 2.1: Edit on the panel's Platform files rows

**Basis.** The panel already lists the map's pieces with a thumbnail and where each came from; the one thing a
user can do with a row today is read it. Edit on the row is the shortest path from "that ledge is wrong" to the
editor with only that ledge ticked. The button appears on hover and focus the way the picture tiles' actions do,
so the list stays quiet until pointed at, and stays keyboard-reachable because it is faded, not collapsed.

Rules:

- `PlatformFileViewModel` gains `bool CanEdit` (the game folder has the file) and `EditCommand`. The Edit button
  (`TileAction` style, `AutomationProperties.Name` "Edit <file name>") sits at the row's right, `Opacity` 0,
  revealed by `IsMouseOver` on the row and `IsKeyboardFocusWithin`; `Visibility` Collapsed when `CanEdit` is
  false. The row's Grid gets `Focusable="True"` and `FocusVisualStyle="{x:Null}"` like the tiles.
- Edit calls `shell.OpenPlatformEditorAsync(map, pack: null, onlyFile: relativePath)`: the in-game set as the
  source, and `PlatformEditorRequest(MapEntry Map, Pack? Pack, string? OnlyFile = null)` carries the file. The
  editor opens with only that row ticked; every other row is present and unticked.
- The set tiles' Edit (2.2) is unchanged and passes no file.

Files: `ViewModels\MapPanelViewModel.cs`, `Views\Pages\MapsView.xaml` (`PlatformFile` template),
`ViewModels\MainViewModel.cs`, `ViewModels\PlatformEditorViewModel.cs`.

## 8. States

| State | Behaviour |
|---|---|
| Map with no pieces | Files block shows no rows and "Files, 0 of 0"; the 2.2 empty text stays; everything disabled |
| Nothing ticked | Sliders, Resets, Replace, Edit in another app disabled; Image line "Tick a file to edit it." |
| Ticked rows disagree | Readout "Mixed"; thumb on the first ticked row's value; moving it sets all ticked |
| Opened from a panel row | Only that row ticked; Source reads "In game, <n> files" |
| Replace cancelled | Nothing changes |
| Replace with an unreadable file | "Could not read <name>. Use a PNG, JPG, BMP, GIF or WebP."; rows unchanged |
| Replace on a transparent piece | Result fully transparent; the preview shows the background there |
| Edit in another app, New pack | Pack created, then the copies written and opened |
| Edit in another app, invalid pack name | Button disabled; the pack name error explains |
| Other program has the file locked | Retried for 3 s, then "Could not read <file>: <message>" |
| Other program saves | Preview and row thumbnail follow within about a quarter second |
| Reset over a working copy | Confirm; on yes the original art is copied over the file |
| Save, working copy at defaults | File left alone |
| Save, destination has foreign files | "Replace platforms?" as in 2.2 |
| Cancel with working copies | Copies kept; rescan; "<file> stays in <pack>." on the status line, no Undo |
| Cancel without working copies | As 2.2: nothing written, nothing rescanned |
| Windows animations off | No change: the editor has no animation |

## 9. Not changing

The five tabs, the set tiles and their Edit, Save into pack and New pack, Apply to game now through
`ApplySetAsync` with one Undo, the preview mechanism and its 60 ms throttle, the temp set under %TEMP%, the
theme, the Settings page, the background editor, the manual's layout.

## 10. Release

Version 2.3.0 in `src\BhMaps.App\BhMaps.App.csproj`. Manual: a "What is new in 2.3" list in this spec's words
and the platform editor description in "## Pages" rewritten for the Files and Image blocks. README download rows.
Publish with `scripts\publish.ps1`, tag `v2.3.0`, GitHub release notes from the item list.

## 11. Verification

Only `tests\BhMaps.Core.Tests` exists and it references Core alone, so `PieceFitter` and the `PlatformRecolor`
overload get unit tests and the editor, launcher and panel are verified on the dev tree: UIA captures through the
2.2 capture library (never activating a window), a scripted overwrite of a working copy to prove the watcher
re-renders, and code reading for the "Open with" dialog, which cannot be driven without focus.

## 12. Rulings made after the pages

1. No "Keep the piece's shape" toggle (owner, chat): a replacement always takes the piece's alpha.
2. Edit in another app writes the row's result when the file is absent or the row was changed in this window,
   and opens an existing file untouched otherwise. Cost if wrong: one overwrite the user asked for by changing
   the row.
3. Reset over a working copy copies the original art over the file rather than deleting it, so the pack's set
   stays complete and the row's state is unambiguous. Cost if wrong: one file the user can delete.
4. The Mixed thumb sits at the first ticked row's value. Cost if wrong: a thumb position.
5. "sunset.jpg, fitted to each piece" when several ticked rows share a source at different sizes, so a two-file
   replace does not read as Mixed. Cost if wrong: one string.
6. The "Replace platforms?" confirm ignores the editor's own working copies. Cost if wrong: one missing confirm on
   files the user just made.
7. The panel row's Edit uses the in-game set (pack null) as the source, whatever pack the game's file came from,
   because the row describes the game's file. Cost if wrong: the Source line names In game instead of a pack.
8. The Cancel status line goes through `MainViewModel.SetLibraryDone` (not undoable), because a working copy is a
   library write, not a game write.
9. The file watcher covers every library set folder the rows read from (a pack's set when the editor was opened
   from that pack's tile, and every working copy's folder), from the moment the folder becomes a source, never
   the game folder. A pack's files are the user's own and can change under the editor just as a working copy
   can, and this lets the watcher be verified by overwriting a pack file with a script while the editor is open
   from the pack's tile. Cost if wrong: one watcher per open editor on a folder that rarely changes.

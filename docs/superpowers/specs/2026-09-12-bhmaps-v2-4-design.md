# BhMaps 2.4 design: preview focus, and right click on pictures and maps

Date: 2026-09-12. Base: main 1c207b4 (v2.3.1). Author: the session, on the owner's go in chat ("looks good"
on both review pages) under the standing autopilot authorisation. Review pages:
https://claude.ai/code/artifact/f4f0a2bc-2e6f-4ba2-9bbf-84048f5b56ea (A, platform preview focus, no answers
saved; section 9 rules every question with the page's recommended choice) and
https://claude.ai/code/artifact/f43fcf21-148c-466b-8830-57d532b599ea (B, right click on pack pictures and
map cards; all four answers saved, all the recommended choice). This spec is binding where the pages and the
spec differ.

## 1. What 2.4 is

Two independent pieces of UI, released together as 2.4.0.

**A. Preview focus in the platform editor.** The editor's preview is always the whole map. When a map has six
platform files and you work on one, you cannot tell which shape is which file. 2.4 adds a preview mode that
follows the ticked files, and a one-click way to tick only the file you are looking at.

**B. Right-click menus on pack pictures and map cards.** A picture in My Backgrounds can only go to the one map
its file name says, and a map card on the Maps page has no menu; everything runs through ticks and the bottom
bar. 2.4 adds a right-click menu to every picture tile in a pack, a right-click menu to every map card, and one
small chooser window that both menus share.

| Item | Change |
|---|---|
| A1 | Two chips over the editor preview: All pieces, Ticked only |
| A2 | Ticked only fades the other pieces to 15 percent and frames the ticked pieces with 12 percent padding |
| A3 | Click a file's name in the editor to tick only that file; Ctrl+click adds it |
| A4 | The map panel row's Edit opens the editor in Ticked only |
| A5 | The editor remembers the mode across sessions |
| B1 | Right-click menu on every picture tile in a pack detail grid |
| B2 | A chooser window: titled, searchable, single choice, used to pick a map or a picture |
| B3 | Right-click menu on every map card |
| B4 | One vocabulary for the apply verbs across the three tile menus; the Backgrounds tile menu gains "Apply to a map..." |

Nothing on disk changes shape. No manifest, no new library file, no settings file change except one new key
(A5). The game folder is written only through the shell's existing write paths.

## 2. Vocabulary

- **Piece**: one PNG of a map's own platform art, named by its path relative to the map art root.
- **Row**: a piece's line in the editor's Files block; a row is **ticked** or not.
- **Focus set**: the relative paths of the ticked pieces, handed to the render.
- **Ghost**: a piece outside the focus set, drawn at 15 percent opacity.
- **Picture**: a background image file in a pack's Backgrounds folder. A picture is **owned** by a map when
  the map's BackgroundSlots match its file name (Pack.FindFolder and slot matching, as today); otherwise it is
  an **orphan** (every My Backgrounds import).
- **Ticked maps**: the cards ticked on the Maps page (MapsViewModel selection), whatever page is showing.
- **Target**: the map or maps a menu line acts on.
- **Chooser**: the B2 window.

## 3. Part A: preview focus in the platform editor

### 3.1 Two chips over the preview (A1)

Top left inside the preview card, above the image, with the same 16 px margin the image has. Two ChipToggle
chips in one group, "All pieces" and "Ticked only"; exactly one is on. Default is All pieces, so an editor
opened the 2.3 way looks the same as before.

- Switching re-renders through the existing 60 ms throttle (PlatformEditorViewModel.PreviewInterval).
  Nothing is written anywhere.
- Opened from a panel row's Edit (PlatformEditorRequest.OnlyFile set) the editor starts in Ticked only (A4).
  Otherwise it starts in the remembered mode (A5).
- Focus: the chips take the dialog focus ring. Tab order: chips, then Source, Files, as today. Space or Enter
  switches. AutomationProperties.Name on each chip is its text.
- A map with no platform art hides the chips together with the preview, as the empty text already does.
- Read errors keep today's Error line; the mode never adds a message of its own.

View model: `PreviewMode` is a bool `IsolatePreview` on PlatformEditorViewModel (true = Ticked only) with two
commands or a two-way binding the ChipToggle style already supports. Changing it schedules a render.

### 3.2 What Ticked only draws (A2)

- Pieces in the focus set draw as today. Pieces outside it draw at 0.15 opacity (the ghost). The background
  stays at full strength. Themed (seasonal) nodes stay hidden as today.
- The frame leans in: the camera box becomes the union of the focused pieces' bounds plus 12 percent padding
  (PlatformBounds.Pad, the value the panel thumbnails already use). Null bounds (nothing ticked, or focused
  pieces with no size) fall back to the full camera.
- All ticked equals All pieces pixel for pixel. An empty focus set in All pieces mode is today's render.
- Ticked only with nothing ticked: the whole map ghosted, one scrim line at the bottom of the preview:
  "Tick a file to see it on its own." (constant `IsolateHintText`). Same wording family as TickHintText.
- Sliders, Replace and Edit in another app act on the ticked pieces exactly as now. The ghost is preview only
  and never in what Save writes.

Core: `MapCompositor.Render` gains an optional focus parameter (a set of relative asset paths, null = none)
and a ghost opacity (default 0.15, a public const `GhostOpacity`); DrawAsset pushes an opacity for assets
outside the set. `PlatformBounds.For` gains an overload that unions only the nodes whose assets are in the
focus set. Existing callers pass nothing and get today's output.

Core tests, three: a ghosted asset's pixel is about 15 percent of its full-strength value; focus bounds cover
only the focused nodes; an empty focus falls back to the full camera. Existing render tests still pass.

### 3.3 Click a file's name to solo it (A3)

- The row's thumbnail and name become one click target. Click ticks only that row and unticks the rest
  (`SoloCommand(row)`). Ctrl+click adds the row to the ticks without clearing the others (`AddTickCommand(row)`).
  The checkbox is unchanged.
- In Ticked only the preview snaps to that file. In All pieces the click just moves the sliders to that file.
- Rows show a hover surface (Surface2) so the target reads as clickable. The name stays plain (no underline).
- Keyboard: the row is focusable; Enter or Space on the name solos it. The checkbox keeps Space for toggling.
- The Files header, sliders and Image line follow the ticks exactly as they do for All and None today.

### 3.4 The panel row's Edit opens in Ticked only (A4)

PlatformEditorRequest.OnlyFile set: `IsolatePreview` starts true. No separate code path.

### 3.5 Remembered mode (A5)

One setting, `PlatformPreviewIsolate` (bool, default false), in AppSettings and SettingsStore, written when the
chip changes, read when the editor opens without OnlyFile. One round-trip test in Core tests.

## 4. Part B: right click on pictures and maps

### 4.1 Right-click menu on every picture tile in a pack (B1)

Applies to every pack's detail grid (PackDetailView), My Backgrounds included. The menu is the existing
`TileMenu` ContextMenu resource with `TileMenuCommand` items, the same as the Backgrounds page tiles. Left
click keeps opening the drawer; the drawer keeps its Apply to this map button.

Header line: the file name, in the menu's muted style (a disabled TileMenuCommand, or a header item styled
Text3 11.5 px; the implementer picks whichever the TileMenu resource supports without a new style).

Lines, in this order, each present only when its condition holds:

1. "Apply to <map>" when the picture is owned by a map. Writes through the shell's existing apply path for one
   picture and one map (no confirm).
2. "Apply to N ticked maps" when the Maps page has ticks (N >= 1). Same path as the Backgrounds tile's line.
3. "Apply to a map..." always. Opens the chooser (B2) in map mode; the chosen map gets the picture, no confirm.
4. "Apply to all maps" always. The shell's existing ApplyPictureAsync over every map with BackgroundSlots:
   the same "Apply {name} to these {N} maps?" confirm, the same Undo snapshot and done line the panel uses.
5. separator
6. "Edit" opens the background editor on this picture as the Backgrounds tile's Edit does.
7. "Show in folder" reveals the file in Explorer.
8. "Remove from pack" moves the file out of the pack the way Remove from library does on the Backgrounds page
   (the same removed-by-BhMaps folder convention). Never touches the game. Rescans afterwards.

The tile also gets the small hover menu button the Backgrounds tiles have, opening the same menu, so the menu
is reachable by mouse without right click and by keyboard (Menu key, Shift+F10) through the TileMenu resource.

Done lines: the shell's existing wording, "{name} applied to {map}", with Undo.

### 4.2 The chooser window (B2)

A small owned window (`ChooserWindow` with `ChooserViewModel`), sized about 420 by 460 at 100 percent, centred
on the main window, the DialogWindow chrome and tokens.

- Title text: "Apply <file> to a map" (map mode) or "Apply a picture to <map>" (picture mode). A one-line
  subtitle in Text3: "One map. Pick it and the picture is written to the game." / "One picture. Pick it and it
  is written to the game."
- Search box focused on open, filtering as you type with the same rule as the Maps page search. Empty result
  text: `No map matches "{text}".` / `No picture matches "{text}".` (same wording as the Maps page empty text).
- Rows: a 40 by 22 thumbnail, the display name, and a right-hand muted text. Map mode: the applied pack name
  or "Default" (from MapStatus, as the cards show; owner answer q4 "show"). Picture mode: the pack name.
- Single choice. Enter, double click or the primary button applies; Escape or Cancel closes. The primary
  button reads "Apply to <name>" (map mode) or "Apply <name>" (picture mode) once a row is chosen and is
  disabled before that.
- List sources: map mode lists every map in the catalog that has BackgroundSlots, in catalog order (the Maps
  page order). Picture mode lists every picture in every pack's Backgrounds folder, My Backgrounds first, then
  the other packs in the Packs page order.
- Shell entry points: `MainViewModel.ChooseMapAsync(string picturePath, string caption)` returns the chosen
  MapEntry or null; `MainViewModel.ChoosePictureAsync(MapEntry map)` returns the chosen picture path or null.
  Applying is the caller's job through the existing paths, so the chooser never writes anything.
- UIA: window AutomationProperties.Name is the title; the search box is "Search"; rows are ListItems named by
  display name; buttons "Cancel" and the primary text.

### 4.3 Right-click menu on every map card (B3)

The Maps page card gets a ContextMenu built from `MapCardViewModel.MenuItems` (TileMenuCommand list, the
TileMenu resource), with two flyouts. Right click does not change ticks and does not open the panel (owner
answer q2 "no"). Left click keeps doing both.

Target rule: if the card is ticked and more than one card is ticked, the target is the ticked set; otherwise
the target is this card alone.

Header: the map's display name, or "N selected maps" when the target is a set.

Lines:

1. "Apply pack" flyout: every pack in Packs page order. A pack that holds nothing for any target map is
   disabled with tooltip "Nothing for <map> in this pack" (set: "Nothing for these maps in this pack").
   Runs the bar's Apply pack command with the target set swapped in.
2. "Apply picture" flyout: My Backgrounds pictures, at most eight (owner answer q3), then "More pictures..."
   opening the chooser in picture mode (single target only; disabled for a set), then "Add Custom Image..."
   opening the Add window on the target as the bar does today.
3. separator
4. "Edit background": opens the background editor for this map as the panel's tile Edit does. Disabled for
   a set.
5. "Edit platforms": opens the platform editor for this map (PlatformEditorRequest(map, packOrNull)).
   Disabled for a set.
6. separator
7. "Reset to default": the bar's reset with the same confirm on a set ("Reset these N maps to the game's own
   art?" as the bar words it today); one map resets without asking; Undo covers it.
8. "Show in game folder": Explorer on the map's folder under mapArt.

MapsViewModel splits its ticked commands so both the bar and the menu call one body:
`ApplyPackToAsync(IReadOnlyList<MapEntry> maps, Pack pack)`, `ApplyPictureToAsync(IReadOnlyList<MapEntry> maps,
string path)`, `ResetAsync(IReadOnlyList<MapEntry> maps)`. The existing ticked commands become one-line callers.
Behaviour of the bar is unchanged.

Keyboard: card focused, Menu key or Shift+F10 opens the menu. Space still ticks, Enter still opens the panel.

### 4.4 One vocabulary (B4)

| Line | Backgrounds tile | Pack picture (B1) | Map card (B3) |
|---|---|---|---|
| Apply to <map> | tile button | if owned | n/a |
| Apply to N ticked maps | yes | yes | header instead |
| Apply to a map... | added in 2.4 | yes | n/a |
| Apply to all maps | yes | yes | n/a |
| Apply pack / Apply picture | n/a | n/a | flyouts |
| Edit | yes | yes | Edit background, Edit platforms |
| Show in folder | yes | yes | Show in game folder |
| Remove | Remove from library | Remove from pack | Reset to default |

The one change to an existing menu: PictureTileViewModel.RebuildMenu gains "Apply to a map..." between the
ticked line and "Apply to all maps", opening the chooser in map mode.

"Universal" (owner answer q1 "action"): there is no remembered universal picture. "Apply to all maps" is the
whole of it. The manual says so in one sentence.

## 5. Constraints

- Game writes go only through MainViewModel.RunGameWriteAsync and the existing apply paths; no new writer.
- Never run the app or tests against the real game folder, library or %APPDATA%\BhMaps; dev tree only.
- `dotnet format BhMaps.slnx` is the only formatter. Builds with 0 warnings in Debug and Release.
- Tests: BhMaps.Core.Tests only (plus the App has no test project); view models are verified by build and
  UIA captures on the dev tree.
- Copy: no em-dashes, no emoji. Wording constants are `public const string` on the view model that owns them.
- `FocusVisualStyle` must be a local attribute; never a bare `x:Static` of a const int into a double property.
- docs/manual.md stays CRLF, UTF-8 without BOM.

## 6. Docs and release

- docs/manual.md: a "What is new in 2.4" list; the editor section gains the two chips, solo click and the
  remembered mode; a Menus subsection with the section 4.4 table and the universal sentence; Known limitations
  unchanged unless a step adds one.
- README rows for 2.4.0.
- Version 2.4.0 in src\BhMaps.App\BhMaps.App.csproj. Branch feature/bhmaps-v2.4, PR "BhMaps 2.4", squash,
  tag v2.4.0, scripts\publish.ps1, GitHub release in the 2.3.1 body layout, run folder rebuild when no
  BhMaps.exe is running.

## 7. Verification

- Core tests for A2 and A5.
- UIA captures on the dev tree: editor in All pieces, Ticked only with one file, nothing ticked scrim, chip
  focus ring; pack tile menu on an orphan and on an owned picture; chooser open with a search typed; map card
  menu on an unticked card and on a ticked set; both flyouts.
- Gates: Debug and Release build 0 warnings, all tests, format verify.

## 8. Not in 2.4

No hover peek in the editor preview. No third preview mode or per-row eye icon. No change to what Save
writes. No menu on Packs page rows beyond More actions. No drag and drop onto map cards. No change to the
Maps page bar.

## 9. Rulings

1. Page A had no saved answers. All four questions take the page's recommended choice: fade to 15 percent
   (not hide), frame the ticked pieces, remember across sessions, single click on the name solos. Cost if
   wrong: one constant or one setting each.
2. Page B answers, all the recommended choice: universal is the action "Apply to all maps"; right click does
   not tick; eight My Backgrounds pictures inline; the chooser shows the applied pack.
3. The chooser is one window class with two list sources rather than two windows, so the two menus stay in
   step.

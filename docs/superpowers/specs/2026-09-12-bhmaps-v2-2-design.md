# BhMaps 2.2 design: pictures belong to packs, one switch, two editors, quieter lists

Date: 2026-09-12. Base: main f5551b4 (v2.1.0). Author: the session, on the owner's go ("ok should be good u can go")
and delegated judgment. Review pages: https://claude.ai/code/artifact/8178d1a5-a04f-48a5-887a-3f60d7c9cbe6 (the
owner's answers q1 to q5 and objections) and https://claude.ai/code/artifact/db71b24e-cf8b-4309-8a73-ecb617058f83
(the plan page, every item as built here). This spec is binding where the pages and the spec differ; section 13
lists every ruling made after the pages were published.

## 1. What 2.2 is

Eleven changes the owner asked for after a day with 2.1, plus one the session proposed. They share one idea: the
app stops inventing categories ("custom", "changed") that the user never asked for and describes things by where
they are and what they do. Nothing on disk changes shape. The library layout (`packs\<pack>\Backgrounds\*.jpg`,
`packs\<pack>\<map folder>\*.png`) and the game folder are exactly what 2.1 wrote.

The owner's answers on the review page, all applied:

| Question | Answer |
|---|---|
| q1 button and window name | Add Image |
| q2 game-folder pictures matching no pack | show as "In game only" with "Save to My Backgrounds" |
| q3 panel and rows order | follow the Packs order |
| q4 switch label | My Backgrounds (11) |
| q5 live hover preview | after 2.2, as the 2.3 headline |

Objections applied: minimal switch with a smooth animation (item 2); the switch mock redrawn and moved off the
search box (item 2); map selection still exists (item 7); the editor-saved stray file removed and a way to make
packs (items 3 and 11); a hue slider (item 4); the stacked panel confirmed (item 5). A later comment on the plan
page replaced the switch's caret with a plus sign that becomes a minus (item 2).

## 2. Vocabulary used by the screens and by this spec

- **Pack**: a folder under `packs\`. `Default` is the captured game art and is always first.
- **Pack picture**: a background in a pack whose file name is a map slot name (`BG_Sewer.jpg`). It is that
  map's picture in that pack.
- **Any-map picture**: a background in a pack whose file name is not a slot name (`sunset.jpg`). It can go on
  any map. In 2.1 these were "custom pictures". `CustomPicture.PackName` names the pack it lives in.
- **In game only**: a picture in the game's `Backgrounds` folder that matches no pack file. In 2.1 this was
  also "custom". `CustomPicture.PackName` is null for these.
- **My Backgrounds**: the pack the Add Image window and both editors save into by default
  (`BackgroundEditorViewModel.DefaultPackName`). It is an ordinary pack.
- **Set**: a map's platform PNGs from one pack (`packs\<pack>\<map folder>\`).

The word "Custom" does not appear on any screen, in the manual, or in any user-facing string after 2.2. Type
names such as `CustomPictureLibrary` may stay; renaming code is not in scope.

## 3. Item 1: any-map pictures are grouped by pack

**Basis.** The app already puts every added picture into My Backgrounds and the editor saves there too, then
hides that by sorting those files into a separate idea called "custom". The word describes how the file got
there; the pack name describes where it is, which is what the user looks for.

Behaviour:

- Wherever the app listed "custom pictures" it now lists any-map pictures grouped by `PackName`, one group per
  pack in the Packs order (section 8), and a final group "In game only" for pictures with a null `PackName`.
- Group header text: `"<pack> (<count>)"`, for example "My Backgrounds (11)"; the game group is
  "In game only (3)". A group with zero pictures is not shown.
- Tile and card labels show the file name without its extension (`Path.GetFileNameWithoutExtension`).
- An "In game only" tile's menu offers "Save to My Backgrounds" (copies the game file into
  `packs\My Backgrounds\Backgrounds\<name>.jpg`, then rescans) in place of 2.1's "Save to library". A pack
  picture's tile keeps "Remove from library" wording as "Remove from <pack>".

Copy changes (every string, old to new):

| Where | 2.1 | 2.2 |
|---|---|---|
| Map panel strip header | Custom pictures (11) | My Backgrounds (11) (one strip per pack, then In game only (N)) |
| Backgrounds row folded tile | Custom (11) | removed (item 4) |
| Backgrounds chip | Custom | removed (item 9) |
| Tile and card labels | sunset.jpg | sunset |
| Map panel status line | Custom picture: sunset.jpg | sunset, from My Backgrounds |
| Status line, platforms half | Custom platforms | My Backgrounds platforms, or "in game only" platforms |
| Maps card tag fallback | Custom | In game only |
| Platforms row state word | Custom | the pack name, or In game only |
| Buttons, window title, panel strip button, menu row | Add Custom Image | Add Image |
| Maps first-run line | ...or add a custom image on Backgrounds. | ...or add an image on Backgrounds. |
| Tile menu | Save to library | Save to My Backgrounds |
| Tile menu | Remove from library | Remove from <pack> |
| Manual | custom picture, Custom chip, Custom (N) | the words above |

Gate: `grep -rn "Custom" src/BhMaps.App --include=*.xaml` and `grep -rn "\"[^\"]*Custom" src/BhMaps.App
--include=*.cs` return no user-facing string. Identifiers are allowed.

Files: `src/BhMaps.App/ViewModels/MapPanelViewModel.cs`, `MapChoices.cs`, `CustomPictureTileViewModel.cs`,
`MapCardViewModel.cs`, `MapRowViewModel.cs`, `Pages/BackgroundsViewModel.cs`, `Pages/PlatformsViewModel.cs`,
`Pages/MapsViewModel.cs`, `Views/Pages/MapsView.xaml`, `Views/Pages/BackgroundsView.xaml`,
`Views/AddPicturesWindow.xaml`, `AppServices.cs`, `docs/manual.md`.

## 4. Item 2: one page switch on Backgrounds

**Basis.** The pictures behind "Custom (11)" are the same set on every row, so the fold answered one question
sixty-seven times. One control for one fact. Placing your own pictures wants them on every row; comparing
packs wants them out of the way.

Behaviour:

- At the right end of the chip row on the Backgrounds page, in the same Grid row as the chips: a toggle made of
  a plus or minus glyph and the text "My Backgrounds (N)". No border, no box. Text2 when off, Text when on.
  N is the number of any-map pictures the rows would show (all packs plus In game only). Hidden when N is 0.
- Off: each row shows its pack pictures only, one line, nothing folded. On: the any-map pictures follow the pack
  pictures on every row, wrapping to further lines as Apocalypse does in 2.1 when unfolded. Rows rebuild in
  place; the vertical scroll offset is kept.
- The per-row folded tile, `MapRowViewModel.IsUnfolded`, `ToggleFold` and the row-body click that toggled them
  are removed. The Platforms page is unchanged.
- Setting: `AppSettings.BackgroundsShowPictures` (bool, default false). Saved when toggled. When the Add Image
  window adds a picture and the Backgrounds page is showing, the switch turns on so the new picture is visible.
- Keyboard: the toggle is a focusable button (Space or Enter toggles); its automation name is the label text.
- The search box and zoom slider do not move.

Animation (shared by every fold in the app, section 4.1): the plus's upright bar scales to zero over 160 ms
ease-out on turn-on and back on turn-off; tiles that appear fade from 0 to 1 and slide up 6 px over 160 ms.
Tiles that disappear are removed at once.

Copy: "My Backgrounds (11)". Tooltip: "Shows your own pictures on every row".

Files: `Pages/BackgroundsViewModel.cs` (`ShowPictures`, `PicturesLabel`, `TogglePicturesCommand`, `BuildRow`),
`MapRowViewModel.cs`, `Views/Pages/BackgroundsView.xaml`, `src/BhMaps.Core/Settings/AppSettings.cs`,
`SettingsStore.cs`, `Theme/Controls.xaml`.

### 4.1 One fold animation

Four things fold after 2.2: the Backgrounds switch, the map panel's picture strips, the map panel's Platform
files list, and the pack page's map drawer. All use the same timing: 160 ms, ease-out (`QuadraticEase`,
`EaseOut`). The three carets rotate 90 degrees; the switch glyph morphs plus to minus. Unfolded content fades in
and slides up 6 px. Defined once in `Theme/Controls.xaml` as a `FoldChevron` style (a Path with a
RotateTransform and a DataTrigger on `IsOpen`) and a `FoldIn` storyboard resource applied by an attached
property `Fold.Animate="True"` on the panel that appears.

Off switch: when `SystemParameters.ClientAreaAnimation` is false every fold snaps and item 8's scrolling is
instant. Read once at startup into `AppServices.AnimationsEnabled`.

## 5. Item 3: the background editor can save for all maps

**Basis.** The editor always saved under the chosen map's slot name, which is what made the picture that
map's. An any-map picture is a file under its own name, so "All maps" is a save under the source's name.

Behaviour:

- `Maps` (the editor's Map picker) gets a first row "All maps" (`MapSlotChoice.AllMaps`, `Slot` empty). It is
  selected by default when the editor opened from an any-map picture (`BackgroundEditorRequest.Slot` is null);
  the map is selected by default when it opened from a pack picture.
- With All maps selected, `PackFilePath()` is `packs\<pack>\Backgrounds\<source name>.jpg` where source name
  is `Path.GetFileNameWithoutExtension(SourcePath)`. If that name equals a slot name (case-insensitive) the
  file is named `<source name> all maps.jpg` so it cannot become a map picture by accident.
- The overwrite prompt keeps its shape: "sunset already exists in My Backgrounds. Replace it?".
- The checkbox reads "Apply to all maps now" when All maps is selected and "Apply to game now" otherwise.
  Saving with it ticked under All maps runs the existing all-maps picture apply through
  `MainViewModel.RunGameWriteAsync` with the confirm that names the map count, as "Apply to all maps" does from
  a tile.
- Everything else in the editor (Fit, Pan, Darken, pack picker with "New pack...") is unchanged.

Files: `ViewModels/BackgroundEditorViewModel.cs`, `BackgroundEditorRequest.cs`, `Views/BackgroundEditorWindow.xaml`,
`MainViewModel.cs`.

## 6. Item 4: a platform editor with Opacity and Hue

**Basis.** A set is the map's folder of PNG pieces. Fading them lets the background show through while the map
plays the same; shifting their hue recolours a set to match a background without touching its shapes. Both are
per-pixel operations with one number each.

Core: `src/BhMaps.Core/Imaging/PlatformRecolor.cs`, `public static class PlatformRecolor` with
`public static void Apply(string sourcePng, string destPng, double opacity, double hueDegrees)` and
`public static (byte B, byte G, byte R, byte A) Pixel((byte B, byte G, byte R, byte A) p, double opacity,
double hueDegrees)`. Decode as Bgra32 (straight alpha), apply `Pixel` to every pixel, encode PNG. Opacity 0..1
scales A only. Hue in degrees rotates the pixel's hue in HSL space, keeping S, L and A. Rules the tests pin:
opacity 1 and hue 0 return the input byte for byte; opacity 0.5 halves A (rounded) and keeps B, G, R; a grey
pixel is unchanged by any hue; pure red with hue +120 becomes pure green, +240 pure blue; a transparent pixel
stays transparent.

Editor (`ViewModels/PlatformEditorViewModel.cs`, `Views/PlatformEditorWindow.xaml`, same frame, slider style
`EditorSlider`, labels and button row as the background editor, window 1040 x 720):

- Title "Edit platforms, <map>". Left: the preview, the map composited over its current in-game background
  with the recoloured set, rendered through `MapCompositor.Render(level, PanelWidth, PanelHeight, new
  AssetSources(gamePath, packRoot: <temp set folder>))` on the existing `RenderQueue`; newest request wins.
  The temp set folder is `%TEMP%\BhMaps\platform-editor\<guid>\<map folder>\` and is deleted on close.
- Right column: "Source" with "<pack>, <n> files" (the set Edit was pressed on: a pack's set or the in-game
  set), "Opacity" slider 0 to 100 default 100 with value text "55%" and Reset, "Hue" slider -180 to 180 default
  0 with value text "+140" / "-30" / "0" and Reset, "Save into pack" combo (packs plus "New pack...", default
  My Backgrounds, the same name rules as the background editor), "Apply to game now" checkbox, Cancel and Save.
- Only the map's own pieces (`MapEntry.PlatformFiles` after item 10) are processed. Files in shared folders
  are never read or written by the editor.
- Save writes every processed piece to `packs\<pack>\<map folder>\`. If the folder exists and holds files the
  prompt is "<pack> already has platforms for <map>. Replace them?". With Apply ticked, Save then runs the
  existing set apply (`MainViewModel.ApplySetAsync`) for that map, one write, one Undo, the usual done line.
- Save with both sliders at default is allowed (a copy of the set).
- "Edit" is added to the platform tile menu on the Platforms rows and in the map panel. The shell exposes
  `OpenPlatformEditor(MapEntry map, Pack? pack)` (null pack = the in-game set).

Files: `Imaging/PlatformRecolor.cs` (new, tests), `PlatformEditorViewModel.cs` (new), `PlatformEditorWindow.xaml`
(new), `PlatformSetTileViewModel.cs`, `MainViewModel.cs`, `Views/MainWindow.xaml.cs` or wherever the
background editor window is opened.

## 7. Item 5: map panel, Background over Platforms

Behaviour: the panel is one scroll. Under the status line and the "Reset this map" / "Open folder" buttons:
a section header "Background" (the small-capitals style the Packs rows use), the background pack tiles in the
Packs order, then one folded strip per any-map group (section 3) with the "Add Image" button in its header;
then a hairline; then "Platforms", the set tiles, then the "Platform files, N" fold, closed by default.
The segment control, `MapPanelViewModel.ShowPlatforms`, the segment properties and
`MapsViewModel.PanelShowsPlatforms` are removed.

Files: `Views/Pages/MapsView.xaml`, `MapPanelViewModel.cs`, `Pages/MapsViewModel.cs`.

## 8. Item 6: Packs order

**Basis.** The pack used last is the one the user is working with.

Behaviour:

- Order everywhere (Packs page, map panel tiles, Backgrounds and Platforms row tiles, the editors' pack
  pickers): `Default` first; then packs with a last-applied stamp, newest first; then packs never applied, by
  name (ordinal ignore case).
- `AppSettings.PackLastApplied`: `IReadOnlyDictionary<string, DateTimeOffset>` keyed by pack name, default
  empty. `RunGameWriteAsync` gains an optional parameter `string? packName = null`; when non-null the stamp is
  written after a successful write. Callers pass the pack for: apply pack, apply set from a pack, apply a pack
  picture or an any-map picture (its `PackName`). Undo, reset and in-game-only pictures pass null. Stamps for
  packs that no longer exist are dropped on the next scan.
- `src/BhMaps.Core/Operations/PackOrder.cs`: `public static IReadOnlyList<Pack> Sort(IReadOnlyList<Pack> packs,
  IReadOnlyDictionary<string, DateTimeOffset> lastApplied)`. The shell sorts once after each scan; every list
  downstream keeps the order it is given (`BackgroundChoices` and `PlatformSetApplier.SetsFor` already put
  Default first and keep input order).
- Packs row counts line gains ", applied 2 min ago" when a stamp exists.
  `src/BhMaps.Core/Status/RelativeTime.cs`: `public static string Describe(DateTimeOffset then, DateTimeOffset
  now)`: "just now" under 60 s, "N min ago" under 60 min, "N h ago" under 24 h, "yesterday" under 48 h,
  "N days ago" under 7 days, otherwise "on 12 Sep" (day and short month). The Packs page refreshes the text
  once a minute while visible.

Files: `Settings/AppSettings.cs`, `SettingsStore.cs`, `Operations/PackOrder.cs` (new, tests),
`Status/RelativeTime.cs` (new, tests), `MainViewModel.cs`, `PackRowViewModel.cs`, `Pages/PacksViewModel.cs`.

## 9. Item 7: tile menus

Rule: the menu holds only what the tile cannot do on its own.

- `PlatformSetTileViewModel`: "Show files" is offered only where it does something (the map panel). On the
  Platforms rows the constructor's `showFiles` is null and the item is absent. "Edit" is added everywhere
  (item 4). "Open folder" stays.
- `CustomPictureTileViewModel.RebuildMenu`: "Apply to <map>" is dropped when the tile has an Apply button
  (`Map` is not null). "Apply to the N selected maps" (while N > 0), "Apply to all maps", "Edit",
  "Show in folder", and "Remove from <pack>" or "Save to My Backgrounds" stay.
- Map selection is unchanged: cards are ticked on the Maps page; the "Selected" chip appears while any are
  ticked; every tile menu on Backgrounds and Platforms then offers "Apply to the N selected maps".

Files: `PlatformSetTileViewModel.cs`, `CustomPictureTileViewModel.cs`, `MapChoices.cs`, `MapPanelViewModel.cs`,
`Pages/PlatformsViewModel.cs`.

## 10. Item 8: smooth scrolling

- Backgrounds and Platforms: `VirtualizingPanel.ScrollUnit="Pixel"` with `CanContentScroll` kept true.
- `src/BhMaps.App/Behaviors/SmoothScroll.cs`: attached property `SmoothScroll.IsEnabled="True"` on a
  ScrollViewer. On `PreviewMouseWheel` (mouse wheel only: `e.Delta` a multiple of 120; touchpad deltas pass
  through untouched) it marks the event handled, adds `-e.Delta / 120 * 3 * lineHeight` (lineHeight 48 px) to a
  target offset clamped to `[0, ScrollableHeight]`, and animates `VerticalOffset` towards the target over
  180 ms ease-out using `CompositionTarget.Rendering`, restarting from the current offset on each notch so a
  fast spin becomes one glide. Scroll bar drag, keys and touch are untouched.
- Applied to: the Maps grid, both rows pages, the Packs list, the pack page grid, the map panel.
- Instant (the behaviour does nothing) when `AppServices.AnimationsEnabled` is false.

Files: `Behaviors/SmoothScroll.cs` (new), `Views/Pages/BackgroundsView.xaml`, `PlatformsView.xaml`, `MapsView.xaml`,
`PacksView.xaml`, `PackDetailView.xaml`.

## 11. Item 9: the Changed and Custom chips go

**Basis.** The set chips are the game's own map lists and answer "which maps am I working on". Changed and
Custom are states of a map, and each map already wears its state on its card tag, its row and its status
line.

- Backgrounds and Platforms chips: All, then the game's sets. Maps: the same, plus "Selected" while any card is
  ticked. `RowsPageViewModel.ChangedChip`, `CustomChip`, `HasCustomChip`, the two empty-state texts and the
  predicates go; `MapsViewModel`'s Changed chip and its wording go.
- A remembered chip that names Changed or Custom falls back to All.
- Without level data the chip row is All alone, as today.

Files: `Pages/RowsPageViewModel.cs`, `Pages/BackgroundsViewModel.cs`, `Pages/PlatformsViewModel.cs`,
`MapRowViewModel.cs`, `Pages/MapsViewModel.cs`, tests naming those chips.

## 12. Item 10: platform files are the map's own pieces

**Basis.** The level data marks event decorations with a theme on the node that places them, and they live
in shared folders (`Snow`, `Halloween`). `MapCompositor` already skips themed nodes; the file list did not.

- `MapCatalog.Entry` and its `Assets` walk skip any `PlatformNode` with `IsThemed` and that node's children,
  mirroring `MapCompositor.cs:173`. `MapEntry.PlatformFiles` for a Brawlhaven-shaped level goes from 18 to 6.
- The panel's "Platform files, N" fold lists the file names without the folder prefix.
- A map with no themed nodes lists exactly what it lists today. The FromFolders fallback is unchanged.

Files: `src/BhMaps.Core/Maps/MapCatalog.cs`, `MapPanelViewModel.cs`, `tests/BhMaps.Core.Tests/MapCatalogTests.cs`.

## 13. Item 11: make a pack in the app

- Packs page header: a "New pack" button beside "Open packs folder". It opens the existing text prompt
  (`IDialogs.PromptText("New pack", "Name", "")`), validates with `PackNameValidator.IsValid`, refuses a taken
  name with "A pack called <name> already exists.", creates `packs\<name>\` and rescans. The new pack shows
  at once with "0 maps".
- The background editor and the Add Image window already offer "New pack..." with the same rules; the platform
  editor gets the same row.
- `src/BhMaps.Core/Operations/PackCreator.cs`: `public static bool TryCreate(string libraryPath, string name,
  out string error)`; tests: valid name creates the folder, taken name errors, bad characters error.

Files: `Operations/PackCreator.cs` (new, tests), `Pages/PacksViewModel.cs`, `Views/Pages/PacksView.xaml`,
`PlatformEditorViewModel.cs`.

## 14. States

| State | Behaviour |
|---|---|
| No any-map pictures | No switch on Backgrounds, no strip in the panel, just Add Image |
| In game only pictures exist | "In game only (N)" strip and tiles, each with Save to My Backgrounds |
| Windows animations off | Folds snap, scrolling instant |
| Switch on and a search active | Rows the search keeps show the pictures too |
| No pack ever applied | Default, then every pack by name; no applied text |
| Pack deleted or renamed | Its stamp is dropped on the next scan; a renamed pack starts as never applied |
| Menu with one item | Opens as a one-line menu |
| All maps, name taken | "sunset already exists in My Backgrounds. Replace it?" |
| Platform editor, opacity 0 | Saves a fully transparent set; the preview shows the bare background |
| Platform editor, defaults | Save writes an identical copy |
| Hue on grey pieces | Nothing visible changes |
| Saved chip is Changed or Custom | Falls back to All |
| Map without themed nodes | File list unchanged |
| New pack, bad name | The validator's message; nothing created |
| Focus and keys | Unchanged from 2.1; the eased scroll keeps the focused tile in view |
| Done line and Undo | Unchanged: "<name> applied to <map>. Shows on the next match load." with Undo |

## 15. Not changing

The five tabs, one row per map, live writes and the two done suffixes, Undo as one snapshot, the Add Image
window's pack picker, the set chips, hiding the dots when a menu has one item, the composed previews, the
theme. Settings page unchanged.

## 16. Release

Version 2.2.0 in `src/BhMaps.App/BhMaps.App.csproj`. Manual and README updated to the words in this spec.
Publish with `scripts/publish.ps1`, tag `v2.2.0`, release notes from the item list.

## 17. Rulings made after the pages

1. The switch's count is every any-map picture the rows show, across packs plus In game only; the label stays
   "My Backgrounds (N)" because that is where the pictures are today. If a second pack ever holds any-map
   pictures the panel strips still group them by pack. Cost if wrong: one label.
2. The switch defaults to off, and turns on when Add Image adds a picture while Backgrounds is showing.
3. All maps with a source named like a slot saves as "<name> all maps.jpg".
4. The All maps apply keeps the count confirm, matching the tile's "Apply to all maps".
5. `RunGameWriteAsync` stamps the pack through an optional parameter rather than a separate call, so a stamp
   can never be written for a write that failed.
6. Sorting happens once at the shell; downstream lists keep input order.
7. The platform editor previews the full-size processed pieces (they are small PNGs) instead of a downscaled
   pass; simpler and the RenderQueue already drops stale requests.
8. BG_HBCross.jpg was moved out of the owner's My Backgrounds by the session on go (plan step 0), to
   `C:\Users\alexa\files\bh\removed by BhMaps 2026-09-12\`.

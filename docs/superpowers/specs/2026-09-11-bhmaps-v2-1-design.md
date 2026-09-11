# BhMaps 2.1 design: one list of maps, one library of pictures, writes that say when they land

Date: 2026-09-11. Base: main b0e4181 (v2.0.0). Author: the session, on the owner's go and delegated judgment
("use your best interest, think about the user and how using the app would go, figure out what needs to be
there and what doesn't, the most natural approach"). Revised once after an adversarial review (section 13).

Review pages: complaints and 20 accepted items at
https://claude.ai/code/artifact/b684eaf0-c10f-46e4-a451-2802ca70cad6, density additions and go at
https://claude.ai/code/artifact/2a5e0089-92a7-4940-ae49-441391d7e079. This spec supersedes both where they
differ; section 9 lists every difference and why.

## 1. Who uses this and what they come to do

A Brawlhalla player with a folder of map-art packs and a few pictures of their own. They know maps by name and
by picture. They do not know slot file names, hashes or level-set ids, and they should never need to.

Jobs, most frequent first:

1. Use a pack: everything in it, on every map it covers. One click.
2. Put one picture on every map, or on a group of maps (the ranked maps, say).
3. Change one map: pick a background or a platform set for it from what is available.
4. See what the game will show right now, and what is not default.
5. Go back: undo the last write, reset a map, reset everything.
6. Make a picture fit a map: pan and darken.
7. Keep the library tidy: add pictures, import, export and remove packs.

Every screen below is justified against this list. Anything that does not serve one of these jobs is not in
the app.

## 2. The shape of the app

Four places, in a top bar: **Maps**, **Backgrounds**, **Packs**, **Settings**. No sidebar.

Why a top bar and not the sidebar. The sidebar held two things: navigation and the full list of maps with tick
boxes. The list is now on the Maps page as the grid itself, so the sidebar's list was the same 67 names a second
time with a second search box. Four destinations with no sub-levels do not need a vertical rail; a row of four
words costs 44 px of height and gives back 240 px of width, and this is a landscape app: every page is a grid of
16:9 pictures, so width is worth more than height. (Reference: Dovetail's project view, tabs in the top bar and
nothing down the side; Customer.io's Assets page, a grid with tick boxes and a selection bar.)

Why not Platforms as a page. A platform set belongs to exactly one map; there is no "put this set on other
maps". So a Platforms page can only ever be "for each map, the sets each pack has for it", which is the map's own
detail repeated 67 times, and the pack's detail repeated per pack. Both of those places exist. The page was the
reason the same list appeared a third time.

Why Backgrounds stays a page. A background picture is not tied to a map: a custom picture can go anywhere, and
a pack's picture for one map can be put on another. "I have a picture, where should it go" is a picture-first
question and needs a place whose unit is the picture. That place is the Backgrounds page.

Why Home becomes Maps. "Home" says nothing; "Maps" says what is on the page. A first-time user reads Maps,
Backgrounds, Packs, Settings and knows where each job lives.

### 2.1 Top bar

44 px, Bg with a hairline (Line) under it.

- Left: "BhMaps" 14 Semibold, then the four tabs as text buttons (13, Text2; the active one Text on a Surface2
  pill, the same look as the old sidebar row). Ctrl+1 to Ctrl+4 switch tabs. Ctrl+K switches to Maps and
  focuses its search box from any tab.
- Right: the game line. Running: a Text dot and "Brawlhalla running". Not running: a Line2 dot, "Brawlhalla not
  running", and a Launch text button. Game folder missing: the red state the app already has, with "Choose
  folder" opening Settings. The "next match load" sentence is not here; it belongs to the moment of a write
  (2.2), and saying it twice on one screen is noise.
- Nothing else. No search in the bar; search belongs to the page that is searched.

### 2.2 Page header row

Under the bar, each page keeps a header: title (22 Semibold), the done line beside it, actions on the right,
and on Maps and Backgrounds the zoom slider (it lives here, not in the chip row, so it never moves when chips
appear). The done line is where every write reports: "flowermap applied to Brawlhaven. Shows on the next match
load. Undo". While the game is closed the sentence is "Shows when Brawlhalla starts." One shared string, so
pages cannot drift.

Window minimum stays 1000 x 640; at 1000 the bar is brand 90 + tabs 380 + game line 220 and fits.

## 3. Maps

The status board and the place for every map-first job (jobs 3, 4, 5 and the group half of 2).

### 3.1 Grid

- Header: "Maps", done line, search box (200 px, "Search maps"), zoom slider, "Reset all to default" (outline).
- Chip row: All, Ranked 1v1, Ranked 2v2, Tournament, Changed, and, once anything is ticked, Ticked. At the right
  end of the chip row, always present: a plain "Select all shown" button (a tick-box glyph and the text; with a
  chip active it reads "Select all 12 shown"). This is what teaches a first-time user that maps can be ticked.
- Zoom 2 to 10 columns, default 6. End glyphs: a small square at the ten end, a large one at the two end, and
  the number.
- Cards: composed preview, name, and a tag only when it says which art is on the map: the pack name when the map
  matches a pack, the picture's file name (ellipsised at 16 characters) when a custom picture is on it, Missing
  when files are missing. Default draws no tag. The Changed chip filters to everything that has a tag other than
  Missing. At 7 and 8 columns the name is 12 px and the tag is hidden; at 9 and 10 the name row goes and the
  name and tag are the tooltip, the card is 4 px padding, gaps are 8 px, and Missing is a 10 px mark on the
  picture.
- The grid is a ListBox with SelectionMode=Extended: the tick box on each card (top-left, visible on hover and
  when ticked, Focusable=False) is bound to IsSelected, so Ctrl+click toggles, Shift+click ranges, Space
  toggles the focused card, Ctrl+A ticks all shown, and arrow keys move focus, all from the control itself. A
  plain click on the card body opens the map panel (3.2) and leaves the ticks alone; a Ctrl or Shift click is
  left to the ListBox. Ticked cards are outlined in Text.
- Empty grid: one line that says why ("No ranked 1v1 map named 'dojo'" / "No map is changed. Every map matches
  the Default pack.") with Clear search when a search caused it.
- First run: when the library holds no pack other than Default, one line under the chip row: "No packs yet.
  Import a folder of map art on the Packs page, or add a custom image on Backgrounds." Both page names are
  links. It goes away when a pack or a custom picture exists.

### 3.2 Map panel

360 px on the right, opened by a card, closed by X, Escape (Escape closes the panel first; a second Escape
clears ticks), or clicking the same card. It scrolls internally. Everything a single map can do is here, so no
other page needs to list maps.

- Name (16 Semibold), sets (12 Text3), close.
- Composed preview, full panel width.
- One line of status in words: "In game: flowermap background, Default platforms" / "Default" / "Custom picture:
  sunset.jpg" / "Missing 2 files".
- Row: Reset this map (outline), Open folder (plain).
- A two-segment control, Background | Platforms, the same control as pack detail's. Background is the default;
  the last choice is remembered for the session. Two sections stacked would put Platforms below the fold at the
  window's minimum height with five packs, which was the whole reason the Platforms page could go.
- Background view: tiles two across (each 156 px wide, 16:9 picture, name under it): Default, then one per pack
  that has a picture for this map's slot, then a collapsible "Custom pictures (12)" strip. The in-game one
  carries the check. Hover shows Apply and a menu button; right-click opens the same menu: Apply to this map,
  Apply to the N ticked maps, Apply to all maps, Edit, Show in folder.
- Platforms view: tiles two across: Default and one per pack that has a set for this map, composited over the
  map's current background as now. Hover: Apply and the menu: Apply to this map, Apply to the N ticked maps
  (each map gets its own set from the same pack), Show files, Open folder.
- The verb is Apply everywhere. "Use" is gone.

### 3.3 Ticks and the selection bar

Ticks are how group jobs are aimed. The ticked set is one list shared by every page (MainViewModel keeps it),
so ticks made on Maps are what a Backgrounds tile's "Apply to the 3 ticked maps" writes to.

- When one or more maps are ticked, a selection bar floats at the bottom of the grid (Surface, hairline, Radius
  6): "3 of 67 maps ticked", then Apply pack, Apply picture, Reset to default, then Select all shown, Clear.
- Select all shown ticks every map the chips and search currently show, so a preset is "chip, then Select all
  shown": Ranked 1v1 is two clicks. There is no separate Sets menu. Ctrl+A does the same; Escape clears when no
  panel is open.
- Apply pack opens a menu of packs. Applying a pack to ticked maps means: for each ticked map, the pack's files
  for that map's platform folder, plus the pack's pictures for that map's background slots, and nothing else in
  the pack. Apply picture opens a menu of custom pictures with "Add Custom Image..." at the bottom, which opens
  the window with the ticked maps as the pre-selected target (7.1). Both confirm with the map count, as
  multi-map writes do today.
- Ticks clear after a successful write to the ticked maps. A cancelled or failed write keeps them.

## 4. Backgrounds

The library of pictures, picture-first (job 2 and the picture half of 3 and 6).

- Header: "Backgrounds", done line, search ("Search pictures": file names, pack names, map names), zoom 2 to 10
  columns default 6, primary button "Add Custom Image".
- Section "Custom pictures (N)": every picture in the library whose file name is not a map slot name, plus any
  in-game file that matches no pack, grouped by content hash so one picture is one tile however many maps use
  it. Tile label: file name; second line "in game on 12 maps" or the pack it lives in. Hover shows "Apply to..."
  with a chevron (a custom picture has no single map, so its Apply is always a choice) and the menu: Apply to the
  N ticked maps, Apply to all maps, Edit, Show in folder, Remove from library. An in-game picture that is in no
  pack offers Save to library instead of Remove.
- Then one section per pack, collapsed to its header and count by default ("flowermap, 15 backgrounds"), the
  Default pack last. Expanding shows the pack's pictures in map order, labelled by map name, with the check on
  any that is in game. Hover: Apply (to its own map) and the menu: Apply to Brawlhaven, Apply to the N ticked
  maps, Apply to all maps, Edit, Show in folder.
- Search: while the box is not empty the page shows one flat grid of every matching tile labelled "map name,
  pack", instead of expanding sections. Clearing the search returns to the sections as they were.
- Empty states: no custom pictures ("No custom pictures yet." with the Add Custom Image button in line); no
  packs ("No packs in the library. Import one from Packs."); search with nothing ("No picture matches 'sewer'.").

Why grouped by pack and not by map. The map-first view of backgrounds is the map panel; repeating it here would
be the third map list. Grouping by pack keeps the unit the picture, and the map name on each tile plus search by
map name answers "which one is Brawlhaven's" without a row per map. Collapsed sections also keep the live tile
count small, which a flat grid of every pack's every picture never could.

## 5. Packs

Unchanged list: one row per pack with Apply all (confirm with the map count), Export, Remove, Open folder;
Import and Capture defaults in the header.

Pack detail:

- Header: back, pack name, then a three-segment control Combined / Backgrounds / Platforms (Combined default),
  Apply all, Open folder, zoom 2 to 10 default 5.
- One grid; the segment picks the items. Combined is the pack's art over the game's current backgrounds;
  Backgrounds is the pack's own pictures by map; Platforms is the maps with the background dropped.
- Clicking a tile opens a drawer on the right (360 px, same shape as the map panel): preview, in-game state,
  sets, the files the pack holds for that map with size and dimensions, Apply to this map, Open folder. Enter
  opens, Escape closes.
- The transparent-files note with Remove stays above the grid in every view.

## 6. Settings

Rows: Game folder (Change, Open), Library folder (Change, Open), Game data (status, Refresh), Defaults (Capture
defaults), Applying (one line: "Changes are written straight into the game folder, with Brawlhalla open or
closed, and show on the next match load. Undo puts back the files of the last write."), Version. The "While the
game runs" row is gone.

## 7. Windows

### 7.1 Add Custom Image

Title "Add Custom Image". Drop zone and Pick files; the picture list; preview; fit modes (Stretch, Center, Fill,
Fit); Import into pack. Then a block "Then" with radios, all of which add to the library first:

- "Add to the library" (the default when the window was opened with no target)
- "Add and apply to Brawlhaven" (present when opened from a map panel; then the default)
- "Add and apply to the 3 ticked maps" (present when maps are ticked; the default when opened from the selection
  bar)
- "Add and apply to all 67 maps"

Footer Cancel, Add. Pressing Enter runs the selected radio and nothing more; a write to more than one map goes
through the usual confirm with the count.

### 7.2 Background editor

Opened from any background tile's Edit with that picture loaded: title "Edit sunset.jpg" (the file name), the
Source row shows a thumbnail, file name, pack and dimensions with Replace beside it, and dropping another image
still works. Then:

- Map: a combo of map display names (a slot shared by several maps lists them together, "Brawlhaven, Small
  Brawlhaven"); the slot file name is resolved internally and never shown as the primary label.
- Fit: Fill, Fit, Stretch (the same words as Add Custom Image; Fill is what the code calls Cover, Fit is
  Contain). Pan X and Pan Y apply in Fill and are at 45 % opacity otherwise.
- Darken.
- Save into pack (the tile's own pack pre-selected, with the hint "Replaces BG_Brawlhaven.jpg in flowermap. Pick
  another pack to keep the original."), Apply to game now, Cancel, Save.

Sliders: one EditorSlider style, move-to-point on, SmallChange 0.01 / 1 and LargeChange 0.1 / 10, a Geist Mono
readout beside each and a Reset link in each label.

Preview: the source is decoded once into a 640 x 360 working bitmap; preview renders from it on a 16 ms throttle
(one in flight, newest values win) with a final render on release; Save fits the original. The preview corner
says "preview 640 x 360".

Opened from Add Custom Image with no picture: the Source row is the drop target and Browse, the preview says
"No picture yet. Drop one here or browse."; a source file deleted since the scan shows "sunset.jpg is no longer
in My Backgrounds. Choose another source." and Browse.

## 8. Writes

- Every write goes through MainViewModel.RunGameWriteAsync as now: busy boundary, undo snapshot once per action,
  failure window listing the files that could not be written. Undo goes through the same wrapper (today it calls
  the launcher directly; that asymmetry goes).
- No restart flow. GameLauncher.RunWriteAsync becomes the write itself; GameProcess keeps IsRunning and Launch.
  AppSettings loses whileRunning; SettingsStore reads an old value and drops it with a log line.
- Every done line ends with the shared sentence (2.2). Undo stays beside it.
- Multi-map writes keep the confirm that names the count; single-map writes are one click.

## 9. What changed against the accepted lists, and why

| Item | Accepted as | Now | Why |
|---|---|---|---|
| S1 | Select all / none / by set in the sidebar | Ticks on cards, a floating selection bar, "Select all shown" always in the chip row; presets are chips | The list moved to the page; chips already are the sets |
| S2 | Sidebar search at 33 px | Gone with the sidebar | No sidebar |
| S3 | Ticks clear after apply | Same | |
| H1, D3 | Zoom 2 to 10, default 6 | Same; the slider sits in the header row | So chips appearing never move it |
| H2 | Tags only for packs and Missing | Pack name, custom file name, or Missing; Default draws nothing | "What is not default" must be visible without a filter; the file name says which picture, "Custom" said nothing |
| H3 | Chips also filter the sidebar | Chips filter the grid; Ticked chip added; the click bug fixed | No sidebar |
| B1, D1 | Backgrounds as one row per map, compact | Backgrounds grouped by pack with a custom shelf; map-first lives in the map panel | A row per map was the map list a third time |
| B2 | Apply target menu, right-click | Same, without "Choose maps" | Ticks and Select all shown already are the chooser |
| B3 | One custom picture is one tile | Same | |
| B4 | Add Custom Image | Same, plus the "Then" radios | A first-timer adding a picture usually wants it somewhere |
| P1, P2, D2 | Platforms as compact map rows, jump from sidebar | Platforms page removed; sets live in the map panel (Background \| Platforms segment) and in pack detail | A set belongs to one map; the page was the list again |
| K1, K2, D4 | Segmented pack detail, drawer, zoom | Same | |
| E1, E2, E3 | Editor source, sliders, live preview | Same; "Slot" becomes "Map", fit words match the dialog's | The user never needs slot names |
| G1, G2, G3 | Live writes, the sentence, no staged Apply | Same; the sentence is in every done line, not in the bar | Once per screen |

Bugs found while capturing, fixed in this release:

- Chips do not take a mouse click: the ChipRow template root is a Grid with no Background and both children are
  IsHitTestVisible=False, so the ListBoxItem never receives the click (UI Automation selection bypasses hit
  testing, which is why the earlier check passed). Fix: Background="Transparent" on the root.
- Typed text in a FieldTextBox starts about 35 px right of its padding: in the header search box (Padding
  33,3,10,0, 200 px wide) a typed "brawl" begins about 70 px from the box's left edge, and the sidebar box showed
  the same extra offset. The padding accounts for 33 of those; the cause of the rest is to be established with
  the running app before the fix (suspects: the Fluent implicit ScrollViewer style on PART_ContentHost, or a
  layout width the TextBoxView is centred in).

## 10. Not changing

Composed previews from the level data; hashing and status; the packs list; Welcome; Import folder; Undo as one
snapshot of the last write; the theme (warm near-black, hairlines, one solid primary, Geist); DialogWindow;
the dev rules (never the real game folder, dev tree via scripts\make-dev-tree.ps1, writes only through
RunGameWriteAsync).

## 11. Copy

- Tabs: Maps, Backgrounds, Packs, Settings.
- Game line: "Brawlhalla running"; "Brawlhalla not running" + Launch.
- Done line suffix: "Shows on the next match load." / "Shows when Brawlhalla starts."
- Chip row: "Select all shown" / "Select all 12 shown". Selection bar: "3 of 67 maps ticked", "Apply pack",
  "Apply picture", "Reset to default", "Select all shown", "Clear".
- Tile menu: "Apply to Brawlhaven", "Apply to the 3 ticked maps", "Apply to all maps", "Apply to...", "Edit",
  "Show in folder", "Show files", "Remove from library", "Save to library".
- Buttons: "Add Custom Image", "Reset this map", "Reset all to default", "Apply all", "Open folder".
- First-run line: "No packs yet. Import a folder of map art on the Packs page, or add a custom image on
  Backgrounds."
- Empty lines as written in sections 3 and 4.
- No em-dashes, no emoji anywhere in the app or docs.

## 12. Verification the owner does

- One live write with Brawlhalla open shows on the next match load (already observed once; observe once more on
  2.1.0 before calling it done).
- A slider drag in the editor moves the preview.
- Ten columns on their own screen size.

## 13. Revisions after the adversarial review

An architect review (opus, 2026-09-11) walked the seven jobs through the first draft. Changes made in response,
in the order of the review's ranking: the map panel got the Background | Platforms segment instead of two stacked
sections (it could not show both); "Choose maps..." was removed as undefined and redundant; "Select all shown" is
always visible in the chip row so ticking is discoverable; Add Custom Image got four "Then" states with the
library-only default unless a target was supplied; custom pictures tag the card with the file name; custom tiles
say "Apply to..."; applying a pack to ticked maps is defined (3.3); the editor says Map, not Slot, and uses the
dialog's fit words; a first-run line points to Packs and Backgrounds; the match-load sentence is said once per
screen; Escape order is defined; the grid is a ListBox so extended selection is not hand-rolled; ContextMenu and
MenuItem get themed styles based on the Fluent ones; Ctrl+K reaches the Maps search from any tab; a search on
Backgrounds is a flat result grid rather than auto-expanding sections; the wording fixes in 11. Kept against the
review: no dot on cards (the tag is the signal), the zoom range and defaults.

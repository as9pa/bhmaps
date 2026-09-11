# BhMaps 2.1 addendum: rows pages and pack previews

Date: 2026-09-11, evening. Supersedes parts of `2026-09-11-bhmaps-v2-1-design.md` as marked. The owner, awake
after the overnight build had reached Task 16, reviewed the shell and asked for a different shape for the two
choosing pages and for the Packs list. Their words, in order:

- "each of them on their screen will have like one map name per row, and next to them will be all the possible
  backgrounds and platforms they can choose from, on the respective screen."
- "having folders for different backgrounds is unnecessary in my opinion because if i wanted to look at
  everything in a specific folder i would just go to the packs screen."
- "i disagree with your take about the platforms tab."
- "for the packs we should have a thumbnail of the first map in each folder... with background + platform on
  it... in that space inbetween those buttons we can just have like a ton of previews like horizontally."
- "when i expand a pack there is dead space on the side."

The model behind it: Maps is state (what each map shows now, ticks, group jobs); Backgrounds and Platforms are
choice (for each map, what it could show); Packs is management (what is in the library). The pack-grouped
Backgrounds page from section 4 put management on a choice page, which is why it read wrong.

Seven questions were put to the owner on the review page
(https://claude.ai/code/artifact/eed15cec-4447-461c-9ca6-48f9894027c9). The owner answered in chat at 18:40
(the page's saving did not work for them): q1 dense, after seeing dense and middle side by side ("the more stuff
on the screen the better"); q2 recommended; q3 recommended; q4 no tick boxes on rows ("realistically you would
just click the background you want... the checkbox just takes up space"); q5 keep the panel; q6 combined only;
q7 recommended. Two more points from the same message: platform thumbnails at row height are too small to see
the platforms, and on Maps a focused map's card grows an empty line under its name. Each `[q<n>]` mark below now
states the owner's answer.

## A. Top bar (amends 2.1)

Five tabs: Maps, Backgrounds, Platforms, Packs, Settings. Ctrl+1 to Ctrl+5. At the 1000 px minimum the bar is
brand 90 + tabs about 470 + game line 220, which fits. Ctrl+K still focuses the Maps search.

## B. Backgrounds (replaces section 4)

One row per map. The row is the map's answer to "what can my background be, and which is it now".

Header: "Backgrounds", the done line, search "Search maps and pictures", zoom 1 to 5, primary button
"Add Custom Image". Under it the Maps chip row (All, the set chips, Changed) plus "Custom" (maps showing a custom
picture), laid out as Maps lays it out but without the Select all button (ticks are made on Maps, q4).

Zoom sets the thumbnail height; width follows at 16:9. `[q1: dense]` 1: 48 px, 2: 56 (default), 3: 72, 4: 96,
5: 128. The setting is `backgroundsZoom`, clamped 1 to 5 (the Task 1 clamp of 2 to 10 changes for this key).
At 56 px a 1080p window shows about twelve rows.

Row (Surface, hairline, Radius 6, padding 6 12):

- `[q4: no ticks]` No tick box. A row is for clicking the choice you want. The tile menus still offer "Apply to
  the N ticked maps" using the ticks made on Maps, and the selection bar does not appear on the rows pages.
- Name column, 168 px: map name (13 Medium, ellipsised), and under it the state tag with the card's rules: the
  pack name, the custom picture's name, "Missing", nothing for Default.
- The strip: the choices as thumbnails with a caption under each. Order: the in-game choice first with the
  check and the 2 px Text border, then Default (if not in game), then each pack that has a picture for this
  map's slot, in the Packs page order, captioned by pack name, then custom pictures. `[q3: folded]` Custom pictures are
  folded behind one "Custom (N)" tile at the end of the strip; clicking it unfolds that row (the strip wraps);
  a map whose in-game picture is custom shows that picture unfolded, first, with the check. `[q2: unfold]` When more
  choices exist than fit the row's width, the strip shows what fits and a "+N" tile; clicking it unfolds the row
  the same way. Unfolded rows fold back when the page is left or the row is clicked folded.
- Click on a thumbnail applies it to that map: one click, live, no confirm, done line "flowermap applied to
  Brawlhaven." plus the shared sentence, Undo. Hover shows "Apply" on the thumbnail and the dots button.
  Right-click or the dots opens the tile menu: Apply to this map, Apply to the N ticked maps, Apply to all maps,
  Edit, Show in folder; a custom tile adds Remove from library, or Save to library for an in-game picture that is
  in no pack (section 4's rules for custom pictures still hold; only their placement changes).
- The thumbnail's tooltip is the full caption and, for a pack picture, the file name.

Search keeps a row when the map's name, or any thumbnail's pack name or file name, matches. Empty:
"No map or picture matches 'sewer'." with Clear. The chips narrow rows the way they narrow cards.

Ticks are made on Maps only (spec 3.3). The rows pages read them for "Apply to the N ticked maps" in the tile
menus and for the Add Custom Image target, nothing more. Ctrl+A and the selection bar belong to Maps.

Keyboard: Up and Down move between rows, Left and Right along the strip, Enter applies the focused thumbnail,
Escape clears the search text when the box has focus. Focus ring on the thumbnail as on the card.

Performance: rows virtualise; a row's thumbnails decode when the row is realised, through one thumbnail cache
keyed by file path, so a custom picture decodes once for all 67 rows and a pack picture once for its map. The
first paint shows captions and grey tiles, then pictures fill in.

Empty states: no packs and no custom pictures (every row shows Default only, and the Maps first-run line under
the chips); no map matches (above); game folder missing (the top bar's red state, as everywhere).

## C. Platforms (new; replaces the removal in section 1 and section 9 row P1, P2, D2)

The same rows, for the other half of the map's look. A platform set fits only its own map, so a row lists
exactly the packs that have a set for that map.

Header: "Platforms", the done line, search "Search maps and packs", zoom 1 to 5 (`platformsZoom`, same steps
as Backgrounds). No primary button. The Maps chip row without "Custom" and without Select all.

Row: name column (state tag: the pack whose set is in game, "Missing", nothing for Default), then the strip:
the in-game set first with the check, then Default, then one thumbnail per pack that has a set for this map,
captioned by pack name. No tick box (q4).

Platform thumbnails are cropped to the platforms. At row height a whole-level composite shows the platforms as
slivers ("you can barely even see the platforms when they are so small"), so the thumbnail is the platform
bounding box from the level data, padded 12 percent on each side and clamped to the level, scaled to fill the
16:9 thumbnail, composed over the map's current background the way the map panel composes a set. Every set of
one map uses the same crop, so the row compares like with like; the map panel and the drawer keep the whole
level. The Platforms page default zoom is 3 (72 px) rather than 2, since shapes need more height than pictures
(`platformsZoom`, clamped 1 to 5).

Click applies that set to that map, live, one done line ("flowermap platforms applied to Brawlhaven."). Menu:
Apply to this map, Apply to the N ticked maps (each map gets its own set from that pack), Show files, Open
folder. No custom tiles here. Search matches map and pack names; empty "No map or pack matches 'sewer'."

Keyboard, virtualisation and empty states as Backgrounds. A map with no set in any pack shows Default alone.

## D. Map panel (amends 3.2) and the Maps card

`[q5: keep]` The panel stays as built (Background | Platforms segment, custom strip, reset, files). It is the
close-up; the rows pages are the overview.

Maps card (owner change O6): with the panel open at zoom 6 on a 1600 px window, a card is about 198 px wide, so
O1's width rule stacks the tag under the name; the grid is a UniformGrid, so every card takes the tallest card's
height, and a card with no tag (Default) shows an empty line under its name. The fix: a card never grows for its
tag. Wide cards (200 px and up) keep the pill on the name row; narrower cards draw the pill over the picture's
bottom-left corner instead (the Missing mark already lives over the picture at zoom 9 and 10), so all cards in
a grid are the same height with nothing empty. One pill template used in both places.

## E. Packs list (amends section 5, first paragraph)

One row per pack, now with a face:

- Lead thumbnail, 128 x 72: the pack's first map in map order, composed: the pack's background with the pack's
  platforms; a background-only pack shows its first picture; a platform-only pack shows its first set over the
  game's current background for that map.
- Name and counts as today.
- Preview strip in the row's middle: the pack's maps in map order, 96 x 54, as many as fit the width, fading at
  the right edge with the remaining count ("+58"). Previews decode lazily as rows are realised and are cached
  by pack, map and file time, so scrolling the list stays quick and a second visit is instant.
- `[q7: recommended]` Actions: "Apply all" stays a button; Export, Open folder and Remove move into the row's dots menu, in that order (Remove last, as the destructive item)
  (TileMenu). Clicking the row opens pack detail.
- Import, Capture defaults, Open library stay in the header.

Empty: "No packs in the library." with Import folder in line.

## F. Pack detail (confirms section 5, pack detail)

Unchanged in substance: one grid of composed tiles that fills the width (zoom sets the column count, tiles
scale), a drawer on click, the transparent-files note above the grid. `[q6: combined]` The Combined |
Backgrounds | Platforms segment is dropped; Combined is the only view and the drawer lists the halves.

## G. Copy (adds to section 11)

Owner change O7 (2026-09-11, 18:50): "Select all shown" becomes "Select all" in the Maps chip row and on the
selection bar ("it should be implied to the user that the select all will only select the things shown"). The
button's tooltip says "Ticks the maps the chips and search show", and the count line "12 of 67 maps ticked"
confirms what happened. The spec's "Select all 12 shown" variant goes with it.

Owner change O8 (2026-09-11, 19:50): "instead of maps ticked it should say maps selected". Every string the user
reads says "selected": the bar "3 of 67 maps selected", the chip "Selected", the tile menus "Apply to the 3
selected maps" / "Apply to the 1 selected map", the Add Custom Image radio "Add and apply to the 3 selected maps".
The tick box stays the control; code identifiers do not change. Sections B and C above read "ticked" where they
quote the menu line; "selected" is the word that ships.

Owner change O9 (2026-09-11, 19:58): "where the bottom bar is that says the number of maps selected and stuff i
think its a little too high". It was: the bar floated 16 px above the page's own 24 px bottom gutter, so 40 px of
empty ground sat under it while only 12 px sat between it and the cards. Now the bar's float is the page gutter
itself: its bottom edge sits 24 px above the window's bottom, the same distance the cards keep from the sides,
and the grid hands back only the bar's height plus the card margin so the last row still clears it.

Tab "Platforms". "Search maps and pictures", "Search maps and packs". "Custom (11)", "+7". "No map or picture
matches 'sewer'.", "No map or pack matches 'sewer'." Done lines: "<pack> platforms applied to <map>." for a
set; "<name> applied to <map>." for a picture, unchanged.

## H. What this removes

Section 4's custom pictures section and per-pack sections; the Task 17 work (pack sections and search) was
stopped before it landed and discarded. Task 16's header, zoom and Add Custom Image survive into the rows page;
its custom shelf goes. The content-hash grouping from Task 11 stays: it is how a custom picture is one thing
across rows and how the state tag names it.

## I. Verification the owner does (adds to section 12)

On their own machine: a click on a row's thumbnail with Brawlhalla open shows on the next match load; ticking
three rows and applying a pack from the bar writes all three; the Packs strip scrolls smoothly with their real
library.

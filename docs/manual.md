# BhMaps manual

BhMaps is a Windows desktop app for managing Brawlhalla map art. It is map-first: every map is
listed under the name the game gives it, above a preview composed the way the game composes it, from
the game's own level data. It keeps a library of _packs_, folders of images that mirror the game's
`mapArt` tree, and copies them into the game folder on demand. The only files it changes are `.png`
and `.jpg` files inside the `mapArt` folder and inside its own library, plus its own files in
`%APPDATA%\BhMaps`. It also writes over the `.jpg` files already in the game's `images\thumbnails`
folder, and it keeps a copy of every original so it can put it back. The game's data files are read and never written, and
nothing else in the game install is touched.

## What is new in 3.5

A map's panel previews before it applies. Click any background or platform set in the panel and the
big picture at the top redraws the whole map with it, without changing anything in the game. Click a
background and then a platform set to see both together. A previewed tile wears a ring, the picture
carries a "Preview" tag, and a line under it names what is shown, with Apply (or Apply both) and Show
current. Escape clears the preview first and closes the panel on the next press. The tile the game is
showing keeps its check and no longer has a border, so the ring is the only outline in the panel.

## What is new in 3.4

Updating happens in place. When a newer release is out, Settings shows one Update button. It downloads
the new version, checks it against the release's checksums, puts it in place of the running copy and
restarts BhMaps, all in one step. This works for both the `.exe` and the `-dotnet.zip` build, so there
is nothing to download by hand. BhMaps must run from a folder it can write to; if it cannot, the update
is undone and an error says so. The What changed button is now called Changelog and still opens the
release notes on GitHub.

## What is new in 3.3

The library can hold packs in your own folder layout. A folder one or two levels under the library that
holds a `mapArt` folder is read as a pack, named after that folder, next to the packs in `packs`. These
packs are read-only: BhMaps shows and applies them but never deletes, renames or changes them. A found
pack whose name is taken shows as "Name (1)".

## What is new in 3.2

Maps are split by layout now. The game builds several layouts from one art folder, such as World's End
and Small World's End, or Terminus and Small Terminus, and each playable layout has its own card with
its own name, preview and set chips. The chips follow the game's own lists: All, Ranked 1v1, Ranked 2v2,
Tournament 1v1, Tournament 2v2 and Minigames. So Tournament 1v1 shows Small World's End and Small
Terminus, the layouts the game really plays there. Layouts that are in no list still get a card under All.
A map's panel and the platform editor show only the files that layout uses, and a file another layout
shares says "also in" with that layout's name, because changing it changes both. The background is one
per folder in the game, so the Backgrounds page keeps one row per folder and the background editor names
every layout that shares it. A pack's page has one tile per layout too.

Packs, a pack's page and Maps have a Both / Platforms / Backgrounds switch. Platforms draws only the
platform pieces, with no background, on a checkerboard of grey squares. Backgrounds draws only the
background picture. On the Packs page the switch also hides packs that have none of that kind. The choice
is one setting for all three places and is remembered.

Lowering a platform's opacity no longer shows bright lines. The game builds platforms from overlapping
pieces, and fading each piece on its own drew the overlaps twice. The app now writes the lower pieces a
little more transparent where the pieces above cover them, so the whole platform fades as one layer. It
needs the game's level data; without it, pieces fade on their own as before and the status line says so.

In the platform editor, Fit and Center keep the platform's shape: the part of a piece the picture does
not reach keeps the piece's own art instead of going transparent. Across the platforms and On each piece
can be picked before a picture is loaded, and a saved On each piece picture reopens with its own fit mode.

A map card shows only the map name. Its right-click menu names the pack the background and the platforms
come from, as a quiet second line under Edit background and Edit platforms.

## What is new in 3.1

The status line is in the top bar. Everything the app is doing or has just done reads on one line
between the tabs and the Refresh button, with its coloured dot, its links and nothing else: no card
round it, no row of its own at the foot of the page, and nothing on the page that moves when the line
comes or goes. A long line is trimmed to the room there is, and hovering it says the whole of it. A
running line stays while the work does and a failure stays until its × puts it away; a note, a done,
an undone and a cancelled line have six seconds and then fade, and that clock stops while the pointer
is on the line or the keyboard is in it. When a faded line left something to undo, its dot stays lit
where it was: hovering the dot says what the line was and clicking it brings the line back with its
Undo. Ctrl+Z and Escape do the same thing whether the line is showing or not. The lines are shorter
with it: one sentence each, and a write says where it will show only while the game is running,
because a closed game shows it at the next start either way.

A pack's own page reads better and asks less of you. The menu on a picture tile says **Edit background**
rather than "Edit", and a map's tile offers **Edit platforms** beside it, so both halves of a map are one
click from the grid, and **Edit platforms** is now on every map's tile, not only the ones this pack
has a background for. The `Default` pack has the eye too, so the game's own art can step out of the
lists like any other pack; Reset, Capture defaults and the top-up after a game update read its folder
and go on working either way. The band behind the pack's name is as tall as the words in it rather than a fixed
height, and the pack's art behind them is dimmed so the name and the line under it stay readable on any
pack. The note about files that change nothing in game can now be dismissed with the X next to Remove: it
stays away until the pack holds a different number of them.

A map card's **Apply picture** flyout is now
**Apply background**, and its **Apply pack** flyout lists only the packs that have something for that map,
with a greyed last line counting the ones left out and naming them when you rest on it. The picture chooser that **More pictures...** and **Apply to a map...** open is a grid now: 760 by 560, resizable, with each picture at 224 by 126 and its name under it, instead of a list with a thumbnail the size of a fingernail.

The chip row is one filter now: All, Ranked 1v1, Ranked 2v2, Tournament and Minigames are shared by
Maps, Backgrounds and Platforms, so a chip picked on one of them is the chip the other two show. Each
page still has its own search box, and every run starts on All.

## What is new in 3.0

3.0 is a redraw. The pages, the words and the dialogs were reworked from one design pass over the
whole app, so most of what changed is how things read and where they sit rather than what the app
can do. Nothing about the files it writes changed.

- **Rows that fill the width.** Maps, Backgrounds, Platforms and a pack's page lay their cards out
  in justified rows: every row is as wide as the page, and the cards in it share the leftover space.
  The zoom slider is gone. A three-step size picker at the top right of each page, Large, Medium
  and Small, takes its place, and Ctrl and the mouse wheel, Ctrl and plus, Ctrl and minus and Ctrl
  and 0 do the same from the keyboard. Each page remembers its own size. At Small the names go and
  the pictures speak for themselves.
- **One status line.** Everything the app is doing or has just done reads on one line at the foot of
  the page, with a coloured dot for its state: running, done, undone, cancelled, an error, or a note.
  A running line has Cancel, a done line has Undo, an error has Retry, and every other line has an
  × to put it away. Ctrl+Z undoes the last write and Escape cancels the one running. The header no
  longer carries status text.
- **Dialogs say what the button does.** A confirm names the action on its button, "Apply to 23
  maps", "Reset 68 maps", "Delete b&w maps", and a button that throws something away is red. Only
  one dialog opens at a time.
- **Empty pages say why.** A search with no match says so and offers to clear it. A page whose game
  folder is gone says "Game folder not found. Choose the Brawlhalla mapArt folder to continue." with
  a Choose folder button, and the app notices the folder going or coming back within a few seconds
  on its own. A library with no packs yet shows a banner offering to capture the Default pack.
- **The Welcome window shows its progress.** Capturing the Default pack on first run reads as a
  progress line inside Welcome, and the main window opens with the done line on its status strip.
  Paths in Welcome and in Settings are shortened in the middle so they fit on one line.
- **Pictures have names.** A picture added to My Backgrounds is named when it is added, from its
  file name with the underscores and hyphens turned into spaces, and can be renamed from its tile's
  menu. The name is what every list, chooser and status line calls it; the file name is never shown
  as a name again.
- **Words, not codes.** A map's level sets read as words in a fixed order, "Ranked 1v1, Ranked 2v2,
  Tournament, Standard, Experimental, Minigames", and everything the panel says ends in a full stop.
  A map whose Reset needs the Default pack says "Needs the Default pack." on the button.
- **Packs rows say where a pack is.** Each row says "On N maps." when the pack is on the game, "Not
  in game." when it is not, "What Reset puts back." for Default and "Hidden from lists." for a pack
  stepped out, and a "+N more" link opens the pack's page, which has a picture band across its top.
- **Settings is three sections.** Folders, Game and Updates, each row a label, a value and one
  button. The Map-select thumbnails switch is gone: thumbnails are always written with the art. The
  Version row says "Latest. Last checked yesterday, 20:44." or "Update available: 3.1.0." with a Get
  button, and Updates is one switch, "Check for updates when BhMaps starts".
- **The game's version.** Settings says which Brawlhalla the game data came from and when it was
  read, "From Brawlhalla 10.10, read 17 September 2026.", and the data is read again on its own when
  the game has updated: at start, and when Brawlhalla is seen starting or stopping. The Refresh
  button in the top bar is an icon with its explanation in the tooltip.
- **Minigames.** A Minigames chip after Tournament holds the ten maps only a game mode uses,
  Brawlball, Bombsketball, Horde and the rest, and All leaves them out. A typed search still finds
  them.
- **Editors end in two buttons.** The background and platform editors end in "Save and apply" and
  "Save only" instead of a Save button and an Apply to game box. Platform pieces are "Piece 1",
  "Piece 2" and so on, with the file name under each, and both editors offer the same four fits,
  Fill, Fit, Center and Stretch. The map chooser's button says what it will do, "Apply to Grove" or
  "Apply to 3 maps", and Add Image counts the pictures picked.

## What is new in 2.8

- **A pack can step out of the lists.** Every Packs row except Default has an eye button before Apply
  all, and its menu has **Hide from lists** / **Show in lists**. A hidden pack stays on the Packs page,
  dimmed, with "Hidden from lists." last on its counts line, and Apply all still works on it; its pictures
  and platform sets stop showing up along the rows on Backgrounds and Platforms and in a map's panel,
  except on a map where the game is showing that pack's art right now. That tile stays, with a muted
  "hidden" after its name, until something else is applied to the map; Undo brings it back with the
  art. The Apply pack menu on a map card keeps every pack, marked the same way. A muted "N packs
  hidden" note at the right of the chip row on Backgrounds and Platforms says why a pack is missing
  and goes to the Packs page. The setting is this machine's, kept in `settings.json`, never part of a
  pack.
- **Refresh, next to Launch.** One button makes the game match what the app says is on. It applies
  again every game file whose source changed since the app put it there (a pack picture edited on
  disk, a picture in My Backgrounds replaced), restores a file missing from a map folder from the
  pack the folder matches or from Default, and rewrites the map-select thumbnails for every map with
  custom art. It is one write with one Undo, and its line reads "Refreshed 12
  maps. Shows on the next match load." A game file the app wrote that has since been changed by
  hand is left alone and counted: "1 file changed by hand, left alone." With nothing to do it says
  "Nothing to refresh." and writes nothing. F5 is still the read-only rescan.
- **The app remembers what it applied.** To know that a source changed, every apply, reset and undo
  now records, per game file, where the file came from and the bytes it had, in
  `%APPDATA%\BhMaps\applied.json`. It only remembers; it never changes what the game shows.
- **A game update's new map goes into Default by itself.** The Default pack is a capture from before
  the update, so a new map used to read as custom art with nothing to reset to. Now the first scan
  that sees a map folder or a background slot the game has and Default lacks copies exactly those
  files into Default and says "Brawlhalla updated: Eternity's End added to Default." It never
  replaces a file Default already has, and it skips a file a pack matches or the app itself wrote,
  so your applied art is never captured as default. Capture defaults stays for the case where the
  whole pack is wrong, and now has a camera icon of its own.
- **Maps selects one map at a time.** The tick box on a card and the bar it summoned are gone, and so
  is selecting several maps: a click opens the panel and the light border marks the open card, the
  right-click menu acts on the card under the pointer under that map's name, Escape closes the
  panel. Apply to all maps, Reset all and the "Apply to a map..." chooser cover the many-maps cases.
- **Cards keep their size when the panel opens.** The zoom slider now sets a card width instead of a
  column count, and the grid holds as many columns as fit, so opening the panel moves cards down a
  row instead of shrinking them, the way a photo grid reflows. The default window looks exactly as
  it did.
- **"Selected only"** is the platform editor's chip, with "Select a file to edit it." and "Select a
  file to see it on its own." as its hints, in place of "Ticked only".
- **No "Object reference not set to an instance of an object" box** on the first Edit from a
  platform tile whose pack has no saved edit yet.

## What is new in 2.7

- **Delete lines on the tile menus.** A My Backgrounds tile and a pack's picture tile have **Delete
  background**, a Platforms page tile has **Delete platform set**, a pack's map tile has **Delete from
  <pack>**, and the Packs row's Remove is now **Delete pack**. They sit last in the menu, in red, and
  none of them appears on the Default pack. The confirm reads **Delete <name>?** and says what will
  happen: "It will be removed from My Backgrounds and Brawlhaven will reset to default." A delete
  removes the pack's files and, where the game is showing that art, puts only that part of the map
  back to default, the background or the platforms, in one write, so one Undo brings back the files
  and the game folder together. Without a Default pack the files still go and the game is left as it
  is.
- **The menu's first line is a title.** It names the map, the picture or the pack in the text colour
  with a rule under it, and a second, smaller line says what the map shows ("sunset.jpg from My
  Backgrounds", "Neon pack", "Default art") or which pack and map a tile belongs to. It no longer
  looks like a greyed-out button.
- **Map-select thumbnails for the Small maps are right.** A folder that holds several levels, Mammoth
  Fortress and Small Mammoth Fortress for one, used to have its big level's picture written over every
  thumbnail it owns. Each thumbnail is now rendered from the level that names it, so Small Brawlhaven,
  Small Mammoth Fortress and the rest show their own layout in the map select screen.

## What is new in 2.6

- **Every tile in a pack has a menu**, map tiles included. The dots button on a tile, or a right-click,
  opens it, headed with the map's name: **Apply to <map>**, **Copy to pack...**, **Move to pack...**,
  **Show in folder** and **Remove from <pack>**. A tile that also has a picture of its own keeps the
  picture lines it had in 2.5.
- **Copy to pack...** and **Move to pack...** move a map or a picture between packs. Both ask which
  pack with a small list that ends in **New pack...**, and both carry the map's folder, its pictures in
  the pack's Backgrounds folder and what the editors remembered for it. If the pack you picked already
  has that map, a confirm asks **"<pack> already has <name>. Replace it?"** A copy is not undoable and
  its done line offers **Open <pack>** instead; a move always is.
- **Ctrl+C**, **Ctrl+X** and **Ctrl+V** do the same on a pack page. Ctrl+C or Ctrl+X takes the tile
  under the pointer, or the one the keyboard is on, and Ctrl+V pastes it into whichever pack you are
  looking at. Pasting into the pack it came from does nothing; Ctrl+V with nothing held says
  **Nothing copied yet**.
- **Import from pack**, a button in the pack header and **Import from another pack...** in the Packs
  menu, copies another pack's maps in one go. Pick the pack to take from, then **All <n> maps** or
  **Choose maps** with a tick list, where a map the pack already has reads **already here** and starts
  unticked. **Replace maps <pack> already has** is off until you turn it on. The button counts what it
  will do, as in **Import 7 maps**, and the import can be cancelled; what was copied stays.
- **Duplicate** in the Packs menu copies a whole pack under the next free name, as in "Neon copy",
  with no name to type. Undo removes the copy.
- **BhMaps checks for a new release** once a day, when it starts, and says so with a **New update
  available** line in the top bar that goes to Settings, with an x beside it that dismisses that
  release. The Settings Version row offers **Update to 2.6.0** and **What changed**; the update
  downloads the new exe, checks it against the release's checksums and swaps it in when you close
  BhMaps. Nothing downloads or restarts on its own. The new **Updates** row turns the check off with
  **Check for updates when BhMaps starts** and has a **Check now** button. It is one request a day to
  github.com, and nothing about you or your library is sent.
- **Map-select thumbnails are written for far more maps.** A map whose mapArt folder holds several
  levels naming different pictures, which is most of the ranked "Small" maps, used to be skipped
  altogether. It now gets its picture written over every one of those files, so Small Mammoth
  Fortress, Small Great Hall, Small Wasteland and the rest change in the map select screen along with
  the maps that always worked. A file that two different maps' folders name is still left alone.

## What is new in 2.5

- The background editor and the platform editor remember what you saved. Save a picture or a set of
  platform values into a pack, open that editor on the same map and the same pack again, and it
  comes back on the values you left: the fit, the pan, the darkening, the opacity, the hue and the
  picture behind them. A line under the source row says where they came from, as in "Values from
  Neon", and a **Start fresh** link beside it throws them away and opens on the defaults.
- **Replace** in the platform editor lays one picture across the platforms. The picture is fitted
  over all the ticked pieces at once, so a single image runs along the whole level and each platform
  shows the part of it that platform sits on; dragging the preview moves the picture under them. A
  switch beside Image chooses between **Across the platforms**, which is what a replace now does, and
  **On each piece**, which fits a separate copy to every ticked piece the way 2.4 did.
- **Edit platforms** works on a selection of maps. Select maps on Maps, open the platform editor, and
  a strip under the title moves between them with **Previous map** and **Next map**, PageUp and
  PageDown, and a count reading "2 of 3 maps". The values you set carry to every map in the
  selection, and one **Save** writes them all, with a line saying how far it has got, as in "Saving 3
  of 59 maps into Default", and a **Cancel save** button that stops between maps and leaves the maps
  already written as they are.
- **Reset to default** also clears what the editors remembered for the maps it resets, so they open
  fresh afterwards, and **Undo** puts that memory back along with the files.
- Settings has a **Map-select thumbnails** switch, off until you turn it on. With it on, a write that
  changes a map's art also renders that map at 290 by 164 and writes the picture over the map's own
  thumbnail in `<game>\images\thumbnails`, so the map select screen shows the art you put on. The
  original thumbnail is kept in `%APPDATA%\BhMaps\thumbnails-original`, and Reset to default, Undo
  and turning the switch off again put it back. A map whose thumbnail is shared with another map,
  one that names no thumbnail, and one whose file is not there are left alone, and the map panel says
  which of those it is.
- A card no longer shows its old picture for a moment after a write. The cards of the maps a write
  touched reload their previews before the rest of the grid refreshes, so what you see is what was
  just written.

## What is new in 2.4

- The platform editor's preview can follow the ticks. Two chips sit over the preview: **All pieces**
  draws every piece as before, and **Ticked only** draws the ticked pieces sharp, fades the rest to a
  faint ghost, and frames the ticked pieces so a small platform fills the picture. The editor
  remembers which chip you left on; opened from a Platform files row it starts on Ticked only, so
  the preview shows that one piece.
- Clicking a Files row's name or thumbnail ticks that piece alone; Ctrl+click adds it to the ticks;
  Enter or Space on a focused row does the same as a click. The tick box still toggles one piece.
- Every picture tile in a pack has a menu, from the dots button on hover or a right-click: apply it to
  the map it belongs to, to the selected maps, to a map chosen from a list, or to all maps, then
  Edit, Show in folder and Remove from the pack.
- Every card on Maps has a right-click menu: Apply pack and Apply picture flyouts, Edit background,
  Edit platforms, Reset to default and Show in game folder. Right-click a selected card and the menu
  works on the whole selection.
- A chooser window lists the maps, or your pictures, with a search box, for "Apply to a map..." and
  "More pictures...".

## What is new in 2.3.1

- When another program deletes a piece you handed it, the editor goes back to the piece's own art and
  says so above the sliders, instead of keeping a stale preview.
- The Opacity and Hue readouts are blank while nothing is ticked.
- Opening a map panel reads each platform file from disk once instead of three times.

## What is new in 2.3

- The platform editor lists the map's pieces as rows with a tick each. Tick the pieces you want to
  change; All and None above the list.
- Opacity and Hue apply to the ticked pieces only, and every piece keeps its own values. When ticked
  pieces disagree the readout says Mixed.
- Image, above the sliders: Replace fits any picture (PNG, JPG, BMP, GIF or WebP) to each ticked
  piece, covering and centring it and cutting it to the piece's own shape. Reset puts the piece's
  own art back.
- Edit in another app writes the ticked pieces into the pack you are saving into and opens each one
  with the Windows "Open with" dialog. The preview follows what the other program saves. Cancel
  keeps those files in the pack.
- On the map panel, every Platform files row has an Edit button that opens the editor with only that
  piece ticked.

## What is new in 2.2.2

One fix to 2.2.1, nothing new on disk.

- **Editor sliders follow the pointer.** A press on the groove drags the thumb under the pointer for as long as the button is held. In 2.2.1 the thumb moved with the pointer but at a fixed distance from it.

## What is new in 2.2.1

Two fixes to 2.2, nothing new on disk.

- **My Backgrounds counts only your own pictures.** The switch on Backgrounds, the picture groups on
  the map panel and every place that lists pictures show the files you imported into packs other than
  Default. The Default pack is a capture of the game's folder, so its files are the game's art; a
  picture only the game has appears on the rows and panel of the maps showing it and is never counted.
- **Editor sliders drag from anywhere.** In the background and platform editors, and on the Maps zoom
  slider, pressing anywhere on the groove moves the thumb there and keeps dragging it while the button
  is held, instead of only when the press lands on the thumb.

## What is new in 2.2

Eleven changes, sharing one idea: the app stops inventing categories the user never asked for and
describes things by where they are and what they do. Nothing on disk changes shape.

- **Any-map pictures are grouped by pack.** Wherever the app listed pictures of its own making, it now
  lists any-map pictures grouped by the pack that holds them, one group per pack in the Packs order,
  with a last group "In game only" for the picture the game is showing on that map that no pack holds.
  A group header is the pack's name and a count, as in "My Backgrounds (11)". The Default pack is a
  capture of the game's own folder, so what is in it is the game's art and not a picture of yours.
- **One switch on Backgrounds.** At the right end of the chip row, a plus that becomes a minus beside
  the text "My Backgrounds (11)" shows or hides your own pictures on every row at once, in place of
  the fold that answered the same question once per row. The count is the pictures you imported, the
  ones in packs other than Default; a picture only the game has is not one of them, and it appears on
  the rows and the panel of the maps showing it. The page remembers it.
- **The background editor can save for all maps.** Its Map picker starts with "All maps", which saves
  the picture into the pack under its own name instead of a map's slot name, and can apply it to every
  map in the same step.
- **A platform editor.** Edit on a platform set opens Opacity and Hue sliders over a live composed
  preview. Fading the pieces lets the background show through while the map plays the same; shifting
  their hue recolours a set to match a background without touching its shapes. It saves into a pack and
  can write the result into the game in the same step.
- **The map panel is one list, Background over Platforms.** The two-part switch is gone, so both halves
  of a map's look are in front of you at once.
- **The Packs order follows what you used last.** Default first, then the packs with a last-applied
  stamp, newest first, then the packs never applied, by name. Each pack row says how many maps are
  wearing that pack's art right now, as in "On 12 maps.". The order is the same on the Packs page, in
  the map panel, on the rows pages and in the editors' pack pickers.
- **Tile menus hold only what the tile cannot do on its own.** "Show files" is offered only in the map
  panel, where it does something, and "Apply to <map>" is dropped wherever the tile already carries an
  Apply button. Edit is on every platform set.
- **Smooth scrolling.** The mouse wheel glides rather than jumping, on every list in the app, and a fast
  spin becomes one glide. Scroll bar drag, keys and touchpad scrolling are untouched, and the whole
  thing is instant when Windows animations are off.
- **The Changed and Custom chips go.** The chips are the game's own map lists and answer which maps you
  are working on: All, then the sets. Changed and Custom are states of a map, and each map already wears
  its state on its card tag, its row and its status line.
- **Platform files are the map's own pieces.** The panel's file list skips the event decorations that
  live in shared seasonal folders, which the composed preview already leaves out, so a Brawlhaven-shaped
  level lists six files rather than eighteen.
- **Make a pack in the app.** "New pack" on the Packs page asks for a name and makes an empty pack, so a
  pack no longer has to arrive by import.

## Where things live

| What                                                 | Where                                                                                                         |
| ---------------------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| Game map art, read and written                       | `C:\Program Files (x86)\Steam\steamapps\common\Brawlhalla\mapArt`                                             |
| Game data files, read only                           | `BrawlhallaAir.swf`, `Dynamic.swz`, `Init.swz` and `Game.swz`, in the folder above `mapArt`                   |
| Game map-select thumbnails, written with the map art | `C:\Program Files (x86)\Steam\steamapps\common\Brawlhalla\images\thumbnails`                                  |
| Pack library                                         | `Documents\BhMaps` by default, changeable in Settings                                                         |
| Packs inside the library                             | `<library>\packs\<pack name>\<GameFolder>\<file>`                                                             |
| Discovered packs, read only                          | `<library>\...\<pack name>\mapArt\<GameFolder>\<file>`, with `mapArt` at most three folders below the library |
| Settings                                             | `%APPDATA%\BhMaps\settings.json`                                                                              |
| Applied record                                       | `%APPDATA%\BhMaps\applied.json`                                                                               |
| Hash cache                                           | `%APPDATA%\BhMaps\hashcache.json`                                                                             |
| Level data cache                                     | `%APPDATA%\BhMaps\leveldata.json`                                                                             |
| Composed previews                                    | `%APPDATA%\BhMaps\previews\`                                                                                  |
| Undo of the last game write                          | `%APPDATA%\BhMaps\undo\`                                                                                      |
| Downloaded updates                                   | `%APPDATA%\BhMaps\updates\`                                                                                   |
| Kept map-select thumbnails                           | `%APPDATA%\BhMaps\thumbnails-original\`                                                                       |

The map art folder and the library are chosen in the welcome window and editable in Settings; the
data files are wherever the game folder is. Only `<library>\packs` is written, so anything
else kept in the library folder is left alone. BhMaps also reads packs kept elsewhere in the library: any
folder with a `mapArt` folder inside it is a pack named after that folder, with its game folders read from
the `mapArt` folder, for example `<library>\Summer\mapArt\BloodMoon\<file>`. The search looks at most
three folders deep, counting the library's own subfolders as the first, so `mapArt` itself has to sit
within those three. It does not look inside a pack it has found, skips `packs`, and skips junctions,
links and folders it cannot open. These discovered packs are read only: they can be viewed, applied,
exported and duplicated into `packs`, but not deleted, edited, imported into, or used as a copy or move
target. When a discovered pack has the same name as another pack, ignoring case, the pack in `packs`
keeps its name and the discovered one gets a number, such as "Summer (1)", then "Summer (2)", in the
order of their folder paths. The settings file holds the two paths, the tile size
each page remembers, when each pack was last applied, whether the Backgrounds page is showing your own
pictures, the names of the packs hidden from the lists, whether the welcome window has been
finished, whether BhMaps checks for updates when it starts, when it last checked, and any update
notice you dismissed. The three caches are speed-ups
only: deleting any of them costs one slower start, and preview files unused for 30 days are deleted
at startup anyway. The undo folder holds one set at a time, the files the last
write into the game folder was about to overwrite or delete. The game folder can be any folder shaped like `mapArt`, one level of subfolders holding `.png` and `.jpg` files. Real map names, level sets and composed previews also need the game's four data files in the folder above it; without them the app still runs, with folder names and single-file thumbnails.

Once a day at most, a few seconds after the first scan, BhMaps asks GitHub whether there is a newer
release. It is one request to `github.com`; nothing about you, your machine or your library is sent,
and no account or token is involved. A newer release shows as one line in the top bar and on the
Settings Version row, and nothing is downloaded until you press **Update**. Turn the whole thing off
with the Updates row in Settings. Update downloads the release's copy of the build you are running,
the `.exe` for the self-contained build and the `-dotnet.zip` for the framework-dependent one, into
`%APPDATA%\BhMaps\updates\`, verifies it against the release's published SHA-256 checksum, puts it
in place of the running `BhMaps.exe` and restarts BhMaps on it. Both builds update this way; the
folder BhMaps runs from has to be one it can write to.

## Pages

The top bar carries the app's name, then the five tabs: Maps, Backgrounds, Platforms, Packs and
Settings. Ctrl+1 to Ctrl+5 switch between them, and Ctrl+K goes to Maps and puts the cursor in its
search box. After the tabs is the status line: what the app is doing or has just done, on one line
with its dot and its links, trimmed to the room between the tabs and the buttons at the right and
said in full by hovering it. On the right is the game line, reading "Brawlhalla running", or "Brawlhalla not running"
beside a Launch button that asks Steam to start the game. Before the game line is **Refresh**, a
square button showing only its arrows, which applies again every game file whose source changed since
the app put it there, restores a file missing from a map folder, and rewrites the map-select
thumbnails; one write, one Undo, and "Nothing to refresh." when there is nothing to do. Hovering it
says "Refresh: re-apply what is on, restore missing files, redraw the map-select thumbnails". When a newer release is out, a **New update
available** line sits before the game line; clicking it opens Settings and the small x beside it hides
it until the release after that. When the game folder cannot be found the
line turns red and offers Choose folder, which opens Settings. F5 rescans; Cancel in a page header
stops a long operation.

- **Maps** is the grid of maps. Chips above the grid
  filter: All and one per level set. The set
  chips are Ranked 1v1, Ranked 2v2 and
  Tournament when the game's data has the ranked sets, and the standard ones when it does not; the
  other sets the game defines are read but not shown. Minigames comes last and holds the ten maps
  only a game mode uses, such as Brawlball and Horde, and All leaves those ten out; a search still
  finds them whatever chip is on. The chip is shared with Backgrounds and Platforms: pick one here and
  those two pages show the same set. The card size is the size control (Large,
  Medium, Small) in the page header, or Ctrl and plus, minus, zero, or the wheel; the grid holds as
  many columns as fit, so opening the panel moves cards down a row instead of shrinking them. Each card is a composed preview and the map's
  name, nothing else: which pack the art comes from is in the card's right-click menu, under Edit
  background and Edit platforms. A map with a missing file is marked with a dot on the picture at
  every size. From the sixth step on the name is smaller; at the last two the name row goes
  altogether and the name becomes the card's tooltip.
- Clicking a card opens the map panel and outlines the card; Enter on a focused card does the same,
  the arrows move between cards, and the card scrolls into view if opening the panel moved it. One
  map is open at a time; there is no selecting several. A write to more than one map, from **Apply to
  all maps** or the chooser, asks first and names the count. Escape closes the panel when one is
  open, and clears the search when none is.
- A right-click on a card opens its menu for that card. The menu opens with the
  map's name and, under it, what the map shows. **Apply pack** unfolds the packs that have something
  for that map, with a hidden pack marked "hidden", and ends in a greyed line counting the packs left
  out, "2 packs have nothing for Grove", which names them when you rest on it;
  **Apply background** unfolds your pictures, up to eight, then "More pictures..."
  for the chooser and "Add Custom Image..."; then **Edit background**, **Edit platforms** (one map
  only), **Reset** and **Show in game folder**. One map applies without asking; a write to
  more than one map asks and names the count.
- The **map panel** opens on the right and holds everything one map can do: its name and the sets it
  belongs to, a larger preview, a line saying in words what is in game, Reset this map and Open
  folder. Below those it is one list, Background first and then Platforms, under a section header
  each with a hairline between them. Background lists the choices two across: Default, then one per
  pack that has a picture for this map in the Packs order, then one folded strip per any-map group,
  one per pack that holds one and a last strip named "In game only" for the picture the game is
  showing on this map that no pack holds, with Add Image beside the first header. Platforms lists
  Default and each pack that has a set for this map, drawn over the map's current background, and under them a "Platform files" fold with a count, closed to begin with,
  listing the pieces that map draws. The choice the game is showing carries a check. Hovering a
  choice shows Apply and a dots button, and the dots or a right-click opens its menu: apply to all
  maps, then Edit and Show in folder for a picture, or Edit, apply to
  this map, Show files and Open folder for a platform set. Reset all to default is in the page
  header.
- **Clicking a choice in the map panel previews it** in the larger preview without applying anything;
  Enter or Space does the same on a focused choice. A background and a platform set preview together,
  so picking one of each shows the whole map as it would look. The previewed choice wears a ring, the
  preview carries a "Preview" tag, and a line under it names what is shown with **Apply** (or **Apply
  both**) and **Show current**. Clicking the previewed choice again, or the one the game is showing,
  takes that half back out. Escape clears the preview first and closes the panel on the next press,
  and opening another map drops it. The Show chip applies here too, so with Backgrounds only a
  background preview is the picture alone.
- **Backgrounds** is one row per map: the map's name and its tag on the left, then a strip of every
  background that map could have. The picture the game is showing comes first, with a check and a
  border; then Default, then one thumbnail for each pack that has a picture for that map in the Packs
  order, captioned with the pack's name. At the right end of the chip row is the "My Backgrounds"
  switch and a count, a plus that becomes a minus: turn it on and every row goes on to show the
  pictures you imported as well, grouped by the pack that holds them, wrapping onto further lines.
  The count is those imports, the pictures in packs other than Default. A picture only the game has
  is not under the switch: it is already first on the row of every map showing it, with its check.
  The page remembers the switch. When more choices exist than fit the
  row, a tile reading "+" and a number stands for the rest; clicking it unfolds that row, and rows
  fold back when the page is left. A pack hidden from the lists on Packs lays no tiles here, except
  on a map where the game is showing its art; that tile stays, with a muted "hidden" after its name,
  until something else is applied. A muted "N packs hidden" at the right of the chip row says so and
  goes to Packs.
  One click on a thumbnail applies it to that map, with no confirmation to answer. Hovering shows
  Apply and a dots button, and the menu offers to apply the picture to all maps, then Edit and Show
  in folder, and for a picture in a pack **Rename**, which renames its file in the library and leaves
  the maps it is on showing it, and **Delete background**, which also puts the default back on
  any map showing it, or Save to My Backgrounds for a picture the game is showing that no pack
  holds. Search matches map names, pack names and file names. The chips are the ones Maps has, and
  they are the one filter: the chip picked here is the chip Maps and Platforms show.
  The thumbnail size is the size control (Large, Medium, Small) in the page header, or Ctrl and
  plus, minus, zero, or the wheel. Add Image is in the header. Up and Down move between rows, Left and Right along a strip, and Enter applies
  the thumbnail the keyboard is on.
- **Platforms** is the same kind of row for the other half of a map's look. A platform set fits only
  the map it was drawn for, so a row holds exactly the packs that have a set for that map: the set in
  game first with a check, then Default, then one thumbnail per pack, captioned with the pack's name.
  Each thumbnail is cropped to the platforms themselves and drawn over the map's current background,
  because a whole level shrunk to row height shows the platforms as slivers. Every thumbnail in a row
  uses the same crop, so they can be compared. The packs follow the Packs order. Hidden packs are
  left out here the same way as on Backgrounds. Clicking a thumbnail
  applies it to that map. The menu offers Edit, apply to this map, Open
  folder and, for a set that is not Default's, **Delete platform set**, which also puts the default
  platforms back if the map is showing that set; Show files belongs to the map panel, where the
  files are listed. Search matches map
  and pack names, and the size control is the one Backgrounds has.
- **Packs** is one row per pack: a composed thumbnail of the pack's first map, the pack's name and
  what it holds and where it is, as in "3 maps, 12 backgrounds. On 12 maps.", and a strip of previews
  of the maps it touches, as many as the width allows, with a "+56 more" link to the pack's own page
  for the rest. A pack with nothing of its in the game says "Not in game."; `Default` says "What Reset
  puts back.", because it is the art every reset lands on rather than a pack you apply. `Default` comes first,
  then the packs that have been applied, the most recent first, then the ones never applied, by
  name; every list of packs in the app follows that order. Apply all is a button on the row; a dots
  button beside it holds **Duplicate**, **Import from another pack...**, Export, Open folder and
  **Delete pack**; `Default` has no Delete pack. Every row, `Default` included, has an eye before Apply
  all: click it and the pack is hidden from the lists on Backgrounds, Platforms and the map panel,
  the row dims and its counts line ends "Hidden from lists."; click again to show it. The dots menu
  says the same in words, **Hide from lists** / **Show in lists**. The header has Import folder, New
  pack, Capture defaults and Open library. After a game update, the first scan that finds a map
  folder or a background slot the game has and Default lacks copies exactly those files into Default
  and says so: "Brawlhalla updated: Eternity's End added to Default." It never replaces a file
  Default already has and skips a file a pack matches or the app wrote, so Capture defaults is only
  for a pack that is wrong as a whole. **New pack** asks for a name and makes an empty pack you can
  fill later from Add Image or the editors. Clicking a row opens the pack.
- **Pack detail** shows one pack as a single grid of its maps, each composed with the game's art
  where the pack has nothing of its own, at the size the header's size control sets. Clicking a tile opens a
  drawer listing that map's files, its background and its platforms, and where each came from.
  Hovering a tile shows a dots button, and the dots or a right-click opens the
  menu. A map's tile is headed with the map's name and holds **Apply to <map>**, **Edit platforms**,
  **Copy to pack...** (Ctrl+C), **Move to pack...** (Ctrl+X), **Show in folder** and **Delete from
  <pack>**; a picture's tile is headed with its file name and keeps 2.5's lines, with Copy and Move
  added after **Edit background**, and **Delete background** last. **Edit platforms** is on every map's
  tile, with a background for the map in this pack or without one, and opens the platform editor on
  that map with this pack's own pieces; it is greyed out, saying
  so, when the pack holds no platform files for that map. A delete puts the default back on whatever the game was showing
  from it, and Undo brings back the files and the game folder together.
  Ctrl+V pastes whatever was copied or cut into the pack you are looking at. **Import from pack** in
  the header opens the import dialog. The Backgrounds page's picture menus gain the same **Apply to a
  map...** line. When
  the pack holds fully transparent PNGs, a line above the grid says how many of its files change
  nothing in game and offers to remove them. Apply all, Open folder and the way back to the pack list
  are in the header.
- **Settings** is one line per setting, under three headings. **Folders** is the game folder and the
  library, each with Change and Open. **Game** is the game data, reading "From Brawlhalla 10.10, read
  16 September 2026." with the version the game's files name, or "From the game's files, read ..."
  when they name none, and Refresh now beside it; and the Default pack, with **Capture**. **Updates**
  is the version, whose line reads "Latest. Last checked ..." or "Update available: <version>." with
  **Changelog** and **Update** beside it; and a checkbox reading **Check for updates when BhMaps
  starts** with a **Check now** button. There is no OK button, so every row saves as it is changed.

These windows open on top of the pages:

- **Welcome** opens on the first run and asks for the three things the app needs: the game folder,
  found through Steam when it can be, the library folder, and whether to capture the game's current
  art as the `Default` pack. It comes back if the saved game folder later stops working.
- **Add Image**, from the Backgrounds header or a map's panel, takes any
  number of images, dropped on the window or picked, fits them all one way (**Fill**, **Fit**,
  **Center** or **Stretch**) and writes each one into an existing or new pack as a 2048x1151 JPEG. A block
  of choices headed "Then" decides what happens after that: add them to the library and stop; add and
  apply to the map the window was opened from; or add and apply
  to every map. Applying goes in order and starts again from the first picture when there are more
  maps than pictures. The pictures it reads are never changed. A picture is called what its file is
  called: adding one picture asks for its name first, offering the file's own name tidied up
  (underscores and hyphens become spaces), and adding several takes those tidied names without
  asking. A name another picture in the pack already has gets " (2)" after it.
- **Chooser**, from "Apply to a map..." on a picture's menu or "More pictures..." on a map card's
  menu, is one list with a search box: every map, each with a thumbnail and the art it shows now, or
  every picture you imported, each with the pack that holds it. The box says "Search maps", and the
  button reads "Apply to <name>" for the row picked, "Apply to 3 maps" when several are, and a plain
  "Apply", greyed out, with nothing picked; the list opens on the first row, and Escape or Cancel
  applies nothing.
- **Import folder** takes several folders at once and makes one pack of each. Every folder's name is
  filled in as its pack name and can be edited; a name that matches a pack already in the library
  adds to that pack after one confirm. The button reads "Import N packs". Inside each folder, every
  `.png` and `.jpg` at any depth is routed to a game folder, and the whole routing table is shown
  before anything is copied.
- The **background editor**, from a background tile's **Edit**, fits one picture to a background slot
  and saves it into a pack. Its title names the file. The source row shows the picture, its name, its
  pack and its size, with **Replace** beside it, and another image can be dropped on it. Under that:
  which map the picture is for, a slot several maps share naming them all; the fit, **Fill**, **Fit**,
  **Center** or **Stretch**, with two pan sliders that apply only to Fill; and how far to darken it. The Map
  picker starts with **All maps**, which saves the picture under its own name rather than a map's
  slot name, so it stays an any-map picture; **Save and apply** then writes it over every map
  in the same step. All maps is what an any-map picture opens on, and the map it belongs to is what a
  pack picture opens on. It saves into a pack, the picture's own to begin with, under a note saying
  which file it replaces and that choosing another pack keeps the original. It ends in two buttons:
  **Save and apply** writes the picture into the pack and on into the game, and **Save only** writes
  it into the pack and leaves the game as it is. When that pack already holds values saved for this map, the
  editor opens on them and a line reads "Values from Neon", with a **Start fresh** link beside it
  that puts the defaults back. Opened from Add Image with no picture, the source row is
  where a picture is dropped or browsed for.
- The **platform editor**, from a platform set tile's **Edit** or from **Edit** on a Platform files
  row of the map panel, changes a map's platform pieces without redrawing them by hand. Its title
  names the map, as in "Edit platforms, Apocalypse", and the source row says which pack the pieces
  come from and how many there are, or "In game" for the art the game has. It opens on one map, from
  a card on Maps or from that map's panel. When the pack under Save into pack already holds values
  saved for this map, a line
  reads "Values from Default" and a **Start fresh** link beside it drops them and puts the defaults
  back. **Files** lists the
  pieces, one row each with a tick box, a thumbnail, the file name and that piece's own values; every
  row starts selected, **All** and **None** above the list change them together, and the header
  counts them, as in "Files, 2 of 6". Opened from a map panel row, only that row's piece is selected.
  Clicking a row's thumbnail or name selects that piece alone, Ctrl+click adds it, and Enter or Space
  on a focused row does what a click does. Two chips sit over the preview: **All pieces** draws the
  whole level, and **Selected only** draws the selected pieces sharp and the rest as a faint ghost,
  framed on the selected pieces with a little room round them; with everything selected the two look
  the same, and with nothing selected the preview says "Select a file to see it on its own." The
  editor remembers the chip you left on, except that a map panel row always opens on Selected only.
  **Image** says what the selected pieces show: the piece's own art, a picture fitted to it, or
  Mixed. **Replace** takes a PNG, JPG, BMP, GIF or WebP and lays it over the selected pieces, cut to
  each piece's own shape so the platforms keep their outlines. A switch beside Image says how it is
  laid on: **Across the platforms**, which is what it starts on, fits one copy of the picture over
  all the selected pieces at once, so each platform shows the part of the picture it sits on, and
  dragging the preview moves the picture under them; **On each piece** fits a separate copy to every
  selected piece. Beside the switch, the same four fits the other windows offer, **Fill**, **Fit**,
  **Center** and **Stretch**, say how the picture fills that box; only Fill hangs over the edges, so
  only Fill can be panned. **Edit in
  another app** writes each selected piece into the pack named under Save into pack and opens it with
  the Windows "Open with" dialog; the preview follows what that program saves, and Cancel leaves
  those files in the pack. **Reset** beside Image puts the piece's own art back, asking first over a
  file another app has been editing. **Opacity** fades the selected pieces so the background shows
  through, and **Hue** turns their colour round the wheel, in degrees, to match a background; each has
  its own reset, the readout says Mixed when selected pieces disagree, and the preview recomposes as
  the slider moves. **Save into pack** takes an existing pack or a new one and writes the selected
  pieces, copying a piece left at its own art and default values as it is. It ends in the editors'
  two buttons: **Save and apply** writes the result over the map's own art in the same step, and
  **Save only** leaves the game as it is. A map with no platform art of its own
  says so and has nothing to edit.

Every change is written straight into the game folder, whether Brawlhalla is open or closed, and
nothing is restarted. Each write reports in the page header: what it did, then "Shows on the next
match load." while the game is running or "Shows when Brawlhalla starts." while it is not, then
**Undo**. Undo puts back the files that write overwrote or deleted. One set is kept, so the next
write into the game folder replaces it.

Confirmations, errors and the list of files an operation could not touch are the app's own windows in
the app's own theme rather than Windows message boxes. Two failures before the main window exists are
the exception and use a message box: an error the app did not expect, and a development run given
`--game` or `--library` without `--appdata`.

### Menus

The words the menus use mean the same thing everywhere:

| Word            | Meaning                                                                                                                         |
| --------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| Picture         | A file you imported into a pack: an Add Image result, an editor save, or a copied file. Files the game owns are never pictures. |
| Owned picture   | A picture in a map's own slot, so it belongs to that map; "Apply to <map>" names it.                                            |
| Any-map picture | A picture in a slot no map owns; it can go on every map.                                                                        |
| Target          | The map a card menu acts on: the card under the pointer.                                                                        |
| Chooser         | The search-and-pick window behind "Apply to a map..." and "More pictures...".                                                   |

There is no universal setting for a picture. "Apply to all maps" writes the picture onto every map
now, and a later apply to one map replaces it there.

## Words the app uses

One action, one verb, wherever it appears. The label tells you what is about to happen; the confirm
repeats it with the number of maps in it.

| Action  | The label                                                                | Where it appears                                                               |
| ------- | ------------------------------------------------------------------------ | ------------------------------------------------------------------------------ |
| Apply   | "Apply all", "Apply to a map...", "Apply to all maps", "Apply to N maps" | The row button on a pack, the tile and card menus, and the confirm button      |
| Reset   | "Reset map", "Reset this map", "Reset all maps", "Reset N maps"          | The card menu, the open map's panel, the Maps page menu, and the confirm       |
| Capture | "Capture the Default pack"                                               | The Packs page menu and the third step of the welcome window                   |
| Refresh | "Refresh", as the tooltip of an icon                                     | The top bar. Re-copies the app's own files over what the game holds now        |
| Rescan  | "Rescan", F5                                                             | The top bar. Re-reads the game folder and the library without writing anything |
| Import  | "Import folder", "Import from pack"                                      | The Packs page and a pack's own page                                           |
| New     | "New pack", "Add image"                                                  | The Packs page and a pack's own page                                           |
| Undo    | "Undo", Ctrl+Z                                                           | The line a write leaves in the top bar, and the dot it leaves behind           |

Reset and Capture are the two that talk about the Default pack: Capture makes it out of the game's
own art, and Reset puts that art back.

## How it works

- **Level data.** The app reads, and never writes, four files in the folder above `mapArt`:
  `BrawlhallaAir.swf`, `Dynamic.swz`, `Init.swz` and `Game.swz`. They hold the levels, their real
  names and the sets they belong to. The `.swz` files are encrypted with a key that is found by
  scanning the compiled ActionScript in `BrawlhallaAir.swf` for the number that unlocks them; it is
  never hard-coded, because it changes with every game update. The parsed result is cached in
  `leveldata.json` next to the size and timestamp of all four files, so an unchanged game starts from
  the cache and a game update re-reads in the background: after the first scan of a run, and again
  whenever Brawlhalla starts or stops, because that is when an update has just landed. The strip says
  "Game data read." when it did, and Settings says which version that was. The version is the one the app finds written in
  `BrawlhallaAir.swf`; neither the `.swz` files nor the game's exe carry it. When the files are not
  there, the key is not found, or an entry fails its checksum, the app falls back to folder names for map names, hides
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
- **Reset** copies the `Default` pack's files for that map back, including its copies of
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
- The **applied record**, `applied.json`, is written by every apply, reset and undo: for each game
  file the app wrote, the source it came from, the hash of the bytes written and the hash of the
  source at the time. The undo session keeps a copy, so Undo puts the record back with the files.
  Refresh reads it: a recorded game file still holding the bytes the app wrote, whose source has
  since changed, is applied again from that source, fitted again when it was a picture; one whose
  bytes changed by hand is left alone and counted.
- A **hidden pack** is a list of pack names in `settings.json`, this machine's only. The rows and the
  map panel skip a hidden pack's tile unless the game is showing that pack's file for the map, which
  is the same test that draws the tick on a tile.
- The **Default top-up** runs after every rescan: any map folder or background slot the game has and
  the Default pack lacks, whose file no pack matches and the app never wrote, is copied into Default,
  once per run. It fills gaps only and never replaces a file.
- **What the editors remember** is kept in the pack, in two files at its root,
  `platforms.bhmaps.json` and `backgrounds.bhmaps.json`, one entry per map holding the values that
  editor was saved on. A pack gets one once something has been saved into it, whichever pack that
  is, and Export carries them with the rest of the pack. Reset drops the
  entries for the maps it resets, and Undo puts them back.
- **Map-select thumbnails** are the one thing the app writes outside `mapArt`. A write that changes
  a map's art renders that map at 290 by 164 and writes the JPEG
  over every picture in the game's `images\thumbnails` folder that the map's own levels name and no
  other map's folder names. A map's folder often holds more than one level, the ranked "Small" one
  beside the casual one, and those levels usually name different pictures; all of them are that map's
  and each is rendered from its own level, so the Small variant gets its own layout. The first time a
  file is written over, the original is copied into
  `%APPDATA%\BhMaps\thumbnails-original` under that file's own name, and those copies are what Reset
  to default and Undo put back. A picture two different maps' folders both
  name is skipped, so one map's art is never written over another map's thumbnail, and the map panel
  says which file was left alone and why.
- **Updates** are read from the GitHub releases page of the project, once a day at most, when BhMaps
  starts and only when the check is on. The request carries nothing but the app's version; no account,
  no token and nothing about your library. When the newest release is newer than the running version,
  Settings shows **Update** and **Changelog**. Changelog opens the release page. Update downloads
  the asset for the running build into `%APPDATA%\BhMaps\updates`, the `.exe` for the self-contained
  build or the `-dotnet.zip` for the framework-dependent one, checks it against the release's
  `SHA256SUMS.txt` and refuses it on a mismatch; from the zip only its `BhMaps.exe` is taken. It then
  renames the running exe to `BhMaps.exe.old`, which Windows allows while it runs, moves the new exe
  into its place, starts it and closes. The new BhMaps waits for the old one to exit and deletes the
  `.old` file, as every start does. If any step fails, both files are put back as they were and a
  dialog says why, with the release page as the way to update by hand. A development run, from
  `dotnet run` or a plain build, never updates itself and shows only Changelog. Update waits while
  BhMaps is busy writing, rather than closing in the middle of it.

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

That writes three files into `dist\`: a self-contained `bhmaps-v<version>-win-x64.exe` that carries
its own runtime, `bhmaps-v<version>-win-x64-dotnet.zip`, a framework-dependent build that needs the
.NET 10 Desktop Runtime on whatever machine runs it, and `SHA256SUMS.txt`, which the app's update
check verifies a download against, so all three go on the release. Each build updates from its own
asset, so the zip must keep holding a single `BhMaps.exe`. The repository has to be public
for the update check to reach the release at all.

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
- Replace fits a picture to the whole piece; there is no pan or zoom. Edit in another app opens the
  Windows Open with dialog; the program you pick must save PNG in place.
- A map-select picture that two different maps' mapArt folders both name is left alone, because
  writing it would change the other map's thumbnail too. The map panel says so.
- The update swap needs the folder BhMaps runs from to be writable. From a folder such as
  `Program Files` the swap fails, BhMaps is left as it was, and the dialog offers the release page.
- Copying a map into another pack is not undoable; moving, importing, removing and duplicating are.

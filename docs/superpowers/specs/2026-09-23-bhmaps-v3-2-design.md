# BhMaps 3.2 design

Review page: https://claude.ai/artifact/UzoaURCy4TsMevLwAvBu1W (db collections answers32, objections32, checks32).
Go given in chat on 2026-09-23 ("answered"). No objections, no list-check notes.

## Owner answers

- Q1 `rows-page-hide`: the switch filters the Packs rows, sits on a pack's page and filters its grid too, and hides packs that have none of the chosen kind.
- Q2 `split`: a card per playable layout. World's End and Small World's End, Terminus and Small Terminus, and so on are separate cards under All. Layouts that are in no set list (Small Thundergard Stadium, Small Blackguard Keep and similar) also get cards. Minigame arenas stay under Minigames.
- Q3 `t12`: chips for Tournament 1v1 and Tournament 2v2 (labels from the game's set names). Tournament 3v3 gets no chip.
- Q4 `original`: for Fit and Center, the part of a piece the picture does not cover keeps the piece's original art.
- Q5 `always`: seam-free opacity is always on. No toggle.
- Page comments: the Packs switch is also on the Maps page (P2). One remembered setting shared by Packs, the pack page and Maps. Platforms mode draws NO background at all: platform pieces on a checkerboard of big grey squares (the usual transparency sign), never the fine grid used for missing art. A pack's page splits per layout too (one tile per layout, opening a tile shows only that layout's platforms).

## Items

### F1 Fit and Center keep the piece's shape

Bug: in the platform editor, Replace with Fit or Center (both Across the platforms and On each piece) leaves the part of the piece the picture does not cover transparent. The output size and the alpha mask are correct; the visible platform shrinks because the uncovered area is empty.

Fix: fill every pixel inside the piece's alpha shape that the placed picture does not cover with the piece's ORIGINAL art at that pixel (Q4). Fill and Stretch are unchanged.

Files: Core/Imaging/PieceFitter.cs, SpanFitter.cs, BackgroundFitter.cs (DestinationRect), tests PieceFitterTests.cs, SpanFitterTests.cs.

Tests: all four modes x both layouts: output size equals the piece, no pixel inside the shape left empty, the covered area is the picture and the uncovered area is the original.

### F2 Across the platforms / On each piece is always enabled

Today `CanUseFit => LoadedPicture is not null` (PlatformEditorViewModel.cs ~412) gates both the Across/Each radio pair and the Fill/Fit/Center/Stretch row (PlatformEditorWindow.xaml ~381-420). Only the Across/Each pair becomes always enabled; the choice is kept and used when a picture is picked. Opening the editor on a map with a saved replaced picture still shows that picture's saved choice. The Fill/Fit/Center/Stretch row stays gated.

### L1 Layouts in the catalog (with Q2 split)

Today MapCatalog.Build groups levels by AssetDir and unions set membership per folder (MapCatalog.cs ~257), so Tournament shows big World's End (NorseWinterFFA) and big Terminus (BP8ThreePlatformFFABig) while the game uses Norse1v1Spike "Small World's End" and BP8ThreePlatform "Small Terminus". 33 rows are wrong across T1/T2/T3/R1/R2 (game version 10.11).

New rule: one MapEntry per playable layout (level). A card carries its level, its own display name, its own sets, its own platform files and thumbnail. Folder-level things (the background, the art folder, applying a pack, the applied record, thumbnails written per folder) keep keying on FolderName. A folder with one layout is unchanged. Playable = has a LevelDesc and is not DevOnly; minigame arenas stay under Minigames as today. FrozenPlains (in Ranked1v1, DevOnly, no LevelDesc) is left out silently. With no level data the catalog falls back to folders exactly as today.

Every place that used FolderName as a card identity (selection, ScrollIntoView, LoadOrder, card reload after a write, hidden or applied lookups) must use a layout key where it means the card and FolderName where it means the art folder. Applying a pack to a card still writes that pack's files for the whole folder (packs are organised per folder); after a write every card of that folder reloads.

Files: MapCatalog.cs (MapEntry, Build, ByFolder callers), MapsViewModel.cs, MapCardViewModel.cs, tests MapCatalogTests.cs using the real 10.11 lists.

### L3 Tournament chips

Chips: All, Ranked 1v1, Ranked 2v2, Tournament 1v1, Tournament 2v2, Minigames. Labels from the game's own set names. A set chip matches layouts, so Tournament 1v1 shows Small World's End and Small Terminus, not the big ones.

Files: MapCatalog.cs (RankedSetNames, SetLabels, PickUiSets, UiSets).

### L2 Per-layout editing and the pack page per layout

The map panel for a layout card scopes its platform file list and the platform editor to that layout's files. Files two layouts share carry a quiet "also in <other layout>" mark. The background stays one per folder; the background editor says it is shared by every layout in the folder when the folder has more than one.

A pack's page shows one tile per layout (for folders the pack has files for); opening a tile's platforms shows only that layout's files.

Files: MapPanelViewModel.cs, PlatformEditorViewModel.cs, MapsView.xaml, PackDetailViewModel.cs.

### P1 Packs switch (Q1 rows-page-hide)

Three chips Both / Platforms / Backgrounds in the Packs header, left of Import folder, styled like the Maps chips. Both is the default and today's look.

- Platforms: row strips and the lead picture render maps with no background at all, pieces on the checkerboard. Packs with no platform files are hidden.
- Backgrounds: strips show the pack's background pictures cropped to fill. Packs with no backgrounds are hidden.
- The same switch sits on a pack's page and filters its grid: Platforms keeps map tiles drawn without backgrounds, Backgrounds keeps only background tiles.
- Arrow keys move inside the switch; Tab lands on the selected chip. Hidden-pack state is untouched. Empty library: the switch hides.

Files: PacksView.xaml, PacksViewModel.cs, PackRowViewModel.cs, PackDetailViewModel.cs, PackDetailView.xaml, AppSettings.

### P2 Maps switch

The same three chips at the right end of the Maps filter chip row. Platforms: cards draw only platform pieces on the checkerboard. Backgrounds: cards show the folder's background picture (active pack's when it has one, else the game's). The map side panel preview follows the setting. One setting shared with P1 (AppSettings), remembered across launches.

Checkerboard: two quiet greys near the tile colour (#2F2D2B family), squares about 12 px at card size; clearly different from the fine missing-art grid.

Files: MapsView.xaml, MapsViewModel.cs, MapCardViewModel.cs, MapCompositor (a no-background render path), AppSettings.

### O1 Seam-free opacity (Q5 always on)

Cause: PlatformRecolor scales each piece's straight alpha on its own. Overlapping pieces are then drawn twice at partial strength, which shows as bright lines. 109 of 120 layouts have overlaps.

Fix: when writing faded pieces, make each lower piece more transparent exactly where pieces above cover it, so the stack composites like one layer at the chosen opacity. Per layout pixel, top-down in draw order, with s_i the piece's native alpha and a_i its opacity:

    wanted weight of piece i = a_i * s_i * prod_{j above i} (1 - s_j)
    written alpha of piece i = wanted / prod_{j above i} (1 - written_j)

With equal opacity a this is s * a(1-t)/(1-a*t), t = 1 - prod(1 - s_j). Hue is unaffected. At 100% opacity nothing changes.

Coverage comes from the same transform walk MapCompositor uses (Scale, Rotate, Translate), mapped back into each piece's own pixels. Only overlaps within the same static group or the same moving platform are corrected. A piece used in more than one place (within or across layouts) is corrected only where every use agrees, else left as today. With no level data, pieces fade on their own as today and the status line says "Seam fix needs the game's level data". The editor preview uses the corrected files.

Files: PlatformRecolor.cs, PlatformPieceViewModel.cs (WriteResult), new Core/Imaging/SeamMask.cs, MapCompositor.cs (shared transform walk), tests (a render test that corrected pieces composite to the one-layer image within 1-2 levels per channel).

In-game check (owner): Shadowscar Landing (SmallDragon) at 50%.

## Build order

F1, F2, L1, L3, L2, P1, P2, O1, then version 3.2.0 and the manual's What is new, PR, merge, run-folder rebuild. No release.

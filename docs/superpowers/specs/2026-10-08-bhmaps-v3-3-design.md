# BhMaps 3.3 design

Review page: https://claude.ai/artifact/79PSLRX64uD3gL7fdoDAXR (db collections answers33, objections33).
Go given in chat on 2026-10-08 ("answeered"). No objections.

## Owner answers

- Q1 `prompt`: Add as new asks for the name first. The Rename-style text prompt opens with the next free New Pack N prefilled and selected; OK saves into that fresh pack.
- Q2 free text: "both should default to new pack ... we can just have custom packs be custom packs for bgs and platforms". Both editors default to a fresh New Pack N when they are not opened from a pack. My Backgrounds stays the picture library (Add image on the Backgrounds page is unchanged) but is no longer an editor default.
- Q3 `blur`: Hue, Saturation, Contrast, plus Blur. Darken stays.
- Q4 `start`: Start menu shortcut on by default, desktop shortcut off.
- Q5 `everywhere`: the mapArt wrapper rule applies to library packs, Import folder, and Import from another pack.

## Shared helper: PackNames.NextFree

`Core/Operations/PackNames.cs`: `NextFree(IEnumerable<string> existing, string stem = "New Pack")` returns "New Pack" when no pack with that name exists (case-insensitive), else "New Pack 2", "New Pack 3", ... the first number not taken. Tests in `PackNamesTests.cs`.

Callers pass the snapshot's pack names plus the folder names on disk under `PackScanner.PacksRoot`, so a folder that exists but is not in the snapshot still counts as taken.

## Items in build order

### E3 New pack... works again

Bug (reproduced): with My Backgrounds present, choosing "New pack..." in the platform editor sets NewPackName to "" (PlatformEditorViewModel ctor ~L175), PackNameError becomes "Pack name is empty.", both Save buttons disable, and the name TextBox plus error line render below the 720-tall window's visible area.

Fix, both editors:

- The "Save into pack" control becomes one editable ComboBox (`IsEditable="True"`, `Text` bound two-way with `UpdateSourceTrigger=PropertyChanged`, `ItemsSource` = pack names, no "New pack..." item). The dropdown picks an existing pack; typing makes a new one. `TargetPack`, `IsNewPack`, `NewPackName` and `NewPackChoice` collapse into one `PackName` string property. `IsNewPack` = no existing pack has that name (case-insensitive). `EffectivePackName` = `PackName.Trim()`.
- A hint line directly under the box (HintText style, always inside the panel, never below the fold): "Type a name, or open the list to pick a pack." when the name is new and valid. An error line (ErrorText style) in the same place shows the PackNameValidator message when the name is invalid or empty; Save only and Save and apply disable only then.
- `EnsurePackFolder` keeps creating the folder through PackCreator for a new name. `PackChoices` gains the new name after the first write so later writes in the same session treat it as existing.
- Background editor: same control, same rules. `OverwriteHint` stays.

Files: PlatformEditorWindow.xaml (~L520-531), PlatformEditorViewModel.cs (ctor ~L162-176, L286-292, L471-481, L1430-1465, L1658-1670), BackgroundEditorWindow.xaml (~L325-333), BackgroundEditorViewModel.cs (ctor L66-90, SaveAsync L384), every other reader of TargetPack / IsNewPack / NewPackChoice (grep the App project).

### E2 Both editors default to a fresh New Pack N (Q2)

- Platform editor opened without a pack and with no map in the set that remembers a source pack: `PackName` = `PackNames.NextFree(...)`. Opened from a pack tile, or with a map whose files came from a pack (the ValuesFrom rule, spec 5.3): that pack, as today.
- Background editor opened without `request.PackName`: `PackName` = NextFree. With `request.PackName` (the tile's own pack): that pack, as today.
- `BackgroundEditorViewModel.DefaultPackName` ("My Backgrounds") stays for AddPicturesViewModel and the picture library; the two editors stop reading it as a default.

Files: PlatformEditorViewModel.cs (ctor ~L172-176), BackgroundEditorViewModel.cs (ctor L80-82).

### E1 Add as new (Q1 prompt)

- Third footer button "Add as new" between Cancel and Save only, OutlineButton style, `AddAsNewCommand`, enabled whenever the editor has something to save (the CanSave rule without the pack-name check: a bad name in the box must not block it, because it asks for its own name).
- Click: `Dialogs.PromptText("Add as new", "Pack name", PackNames.NextFree(existing))`. TextPromptWindow already selects the value on open. Null = cancelled, nothing happens. Validate with PackNameValidator and refuse a name an existing pack holds ("A pack called X already exists."); on either error show it with `Dialogs.Error` and return.
- Then set `PackName` to the chosen name and run the Save only path (no apply; no Replace prompt because the pack is fresh). The editor closes as after Save only and the shell's existing post-save rescan runs.
- Tooltip: "Save into a fresh pack with its own name."

Files: PlatformEditorWindow.xaml footer (~L560-575), PlatformEditorViewModel.cs (AddAsNewCommand near SaveOnlyAsync L1655), BackgroundEditorWindow.xaml footer (~L336-351), BackgroundEditorViewModel.cs (near SaveOnlyAsync L381).

### P1 Rename pack

- `Core/Operations/PackRenamer.cs`: `TryRename(libraryPath, oldName, newName, out error)`: PackNameValidator, refuse when another pack holds newName (case-insensitive, same check as PackCreator; a case-only change of the same pack is allowed and goes through a temporary name), `Directory.Move` of the outer pack folder under packs\. Tests `PackRenamerTests.cs`.
- PacksViewModel.BuildMenu: "Rename..." right after Duplicate, not for the Default pack. F2 on a focused pack row runs the same command.
- MainViewModel.RenamePackAsync(pack): `Dialogs.PromptText("Rename pack", "Name", pack.Name)`; same name or null = nothing. On success: HiddenPackNames swaps old for new when present; the applied record (Core/Operations/AppliedRecord.cs) swaps the pack name wherever it names the old pack; PackLastApplied and DismissedTransparentNotes keys move too. Then RescanAsync. Status line "Renamed X to Y" with Undo that renames back with the same swaps (use the app's existing undo mechanism for status lines; if it cannot carry a custom action, a plain Done line without Undo is acceptable and must be reported).
- Errors shown with Dialogs.Error.

Files: PacksViewModel.cs (BuildMenu L207-230, RenameCommand), MainViewModel.cs (RenamePackAsync next to NewPackAsync L1020), new Core/Operations/PackRenamer.cs, Core/Settings/AppSettings.cs (a `WithPackRenamed(old, new)` helper), Core/Operations/AppliedRecord.cs, tests.

### P2 A pack folder that wraps a mapArt folder (Q5 everywhere)

Rule: a pack folder with a direct child named `mapArt` (any case) is read with that child as its content root. The pack keeps the outer folder's name. A folder named `mapArt` placed directly under packs\ is a pack called mapArt.

- `Core/Scanning/PackScanner.cs`: `ContentRoot(packFolder)` returns `packFolder\mapArt` when that directory exists, else `packFolder`. `ScanPack` builds the Pack from the content root. The `Pack` record (Core/Model/Pack.cs: Name, FullPath, Folders) gains `FolderPath` (the outer folder: Delete, Export, Open folder, Rename, Duplicate's source) while `FullPath` becomes the content root (every file read and write). Check every `FullPath` reader and pick the right one.
- `PackScanner.PackRootFor(libraryPath, packName)`: `packs\<name>\mapArt` when that directory exists, else `packs\<name>`. Every place that builds a pack root from a name (`Path.Combine(PackScanner.PacksRoot(...), name)`: both editors, ImportRouter.Execute, PackCopier, CustomPictureLibrary, AddPictures, PackDetail, thumbnails, PlatformRecolor callers) goes through it, so writes into a wrapped pack land under mapArt and the folder stays droppable into the game.
- Import folder (MainViewModel.ImportAsync ~L326): when the picked folder has a `mapArt` child, the default pack name is the picked folder's name and the plan walks the child. Import from another pack (ImportFromPackAsync ~L1057): the same when the picked folder has a `mapArt` child.
- Packs page: a pack named mapArt directly under packs\ gets a quiet status note once per start: "mapArt is a pack name; rename it to what the maps are." (Status.Note).
- Tests: PackScannerTests with a `packs\anything\mapArt\BloodMoon` tree: one pack "anything", FullPath ends in mapArt, FolderPath is the outer folder; a plain pack is unchanged; PackRootFor both ways.

Files: PackScanner.cs, Core/Model/Pack.cs, PackDeleter.cs, PackExporter.cs, PackCopier.cs, ImportRouter.cs, MainViewModel.cs (ImportAsync, ImportFromPackAsync, OpenFolder), PacksViewModel.cs, both editors, tests.

### L1 In-place row update after a save

- `RowsPageViewModel.UpdateFolders(ScanSnapshot snapshot, IReadOnlyList<string> folders)`: set Snapshot; re-point every existing row at the new card object of the same key when rows hold the card; for each row whose FolderName is in `folders` (case-insensitive) build a new row with BuildRow and assign it at the same index in `_all` and in `Rows` (`Rows[i] = newRow`, no Clear, no cancel of the other rows' loads); realise it if the old one was realised. A folder in `folders` with no row yet (a map that gained art) or a row whose folder vanished: fall back to the full `Refresh(snapshot)` silently. Then RebuildMenus and the hidden-pack counters as Refresh does.
- `MainViewModel.RescanAsync(writtenFolders)`: when `writtenFolders` is non-empty, call `page.UpdateFolders(snapshot, writtenFolders)` on every page; pages that do not override it fall back to their Refresh (PageViewModel virtual). Maps page keeps its existing Refresh(snapshot, writtenFolders). Packs and PackDetail keep Refresh (a save into a new pack must add the row; their lists are short).
- `ThumbnailCache.Evict(IReadOnlyList<string> folders)`: forget decodes whose path contains `\<folder>\` for a written folder, plus every path under a `Backgrounds` folder when a background was written (pass "Backgrounds" as a folder). RescanAsync uses it in place of Clear when `writtenFolders` is given.
- Callers: platform editor Saved path passes `saved.Maps` folder names (already); background editor Save only (MainViewModel ~L857) passes the slot's maps' folder names plus "Backgrounds" when a slot was saved and null for an all-maps picture; the custom picture tile, pack detail and other RescanAsync() calls stay full.
- Row visual while recomposing: the replaced row starts with its tiles at 45 percent opacity until its first image arrives. A 2 px progress line at the top edge of the row card is welcome if the row template makes it cheap; dimming alone is enough otherwise.

Files: ViewModels/Pages/RowsPageViewModel.cs (142-200, 279-286), PageViewModel.cs, MainViewModel.cs (1334-1352, ~857, 1187, 1760), Services/ThumbnailCache.cs, PlatformsViewModel.cs, BackgroundsViewModel.cs, the row DataTemplate for the dim, tests where a Core-level test is possible.

### E4 Hue, Saturation, Contrast, Blur in the background editor (Q3)

- Sliders: Hue (-180..180, step 1, default 0, readout "+12" / "-30" / "0"), Saturation (-100..100, 0), Contrast (-100..100, 0), Blur (0..100, 0, plain number readout), Darken unchanged. Each has its own Reset link like Pan X.
- Layout (mock p-bg-editor.png): Pan X and Pan Y keep the full width. Then two columns of 172 px: Hue | Saturation, Contrast | Darken. Blur full width under them. The pack box stays where it is; the panel must not grow past the window.
- `FitOptions` gains `Hue` (degrees), `Saturation` (-1..1), `Contrast` (-1..1), `Blur` (0..1). `BackgroundFitter.Render` applies, in order: the fit draw, blur, hue/saturation/contrast per pixel, then the Darken shade. Blur = `BlurEffect` on the DrawingVisual with `Radius = Blur * 40 * (width / OutputWidth)` so the preview matches the full-size output. Hue, saturation and contrast run on the rendered pixel buffer: hue rotation and saturation through the HSL helpers already in PlatformRecolor (move `ToHsl` / `ToRgb` into a shared `Core/Imaging/ColorMath.cs` and have PlatformRecolor call it); saturation `s' = clamp(s * (1 + Saturation))`; contrast `v' = clamp((v - 128) * k + 128)` with `k = Contrast >= 0 ? 1 + Contrast * 2 : 1 + Contrast`. Skip the pixel pass when all three are 0 and the effect when Blur is 0.
- `BackgroundSlotEntry` gains `Hue`, `Saturation`, `Contrast`, `Blur` as nullable numbers; old records read as 0. `PictureFits.Options` takes the four. Reopening a saved background restores them like Pan does.
- Tests: BackgroundFitterTests for each slider on a flat-colour source (hue 180 turns red to cyan, saturation -1 greys it, contrast 0 is identity, blur 0 is identity, blur > 0 softens an edge between two colours), EditRecordTests round-trip of the four fields.

Files: Core/Imaging/BackgroundFitter.cs (FitOptions L16, Render L141-172), new Core/Imaging/ColorMath.cs, Core/Imaging/PlatformRecolor.cs (L117-150), Core/Packs/BackgroundEditRecord.cs (BackgroundSlotEntry), Core/Operations/PictureFit.cs, BackgroundEditorViewModel.cs (L133, L219, L245, L263-274, L303, L401-428), BackgroundEditorWindow.xaml (L250-320), tests.

### O1 Shortcuts (Q4 start on, desktop off)

- Welcome window step 4 "Shortcuts": text "Where Windows can find BhMaps. The exe stays where it is; a shortcut only points at it." Two CheckBoxes: "Add to the Start menu" (sub text "Shows BhMaps in Start and in Windows search.", default on) and "Desktop shortcut" (sub text "A BhMaps icon on the desktop.", default off). Same StepGrid style as steps 1-3. Window height stays under 720.
- `Services/Shortcuts.cs` (App project; use the Windows Script Host `WScript.Shell` COM object through `Type.GetTypeFromProgID` and `dynamic`, no new package): `StartMenuPath` = `%APPDATA%\Microsoft\Windows\Start Menu\Programs\BhMaps.lnk`; `DesktopPath` = `Environment.GetFolderPath(SpecialFolder.DesktopDirectory)\BhMaps.lnk`. `Exists(path)`, `Create(path)` (target = `Environment.ProcessPath`, icon = the exe, working folder = the exe's folder), `Remove(path)`, `RepointIfStale(path)` (a .lnk whose target no longer exists is rewritten to the running exe). Every call returns an error message or null, never throws to the caller.
- Finish: after the paths are saved and before the capture, create the ticked shortcuts. A failed write does not stop Finish; the main window's status line then says which shortcut was not made (Status.Error with a retry).
- Settings page: a "Shortcuts" row with the same two boxes, read from `Exists` on page refresh; ticking creates, unticking removes.
- Start-up: `RepointIfStale` for both paths. A dev run (an `--appdata` override) never touches shortcuts: the welcome boxes still show but Finish skips the writes, Settings shows the boxes disabled with the hint "Not in a dev run".
- AppSettings: no new fields; the files on disk are the state.
- Tests: none in Core (COM); a manual check on a throwaway appdata is not possible because dev runs skip writes, so the manual check is against the real run folder once, by the owner.

Files: Views/WelcomeWindow.xaml (after step 3, ~L123-145), ViewModels/WelcomeViewModel.cs (two bool properties, FinishAsync L103), new Services/Shortcuts.cs, ViewModels/Pages/SettingsPageViewModel.cs, Views/Pages/SettingsPageView.xaml, App.xaml.cs start-up.

## Verification

- E3: dev tree, open the platform editor on a card, pick a pack from the list, type a new name, clear it (Save greys, error under the box), type again (Save back). Background editor the same.
- E2: open the editor from a card not in a pack: box reads New Pack N; from a pack tile: that pack.
- E1: Core test for PackNames.NextFree; Add as new twice on the same map gives New Pack and New Pack 2 with the right files and records.
- P1: rename a hidden, in-game pack; it stays hidden and In game under the new name.
- P2: Core tests with a packs\anything\mapArt tree; the Packs page shows "anything" with its maps; an edit saved into it lands under mapArt.
- L1: save a platform set; the list keeps its scroll and only that row's tiles change.
- E4: slider tests above; a saved background reopens with the same values.
- O1: welcome shot against the mock; Settings row present.

Tests run with the dev tree only, never the real game folder, real library or real %APPDATA%\BhMaps.

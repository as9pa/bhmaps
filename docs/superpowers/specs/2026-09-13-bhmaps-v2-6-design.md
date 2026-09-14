# BhMaps 2.6 design: pack tile menus, copy and move, Import from pack, Duplicate, auto-update, thumbnails

Date: 2026-09-13
Base: main 9d90568 (BhMaps 2.5.0)
Author: Claude Fable 5.1 with the owner's answers
Review page: https://claude.ai/code/artifact/a17ff296-3de8-49f8-960e-b1add117cabf (items 1.1 to 5.1, seven questions, all answered, no objections). The owner's "answers are in" is the go.

## 1. What 2.6 is

| Item | Change | Owner's decision |
| --- | --- | --- |
| 1.1 | Every pack detail tile has a menu, map tiles included | As proposed |
| 1.2 | Copy to pack... and Move to pack..., plus Ctrl+C / Ctrl+X / Ctrl+V | Q3: menu lines and the keys |
| 1.3, 3.2 | Import from pack button on pack detail, Import from another pack... in the Packs row menu | As proposed |
| 2.1 | Import from pack dialog: source pack, all maps or chosen maps, Replace checkbox | Q4: skip existing by default, Replace opt in |
| 3.1 | Duplicate in the Packs row menu | Q5: automatic name "{name} copy", no prompt |
| 4.1 | Auto-update from GitHub releases | Q1: the bhmaps repo goes public (history scrubbed first, separate step outside this spec). Q2: top bar line "New update available" with a dismiss, plus the Settings Version row |
| 5.1 | Map-select thumbnails: some ranked maps stay stock | Q6: owner's list of maps; diagnosis pending, fix in this release |

Nothing here removes a 2.5 behaviour. Packs from 2.1 onward open as they are.

## 2. Vocabulary

- **Map tile**: a pack detail tile for a map the pack has a folder with files for (`PackDetailViewModel.MapsIn`). Its `PicturePath` is null unless the pack's `Backgrounds` folder has a jpg the map owns (`MapForSlot`).
- **File tile**: a tile for a `Backgrounds` jpg no map owns.
- **A map's files in a pack**: the map's folder under the pack root (every file in it), plus every `Backgrounds\<slot>.jpg` whose slot `MapForSlot` assigns to this map, plus the map's entries in `platforms.bhmaps.json` (keyed by map folder, `PlatformEditRecord.Map(mapFolder)`) and `backgrounds.bhmaps.json` (keyed by relative path, `BackgroundEditRecord.Entry(relativePath)`, for each of those files).
- **Target pack**: the pack receiving a copy, move or import. **Source pack**: where the files come from.
- **App clipboard**: one remembered tile (source pack, map or file, cut or copy), held by the shell, never the Windows clipboard.
- **Release**: a GitHub release of as9pa/bhmaps whose tag is `vX.Y.Z` and whose assets are `bhmaps-vX.Y.Z-win-x64.exe`, `bhmaps-vX.Y.Z-win-x64-dotnet.zip` and `SHA256SUMS.txt`.

## 3. Pack detail tile menu (1.1)

`PackDetailViewModel.BuildTileMenu(tile)` always produces a menu. The dots button always opens it; the "no items, return" branch in `PackDetailView.xaml.cs` goes away, as does the `e.Handled` when empty in `OnTileMenuOpening`.

Map tile menu, in this order:

1. Header: the map's display name (not a command).
2. `Apply to {map}`: `Shell.ApplySetAsync(pack, [map], clearTicks: false)`. Gated on CanWrite like Apply all.
3. `Copy to pack...` (Ctrl+C shown as the gesture text).
4. `Move to pack...` (Ctrl+X).
5. `Show in folder`: opens the map's folder in the pack.
6. Separator.
7. `Remove from {pack}`: deletes the map's files in the pack (section 2's definition). Undoable through the library side of an undo session. Confirmation as Remove for file tiles does today, if it asks; otherwise none.

Map tile whose pack also has an owned Backgrounds jpg: the existing picture lines stay (Apply to a map..., Apply to all maps, the ticked apply) and come after `Apply to {map}`; Copy and Move are added before Show in folder.

File tile menu: unchanged, plus `Copy to pack...` and `Move to pack...` after `Edit`.

## 4. Copy and move (1.2)

### 4.1 Core: `BhMaps.Core/Packs/PackCopier.cs`

Static class. Every operation works on paths only; no UI types.

- `PackCopyResult CopyMap(Pack source, Pack target, MapEntry map, MapCatalog catalog, bool replace)`: copies the map's files (section 2) into the target under the same relative paths and merges the record entries into the target's two record files (load target record, add or overwrite the entries, save). If the target already has the map's folder and `replace` is false: returns `Skipped` with nothing written. If `replace` is true: the target's copy of the map's files is deleted first, then written. Backgrounds slot jpgs that the target already has for that slot follow the same replace rule.
- `PackCopyResult CopyFile(Pack source, Pack target, string relativePath, bool replace)`: one Backgrounds jpg plus its background record entry.
- `PackCopyResult MoveMap(...)` and `MoveFile(...)`: CopyMap or CopyFile, then delete from the source (files and record entries). Not performed when the copy was skipped.
- `string DuplicatePack(string libraryPath, string name)`: copies the whole pack folder to a free name and returns it. Free name: `"{name} copy"`, then `"{name} copy 2"`, `"{name} copy 3"`, ... (case-insensitive check against existing folders under the packs root). On any failure the half-written folder is deleted and the exception rethrown.
- `PackCopyResult`: `record(IReadOnlyList<string> Written, IReadOnlyList<string> Removed, bool Skipped, IReadOnlyList<FileFailure> Failures)`. Paths in `Written` and `Removed` are library-relative (`Packs\<pack>\...`), the form `UndoSession.CaptureLibrary` takes.
- Removing a map from a pack (section 3, item 7) uses the same delete routine: `PackCopier.RemoveMap(Pack pack, MapEntry map, MapCatalog catalog)` returning the removed relative paths.

The Default pack is a valid source and a valid target for copy; Move out of and Remove from the Default pack are not offered (its menu never shows them).

### 4.2 Undo

Before a move, a remove or an import the shell begins an undo session and `CaptureLibrary`s every path the operation will write or remove (source and target). Undo restores them the way 2.5's Reset undo does. A plain copy does not get an undo session; its toast says so by offering `Open {target}` instead of Undo.

### 4.3 App

- Pack chooser: `Shell.ChoosePackAsync(string title, Pack exclude)` shows a small modal list of the packs except `exclude`, in the same style as the map chooser (`ChooseMapAsync`), with a `New pack...` line last that runs the existing new-pack flow and returns the created pack. Returns null on cancel.
- `Copy to pack...`: chooser titled `Copy {name} to`; then `CopyMap` or `CopyFile` with `replace: false`. If `Skipped`: a small confirm `"{target} already has {name}. Replace it?"` with Replace and Cancel; Replace repeats with `replace: true` inside an undo session (the target's old copy is captured).
- `Move to pack...`: chooser titled `Move {name} to`; same skip and replace flow; always inside an undo session.
- After any of these: `Shell.RescanAsync` with the written folders, and the done line as the existing DoneUndoable path shows it: `"{name} copied to {target}"` (with Open {target}), `"{name} moved to {target}"` (with Undo).
- App clipboard: `MainViewModel.PackClipboard` holding `(Pack source, PackTileViewModel tile, bool cut)`; set by Ctrl+C and Ctrl+X on the focused or hovered tile in pack detail (the tile under the pointer wins if there is one, else the keyboard-focused tile); Ctrl+V on a pack detail page pastes into that page's pack (copy or move by `cut`, same skip and replace flow, clipboard cleared after a cut is pasted). Ctrl+V with nothing held: the page's status line shows `Nothing copied yet` for two seconds. Ctrl+V into the source pack itself: nothing happens.
- Keys are `KeyBinding`s on `PackDetailView`, active only when the page is shown and no dialog is open.

## 5. Import from pack (2.1, 1.3, 3.2)

### 5.1 Core

- `PackImportPlan(Pack source, Pack target, IReadOnlyList<MapEntry> maps, IReadOnlyList<string> looseFiles, bool replace)`.
- `PackImportResult PackCopier.Import(PackImportPlan plan, MapCatalog catalog, IProgress<string>? progress, CancellationToken ct)`: `CopyMap` for each map and `CopyFile` for each loose file, in plan order, collecting `Written`, `Removed`, `Skipped` (names), `Failures`. Cancellation stops between items; what was copied stays. Progress lines: `"Importing 3 of 12: Kings Pass"`.

### 5.2 Dialog `Views/Dialogs/ImportFromPackDialog.xaml`

Modal in the map chooser's style. Title `Import into {target}`. Subtitle: `Copies map folders, their pictures and their editor values. The source pack is not changed.`

- `From`: a list (or combo) of every pack but the target, each with `{n} maps, {m} backgrounds`. Default selection: the first pack that is not the target. When there is no other pack the button that opens the dialog is disabled with tooltip `No other pack to import from`.
- `Which maps`: radio `All {n} maps` / `Choose maps`. Choose shows a tick list: preview, display name, `"{k} files"`, or `already here` in `MissingFgBrush` when the target has the map's folder. Rows already in the target start unticked; others ticked. Loose Backgrounds files no map owns are listed under a `Pictures` divider with the file name.
- Checkbox `Replace maps {target} already has`, off by default. Off: "already here" rows are skipped even when ticked (and their tick box is disabled). On: they are enabled and replace.
- Primary button `Import {k} maps` (k counts ticked, importable rows; `Import 1 map`; `Nothing to import`, disabled, when k is 0). Cancel.
- While importing: the primary button reads `Importing 3 of 12...`, Cancel stays, rows tick off. Done: dialog closes, done line `"{k} maps imported into {target}"` with Undo. Partial failure: `"2 of 3 imported. Kings Pass: file in use."`.
- Keyboard: Space ticks, Enter imports, Esc cancels. First focus on From.

### 5.3 Entry points

- Pack detail header: outline button `Import from pack` with `Icon.Plus`, before Apply all. Command `ImportFromPackCommand` on `PackDetailViewModel`.
- Packs row menu: `Import from another pack...` as the second line (section 6).

## 6. Packs row menu and Duplicate (3.1, 3.2)

`PacksViewModel.BuildMenu(row)` becomes: `Duplicate`, `Import from another pack...`, `Export`, `Open folder`, separator, `Remove`. The Default pack's menu omits Remove as today (check the current rule and keep it).

Duplicate: `PackCopier.DuplicatePack` off the UI thread, then `RescanAsync`. Done line `"{name} duplicated as {copy}"` with Undo (the undo session captures the copy's paths as absent, so Undo deletes the folder). Failure: `"Could not duplicate {name}: {reason}"`.

## 7. Auto-update (4.1, 4.2)

### 7.1 Core: `BhMaps.Core/Update/`

- `ReleaseInfo(Version Version, string TagName, DateTimeOffset PublishedAt, string HtmlUrl, string? ExeUrl, long ExeSize, string? ChecksumsUrl, string Body)`.
- `static ReleaseInfo? ReleaseChecker.Parse(string json)`: reads the GitHub `releases/latest` JSON (`tag_name`, `published_at`, `html_url`, `body`, `assets[].name/browser_download_url/size`). Tag `vX.Y.Z` parses to `Version`; a tag that does not parse returns null. Drafts and prereleases return null.
- `static bool ReleaseChecker.IsNewer(ReleaseInfo release, Version current)`: three-part compare.
- `sealed class UpdateClient(HttpClient http)`: `Task<ReleaseInfo?> CheckAsync(CancellationToken)` GETs `https://api.github.com/repos/as9pa/bhmaps/releases/latest` with `User-Agent: BhMaps/{version}` and `Accept: application/vnd.github+json`, 10 s timeout, returns null on any failure (no throw). `Task<string> DownloadAsync(ReleaseInfo release, string updatesDir, IProgress<(long done, long total)> progress, CancellationToken)`: streams the exe to `updatesDir\bhmaps-vX.Y.Z-win-x64.exe.partial`, downloads `SHA256SUMS.txt`, verifies the exe's SHA-256 against the line naming it (format `<hex>  <filename>`, sha256sum style), renames `.partial` to the final name, returns the path. A checksum mismatch deletes the file and throws `InvalidDataException`.
- `static string UpdateInstaller.WriteApplyScript(string updatesDir, string newExe, string runningExe, int pid)`: writes `apply-update.cmd` that waits until the process `pid` is gone (`tasklist /FI "PID eq {pid}"` loop with a 1 s `timeout`), renames `runningExe` to `BhMaps.exe.old`, moves `newExe` to `runningExe`, starts it, deletes `BhMaps.exe.old` and itself. Returns the script path. Tests check the script text, not its execution.
- `static bool UpdateInstaller.CanSwap(string exePath)`: the exe's folder is writable (create and delete a probe file) and the build is self-contained (`RuntimeEnvironment.GetRuntimeDirectory()` starts with `AppContext.BaseDirectory`). False means the button falls back to `Open release page`.

### 7.2 Settings

`settings.json` gains `checkForUpdates` (bool, default true), `lastUpdateCheck` (ISO timestamp or null), `dismissedUpdate` (tag string or null).

### 7.3 App

- On start, when `checkForUpdates` is on and `lastUpdateCheck` is older than 24 h (or null): `CheckAsync` off the UI thread, 5 s after the first scan finished, never blocking anything. Result stored on the shell as `AvailableUpdate` (`ReleaseInfo?`) and `lastUpdateCheck` written.
- Top bar: when `AvailableUpdate` is newer than the running version and its tag is not `dismissedUpdate`, a line `New update available` (PlainButton, Text2 colour, before the Brawlhalla state) that navigates to Settings, with a small dismiss `x` beside it that sets `dismissedUpdate` to the tag. A later release shows the line again.
- Settings, Version row: first line the running version. Second line: `2.6.0 is available. Released 14 Sep 2026. Update downloads the new exe and swaps it in when you close BhMaps.` with buttons `Update to 2.6.0` (PrimaryButton) and `What changed` (PlainButton, opens `HtmlUrl`). Up to date: `You have the latest version. Checked today, 15:40.` Never checked: `Not checked yet.` Failure: `Could not reach GitHub. Try again later.`
- New Settings row `Updates` under Version: checkbox `Check for updates when BhMaps starts`, the line `Once a day, one small request to github.com. Nothing is sent about you or your library. Last checked {when}.`, button `Check now`.
- Update button states: `Update to X` runs `DownloadAsync` to `%APPDATA%\BhMaps\updates` (the app data dir the shell already knows) with the second line `Downloading 2.6.0, 41 of 135 MB` and a thin progress bar and Cancel; done: `2.6.0 is ready. It installs when you close BhMaps.` and the button `Close and update`, which writes the apply script, starts it hidden (`WindowStyle.Hidden`, `UseShellExecute: false`) and closes the app. If `CanSwap` is false the primary button reads `Open release page` from the start and the line ends with `Download the new version from the release page.`
- Never: no download without the button, no restart on its own, no token in the app, no dialog on start.

### 7.4 Release side

`scripts/publish.ps1` writes `dist\SHA256SUMS.txt` with one `<sha256 lowercase hex>  <filename>` line per release file. The release step uploads it as a third asset. The manual's install page mentions the update check and how to turn it off.

## 8. Thumbnails (5.1)

The owner's ranked 1v1 list: stock thumbnail after an apply with the switch on for small brawlhaven, twilight grove, small enigma, small mammoth fortress, small great hall, shipwreck falls, miami dome, small fangwild, demon island, crystal temple, small galvan prime, spirit realm, small turtles lair, suzaku castle, small fabled city, florence rooftop, western air temple, mishima dojo, small terminus, small worlds end, crumbling chasm, small wasteland. Custom thumbnail for plains of passage, apocalypse, small fortress of wolves, shorwind fishing port, bikini bottom, mos eisley spaceport, lichs tomb, jikoku, shadowscar landing, elysium, shreks swamp.

Cause, confirmed against the real level data cache and a read-only listing of the game's `images\thumbnails`: every skipped map's folder holds two or three levels that name different thumbnail jpgs (the ranked "Small X" level and the casual "X" level share one mapArt folder, for example `Fortress` names `Mammoth.jpg` and `MammothSmall.jpg`; `GreatHall` names three). `MapCatalog.Thumbnails()` sets `ThumbnailFile` only when all included levels of a folder agree, so these maps get null and `ThumbnailWriter.Plan` skips them with `NoFile`. Every jpg exists. The eleven maps that worked have exactly one candidate. No cross-folder sharing was found in this list.

Fix: a map owns every thumbnail candidate that no other map's folder also names. `MapEntry` gains `ThumbnailFiles` (`IReadOnlyList<string>`, the owned candidates, in level order); `ThumbnailFile` stays as the first entry or null for callers that need one. `ThumbnailWriter.Plan` writes the same rendered picture over each owned file, keeps the original of each in `thumbnails-original` under the same file name, and restores each on Reset, Undo and switch off. Candidates that another folder also names stay skipped with the existing "shares its thumbnail with ..." note; the `NoFile` skip remains only for folders with zero candidates. A map with two owned files and one missing writes the one that exists and notes the missing one.

## 9. Not in 2.6

Windows clipboard, drag and drop between packs, Rename, multi-select in pack detail, an update dialog on start, downloads for the dotnet zip build.

## 10. Global constraints

- .NET 10, WPF, TreatWarningsAsErrors. `dotnet format BhMaps.slnx` is the only formatter. Tests only in `tests\BhMaps.Core.Tests`; every Core type above gets tests (PackCopier on temp folders, ReleaseChecker on saved JSON strings, UpdateInstaller script text and checksum parsing, UpdateClient with a fake `HttpMessageHandler`).
- No network in tests. No test touches the real game folder, the real library or the real `%APPDATA%\BhMaps`.
- Copy in the UI: sentence case, no em-dashes, no emoji. Docs CRLF UTF-8 without BOM.
- Every library write outside a plain copy goes through an undo session.
- Version 2.6.0 in `BhMaps.App.csproj`; README and manual updated in the release task.

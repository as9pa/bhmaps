# BhMaps design

Date: 2026-09-08
Status: approved

## 1. Goal and scope

BhMaps is a Windows desktop app for managing Brawlhalla map art. It lets the user swap, import, reset, and create the image files the game reads from its `mapArt` folder.

Default paths:

| Setting | Default |
| --- | --- |
| Game dir | `C:\Program Files (x86)\Steam\steamapps\common\Brawlhalla\mapArt` |
| Library dir | `C:\Users\alexa\files\bh` |

Both are editable in Settings.

In scope:

- Show every game folder with a thumbnail and which pack is currently applied.
- Apply a pack to the game: whole pack, one folder, or one file.
- Reset a folder, or everything, back to game defaults.
- Import an arbitrary folder of images into a new pack, routing each file to the right game folder.
- Snapshot the current game folder as a pack.
- Create a background from any image: fit it to 2048x1151, darken it, save it into a pack, and apply it.

Out of scope:

- Painting or editing platform art pixel by pixel.
- `.bmod` files and the Brawlhalla Mod Loader.
- Animated backgrounds.
- Any file outside `mapArt`.
- Guessing which background belongs to which map. The `Backgrounds` folder is treated as its own folder of 67 named slots. Names like `BG_Fjord.jpg` and `BG_Golem.jpg` match no map folder, so no mapping is attempted.

## 2. Facts about the game tree

Measured on 2026-09-08. The app must not hard-code these numbers; it scans the folder at runtime. They are here so the implementer knows the shape of the data.

- 68 immediate subfolders under `mapArt`. Every one is exactly one level deep. There is no nesting.
- 824 files total. Every file is `.png` or `.jpg`. There are no files at the `mapArt` root.
- `Backgrounds` holds 67 JPGs, all 2048x1151.
- Platform art is PNG with varied dimensions, from about 200 px to about 1000 px on a side.
- Filenames are unique across folders except four:

| Filename | Folders |
| --- | --- |
| `Lava3_Bottom.png` | BrawlFest, Mustafar |
| `Lava3_Top.png` | BrawlFest, Mustafar |
| `LeftWall.png` | BP8, Zombie |
| `MainPlat.png` | BP8, Tekken |

Key assumption, stated by the user: if a file is deleted from a `mapArt` folder, the game writes the default file back on next launch. The Reset feature relies on this. The app never needs a copy of the vanilla files.

Folder list, sorted:

```
AOT, ATLA, Backgrounds, Batavia, BattleHill, Blackguard, BloodMoon, Bombsketball,
BP11, BP12, BP13, BP5, BP6, BP7, BP8, BP9, Brawlball, BrawlFest, Brawlhaven, Buddy,
Climb, CrystalTemple, DemonIsland, Dojo, Elysium, Enigma, Fangwild, Fortress,
FortressOfWolves, GreatHall, Grove, Halloween, Halo, Horde, HordeTwo, KFP, KingsPass,
LostLabyrinth, MiamiDome, MosEisley, Mustafar, NordicWinter, Refinery, Ring, Rooftop,
Seven, Sewer, ShipwreckFalls, Snow, Soccer, Soccer4, Space, Spongebob, Stadium,
StreetFighter, StreetFighter2, Swamp, Synthwave, Tekken, Temple, Test, ThreeShips,
TitansEnd, TreeHouse, Tutorial, VolleyBattle, WarShuttle, Zombie
```

## 3. Storage layout

### Packs

A pack is a folder at `<library>\packs\<packname>\`. Inside, it mirrors the game tree exactly:

```
<library>\packs\<packname>\<GameFolder>\<file>.png
<library>\packs\<packname>\Backgrounds\BG_x.jpg
```

A pack may contain any subset of game folders, and any subset of files within a folder. A pack with one file is valid. The pack name is the folder name. Any folder directly under `packs\` is a pack. Files at the pack root, or nested deeper than `<GameFolder>\<file>`, are ignored.

The app reads and writes only under `<library>\packs\`. Everything else in the library folder (the user's existing `flowermap`, `black bgs`, `b&w maps`, `bh modloader` folders) is never read, moved, or written. The user brings those in through Import when they want to.

### App data

App data lives at `%APPDATA%\BhMaps\`:

- `settings.json`

  ```json
  {
    "gamePath": "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Brawlhalla\\mapArt",
    "libraryPath": "C:\\Users\\alexa\\files\\bh",
    "firstRunDone": false
  }
  ```

- `hashcache.json`: a map from full file path to `{ size, mtimeTicks, sha256 }`. Entries whose size or mtime no longer match the file on disk are recomputed and replaced. Missing files are dropped from the cache on the next save.

Both files are written atomically: write to a temp file in the same directory, then move over the target.

### First run

When `firstRunDone` is false and the game path is valid, the app shows one dialog offering to snapshot the current game folder as a pack named `backup-YYYY-MM-DD` (today's date). Yes runs Import against the game dir with that name. No skips it. Either way `firstRunDone` becomes true and the dialog never shows again. The user can do the same later with Save Current.

## 4. Core operations

Every operation below is its own class in `BhMaps.Core` with a small public surface. None of them reference WPF UI types. They take paths and options and return plain result objects. Long-running ones accept a `CancellationToken` and an `IProgress<T>`.

### 4.1 Scan

`GameTreeScanner.Scan(gamePath)` returns a `GameTree`: an ordered list of `GameFolder` (name, full path, list of `GameFile` with name, full path, size, mtime). `PackScanner.ScanAll(libraryPath)` returns a list of `Pack` (name, full path, list of folders and files in the same shape). Both:

- Include only files whose extension is `.png` or `.jpg`, case-insensitive. Other files are skipped silently.
- Look exactly one level deep. Deeper folders are ignored.
- Sort folders and files by name, ordinal ignore-case, so the UI order is stable.
- Return an empty tree, not an exception, when the path is missing. The caller decides how to report that.

### 4.2 Hashing

`FileHasher.Hash(path)` returns the lowercase hex SHA-256 of the file. `HashCache` wraps it: `GetOrCompute(path, size, mtimeTicks)` returns the cached hash when size and mtime match, else computes and stores it. The cache is loaded once at startup and saved after every scan. Hashing runs on a thread pool thread.

### 4.3 Status detection

`StatusDetector.Detect(gameTree, packs, hashCache)` returns a `FolderStatus` for every game folder and a `FileStatus` for every game file.

Folder status, evaluated in this order:

1. **Empty**: the game folder has no image files. Shown as "Reset, launch game to regenerate".
2. **Applied(packNames)**: one or more packs match. A pack matches a folder when the pack has at least one file for that folder, and every file the pack has for that folder exists in the game folder with an identical hash. Extra files in the game folder that the pack does not have are ignored. If several packs match, all their names are listed.
3. **Unmanaged**: nothing else applies. Shown as "Default or unmanaged". The app cannot tell vanilla files from hand edits, and does not try.

File status: the list of pack names whose copy of that file (same folder, same filename) has an identical hash. Empty list means "Default or unmanaged".

### 4.4 Apply

`PackApplier` copies files from a pack into the game folder, overwriting. Three entry points:

- `ApplyPack(pack, gamePath)`: every folder the pack contains.
- `ApplyFolder(pack, folderName, gamePath)`: one folder.
- `ApplyFile(pack, folderName, fileName, gamePath)`: one file.

Rules:

- The target game folder is created if missing. Nothing else is created.
- Each copy is attempted independently. A failure (locked file, permission denied) is recorded in the result and the batch continues.
- Returns `ApplyResult` with counts of copied and failed files and a list of `(path, error message)` for failures.
- Applying never deletes anything in the game folder.

### 4.5 Reset

`GameResetter`:

- `ResetFolder(gamePath, folderName)`: deletes every `*.png` and `*.jpg` (case-insensitive) directly inside that game folder. Never deletes the folder itself. Never touches other extensions or subfolders.
- `ResetAll(gamePath)`: `ResetFolder` for every folder in the scanned tree.

Failures are collected per file, same as Apply. Returns `ResetResult` with deleted and failed counts.

### 4.6 Import routing

`ImportRouter.Plan(sourcePath, gameTree)` walks the source folder recursively and returns an `ImportPlan`: one `ImportRow` per image file found. Each row has the source path, a `Route` (one of Routed, Ambiguous, Unmatched), the target folder name when routed, candidate folder names when ambiguous, and an `Include` flag.

Routing rules, applied in order per file:

1. If the file's immediate parent folder name matches a game folder name, case-insensitive, route to that game folder. This holds even when the filename is unknown to the game. This rule makes an existing game-tree mirror import cleanly, including the four colliding names.
2. Else if the filename matches exactly one file anywhere in the game tree, case-insensitive, route to that file's folder.
3. Else if the filename matches files in more than one game folder, mark Ambiguous and list those folders as candidates.
4. Else mark Unmatched.

Default `Include`: Routed rows are included. Ambiguous and Unmatched rows are excluded until the user picks a target folder. When the user assigns a folder to an Ambiguous or Unmatched row, its route becomes Routed and it is included.

Two source files routing to the same target path is a conflict. The plan flags both rows and the later one in walk order is excluded by default. The user may include either, but not both.

`ImportRouter.Execute(plan, packName, libraryPath)` creates `<library>\packs\<packName>\` and copies every included row to `<packName>\<TargetFolder>\<fileName>`. It never moves or deletes source files. If the pack folder already exists the caller must have confirmed overwrite; Execute overwrites files with the same name and leaves other existing files in place.

Pack name validation: non-empty, no path separators, no characters invalid in a Windows file name, not `.` or `..`.

**Save Current** is `Plan` and `Execute` with the game dir as the source. Every file routes by rule 1.

### 4.7 Background fit

`BackgroundFitter.Fit(sourcePath, FitOptions)` returns JPEG bytes, 2048 by 1151, quality 90.

`FitOptions`:

| Field | Type | Meaning |
| --- | --- | --- |
| `Mode` | `Cover`, `Contain`, `Stretch` | See below |
| `PanX` | double 0..1 | Cover only. 0 shows the left edge, 1 the right edge, 0.5 centered. |
| `PanY` | double 0..1 | Cover only. 0 shows the top edge, 1 the bottom edge, 0.5 centered. |
| `Darken` | double 0..1 | Every RGB channel is multiplied by `(1 - Darken)`. 0 is unchanged, 1 is black. |

Modes:

- **Cover**: scale the source uniformly so it fills 2048x1151 completely, then crop the overflow. Pan selects which part of the overflow survives.
- **Contain**: scale uniformly so the whole source fits inside 2048x1151, centered, with black bars filling the rest.
- **Stretch**: scale non-uniformly to exactly 2048x1151.

Implementation uses WPF imaging: `BitmapDecoder` to load, `TransformedBitmap` with a `ScaleTransform` to resize, `CroppedBitmap` for the Cover crop, a `WriteableBitmap` or `DrawingVisual` render to composite onto the black canvas and apply darken, and `JpegBitmapEncoder` with `QualityLevel = 90` to encode. Output pixel format is 24-bit RGB (no alpha). PNG sources with transparency are composited over black. The method runs off the UI thread; every bitmap it creates is frozen.

Two public methods:

- `Render(sourcePath, options, width, height)` returns a frozen `BitmapSource` of the given size. This drives the live preview in the editor at a small size.
- `Fit(sourcePath, options)` calls `Render` at 2048x1151 and encodes the result to JPEG bytes at quality 90.

### 4.8 Game-running check

`GameProcess.IsRunning()` returns true when `Process.GetProcessesByName("Brawlhalla")` is non-empty. The UI calls it before Apply and Reset and warns. The user can proceed anyway.

### 4.9 Launch

The Launch Brawlhalla button opens the URL `steam://rungameid/291550` with `Process.Start` and `UseShellExecute = true`.

### 4.10 Thumbnails

`ThumbnailProvider` returns a frozen `BitmapSource` decoded with `DecodePixelWidth = 240`, on a thread pool thread, cached in memory by full path plus mtime.

Which image represents a folder:

- Map folders: the largest file by size in that folder. This is a cheap proxy for the main platform art.
- `Backgrounds`: the card shows a fixed built-in tile (an icon resource in the app). The detail view shows each slot's own image.
- A pack's version of a folder uses the same rule against the pack's files. This is used in the Apply-from dropdown and in the detail view.

## 5. UI

The app is WPF with the MVVM pattern. Each window has one view model. View models call Core classes directly. There is no DI container; `App.xaml.cs` constructs the Core services and hands them to the main view model.

### 5.1 Main window

Layout, left to right:

- **Pack list** (left column). One row per pack under `packs\`, with name and file count. Buttons below the list, acting on the selected pack: Apply All, Open Folder (opens the pack folder in Explorer), Delete. Buttons that need no selection: Import, Save Current.
- **Folder grid** (center, fills the window). One card per game folder, wrapped in a scrollable `WrapPanel`. Each card shows: thumbnail, folder name, status badge, an "Apply from" dropdown, and a Reset button. The dropdown lists only packs that contain at least one file for that folder. Choosing an entry applies that pack's folder immediately. Clicking the card body opens the folder detail view.
- **Top bar**: Refresh, Reset All, Settings, Launch Brawlhalla. A status text at the right end shows the game path and, during long operations, a progress message.

Status badge text and color:

| Status | Text | Color |
| --- | --- | --- |
| Applied | pack name, or names joined with ", " | green |
| Empty | "Reset, launch game to regenerate" | amber |
| Unmanaged | "Default or unmanaged" | gray |

Every action that changes the game folder or the packs folder triggers a rescan when it finishes.

### 5.2 Folder detail view

A second window, or a panel replacing the grid with a Back button. Pick the panel. It shows the folder name at the top and one row per game file: thumbnail, filename, size, per-file status (pack names or "Default or unmanaged"), an "Apply from" dropdown listing packs that have that exact file, and Reset (deletes that one file). For the `Backgrounds` folder every row also has an Edit button that opens the background editor with that slot selected.

Rows for files that exist in any pack for this folder but not in the game folder (because the folder was reset) are also listed, with an empty thumbnail and status "Missing, launch game or apply". Their "Apply from" dropdown lists the packs that have the file, so the user can apply them.

### 5.3 Background editor

A modal window. Controls:

- **Slot**: a combo box of the 67 background filenames from the scanned game tree, plus a free text entry for a new name. The name must end in `.jpg`.
- **Source image**: a Browse button (file dialog filtered to png, jpg, jpeg, bmp, webp) and a drop zone that accepts one file dragged from Explorer.
- **Preview**: an `Image` with a fixed 2048:1151 aspect ratio, showing `BackgroundFitter.Fit` output at preview size. It re-renders on every option change, debounced by 150 ms, on a background thread, with the latest request winning.
- **Fit mode**: three radio buttons. Cover is the default.
- **Pan X, Pan Y**: sliders 0..1, default 0.5, enabled only in Cover mode.
- **Darken**: slider 0..100 percent, default 0.
- **Target pack**: a combo box of existing packs plus a "New pack..." entry that reveals a name field. Default is the pack named `My Backgrounds`, created on first save if it does not exist.
- **Apply now**: checkbox, default on. When on, Save also copies the file into the game `Backgrounds` folder.
- **Save**: runs the full-size fit, writes `<library>\packs\<pack>\Backgrounds\<slot>`, applies if requested, closes the window, and triggers a rescan.

### 5.4 Settings dialog

Modal. Two text fields with Browse buttons: game path, library path. Validation on OK: the game path must exist and contain at least one subfolder; the library path must exist or be creatable. Errors show inline. OK saves `settings.json` and rescans.

### 5.5 Long operations

Initial scan, Apply All, Reset All, Import execute, and Save Current run on a background thread with a progress message in the top bar and the action buttons disabled. They report per-file progress through `IProgress<string>`. They can be cancelled with a Cancel button that appears next to the progress message.

## 6. Safety and errors

- **Game running**: before Apply or Reset, if `GameProcess.IsRunning()` is true, show "Brawlhalla is running. Changes will not show until it restarts, and some files may be locked. Continue?" with Continue and Cancel.
- **Per-file failures**: Apply, Reset, and Import never abort on a single file. Each failure is collected. When the batch ends, one summary dialog lists every failed path and its error. A batch with zero failures shows no dialog.
- **Missing game path**: on launch, if the game path does not exist or has no subfolders, the app opens Settings immediately with a message instead of the main grid. The main grid loads once Settings saves a valid path.
- **Missing library path**: created on demand when the first pack is written. Scanning a missing library returns zero packs and shows no error.
- **Delete Pack**: confirmation dialog naming the pack. Before deleting, the app resolves the full path and verifies it starts with `<library>\packs\` and is not `packs\` itself. Otherwise it refuses and shows an error.
- **Reset All**: confirmation dialog stating how many folders will be cleared.
- **Import**: never deletes or moves source files. If the target pack name already exists, a dialog asks whether to merge into it (overwrite same-named files) or cancel.
- **Unexpected exceptions** in a view model command are caught at the command boundary and shown in a dialog with the message. The app does not crash.

## 7. Project layout and tooling

```
C:\Users\alexa\projects\bhmaps\
  BhMaps.sln
  .gitignore
  docs\superpowers\specs\2026-09-08-bhmaps-design.md
  src\BhMaps.Core\        class library
  src\BhMaps.App\         WPF app
  tests\BhMaps.Core.Tests\ xunit
```

### BhMaps.Core

- Target `net10.0-windows`, `<UseWPF>true</UseWPF>` so it can use `System.Windows.Media.Imaging` (`BitmapDecoder`, `TransformedBitmap`, `CroppedBitmap`, `WriteableBitmap`, `JpegBitmapEncoder`).
- No NuGet packages.
- No references to windows, controls, dialogs, or dispatchers. Imaging types only.
- Namespaces by concern: `BhMaps.Core.Model` (GameTree, Pack, statuses, results), `BhMaps.Core.Scanning`, `BhMaps.Core.Hashing`, `BhMaps.Core.Operations` (apply, reset, import), `BhMaps.Core.Imaging` (fitter, thumbnails), `BhMaps.Core.Settings`.

### BhMaps.App

- WPF, target `net10.0-windows`.
- One NuGet package: `CommunityToolkit.Mvvm`, for `ObservableObject`, `[ObservableProperty]`, and `[RelayCommand]`.
- Folders: `Views`, `ViewModels`, `Converters`, `Assets` (the Backgrounds tile icon).
- `App.xaml.cs` loads settings, builds the Core services, and shows either the main window or Settings.

### BhMaps.Core.Tests

- xunit, `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`.
- A `TempDir` fixture that creates a unique folder under the system temp path and deletes it on dispose.
- A `FakeGameTree` builder that writes a small tree with named folders and files of given bytes, including the four colliding filenames.
- Synthetic bitmaps for fit tests are generated in memory with `WriteableBitmap` and saved as PNG into the temp dir.

### Testing policy for this repository

Core is unit tested. The WPF layer is not unit tested and is verified by hand.

Required Core tests:

- Scan: folders and files found, non-image files skipped, nested folders ignored, missing path returns empty tree, ordering is stable.
- Hash cache: hit on same size and mtime, recompute on change, missing files dropped on save, round-trip through JSON.
- Status: Empty, Applied with one pack, Applied with two packs, Unmanaged when one file differs, extra game files ignored, per-file pack list.
- Apply: pack, folder, and file entry points copy and overwrite; failure on a locked file is recorded and the batch continues.
- Reset: deletes png and jpg case-insensitively, leaves other extensions and the folder itself, ResetAll covers every folder.
- Import routing: parent-folder rule wins including the four colliding names; unique filename rule; Ambiguous lists both candidates; Unmatched excluded by default; assigning a folder includes the row; duplicate target conflict; Execute copies and never removes sources; pack name validation.
- Background fit: output is 2048x1151 for all three modes; Cover crops (a wide source loses width, a tall source loses height); Contain adds black bars (corner pixels are black for a non-matching aspect); Stretch keeps corner colors; Darken 0.5 halves mean luminance within tolerance; Pan 0 and Pan 1 show different content.
- Settings: round-trip through JSON, defaults when the file is missing, atomic write leaves no temp file.

### Commands

```
dotnet build
dotnet test
dotnet format
dotnet publish src\BhMaps.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

Publish output is a single framework-dependent exe. The machine has the .NET 10 Windows Desktop runtime installed, so self-contained is not needed.

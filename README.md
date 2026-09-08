# BhMaps

BhMaps is a Windows desktop app for managing Brawlhalla map art. It keeps a library of *packs* —
folders of images that mirror the game's `mapArt` tree — and copies them into the game folder on
demand. It can also import loose images into a pack, snapshot what the game has right now, reset a
folder so the game regenerates its defaults, and fit any picture to the background size. The only
files it changes are `.png` and `.jpg` files inside the `mapArt` folder and inside its own library,
plus its own two files in `%APPDATA%`. Nothing else in the game install is touched.

The design notes are in `docs/superpowers/specs/2026-09-08-bhmaps-design.md`.

## Requirements

- Windows 10 or 11.
- The .NET 10 Desktop Runtime, to run the published exe. It is already installed on this machine.
- The .NET 10 SDK, to build. `dotnet --list-sdks` reports `10.0.300` here.
- Brawlhalla installed through Steam, or any folder shaped like `mapArt`: one level of subfolders
  holding `.png` and `.jpg` files.

## Build, test, run

From the repo root:

```
dotnet build BhMaps.slnx
dotnet test
dotnet run --project src\BhMaps.App
```

With no arguments the app uses the saved settings, or the defaults below on a first run.

## Publish a single exe

```
dotnet publish src\BhMaps.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

The output is `src\BhMaps.App\bin\Release\net10.0-windows\win-x64\publish\BhMaps.exe`, one
framework-dependent file of about 470 KB (the two `.pdb` files beside it are debug symbols and are
not needed to run). Copy the exe anywhere and pin it. It needs the .NET 10 Desktop Runtime on
whatever machine runs it. The publish folder is under `bin\` and is not committed.

## Where things live

| What | Where |
| --- | --- |
| Game map art, read and written | `C:\Program Files (x86)\Steam\steamapps\common\Brawlhalla\mapArt` |
| Pack library | `C:\Users\alexa\files\bh` |
| Packs inside the library | `<library>\packs\<pack name>\<GameFolder>\<file>` |
| Settings | `%APPDATA%\BhMaps\settings.json` |
| Hash cache | `%APPDATA%\BhMaps\hashcache.json` |

The first two are the defaults; both are editable in Settings. Only `<library>\packs` is read and
written, so anything else kept in the library folder is left alone. The settings file holds the two
paths and a flag for whether the first-run backup has been offered. The hash cache is a speed-up
only: deleting it costs one slower scan.

## What you can do

- **Refresh** rescans the game folder and the library.
- **Apply All** copies the whole selected pack into the game folder. **Apply from** on a folder card
  copies just that folder from one pack; the same button inside a folder does one file.
- **Reset** on a folder card deletes that folder's images. **Reset All** does every folder.
- **Import...** brings a folder of loose images into a new or existing pack.
- **Save Current...** writes the game folder back out as a pack. On the very first run the app
  offers to do this once, as `backup-<date>`.
- **New Background...** opens the background editor. Inside a `Backgrounds` folder, **Edit...** on a
  file opens the editor aimed at that slot.
- **Open Folder** shows the selected pack in Explorer. **Delete** removes it from the library.
- **Launch Brawlhalla** asks Steam to start the game.
- **Cancel** stops a long operation. **Settings** changes the two paths.

Clicking a folder card opens that folder and lists its files with per-file status and buttons.

## How it works

- A **pack** is a folder under `<library>\packs`. Inside it are folders named exactly like the game's
  folders, holding the images that replace the game's. A pack may hold one file or every folder.
- **Status** is decided by SHA-256. A folder shows as applied for a pack when every file that pack
  has for that folder matches the game's copy byte for byte. A folder can show more than one pack
  when two packs hold identical files. A folder with no images shows as reset; anything else shows
  as default or unmanaged. Hashes are cached by full path, size, and last-write time, so a rescan
  only rehashes what changed.
- **Apply** copies files from the pack into the game folder, overwriting what is there. It never
  deletes a game file that the pack does not have.
- **Reset** deletes the `.png` and `.jpg` files in a game folder. It never deletes the folder itself.
  Brawlhalla writes its default art back the next time it launches, so a reset folder looks empty
  until then.
- **Import** walks the source folder recursively and routes every `.png` and `.jpg` it finds. A file
  whose parent folder is named like a game folder goes to that folder. Otherwise, if its filename
  exists in exactly one game folder, it goes there. If the filename exists in several, the file is
  ambiguous and you pick the folder. If it matches nothing, it is unmatched and is left out until
  you assign a folder. Two files that would land on the same path conflict, and only the first is
  included. The whole routing table is shown before anything is copied, and sources are only read —
  never moved or deleted.
- The **background editor** fits any image to 2048x1151 with cover, contain, or stretch, pans a
  cover fit, and optionally darkens the result. It writes a JPEG at quality 90 into
  `<library>\packs\<pack>\Backgrounds\<slot>`, where the slot is a file name ending in `.jpg`, and,
  unless you clear "Apply to game now", writes the same bytes into the game's `Backgrounds` folder.

Changes only show up in the game after it restarts. The app warns before changing the game folder
while Brawlhalla is running, because the game may hold files open.

## Development

Never point a development run at the real game folder. Build a throwaway copy instead:

```
pwsh -File scripts\make-dev-tree.ps1
```

That writes a small fake tree, two packs, and a folder of loose images under `%TEMP%\bhmaps-dev`. It
reads the real game folder to get the images and never writes to it. Then run against the copy:

```
dotnet run --project src\BhMaps.App -- --game "$env:TEMP\bhmaps-dev\game" --library "$env:TEMP\bhmaps-dev\lib" --appdata "$env:TEMP\bhmaps-dev\appdata"
```

`--game`, `--library`, and `--appdata` each take a path. The first two override the saved settings
for that run only and are never written back; `--appdata` chooses the folder holding `settings.json`
and `hashcache.json`. Any of the three may be left out. The script prints the command with the paths
already filled in.

Formatting and layout:

- `dotnet format BhMaps.slnx` before every commit, and
  `dotnet format BhMaps.slnx --verify-no-changes` to check. `.editorconfig` sets CRLF everywhere,
  four-space indents in C#, and two in the markup and project files.
- All three projects build with `TreatWarningsAsErrors`, so a warning fails the build.
- `System.IO` is a project-level global using in every `.csproj`; do not add `using System.IO;` to a
  file.
- `.csharpierignore` at the repo root ignores every file. It is there to switch off a csharpier
  formatting hook installed outside this repo. `dotnet format` is the only formatter used here.
- `BhMaps.Core` holds all the logic and is unit tested in `tests\BhMaps.Core.Tests`. The WPF layer
  in `BhMaps.App` has no unit tests and is checked by hand against the dev tree.

## Known limitations

- A subfolder inside the game's `mapArt` that cannot be listed, because it is locked or the ACL
  denies access, fails the whole scan with an error dialog instead of being skipped.
- Cancel interrupts only the operations that check for it: the scan, Reset All, applying a pack or a
  folder, and an import. A single-file apply, a single-folder reset, a single-file reset, and
  deleting a pack run to the end.
- Cancelling an import or an apply leaves whatever was already copied on disk. The rescan that
  follows shows it.
- The import dialog's text boxes stay editable while a scan is running, and editing the source path
  after a scan does not invalidate the plan. Retyping the source and pressing Import without
  pressing Scan again imports the previous folder's plan.
- The thumbnail cache never evicts. Every thumbnail decoded during a session stays in memory until
  the app closes.
- The guard on deleting a pack compares resolved paths as text. A directory junction placed inside
  `packs\` is treated as a pack folder, so deleting it removes the link rather than refusing.

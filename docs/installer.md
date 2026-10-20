# The installer

What the AF Mod Loader installer does, exactly. This page must change in the same commit as the installer.

It is one small Windows program (`AFModLoader-Installer-<version>.exe`, C# WinForms on .NET Framework 4.8, which ships with Windows 10 and 11). One window, no wizard, no administrator rights, no entry in Windows "Installed apps", no uninstaller, no registry writes and no log file. Source: `installer/AFModLoader.Installer/` (all the logic is in `Core/`; `MainForm.cs` only shows it).

Checked on 2026-10-20 with the built exe against a fake game folder (real download from GitHub, hash check, install, remove) and with the unit tests. Not yet run by a player in the real game.

## The window

- **Folder**: the game folder, found through Steam when possible, with a `...` button to browse.
- **Status line**: `Not installed`, `Installed: AF Mod Loader <version> (UE4SS <build>)`, or a problem in plain words: another UE4SS is in the game, the game is running, or the folder is wrong.
- **Before installing it says** what will be downloaded: UE4SS `v3.0.1-1181` from `github.com/UE4SS-RE/RE-UE4SS`, with the short SHA-256, and below it the full URL and full SHA-256.
- **Enable UE4SS console (for mod developers)**: off by default. On an installed copy it shows what the settings file says now. Changing it takes effect when you click **Update**.
- **Install / Update** and **Remove**. Both are disabled while work is running. While the download is still running (nothing in the game folder has changed yet) a **Cancel** button appears, and closing the window cancels. Once the download is verified and files are being written, the window cannot be closed (except by Windows shutting down).
- **Progress bar** and **Show details**: every step in order (URL, both hosts contacted, the hash result, each file written or deleted). **Copy** puts that text on the clipboard.

## Install / Update

1. Finds the game folder from Steam. It only reads the registry: `HKCU\Software\Valve\Steam` `SteamPath`, then `HKLM\SOFTWARE\Valve\Steam` `InstallPath` (32-bit view). In each Steam folder it reads `steamapps\libraryfolders.vdf` (both the current and the old format) and looks in every library for `appmanifest_3669570.acf` and its `installdir`. If Steam does not know the game, you choose the folder.
2. Checks, each with a message that says what to do. If any fails, nothing is changed:
   - `AlchemyFactory\Binaries\Win64\AlchemyFactory-Win64-Shipping.exe` exists in the folder.
   - The path is plain ASCII. AF mods cannot read files in folders with letters like `ñ` or accents (see `docs/ue4ss.md`).
   - `Win64` is writable (a probe file is created and deleted).
   - The game is closed: no `AlchemyFactory-Win64-Shipping` or `AlchemyFactory` process, and `Win64\dwmapi.dll` can be opened for writing (a loaded DLL cannot).
   - No other UE4SS: if `dwmapi.dll` or `ue4ss\UE4SS.dll` exists and there is no install list from this installer (below), it refuses to install or remove, and says what to delete by hand.
3. Downloads the pinned UE4SS zip to a temporary file and checks its size and SHA-256. A mismatch stops the install **before anything in the game folder is touched**. HTTP 404 means GitHub dropped that build: a newer installer is needed.
4. Writes the **install list** first (every file this install will create, plus any the previous install made), then the files, then rewrites the list with what really exists. If anything stops half way (a crash, a full disk, a power cut), the list is already there, so Install can be run again and Remove still works.
5. Creates `<game>\mods` and `<game>\mods\.data`.

If a failure is not one of the above, the window shows the reason. If it happened before any change it says the game folder was not changed; if files were already being written it says to run Install again or Remove.

The installer also refuses to install when files it would write already exist in the game and were not installed by it (another UE4SS, or leftovers). It does not adopt them. An existing `UE4SS-settings.ini` is the one exception: it is kept as it is, and is not ours. Files are never written through an existing file: an existing file is deleted and a new one is created, so a hard link cannot send a write to a file outside the game folder. A link (junction or symbolic link) at any folder level or at a target file refuses the install before anything is changed.

The download is cut off if it is bigger than the pinned size, and fails with a plain message if no data arrives for 30 seconds.

## The download

| | |
|---|---|
| URL | `https://github.com/UE4SS-RE/RE-UE4SS/releases/download/experimental/UE4SS_v3.0.1-1181-g83330635.zip` |
| SHA-256 | `b9a166a756605b21058a64cde93bcaab1a855dba3553e5c7e6ce0fa8fdfad7b9` |
| Size | 8,748,315 bytes |

This is the only network access. Two hosts are contacted: `github.com`, which redirects to `release-assets.githubusercontent.com`. Nothing is sent but the download request. The pin is in `Core/Ue4ssPin.cs`; why this build and where builds live is in `docs/ue4ss.md`.

## Files created or changed

Inside `<game>`, all relative to `AlchemyFactory\Binaries\Win64\` unless noted:

| Path | What | Remove |
|---|---|---|
| `dwmapi.dll` | UE4SS proxy DLL, from the download | deleted |
| `ue4ss\UE4SS.dll`, `ue4ss\LICENSE` | UE4SS, from the download | deleted |
| `ue4ss\UE4SS-settings.ini` | UE4SS settings, from the download. Written only if missing, so your edits survive an update. With the console option, `ConsoleEnabled`, `GuiConsoleEnabled` and `GuiConsoleVisible` in `[Debug]` are set to 1 and `GraphicsAPI` to `dx11`; turning it off sets the first three to 0 and leaves `GraphicsAPI`. Every other line is kept as it is | deleted |
| `ue4ss\Mods\shared\...` | UE4SS's `UEHelpers` library, from the download | deleted |
| `ue4ss\Mods\AFModLoader\...` | The loader (`loader\AFModLoader` in this repository), including an empty `enabled.txt` that makes UE4SS start it. On an update, the files the previous install listed there are deleted first, so files dropped by a newer loader do not stay | deleted |
| `ue4ss\af-modloader-install.txt` | The install list (below) | deleted |
| `<game>\mods\` and `<game>\mods\.data\` | Where mods and their data go. Created empty | **kept**: they belong to you |

Not installed from the UE4SS zip: its sample mods, `mods.txt`, `mods.json` and `UE4SS_SDK_Backends` (only the Dumpers window uses it). AF Mod Loader never edits `mods.txt`: UE4SS starts the loader because of its `enabled.txt`.

The install list is plain text: three header lines (installer version, UE4SS build, UTC date), then one path per line, relative to `Win64`, with `/` separators. It is how Remove knows what is ours, and it is relative so a game folder that was moved or copied can still be cleaned.

## Remove

Needs the install list. It deletes only entries that are in the list **and** are on the installer's allow-list: `dwmapi.dll`, `ue4ss/UE4SS.dll`, `ue4ss/LICENSE`, `ue4ss/UE4SS-settings.ini`, `ue4ss/Mods/shared/**` and `ue4ss/Mods/AFModLoader/**`. Each entry must be a relative path with no `..` and no drive, and must stay inside `Win64`. Anything else is reported in the details and skipped, never deleted. The installer never follows links: before the first delete it checks every listed entry, and it refuses the whole removal, changing nothing, if any folder level above an entry, or the entry itself, is a junction or symbolic link.

Then it deletes the files UE4SS creates by itself in the ue4ss folder, which the installer did not install (UE4SS.log, imgui.ini, crash_*.dmp). A UE4SS-settings.ini that was already there before the installer is not ours and stays. It removes only the empty folders that held listed files (deepest first, the ue4ss folder last), and deletes the install list last, so if something fails half way Remove can be run again. A folder that cannot be deleted is a warning in the details, not a failure. Anything else in the ue4ss folder (for example files you generated with the Dumpers) stays, and the details list it.

**Kept**: `<game>\mods\` and `<game>\mods\.data\` with everything in them, and the game itself.

## Outside the game folder

- A temporary file in `%TEMP%` for the downloaded zip. It is deleted when the install ends, also on errors.
- The registry is only **read**, to find Steam (the two keys above).

Nothing else: no Start menu or desktop entries, no registry writes, no services, no environment changes, no log file.

## Removing it by hand

Close the game, then in `<game>\AlchemyFactory\Binaries\Win64\` delete `dwmapi.dll` and the `ue4ss` folder. Delete `<game>\mods` too if you want mods gone. Steam's "Verify integrity of game files" also puts the game back as it was. There is nothing to uninstall in Windows.

## What is inside the exe

Only this project's code and the loader files (`loader\AFModLoader`, embedded as they are, including `afml_json.lua` from rxi/json.lua under the MIT license, see `loader/AFModLoader/THIRD_PARTY.md`). No UE4SS, no third-party DLLs. NuGet packages (the test framework) are used only to build and test, and are not in the exe. The exe is built without debug symbols and deterministically, so the same source gives the same file.

## Build and test

Needs the .NET SDK pinned in `global.json`. The first build restores NuGet packages (exact versions are in the committed `packages.lock.json` files).

```
lua scripts/build-installer.lua     builds dist\AFModLoader-Installer-<version>.exe
lua scripts/check.lua               also builds, tests and format-checks the installer
dotnet test installer/AFModLoader.Installer.sln
```

The version is `LOADER_VERSION` from `loader/AFModLoader/Scripts/main.lua`. The loader files are embedded at build time, so rebuild after any change in `loader/`.

The unit tests are offline. They use a fake game folder in the temp folder, a small generated zip with its own pin, and a fake downloader. They cover the Steam file parsing, install and remove, update (edited settings kept, loader files replaced), the console option, a bad hash or zip leaving the game untouched, an interrupted install, a moved game folder, install lists with unsafe entries, a foreign UE4SS, and the embedded loader matching `loader/AFModLoader` byte for byte.

For a manual run without the real game, copy the folder structure (an empty file named `AlchemyFactory-Win64-Shipping.exe` in `<folder>\AlchemyFactory\Binaries\Win64\`) and start the exe with `AFModLoader-Installer-<version>.exe --game "<folder>"`.

### Online smoke test of the pinned download

Checks that the pinned file is still on GitHub and still has the pinned hash (PowerShell):

```
$zip = Join-Path $env:TEMP "ue4ss-check.zip"
Invoke-WebRequest "https://github.com/UE4SS-RE/RE-UE4SS/releases/download/experimental/UE4SS_v3.0.1-1181-g83330635.zip" -OutFile $zip
(Get-Item $zip).Length
(Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
Remove-Item $zip
```

Expected: `8748315` and `b9a166a756605b21058a64cde93bcaab1a855dba3553e5c7e6ce0fa8fdfad7b9`.

## Warnings from Windows

The exe is not code-signed yet, so SmartScreen may say it is an unknown app, and some antivirus programs are cautious about programs that download a file and place a DLL. The source is in this repository and releases will come with checksums.

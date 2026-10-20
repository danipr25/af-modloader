# UE4SS on Alchemy Factory

Verified facts about running UE4SS with the game. Game 1.1.5023, checked on 2026-10-20 unless noted.

## Which build works

- The game runs on Unreal Engine 5.7 (UE4SS log: `Found EngineVersion: 5.7`). Only UE4SS's experimental build supports it.
- Build used: `UE4SS_v3.0.1-1181-g83330635.zip`, pinned by the installer.
  - URL: `https://github.com/UE4SS-RE/RE-UE4SS/releases/download/experimental/UE4SS_v3.0.1-1181-g83330635.zip`
  - Size 8,748,315 bytes, SHA-256 `b9a166a756605b21058a64cde93bcaab1a855dba3553e5c7e6ce0fa8fdfad7b9`.
  - Loaded the game, ran Lua mods, key binds and the dumpers without errors.
- Where builds live (checked 2026-10-20):
  - `experimental-latest` is a rolling release: its files are replaced in place by each new build, so an old build disappears from it. Build `v3.0.1-1142` (used for the first `.usmap` dump) was replaced by 1181 there on 2026-10-17.
  - `experimental` is the archive release: it keeps the old builds, which is why the installer pins a file from it. It held 973 assets, close to GitHub's limit of about 1000 assets per release. If UE4SS's maintainers prune it to make room, a pinned build could be deleted someday: the installer then gets HTTP 404, tells the player a newer installer is needed, and installs nothing. A new installer then pins a newer build (its own reviewed change, re-tested in game).
  - GitHub's release API publishes a SHA-256 `digest` for each file, but the installer does not use it: the pinned hash is fixed in `installer/AFModLoader.Installer/Core/Ue4ssPin.cs`.

## Install layout

Everything goes in `<game>\AlchemyFactory\Binaries\Win64\`, where `<game>` is the Steam install folder (for example `C:\Program Files (x86)\Steam\steamapps\common\Alchemy Factory`):

| Path | What |
|---|---|
| `dwmapi.dll` | Proxy DLL that loads UE4SS when the game starts. |
| `ue4ss\UE4SS.dll`, `ue4ss\UE4SS-settings.ini` | UE4SS and its settings. |
| `ue4ss\Mods\` | Lua mods, plus `mods.txt` and `mods.json`. |
| `ue4ss\UE4SS.log` | Log, rewritten on every start. Lua `print` output lands here. |

- The `Win64` folder is writable without administrator rights in a default Steam install.
- The game must be closed to install or update: the DLL is only loaded at start.
- To remove: delete `dwmapi.dll` and `ue4ss\`, or use Steam's "Verify integrity of game files".

## Settings used for development

Changes from the build's default `UE4SS-settings.ini`: `ConsoleEnabled = 1`, `GuiConsoleEnabled = 1`, `GuiConsoleVisible = 1` (the UE4SS window with Live View and Dumpers) and `GraphicsAPI = dx11`. Players don't need the window: the installer keeps the build's defaults, and sets these values only if the player ticks the console option (all four keys are in the `[Debug]` section).

`[Overrides] ModsFolderPath` exists in the settings file. Its behaviour has not been tested.

## Registering a Lua mod

- Layout: `ue4ss\Mods\<Name>\Scripts\main.lua`.
- Enabled with a `<Name> : 1` line in `mods.txt`, placed above the built-in `Keybinds` line. Verified: a mod registered this way loads.
- The build also ships `mods.json` with the same list, but the 1181 `UE4SS.dll` never reads it (searched the binary for the name, ASCII and UTF-16).
- An empty `enabled.txt` inside the mod folder also enables it, with no `mods.txt` line. Verified in game 2026-10-20: the log shows `Mod 'AFModLoader' has enabled.txt, starting mod.` AF Mod Loader uses this, so it never edits `mods.txt`. Also verified 2026-10-20 with the layout the installer creates, which has no `mods.txt` at all: UE4SS logs `Starting mods (from enabled.txt ...)` and starts the loader, and after Remove no file UE4SS created was left besides its log.

## Dumpers

From the UE4SS window, Dumpers tab:

- **Dump CXX Headers** writes `ue4ss\CXXHeaderDump\` (967 files for 1.1.5023). The game's own native module is `BeltTD.hpp`; Blueprints are `BP_*.hpp` and widgets `WBP_*.hpp`.
- **Generate Lua Types** writes `ue4ss\Mods\shared\types\`, Lua annotations for the Lua Language Server.

Both are generated from the running game and belong to the game: never commit them.

## Lua environment (verified in game 2026-10-20)

Checked with an in-game spike on game 1.1.5023.

- `IterateGameDirectories()` returns the install root: its own `__absolute_path` is the install folder. Its child keys are `Engine`, `Game` and any other folder there, such as `mods`. `Game` is the project folder and its `__name` is `AlchemyFactory`, so always read `__name`, not the key. `__files` lists files, each with `__name` and `__absolute_path`.
- `io.open` reads and writes files in `<install>/mods/.data` from the game process, and `os.remove` deletes them, with no admin rights.
- `io.open` fails ("No such file or directory") on a path with non-ASCII characters (tested with a folder named `spike-ñandú`), because UE4SS's Lua uses the ANSI `fopen`. ASCII paths with spaces work.
- `load(source, chunkname, "t", env)` works, and globals assigned by the chunk stay in `env`.
- A Lua mod's `main.lua` runs before any game world exists (the game state is not present yet). `RegisterInitGameStatePostHook` fired twice afterwards: once for the menu, then once for the loaded save.

## Lua environment (from the UE4SS source, matching build 1181)

Read in the RE-UE4SS source, not exercised in game:

- `print` adds no newline: every log line must end with `"\n"`, or lines run together in `UE4SS.log`.
- `ModRef.OnUnload = fn` is a property assignment (a function or nil), not a method. It runs when UE4SS reloads, restarts or uninstalls its mods, and **not** when the game closes.
- `ModRef`, `RestartCurrentMod`, `UninstallCurrentMod`, `RestartMod`, `UninstallMod` and `ClearAllDelayedActions` act on the UE4SS mod that owns the Lua state. For AF mods that is AFModLoader, so they would affect every AF mod.
- UE4SS's `require` searches only the owning mod's `Scripts` folder and `Mods/shared`, loads modules in the global environment, and keeps one `package.loaded` for the whole state.

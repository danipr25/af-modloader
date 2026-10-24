# Mod format

What an AF mod looks like and what the loader gives it. This is a contract with every mod author: changes are versioned through `schemaVersion`, and older mods keep working or fail with a clear message.

Loader 0.1.0, `schemaVersion` 1, game 1.1.5023, UE4SS 1181.

Verified in game with the sample mods in `tests/fixtures/mods/`: loading in dependency order, `AF.log`, local `require` (two mods with their own `util.lua`), `AF.require`, a dependency cycle, a `.disabled` folder, a non-ASCII folder name, a broken `mod.json`, writing and reading a JSON file in `AF.dataDir`, and the `ModRef` block. The rest of this page is covered by the loader's unit tests, except where a line says "not yet tested in game".

## Where mods go

```
<game folder>\
├─ mods\
│  ├─ .data\               shared data folder (AF.dataDir)
│  ├─ my-mod\
│  │  ├─ mod.json
│  │  └─ scripts\
│  │     ├─ main.lua       entrypoint
│  │     └─ util.lua       local module: require("util")
│  └─ old-mod.disabled\    not loaded
└─ AlchemyFactory\ ...
```

`<game folder>` is the folder Steam installs the game in, for example `C:\Program Files (x86)\Steam\steamapps\common\Alchemy Factory`. The `mods` and `mods\.data` folders are created by the installer, or by hand with a manual install (see the README).

- Each mod is one folder in `mods`. The folder name doesn't have to match the mod id.
- A folder ending in `.disabled` is not loaded; the log says how to turn it back on.
- Folders starting with `.` (like `.data`) belong to the loader and are never loaded as mods.
- A folder without `mod.json` is skipped with a message.
- Folder names, and the path to the game, must use plain letters, digits and symbols (ASCII). UE4SS's Lua can't open files under paths with letters like `ñ` or accents. A mod folder named that way is skipped with "rename the folder"; if the game folder itself has them, no mod loads and the log says so.
- The loader matches `mods` without caring about case, so `Mods` also works.

## mod.json

```json
{
  "schemaVersion": 1,
  "id": "my-mod",
  "version": "1.0.0",
  "name": "My Mod",
  "description": "What it does, in one line.",
  "authors": ["Your Name"],
  "license": "MIT",
  "source": "https://github.com/you/my-mod",
  "entrypoint": "scripts/main.lua",
  "depends": { "af-modloader": ">=0.1.0" }
}
```

UTF-8, with or without a BOM.

| Field | Required | Rules |
|---|---|---|
| `schemaVersion` | yes | The whole number `1`. A higher number means the mod needs a newer loader, and the log says so. |
| `id` | yes | 2 to 64 characters: lowercase letters, digits, `-` and `_`, starting with a letter. Unique among installed mods: if two folders share an id, neither loads. `af-modloader` is reserved. |
| `version` | yes | `MAJOR.MINOR.PATCH`, three numbers without leading zeros, like `1.2.3`. No `-beta` or `+build` suffix. |
| `name` | yes | Non-empty text, shown in the log. Spaces around it are trimmed. |
| `description`, `license`, `source` | no | Text. |
| `authors` | no | List of names. |
| `entrypoint` | no | Relative path to a `.lua` file inside the mod folder. Default `scripts/main.lua`. No `..`, drive letters or absolute paths. |
| `depends` | no | Object mapping a mod id to a version range. Each key is a valid mod id or `af-modloader`; a mod cannot list itself. |

Unknown fields give a warning in the log and are ignored.

### Version ranges

A range is `*` on its own (any version), an exact version like `1.2.3`, or comparators separated by spaces, all of which must match. Each comparator is an operator directly followed by a version: `>=1.2.0`, `>=1.2.0 <2.0.0`, `=1.0.0`. Operators: `>=`, `>`, `<=`, `<`, `=`. No `^`, `~`, `||` or hyphen ranges, and no space between the operator and the version.

`af-modloader` can be listed in `depends` to require a loader version; it is always present.

## Load order and skipped mods

A mod loads after all its dependencies. Among the mods whose dependencies have already loaded, the loader always takes the one whose id sorts first (byte order). For example, a mod `a` that depends on `z` loads after both `b` and `z`.

A mod is skipped, with the reason and what to do in the log summary, when:

| Case | What the player is told |
|---|---|
| `mod.json` is not valid or breaks a rule above | the problem, then "fix the mod.json, or ask the mod author" |
| the folder has no `mod.json` | "add a mod.json, or move this folder out of mods" |
| the folder name is not plain ASCII | "rename the folder to plain letters and numbers" |
| a dependency is not installed | "install `<id>`" |
| a dependency has the wrong version | "update `<id>` (needs `<range>`, found `<version>`)" |
| a dependency was skipped or failed | "fix `<id>` first" |
| mods depend on each other in a circle | the cycle, then "ask the mod authors to remove the circular dependency" |
| two folders have the same id | "keep only one of these folders" |
| the entrypoint is missing, has a syntax error or raised an error | the first line of the error (the full trace is logged above it), then "check the messages above, or ask the mod author". The mod may be partly loaded: anything it registered before the error stays. |

## When the entrypoint runs

All mods run once, in order, when UE4SS starts the loader. That is **before any game world exists**: there is no game state, no player and no save yet. (According to the UE4SS source it also runs on UE4SS's own thread, not the game thread.)

So the entrypoint only sets things up: register hooks (`RegisterHook`, `NotifyOnNewObject`, `RegisterInitGameStatePostHook`, key binds) and do game work inside them, or inside `ExecuteInGameThread`. Note that `RegisterInitGameStatePostHook` fires more than once (for the main menu, then for the loaded save).

## The environment

Each mod runs in its own Lua environment. A global set by one mod is not visible to others: `_G` is the mod's own environment, and `load` without an environment uses it too. Every UE4SS function and the Lua standard library are readable as usual.

The isolation has limits; it keeps mods from clashing by accident, it is not a security boundary:

- The standard library tables (`string`, `table`, `math`, `package`...) are shared: changing them affects every mod.
- `dofile` and `loadfile` are not redirected and run in the real global environment.
- Shared modules from UE4SS's `Mods/shared` folder run in the real global environment and are shared by everything that uses them.

### require

`require("name")` loads `name.lua` from the folder that holds the entrypoint (`scripts/` by default). Dots are folders: `require("lib.util")` loads `scripts/lib/util.lua`.

- Each mod has its own modules: two mods can both have a `util.lua` without clashing.
- Names may use letters, digits, `_`, `-`, `.` and `/`. `..`, backslashes, drive letters and names starting or ending with `.` or `/` are refused.
- `main` and names starting with `afml_` are refused: they are the loader's own files.
- A name not found in the mod is passed to UE4SS's own `require`, which looks in the loader's `Scripts` folder and then in `Mods/shared` (for example `UEHelpers`). Not yet tested in game.
- A module that requires itself, directly or through others, raises "circular require".

### Blocked UE4SS functions

These act on the UE4SS mod that runs the loader, so they would affect every AF mod. Calling them raises an error that says so:

`ModRef`, `RestartCurrentMod`, `UninstallCurrentMod`, `RestartMod`, `UninstallMod`, `ClearAllDelayedActions`.

Blocking `ModRef` also removes `ModRef:SetSharedVariable` and `ModRef:GetSharedVariable`. For code that must run on unload, use `AF.onUnload`.

### The AF table

| Member | What it is |
|---|---|
| `AF.modId` | This mod's id. |
| `AF.modDir` | Absolute path of this mod's folder. |
| `AF.dataDir` | Absolute path of `<game folder>\mods\.data`, without a trailing `\`. See below. |
| `AF.loaderVersion` | The loader version, like `0.1.0`. |
| `AF.log(message)` | Writes `[<modId>] message` as one line in `UE4SS.log`. Use it instead of `print`, which adds no newline. |
| `AF.json` | JSON functions: `AF.json.encode(value)` returns text, `AF.json.decode(text)` returns a value and raises on invalid JSON. Provided by the loader (rxi's json.lua). |
| `AF.require(id)` | What the mod `id` returned from its entrypoint: `{}` if it returned nothing, otherwise the value as is (even `false`). Every dependent gets the same value, so changes to a returned table are shared. Only ids listed in this mod's `depends`, or `af-modloader`, which returns `{ version = "0.1.0" }`. |
| `AF.onUnload(fn)` | Registers `fn` to run when UE4SS reloads or restarts its mods. Handlers run in reverse order of registration; an error in one is logged and the rest still run. **It does not run when the game closes**: save data when it changes, not on unload. Not yet tested in game. |

Example. Its `mod.json` must list `"other-mod"` in `depends`:

```lua
local other = AF.require("other-mod")
local util = require("util")

RegisterInitGameStatePostHook(function()
  AF.log("a game state is ready")
end)

return { greet = function() return "hello from " .. AF.modId end }
```

### Data files

Mods may keep their own files in `AF.dataDir`, the folder `<game folder>\mods\.data`. It is shared by every mod and is meant to survive updating or replacing a mod folder.

- Only files, directly in that folder: mods cannot create subfolders (Lua has no way to make folders).
- Name every file after the mod id, `<modId>.json` or `<modId>-<something>`, so mods never overwrite each other's files.
- Build paths as `AF.dataDir .. "\\" .. AF.modId .. ".json"`.
- Write a file again whenever its data changes; don't wait for `AF.onUnload`.
- Handle a failed `io.open` (for example a missing `.data` folder) with a log message instead of an error.

```lua
local path = AF.dataDir .. "\\" .. AF.modId .. ".json"
local file, problem = io.open(path, "wb")
if file then
  file:write(AF.json.encode({ count = 3 }))
  file:close()
else
  AF.log("could not save " .. path .. ": " .. tostring(problem))
end
```

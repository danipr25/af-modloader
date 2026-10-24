# Changelog

All notable changes to AF Mod Loader. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/). While the version is 0.x, a new minor version may break mods and says how here.

## [0.1.4] - Unreleased

- Releases also include `AFModLoader-<version>.zip`, the loader folder alone, for players who already have UE4SS. The README explains the manual install.
- When the `mods` folder is missing, the loader's log line now says to create it (or reinstall).

## [0.1.1] - 2026-10-23

- Installer window redesigned: app icon and name, status icon with a Check again link, one line of progress with the current step (downloading, checking, writing files), and a short result message with what to do next.
- The main button now says Install, Update to X.Y.Z or Reinstall depending on what is installed.
- New buttons to open the mods folder and the game folder, and a "What gets installed" list in the window.
- The window scales with Windows display scaling and has its own icon.
- New look that follows Windows light and dark mode (and high contrast), with a header and footer, a status card and the standard Windows folder picker. The installer reads the Windows `AppsUseLightTheme` setting for this; it still writes nothing to the registry.
- Remove asks for confirmation inside the window instead of a pop-up. Open mods folder is always available.
- Fixed: the installer could look at the game folder while an install or removal was running.

## [0.1.0] - 2026-10-23

First public release.

- Installer for Windows. It finds Alchemy Factory in your Steam libraries, downloads UE4SS from its official GitHub release, checks it against a pinned SHA-256, installs it with the loader and creates the `mods` folder.
- Loader that runs with the game and loads every mod in `<game>\mods`, one folder per mod, in dependency order.
- Mod manifest (`mod.json`, `schemaVersion` 1) with version ranges for dependencies. A mod that is broken, or whose dependencies are missing or too old, is skipped with a plain message in the UE4SS log, and the others still load.
- Remove in the installer deletes only what it installed and keeps your mods.
- No telemetry. The only network access is the UE4SS download.

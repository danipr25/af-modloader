# Changelog

All notable changes to AF Mod Loader. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/). While the version is 0.x, a new minor version may break mods and says how here.

## [0.1.0] - Unreleased

First public release.

- Installer for Windows. It finds Alchemy Factory in your Steam libraries, downloads UE4SS from its official GitHub release, checks it against a pinned SHA-256, installs it with the loader and creates the `mods` folder.
- Loader that runs with the game and loads every mod in `<game>\mods`, one folder per mod, in dependency order.
- Mod manifest (`mod.json`, `schemaVersion` 1) with version ranges for dependencies. A mod that is broken, or whose dependencies are missing or too old, is skipped with a plain message in the UE4SS log, and the others still load.
- Remove in the installer deletes only what it installed and keeps your mods.
- No telemetry. The only network access is the UE4SS download.

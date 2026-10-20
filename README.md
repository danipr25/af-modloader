# AF Mod Loader

A mod loader for **Alchemy Factory**, in the spirit of Fabric for Minecraft: install it once, then drop mods into a plain `mods` folder and start the game from Steam as usual. Built on [UE4SS](https://github.com/UE4SS-RE/RE-UE4SS), which is downloaded from its official GitHub releases and never bundled.

Status: early development. Not usable yet.

Unofficial fan project. Alchemy Factory belongs to its developers.

## For players

Not released yet: there is no download link for now. When there is, you will download one small program, `AFModLoader-Installer-<version>.exe`, and run it:

1. Close the game. The installer finds Alchemy Factory in your Steam libraries (or you choose the folder with `...`).
2. Click **Install**. It downloads UE4SS from its official GitHub release, checks it against a fixed SHA-256 (it tells you the URL and the hash before it starts), and installs it together with the loader. It also creates a `mods` folder in the game folder, where mods go.
3. To take it all back, run the installer again and click **Remove**. It removes exactly what it installed and keeps your `mods` folder.

It needs no administrator rights, adds nothing to Windows "Installed apps", writes nothing to the registry and sends nothing anywhere. If another UE4SS is already in the game, it stops instead of overwriting it. You can also remove everything by hand: delete `dwmapi.dll` and the `ue4ss` folder in `<game>\AlchemyFactory\Binaries\Win64`. The full list of files is in [docs/installer.md](docs/installer.md).

The installer is not code-signed yet, so Windows SmartScreen may warn about an unknown app, and some antivirus programs may be cautious about it. The source is in this repository, and releases will come with checksums.

## Docs

- [The installer](docs/installer.md): every file it creates or changes, what Remove deletes, how to build and test it.
- [Mod format](docs/mod-format.md): how to write a mod (`mod.json`, environment, the `AF` table, data files).
- [UE4SS on Alchemy Factory](docs/ue4ss.md): verified facts about UE4SS and this game.

## Development

`lua scripts/check.lua` runs formatting, lint and tests for the Lua code and the installer (needs the .NET SDK). `lua scripts/build-installer.lua` builds the installer exe into `dist/`. `lua scripts/dev-install.lua --help` explains how to copy the loader (and the sample mods) into a local game install for testing. It is a developer tool, not the player installer.

## License

MIT. See [LICENSE](LICENSE).

# AF Mod Loader

A mod loader for **Alchemy Factory**, in the spirit of Fabric for Minecraft: install it once, then drop mods into a plain `mods` folder and start the game from Steam as usual. Built on [UE4SS](https://github.com/UE4SS-RE/RE-UE4SS), which is downloaded from its official GitHub releases and never bundled.

Status: early development. Not usable yet.

Unofficial fan project. Alchemy Factory belongs to its developers.

## Docs

- [Mod format](docs/mod-format.md): how to write a mod (`mod.json`, environment, the `AF` table, data files).
- [UE4SS on Alchemy Factory](docs/ue4ss.md): verified facts about UE4SS and this game.

## Development

`lua scripts/check.lua` runs formatting, lint and tests. `lua scripts/dev-install.lua --help` explains how to copy the loader (and the sample mods) into a local game install for testing. It is a developer tool, not the player installer.

## License

MIT. See [LICENSE](LICENSE).

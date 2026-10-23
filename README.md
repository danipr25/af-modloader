# AF Mod Loader

A mod loader for **Alchemy Factory**. Install it once, then put mods in a `mods` folder inside the game folder and start the game from Steam as usual. It runs on [UE4SS](https://github.com/UE4SS-RE/RE-UE4SS) (MIT), which the installer downloads from its official GitHub release. UE4SS is not included in this repository.

Unofficial fan project. Alchemy Factory belongs to its developers. Status: version 0.1.0. See the [changelog](CHANGELOG.md).

## Install

1. Download `AFModLoader-Installer-<version>.exe` from [Releases](https://github.com/danipr25/af-modloader/releases) and close the game.
2. Run it. It finds the game in your Steam libraries, or you pick the folder with `...`.
3. Click **Install**. It downloads UE4SS, checks its SHA-256 (it shows the URL and the hash first), installs the loader and creates `<game>\mods`.
4. Put mods in `<game>\mods`, one folder per mod.

Every release lists the SHA-256 of its files in `SHA256SUMS.txt`, and GitHub Actions builds the files and attests them. To check a download, run `Get-FileHash -Algorithm SHA256 <file>` and compare, or use the [GitHub CLI](https://cli.github.com/):

```
gh attestation verify <file> --repo danipr25/af-modloader --signer-workflow danipr25/af-modloader/.github/workflows/release.yml
```

To uninstall, run the installer again and click **Remove**. It deletes only what it installed and keeps `mods`.

The installer needs no administrator rights and writes nothing to the registry. It stops if another UE4SS is already installed. It is not code-signed yet, so Windows SmartScreen may warn about it. Every file it touches is listed in [docs/installer.md](docs/installer.md).

## Development

Tools (on the PATH):

- Lua 5.4: `winget install --id DEVCOM.Lua`
- StyLua 2.5.2: `winget install --id JohnnyMorganz.StyLua --version 2.5.2`
- Luacheck 1.2.0: `luacheck.exe` from the [v1.2.0 release](https://github.com/lunarmodules/luacheck/releases/tag/v1.2.0), placed next to `lua.exe`
- .NET SDK 10, for the installer

Commands, from the repository root:

| Command | What it does |
|---|---|
| `lua scripts/check.lua` | Formatting, lint, Lua tests, and the installer's build, tests and format check. `--fix` applies the formatting. |
| `lua scripts/build-installer.lua` | Builds `dist/AFModLoader-Installer-<version>.exe`. |
| `lua scripts/dev-install.lua` | Copies the loader into a game that already has UE4SS (install it once with the installer). `--with-samples` also copies the sample mods. The game must be closed. `--help` for details. |

The game folder defaults to the Steam one. Pass another as the first argument or set `AF_GAME_DIR`.

Docs: [mod format](docs/mod-format.md) (how to write a mod), [installer](docs/installer.md), [UE4SS on this game](docs/ue4ss.md).

### Releasing

Work reaches `development` by pull request, and CI (the `check` job, the same as `lua scripts/check.lua`) must pass. Only the owner publishes, and only from GitHub Actions: nothing is built or uploaded from a personal machine.

1. On `development`, set the new version (`LOADER_VERSION` in `loader/AFModLoader/Scripts/main.lua`) and write its section in [CHANGELOG.md](CHANGELOG.md).
2. Open a pull request from `development` into `main` and merge it. `main` accepts nothing else.
3. Tag the merge commit `vX.Y.Z` (the version) on `main` and push the tag. The [release workflow](.github/workflows/release.yml) checks that the commit is on `main` and that the tag matches the code, runs the checks, builds the installer exe, attests it and publishes a GitHub release with the CHANGELOG section as notes. Versions before 1.0 are marked as pre-releases.

To rehearse, run the Release workflow by hand from `main` ("Run workflow"): it does everything except the attestation and the release, and keeps the files and notes as a workflow artifact.

## License

MIT. See [LICENSE](LICENSE). Third-party code shipped with the loader is listed in [loader/AFModLoader/THIRD_PARTY.md](loader/AFModLoader/THIRD_PARTY.md).

# Third-party code shipped with the loader

| File | Origin | License | SHA-256 |
|---|---|---|---|
| `Scripts/afml_json.lua` | rxi/json.lua, tag `v0.1.2`: `https://raw.githubusercontent.com/rxi/json.lua/v0.1.2/json.lua` | MIT (rxi), notice in the file header | `b13df59b32c77db3bec7a1619280ea77ee5014e715ccffaa4876d857e3e9ab87` |

This file is shipped with the loader and must not be edited by hand. It is the upstream `json.lua`, byte for byte, only renamed so UE4SS can load it with a flat module name. It is excluded from formatting and lint.

To update: download the new tag from the same repository, save it as `Scripts/afml_json.lua` unmodified, check that the MIT notice is still in the header, run `sha256sum` on it, and update the tag, URL and SHA-256 in this table in the same commit.

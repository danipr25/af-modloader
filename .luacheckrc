-- Luacheck configuration. UE4SS runs Lua 5.4.
std = "lua54"
max_line_length = 120
exclude_files = { "tests/vendor/", "loader/AFModLoader/Scripts/afml_json.lua" }

-- Globals that UE4SS provides to its Lua mods. Only the ones the loader uses.
read_globals = { "IterateGameDirectories" }

-- The loader sets ModRef.OnUnload (a property assignment, not a read-only table).
files["loader/AFModLoader/Scripts/main.lua"] = { globals = { "ModRef" } }

-- Sample mods run inside the loader, which gives them AF and blocks ModRef. They are test data, not loader code.
files["tests/fixtures/**"] = { read_globals = { "AF", "ModRef" } }

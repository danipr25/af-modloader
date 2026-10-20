local lu = require("luaunit")
local discover = require("afml_discover")

local GAME = "C:\\Games\\Alchemy Factory"
local MODS = GAME .. "\\mods"

-- Builds a tree node like IterateGameDirectories() does: __name, __absolute_path and __files come through a
-- metatable, so pairs() yields only the subfolders.
local function node(parentPath, name, files, children)
  local path = parentPath .. "\\" .. name
  local meta = { __name = name, __absolute_path = path, __files = {} }
  for _, fileName in ipairs(files or {}) do
    table.insert(meta.__files, { __name = fileName, __absolute_path = path .. "\\" .. fileName })
  end
  local result = setmetatable({}, { __index = meta })
  for _, child in ipairs(children or {}) do
    result[child.__name] = child
  end
  return result
end

local function gameRoot(modsName, modFolders)
  return node("C:\\Games", "Alchemy Factory", {}, { node(GAME, "Engine"), node(GAME, modsName, {}, modFolders) })
end

local function names(list)
  local result = {}
  for _, item in ipairs(list) do
    result[#result + 1] = item.folder
  end
  return result
end

return {
  TestDiscover = {
    testFindsModsAndSkipsTheRest = function()
      local result = discover.discover(gameRoot("mods", {
        node(MODS, "zeta", { "mod.json" }),
        node(MODS, "alpha", { "mod.json", "x.lua" }),
        node(MODS, ".data"),
        node(MODS, "old.disabled", { "mod.json" }),
        node(MODS, "empty", { "readme.txt" }),
        node(MODS, "sample-\195\177", { "mod.json" }),
      }))
      lu.assertTrue(result.ok)
      lu.assertEquals(names(result.candidates), { "alpha", "zeta" })
      lu.assertEquals(result.candidates[1].manifestPath, MODS .. "\\alpha\\mod.json")
      lu.assertEquals(result.dataDir, MODS .. "\\.data")
      lu.assertEquals(#result.notes, 1)
      lu.assertStrContains(result.notes[1], "old.disabled")
      lu.assertEquals(names(result.skipped), { "empty", "sample-\195\177" })
      lu.assertStrContains(result.skipped[1].reason, "mod.json")
      lu.assertStrContains(result.skipped[2].advice, "rename")
    end,

    testModsFolderCaseIsIgnoredWithANote = function()
      local result = discover.discover(gameRoot("Mods", {}))
      lu.assertTrue(result.ok)
      lu.assertStrContains(result.notes[1], "'Mods', expected 'mods'")
    end,

    testMissingModsFolderIsAnError = function()
      local result = discover.discover(gameRoot("other", {}))
      lu.assertFalse(result.ok)
      lu.assertStrContains(result.error, MODS)
      lu.assertStrContains(result.error, "reinstall")
      lu.assertFalse(discover.discover(nil).ok)
    end,

    testNonAsciiGamePathIsAnError = function()
      local result = discover.discover(node("C:\\Juegos", "Alquimia \195\177"))
      lu.assertFalse(result.ok)
      lu.assertStrContains(result.error, "plain letters")
    end,
  },
}

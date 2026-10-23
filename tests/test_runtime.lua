local lu = require("luaunit")
local runtime = require("afml_runtime")

-- Runs mod `id` whose files are a { "relative/path.lua" = text } table. Returns export, error.
-- `globals` is the table the mod reads globals from (shared between runs to show nothing leaks into it).
local function run(id, files, globals)
  local prefix = "mods\\" .. id .. "\\"
  local deps = {
    readFile = function(path)
      local relative = path:sub(1, #prefix) == prefix and path:sub(#prefix + 1):gsub("\\", "/")
      return files[relative or ""], "not found"
    end,
    write = function() end,
    globals = globals or {
      require = function(name)
        error("module '" .. name .. "' not found")
      end,
    },
    json = {},
    loaderVersion = "0.1.0",
    dataDir = "mods\\.data",
    exports = { ["af-modloader"] = { version = "0.1.0" } },
    unloadHandlers = {},
  }
  local mod = { id = id, folder = id, dir = "mods\\" .. id, entrypoint = "scripts/main.lua", depends = {} }
  return runtime.run(mod, deps)
end

local function runCode(code, files)
  files = files or {}
  files["scripts/main.lua"] = code
  return run("m", files)
end

return {
  TestRuntime = {
    testEachModGetsItsOwnModulesAndGlobals = function()
      local globals = { require = function() end }
      local main = 'leaked = 1; return require("util")'
      local a =
        run("a", { ["scripts/main.lua"] = main, ["scripts/util.lua"] = "return { who = AF.modId .. '-a' }" }, globals)
      local b =
        run("b", { ["scripts/main.lua"] = main, ["scripts/util.lua"] = "return { who = AF.modId .. '-b' }" }, globals)
      lu.assertEquals(a.who, "a-a")
      lu.assertEquals(b.who, "b-b")
      lu.assertNil(globals.leaked)
    end,

    testNilReturnExportsAnEmptyTable = function()
      lu.assertEquals(runCode("local x = 1"), {})
    end,

    testLoaderFunctionsAreBlocked = function()
      for _, code in ipairs({
        "RestartCurrentMod()",
        "UninstallMod('x')",
        "ModRef.OnUnload = nil",
        "return ModRef.Name",
      }) do
        local export, problem = runCode(code)
        lu.assertNil(export)
        lu.assertStrContains(problem, "is not available to AF mods")
      end
      lu.assertStrContains(select(2, runCode("ModRef.OnUnload = nil")), "AF.onUnload")
    end,

    testRequireRefusesLoaderAndEscapingNames = function()
      for _, name in ipairs({ "main", "afml_json", "../x", "Main", "./main", "AFML_json", "a\\\\b", "c:x", "/x" }) do
        local export, problem = runCode('require("' .. name .. '")')
        lu.assertNil(export)
        lu.assertStrContains(problem, "not allowed")
      end
    end,

    testCircularRequireIsReported = function()
      local export, problem =
        runCode('require("a")', { ["scripts/a.lua"] = 'require("b")', ["scripts/b.lua"] = 'require("a")' })
      lu.assertNil(export)
      lu.assertStrContains(problem, "circular require of 'a' in mod m")
    end,

    testAFRequireNeedsADeclaredAndLoadedMod = function()
      local export, problem = runCode('AF.require("other")')
      lu.assertNil(export)
      lu.assertStrContains(problem, '"depends"')
      lu.assertEquals(runCode('return AF.require("af-modloader")'), { version = "0.1.0" })
    end,
  },
}

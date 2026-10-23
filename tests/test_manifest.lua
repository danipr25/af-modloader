local lu = require("luaunit")
local manifest = require("afml_manifest")

local function valid(overrides)
  local data = { schemaVersion = 1, id = "my-mod", version = "1.0.0", name = "My Mod" }
  for key, value in pairs(overrides or {}) do
    data[key] = value
  end
  return data
end

local function errorsOf(data)
  local result, errors = manifest.validate(data, "0.1.0")
  lu.assertNil(result)
  return table.concat(errors, "\n")
end

return {
  TestManifest = {
    testMinimalManifestGetsDefaults = function()
      local result, errors, warnings = manifest.validate(valid(), "0.1.0")
      lu.assertEquals(errors, {})
      lu.assertEquals(warnings, {})
      lu.assertEquals(result.entrypoint, "scripts/main.lua")
      lu.assertEquals(result.depends, {})
      lu.assertEquals(result.authors, {})
      lu.assertEquals(result.description, "")
    end,

    testAllRequiredFieldErrorsAreCollected = function()
      local text = errorsOf({})
      for _, field in ipairs({ "schemaVersion", "id", "version", "name" }) do
        lu.assertStrContains(text, '"' .. field .. '"')
      end
    end,

    testInvalidValuesAreRejected = function()
      lu.assertStrContains(errorsOf(valid({ id = "My Mod" })), '"id"')
      lu.assertStrContains(errorsOf(valid({ id = "af-modloader" })), "reserved")
      lu.assertStrContains(errorsOf(valid({ version = "1.0" })), '"version"')
      lu.assertStrContains(errorsOf(valid({ authors = { 1 } })), '"authors"')
      lu.assertStrContains(errorsOf(valid({ depends = { ["other-mod"] = "^1" } })), "other-mod")
    end,

    testNewerSchemaVersionGivesOneClearError = function()
      local result, errors = manifest.validate(valid({ schemaVersion = 2, id = "X" }), "0.1.0")
      lu.assertNil(result)
      lu.assertEquals(#errors, 1)
      lu.assertStrContains(errors[1], "needs a newer AF Mod Loader (supports schemaVersion 1, this is loader 0.1.0)")
    end,

    testSchemaVersionMustBeTheIntegerOne = function()
      lu.assertStrContains(errorsOf(valid({ schemaVersion = 1.0 })), '"schemaVersion"')
      lu.assertStrContains(errorsOf(valid({ schemaVersion = 2.0 })), "needs a newer AF Mod Loader")
    end,

    testEntrypointMustStayInsideTheModFolder = function()
      local bad = { "../main.lua", "a/../../main.lua", "/main.lua", "\\main.lua", "C:\\main.lua", "main.txt" }
      for _, path in ipairs(bad) do
        lu.assertStrContains(errorsOf(valid({ entrypoint = path })), '"entrypoint"')
      end
      local result = manifest.validate(valid({ entrypoint = "src\\start.lua" }), "0.1.0")
      lu.assertEquals(result.entrypoint, "src/start.lua")
    end,

    testUnknownFieldIsAWarning = function()
      local result, errors, warnings = manifest.validate(valid({ colour = "red" }), "0.1.0")
      lu.assertNotNil(result)
      lu.assertEquals(errors, {})
      lu.assertEquals(#warnings, 1)
      lu.assertStrContains(warnings[1], "colour")
    end,

    testDecodeIgnoresBomAndReportsInvalidJson = function()
      local data = manifest.decode('\239\187\191{"id": "my-mod"}')
      lu.assertEquals(data.id, "my-mod")
      local result, message = manifest.decode("{ not json")
      lu.assertNil(result)
      lu.assertStrContains(message, "not valid JSON")
      lu.assertNil((manifest.decode('["a"]')))
    end,
  },
}

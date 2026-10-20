local lu = require("luaunit")
local resolve = require("afml_resolve")

local function mod(id, version, depends)
  return { folder = id .. "-folder", manifest = { id = id, version = version or "1.0.0", depends = depends or {} } }
end

local function ids(order)
  local result = {}
  for _, candidate in ipairs(order) do
    result[#result + 1] = candidate.manifest.id
  end
  return result
end

local function entryFor(skipped, id)
  for _, entry in ipairs(skipped) do
    if entry.id == id then
      return entry
    end
  end
end

local function reasonFor(skipped, id)
  return entryFor(skipped, id).reason
end

return {
  TestResolve = {
    testDependenciesLoadFirstAndTiesSortById = function()
      local a = mod("a", "1.0.0", { z = "*", ["af-modloader"] = ">=0.1.0" })
      local b, z, m = mod("b"), mod("z"), mod("m")
      local expected = { "b", "m", "z", "a" }
      lu.assertEquals(ids((resolve.resolve({ a, b, z, m }, "0.1.0"))), expected)
      lu.assertEquals(ids((resolve.resolve({ m, z, b, a }, "0.1.0"))), expected)
    end,

    testMissingDependencyIsSkipped = function()
      local order, skipped = resolve.resolve({ mod("a", "1.0.0", { ghost = ">=1.0.0" }) }, "0.1.0")
      lu.assertEquals(order, {})
      lu.assertStrContains(reasonFor(skipped, "a"), "ghost")
      lu.assertStrContains(reasonFor(skipped, "a"), "not installed")
      lu.assertEquals(entryFor(skipped, "a").kind, "missing")
      lu.assertEquals(entryFor(skipped, "a").dep, "ghost")
    end,

    testVersionOutsideRangeIsSkipped = function()
      local candidates = {
        mod("a", "1.0.0", { b = ">=2.0.0" }),
        mod("b", "1.5.0"),
        mod("c", "1.0.0", { ["af-modloader"] = ">=1.0.0" }),
      }
      local order, skipped = resolve.resolve(candidates, "0.1.0")
      lu.assertEquals(ids(order), { "b" })
      lu.assertStrContains(reasonFor(skipped, "a"), ">=2.0.0")
      lu.assertStrContains(reasonFor(skipped, "a"), "1.5.0")
      lu.assertStrContains(reasonFor(skipped, "c"), "0.1.0")
      lu.assertEquals(entryFor(skipped, "a").kind, "version")
      lu.assertEquals(entryFor(skipped, "a").installed, "1.5.0")
    end,

    testSkipIsTransitive = function()
      local candidates = {
        mod("a", "1.0.0", { b = "*" }),
        mod("b", "1.0.0", { c = "*" }),
        mod("c", "1.0.0", { ghost = "*" }),
      }
      local order, skipped = resolve.resolve(candidates, "0.1.0")
      lu.assertEquals(order, {})
      lu.assertEquals(#skipped, 3)
      lu.assertEquals(entryFor(skipped, "c").kind, "missing")
      lu.assertEquals(entryFor(skipped, "b").kind, "skipped-dependency")
      lu.assertEquals(entryFor(skipped, "a").kind, "skipped-dependency")
    end,

    testCycleSkipsItsMembersAndDependents = function()
      local candidates = {
        mod("a", "1.0.0", { b = "*" }),
        mod("b", "1.0.0", { a = "*" }),
        mod("d", "1.0.0", { a = "*" }),
        mod("ok"),
      }
      local order, skipped = resolve.resolve(candidates, "0.1.0")
      lu.assertEquals(ids(order), { "ok" })
      lu.assertEquals(#skipped, 3)
      lu.assertStrContains(reasonFor(skipped, "a"), "a -> b -> a")
      lu.assertStrContains(reasonFor(skipped, "b"), "b -> a -> b")
      lu.assertEquals(entryFor(skipped, "a").kind, "cycle")
      lu.assertEquals(entryFor(skipped, "d").kind, "skipped-dependency")
    end,

    testDuplicateIdSkipsEveryFolder = function()
      local one, two = mod("a"), mod("a")
      one.folder, two.folder = "first", "second"
      local order, skipped = resolve.resolve({ one, two, mod("b", "1.0.0", { a = "*" }) }, "0.1.0")
      lu.assertEquals(order, {})
      lu.assertEquals(#skipped, 3)
      lu.assertStrContains(skipped[1].reason, "first")
      lu.assertStrContains(skipped[1].reason, "second")
      lu.assertEquals(skipped[1].kind, "duplicate")
    end,
  },
}

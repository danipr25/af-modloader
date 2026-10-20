local lu = require("luaunit")
local semver = require("afml_semver")

return {
  TestSemver = {
    testSatisfiesRangeForms = function()
      local cases = {
        { "1.2.3", "*", true },
        { "1.2.3", "1.2.3", true },
        { "1.2.4", "1.2.3", false },
        { "0.1.5", ">=0.1.0 <0.2.0", true },
        { "0.2.0", ">=0.1.0 <0.2.0", false },
        { "0.0.9", ">=0.1.0", false },
        { "0.1.0", ">0.1.0", false },
        { "1.10.0", ">1.9.0", true },
        { "1.0.1", "<=1.0.0", false },
        { "1.0.0", "<1.0.0", false },
        { "1.0.0", "=1.0.0", true },
      }
      for _, case in ipairs(cases) do
        lu.assertEquals(semver.satisfies(case[1], case[2]), case[3], case[1] .. " in '" .. case[2] .. "'")
      end
    end,

    testParseRejectsMalformedVersions = function()
      lu.assertEquals(semver.parse("1.2.3"), { major = 1, minor = 2, patch = 3 })
      for _, text in ipairs({ "1.2", "v1.2.3", "1.2.3-beta", "01.2.3" }) do
        lu.assertNil(semver.parse(text), text)
      end
      lu.assertNil(semver.parse(nil))
    end,

    testParseRangeExplainsInvalidRange = function()
      local comparators, message = semver.parseRange("^1.0.0")
      lu.assertNil(comparators)
      lu.assertStrContains(message, "^1.0.0")
    end,
  },
}

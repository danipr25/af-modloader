-- Semantic versions and version ranges. Pure logic: no UE4SS, no file access.
-- Versions are exactly MAJOR.MINOR.PATCH. Ranges are "*", an exact version, or space-separated comparators.
local semver = {}

local OPERATORS = { [">="] = true, [">"] = true, ["<="] = true, ["<"] = true, ["="] = true }

-- Returns { major, minor, patch } or nil when text is not exactly MAJOR.MINOR.PATCH.
function semver.parse(text)
  if type(text) ~= "string" then
    return nil
  end
  local major, minor, patch = text:match("^(%d+)%.(%d+)%.(%d+)$")
  if not major then
    return nil
  end
  for _, part in ipairs({ major, minor, patch }) do
    if #part > 1 and part:sub(1, 1) == "0" then
      return nil
    end
  end
  return { major = tonumber(major), minor = tonumber(minor), patch = tonumber(patch) }
end

-- Compares two parsed versions: -1 when a < b, 0 when equal, 1 when a > b.
function semver.compare(a, b)
  for _, part in ipairs({ "major", "minor", "patch" }) do
    if a[part] ~= b[part] then
      return a[part] < b[part] and -1 or 1
    end
  end
  return 0
end

-- Returns a list of { op, version } comparators (empty list means any version), or nil and an error message.
function semver.parseRange(text)
  if type(text) ~= "string" then
    return nil, 'a version range must be text like ">=0.1.0"'
  end
  local invalid = 'invalid version range "' .. text .. '": expected "*", "1.2.3" or like ">=1.0.0 <2.0.0"'
  if text:match("^%s*%*%s*$") then
    return {}
  end
  local comparators = {}
  for token in text:gmatch("%S+") do
    local op, versionText = token:match("^([<>=]*)(.*)$")
    if op == "" then
      op = "="
    end
    local version = semver.parse(versionText)
    if not OPERATORS[op] or not version then
      return nil, invalid
    end
    comparators[#comparators + 1] = { op = op, version = version }
  end
  if #comparators == 0 then
    return nil, invalid
  end
  return comparators
end

local CHECKS = {
  [">="] = function(c)
    return c >= 0
  end,
  [">"] = function(c)
    return c > 0
  end,
  ["<="] = function(c)
    return c <= 0
  end,
  ["<"] = function(c)
    return c < 0
  end,
  ["="] = function(c)
    return c == 0
  end,
}

-- True when the version meets every comparator of the range. False when either one is invalid.
function semver.satisfies(versionText, rangeText)
  local version = semver.parse(versionText)
  local comparators = semver.parseRange(rangeText)
  if not version or not comparators then
    return false
  end
  for _, comparator in ipairs(comparators) do
    if not CHECKS[comparator.op](semver.compare(version, comparator.version)) then
      return false
    end
  end
  return true
end

return semver

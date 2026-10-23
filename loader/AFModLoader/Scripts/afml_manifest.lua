-- Reads and validates one mod.json. Pure logic: no UE4SS, no file access.
local json = require("afml_json")
local semver = require("afml_semver")

local manifest = {}

local RESERVED_ID = "af-modloader"
local KNOWN_FIELDS = {
  schemaVersion = true,
  id = true,
  version = true,
  name = true,
  description = true,
  authors = true,
  license = true,
  source = true,
  entrypoint = true,
  depends = true,
}

-- Decodes the text of a mod.json (a UTF-8 BOM is ignored). Returns the table, or nil and a message.
function manifest.decode(text)
  if text:sub(1, 3) == "\239\187\191" then
    text = text:sub(4)
  end
  local ok, result = pcall(json.decode, text)
  if not ok then
    -- rxi's errors start with the Lua file and line of the decoder: players only need the JSON problem.
    local problem = tostring(result):gsub("^.-:%d+: ", "")
    return nil, "mod.json is not valid JSON: " .. problem
  end
  if type(result) ~= "table" or #result > 0 then
    return nil, "mod.json must contain a JSON object ({ ... })"
  end
  return result
end

local function isModId(value)
  return type(value) == "string" and value:match("^[a-z][a-z0-9_-]*$") ~= nil and #value >= 2 and #value <= 64
end

local function sortedKeys(tbl)
  local keys = {}
  for key in pairs(tbl) do
    keys[#keys + 1] = key
  end
  table.sort(keys, function(a, b)
    return tostring(a) < tostring(b)
  end)
  return keys
end

local function isStringArray(value)
  if type(value) ~= "table" then
    return false
  end
  local count = 0
  for key, item in pairs(value) do
    count = count + 1
    if type(key) ~= "number" or type(item) ~= "string" then
      return false
    end
  end
  return count == #value
end

-- Returns the normalized entrypoint, or nil and an error message.
local function checkEntrypoint(value)
  local what = '"entrypoint" must be a relative path to a .lua file inside the mod folder, like "scripts/main.lua"'
  if type(value) ~= "string" then
    return nil, what
  end
  local path = value:gsub("\\", "/")
  if path:sub(1, 1) == "/" or path:find("[%c:]") then
    return nil, what
  end
  local segments = {}
  for segment in path:gmatch("[^/]+") do
    if segment == ".." then
      return nil, what .. ' (".." is not allowed)'
    elseif segment ~= "." then
      segments[#segments + 1] = segment
    end
  end
  if #segments == 0 or not segments[#segments]:match("^.+%.lua$") then
    return nil, what
  end
  return table.concat(segments, "/")
end

-- Validates decoded mod.json data. Returns manifest, errors, warnings. The manifest is nil when there are errors.
function manifest.validate(data, loaderVersion)
  local errors, warnings = {}, {}
  local function fail(message)
    errors[#errors + 1] = message
  end

  local schema = data.schemaVersion
  if type(schema) == "number" and schema > 1 then
    local message = '"schemaVersion" is %s: this mod needs a newer AF Mod Loader'
      .. " (supports schemaVersion 1, this is loader %s)"
    return nil, { message:format(schema, loaderVersion) }, warnings
  elseif schema == nil then
    fail('"schemaVersion" is required and must be the number 1')
  elseif math.type(schema) ~= "integer" or schema ~= 1 then
    fail('"schemaVersion" must be the number 1')
  end

  if not isModId(data.id) then
    fail('"id" is required: 2 to 64 characters, lowercase letters, digits, "-" and "_", starting with a letter')
  elseif data.id == RESERVED_ID then
    fail('"id" cannot be "' .. RESERVED_ID .. '": that name is reserved for the loader')
  end

  if semver.parse(data.version) == nil then
    fail('"version" is required and must look like 1.2.3 (three numbers separated by dots)')
  end

  local name = type(data.name) == "string" and data.name:match("^%s*(.-)%s*$")
  if not name or name == "" then
    fail('"name" is required and must be non-empty text')
  end

  for _, field in ipairs({ "description", "license", "source" }) do
    if data[field] ~= nil and type(data[field]) ~= "string" then
      fail('"' .. field .. '" must be text')
    end
  end
  if data.authors ~= nil and not isStringArray(data.authors) then
    fail('"authors" must be a list of names, like ["Name"]')
  end

  local entrypoint = "scripts/main.lua"
  if data.entrypoint ~= nil then
    local path, message = checkEntrypoint(data.entrypoint)
    if path then
      entrypoint = path
    else
      fail(message)
    end
  end

  local depends = {}
  if data.depends ~= nil then
    if type(data.depends) ~= "table" or #data.depends > 0 then
      fail('"depends" must be an object mapping a mod id to a version range, like { "af-modloader": ">=0.1.0" }')
    else
      for _, id in ipairs(sortedKeys(data.depends)) do
        local range = data.depends[id]
        if id == data.id then
          fail('"depends" cannot list the mod itself ("' .. id .. '")')
        elseif id ~= RESERVED_ID and not isModId(id) then
          fail('"depends" has "' .. tostring(id) .. '", which is not a valid mod id')
        else
          local _, rangeError = semver.parseRange(range)
          if rangeError then
            fail('"depends" entry "' .. id .. '": ' .. rangeError)
          else
            depends[id] = range
          end
        end
      end
    end
  end

  for _, field in ipairs(sortedKeys(data)) do
    if not KNOWN_FIELDS[field] then
      warnings[#warnings + 1] = 'unknown field "' .. tostring(field) .. '" is ignored'
    end
  end

  if #errors > 0 then
    return nil, errors, warnings
  end
  local result = {
    schemaVersion = 1,
    id = data.id,
    version = data.version,
    name = name,
    description = data.description or "",
    authors = data.authors or {},
    license = data.license,
    source = data.source,
    entrypoint = entrypoint,
    depends = depends,
  }
  return result, errors, warnings
end

return manifest

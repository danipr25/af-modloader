-- Decides which mods load and in what order. Pure logic: no UE4SS, no file access.
local semver = require("afml_semver")

local resolve = {}

local BUILTIN_ID = "af-modloader"

local function sortedKeys(tbl)
  local keys = {}
  for key in pairs(tbl) do
    keys[#keys + 1] = key
  end
  table.sort(keys)
  return keys
end

-- candidates: list of { manifest, folder }. Returns order (candidates in load order) and
-- skipped (list of { folder, id, reason }).
function resolve.resolve(candidates, loaderVersion)
  local skipped = {}
  local skippedIds = {}
  local active = {} -- id -> candidate

  local function skip(candidate, reason)
    skipped[#skipped + 1] = { folder = candidate.folder, id = candidate.manifest.id, reason = reason }
    skippedIds[candidate.manifest.id] = true
  end

  -- Duplicate ids: every folder sharing an id is skipped.
  local byId = {}
  for _, candidate in ipairs(candidates) do
    local id = candidate.manifest.id
    byId[id] = byId[id] or {}
    table.insert(byId[id], candidate)
  end
  for _, id in ipairs(sortedKeys(byId)) do
    local group = byId[id]
    if #group == 1 then
      active[id] = group[1]
    else
      local folders = {}
      for _, candidate in ipairs(group) do
        folders[#folders + 1] = '"' .. candidate.folder .. '"'
      end
      table.sort(folders)
      local reason = 'the id "' .. id .. '" is used by several mods (folders ' .. table.concat(folders, ", ") .. ")"
      for _, candidate in ipairs(group) do
        skip(candidate, reason)
      end
    end
  end

  -- Skips mods whose dependencies are missing, out of range or skipped, until nothing changes.
  local function dropUnsatisfied()
    local changed = true
    while changed do
      changed = false
      for _, id in ipairs(sortedKeys(active)) do
        local depends = active[id].manifest.depends
        for _, depId in ipairs(sortedKeys(depends)) do
          local range = depends[depId]
          local installed = loaderVersion
          if depId ~= BUILTIN_ID then
            installed = active[depId] and active[depId].manifest.version
          end
          local reason
          if not installed and skippedIds[depId] then
            reason = 'needs "' .. depId .. '", which is skipped'
          elseif not installed then
            reason = 'needs "' .. depId .. '" (' .. range .. "), which is not installed"
          elseif not semver.satisfies(installed, range) then
            reason = 'needs "' .. depId .. '" ' .. range .. ", but " .. installed .. " is installed"
          end
          if reason then
            skip(active[id], reason)
            active[id] = nil
            changed = true
            break
          end
        end
      end
    end
  end

  dropUnsatisfied()

  -- Load order: repeatedly take the alphabetically first mod whose dependencies are all already loaded.
  local order, loaded = {}, {}
  local function nextReady()
    for _, id in ipairs(sortedKeys(active)) do
      local ready = not loaded[id]
      for depId in pairs(active[id].manifest.depends) do
        ready = ready and (depId == BUILTIN_ID or loaded[depId] == true)
      end
      if ready then
        return id
      end
    end
  end
  local nextId = nextReady()
  while nextId do
    loaded[nextId] = true
    order[#order + 1] = active[nextId]
    nextId = nextReady()
  end

  -- Whatever is left is in a dependency cycle or depends on one.
  local function findCycle(startId)
    local function walk(current, path, seen)
      for _, depId in ipairs(sortedKeys(active[current].manifest.depends)) do
        if depId == startId then
          return path
        end
        if active[depId] and not loaded[depId] and not seen[depId] then
          seen[depId] = true
          path[#path + 1] = depId
          local found = walk(depId, path, seen)
          if found then
            return found
          end
          path[#path] = nil
        end
      end
    end
    return walk(startId, { startId }, { [startId] = true })
  end
  local cycles = {}
  for _, id in ipairs(sortedKeys(active)) do
    local path = not loaded[id] and findCycle(id)
    if path then
      cycles[id] = "dependency cycle: " .. table.concat(path, " -> ") .. " -> " .. id
    end
  end
  for _, id in ipairs(sortedKeys(cycles)) do
    skip(active[id], cycles[id])
    active[id] = nil
  end
  dropUnsatisfied()

  table.sort(skipped, function(a, b)
    if a.id ~= b.id then
      return a.id < b.id
    end
    return a.folder < b.folder
  end)
  return order, skipped
end

return resolve

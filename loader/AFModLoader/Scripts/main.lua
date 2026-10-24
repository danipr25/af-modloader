-- AF Mod Loader entry point. UE4SS runs this when the game starts, before any world exists.
-- It finds the mods in the game's "mods" folder, checks them, and runs each one right away (so a mod can register
-- its hooks early). The logic lives in the afml_* modules: this file only connects them to UE4SS.
local LOADER_VERSION = "0.1.4"

local json = require("afml_json")
local manifest = require("afml_manifest")
local discover = require("afml_discover")
local resolve = require("afml_resolve")
local runtime = require("afml_runtime")

local function log(message)
  print("[AF Mod Loader] " .. message .. "\n")
end

local function readFile(path)
  local file, problem = io.open(path, "rb")
  if not file then
    return nil, problem
  end
  local text = file:read("a")
  file:close()
  return text
end

-- What a player can do about a skipped mod.
local function adviceFor(entry)
  if entry.kind == "missing" then
    return "install " .. entry.dep
  elseif entry.kind == "version" then
    return "update " .. entry.dep .. " (needs " .. entry.range .. ", found " .. entry.installed .. ")"
  elseif entry.kind == "skipped-dependency" then
    return "fix " .. entry.dep .. " first"
  elseif entry.kind == "cycle" then
    return "ask the mod authors to remove the circular dependency"
  elseif entry.kind == "duplicate" then
    return "keep only one of these folders"
  elseif entry.kind == "folder" then
    return entry.advice
  elseif entry.kind == "invalid" then
    return "fix the mod.json, or ask the mod author"
  end
  return "check the messages above, or ask the mod author"
end

-- Reads and checks every mod.json. Returns the valid candidates and the entries for the ones that are not.
local function readManifests(found)
  local candidates, skipped = {}, {}
  local function invalid(folder, problems)
    for _, problem in ipairs(problems) do
      log(folder .. ": " .. problem)
    end
    skipped[#skipped + 1] = { folder = folder, kind = "invalid", reason = problems[1] }
  end
  for _, candidate in ipairs(found.candidates) do
    local text, problem = readFile(candidate.manifestPath)
    local data
    if text then
      data, problem = manifest.decode(text)
    end
    if not data then
      invalid(candidate.folder, { problem })
    else
      local result, errors, warnings = manifest.validate(data, LOADER_VERSION)
      for _, warning in ipairs(warnings) do
        log(candidate.folder .. ": mod.json " .. warning)
      end
      if result then
        candidate.manifest = result
        candidates[#candidates + 1] = candidate
      else
        invalid(candidate.folder, errors)
      end
    end
  end
  return candidates, skipped
end

local function main()
  log("version " .. LOADER_VERSION)
  local found = discover.discover(IterateGameDirectories())
  if not found.ok then
    log("no mods were loaded: " .. found.error)
    return
  end
  for _, note in ipairs(found.notes) do
    log(note)
  end
  local candidates, skipped = readManifests(found)
  for _, entry in ipairs(found.skipped) do
    log("skipped " .. entry.folder .. ": " .. entry.reason)
    skipped[#skipped + 1] = { folder = entry.folder, kind = "folder", reason = entry.reason, advice = entry.advice }
  end
  local order, unresolved = resolve.resolve(candidates, LOADER_VERSION)
  for _, entry in ipairs(unresolved) do
    skipped[#skipped + 1] = entry
  end

  local exports = { ["af-modloader"] = { version = LOADER_VERSION } }
  local unloadHandlers = {}
  local deps = {
    readFile = readFile,
    write = print,
    globals = _G,
    json = json,
    loaderVersion = LOADER_VERSION,
    dataDir = found.dataDir,
    exports = exports,
    unloadHandlers = unloadHandlers,
  }

  local loaded, failed = {}, {}
  for _, candidate in ipairs(order) do
    local mod = candidate.manifest
    local brokenDep
    local depIds = {}
    for depId in pairs(mod.depends) do
      depIds[#depIds + 1] = depId
    end
    table.sort(depIds)
    for _, depId in ipairs(depIds) do
      brokenDep = brokenDep or failed[depId]
    end
    if brokenDep then
      skipped[#skipped + 1] = {
        folder = candidate.folder,
        id = mod.id,
        kind = "skipped-dependency",
        dep = brokenDep,
        reason = 'needs "' .. brokenDep .. '", which failed to load',
      }
      failed[mod.id] = mod.id
    else
      local export, problem = runtime.run({
        id = mod.id,
        folder = candidate.folder,
        dir = candidate.dir,
        entrypoint = mod.entrypoint,
        depends = mod.depends,
      }, deps)
      if problem == nil then
        exports[mod.id] = export
        loaded[#loaded + 1] = mod
      else
        log(mod.name .. " failed to load, it may be partially loaded: " .. tostring(problem))
        skipped[#skipped + 1] = { folder = candidate.folder, id = mod.id, kind = "runtime", reason = tostring(problem) }
        failed[mod.id] = mod.id
      end
    end
  end

  -- UE4SS calls this when it reloads or restarts its mods (not when the game closes).
  -- Set always: a mod may call AF.onUnload later, from a hook.
  ModRef.OnUnload = function()
    for index = #unloadHandlers, 1, -1 do
      local handler = unloadHandlers[index]
      local ok, problem = pcall(handler.fn)
      if not ok then
        log(handler.id .. " failed while unloading: " .. tostring(problem))
      end
    end
  end

  log("loaded " .. #loaded .. " mod(s)")
  for _, mod in ipairs(loaded) do
    log("  " .. mod.name .. " (" .. mod.id .. ") " .. mod.version)
  end
  if #skipped > 0 then
    log("skipped " .. #skipped .. " mod(s)")
    for _, entry in ipairs(skipped) do
      local first = (entry.reason or ""):match("^[^\n]*")
      log("  " .. entry.folder .. ": " .. first .. ". What to do: " .. adviceFor(entry))
    end
  end
end

main()

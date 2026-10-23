-- Runs one mod in its own environment. Everything outside the mod (reading files, the real globals, printing)
-- is passed in through deps, so this can be tested without the game.
local runtime = {}

local LOADING = {} -- marker in a mod's module cache while a module is still running

-- Functions that act on the whole loader (every AF mod), so a single mod must not call them.
local BLOCKED = {
  RestartCurrentMod = "Ask the player to restart the game instead.",
  RestartMod = "Ask the player to restart the game instead.",
  UninstallCurrentMod = "A mod cannot remove the loader.",
  UninstallMod = "A mod cannot remove the loader.",
  ClearAllDelayedActions = "Other mods may have their own delayed actions.",
}
local MOD_REF_HINT = "Use AF.onUnload(fn) to run code when the mods are unloaded."

local function stripBom(text)
  if text:sub(1, 3) == "\239\187\191" then
    return text:sub(4)
  end
  return text
end

local function blockedMessage(name, hint)
  return name .. " is not available to AF mods (it would affect every mod). " .. hint
end

-- Module names are file paths inside the mod, so keep them from leaving it or reaching the loader's own files.
local function checkModuleName(name, modId)
  if type(name) ~= "string" or name == "" then
    error('require needs the module name as text, like require("util")', 3)
  end
  -- Letters, digits, "_" and "-" in segments split by "." or "/": no "..", leading "./", drive letters or backslashes.
  -- (Lua patterns cannot repeat a group, so the shape is checked in a few simple tests.)
  if name:find("[^%w_%-./]") or name:find("^[./]") or name:find("[./]$") or name:find("[./][./]") then
    error("require '" .. name .. '\' is not allowed: use a plain module name like "util" or "lib.util"', 3)
  end
  -- Compared in lowercase: on Windows "Main" and "main" are the same file.
  local lower = name:lower()
  if lower == "main" or lower:sub(1, 5) == "afml_" then
    error("require '" .. name .. "' is not allowed: that name belongs to the AF Mod Loader (mod " .. modId .. ")", 3)
  end
end

-- mod: { id, folder, dir, entrypoint, depends }. deps:
--   readFile(path) -> text, or nil and a message
--   write(text)    -> prints raw text (it adds no newline)
--   globals        -> the table the mod reads globals from (the real _G in the game)
--   json, loaderVersion, dataDir
--   exports        -> id -> what each loaded mod returned (the loader fills in "af-modloader")
--   unloadHandlers -> list that AF.onUnload appends { id, fn } to
-- Returns the mod's export (its return value, or {} when it returns nothing), or nil and an error message.
-- Success means the second value is nil: an export may legitimately be false.
function runtime.run(mod, deps)
  local env = {}
  local entryDir = mod.entrypoint:match("^(.*)/[^/]*$")
  local cache = {}

  local function modRequire(name)
    checkModuleName(name, mod.id)
    if cache[name] == LOADING then
      error("circular require of '" .. name .. "' in mod " .. mod.id, 2)
    end
    if cache[name] ~= nil then
      return cache[name]
    end
    local relPath = (entryDir and entryDir .. "/" or "") .. name:gsub("%.", "/") .. ".lua"
    local source = deps.readFile(mod.dir .. "\\" .. relPath:gsub("/", "\\"))
    if not source then
      -- Not in the mod: maybe a shared module (Mods/shared, like UEHelpers), served by UE4SS's own require.
      local ok, result = pcall(deps.globals.require, name)
      if not ok then
        if tostring(result):find("not found", 1, true) then
          error("module '" .. name .. "' was not found in mod " .. mod.id .. " or in the shared modules", 2)
        end
        error(result, 0) -- the shared module exists but failed: show its own message
      end
      return result
    end
    local chunk, problem = load(stripBom(source), "@mods/" .. mod.folder .. "/" .. relPath, "t", env)
    if not chunk then
      error(problem, 0)
    end
    cache[name] = LOADING
    local ok, result = pcall(chunk, name)
    if not ok then
      cache[name] = nil
      error(result, 0)
    end
    if result == nil then
      result = true
    end
    cache[name] = result
    return result
  end

  -- dataDir has no trailing separator and is shared by all mods: name your files <modId>.json or <modId>-*.
  local AF = {
    modId = mod.id,
    modDir = mod.dir,
    dataDir = deps.dataDir,
    loaderVersion = deps.loaderVersion,
    json = deps.json,
  }
  function AF.log(message)
    deps.write("[" .. mod.id .. "] " .. tostring(message) .. "\n")
  end
  function AF.require(id)
    if id ~= "af-modloader" and not mod.depends[id] then
      error("mod " .. mod.id .. ' did not list "' .. tostring(id) .. '" in "depends" in its mod.json', 2)
    end
    local export = deps.exports[id]
    if export == nil then
      error('"' .. id .. '" is not loaded, so mod ' .. mod.id .. " cannot use it", 2)
    end
    return export
  end
  -- The loader runs these when UE4SS reloads or restarts its mods. They do not run when the game closes.
  function AF.onUnload(fn)
    if type(fn) ~= "function" then
      error("AF.onUnload needs a function", 2)
    end
    deps.unloadHandlers[#deps.unloadHandlers + 1] = { id = mod.id, fn = fn }
  end

  setmetatable(env, { __index = deps.globals })
  env._G = env
  -- load() without an environment would use the real globals: default it to the mod's own.
  env.load = function(chunk, chunkName, mode, ...)
    if select("#", ...) == 0 then
      return load(chunk, chunkName, mode, env)
    end
    return load(chunk, chunkName, mode, ...)
  end
  env.AF = AF
  env.require = modRequire
  for name, hint in pairs(BLOCKED) do
    env[name] = function()
      error(blockedMessage(name, hint), 2)
    end
  end
  env.ModRef = setmetatable({}, {
    __index = function()
      error(blockedMessage("ModRef", MOD_REF_HINT), 2)
    end,
    __newindex = function()
      error(blockedMessage("ModRef", MOD_REF_HINT), 2)
    end,
  })

  local text, readProblem = deps.readFile(mod.dir .. "\\" .. mod.entrypoint:gsub("/", "\\"))
  if not text then
    return nil, "cannot read " .. mod.entrypoint .. ": " .. tostring(readProblem)
  end
  local chunk, loadProblem = load(stripBom(text), "@mods/" .. mod.folder .. "/" .. mod.entrypoint, "t", env)
  if not chunk then
    return nil, loadProblem
  end
  local ok, result = xpcall(chunk, function(err)
    -- Cut the trace where the loader starts, so players only see the mod's own frames.
    local trace = debug.traceback(tostring(err), 2)
    return (trace:gsub("\n%s*%[C%]: in function 'xpcall'.*$", ""))
  end)
  if not ok then
    return nil, result
  end
  if result == nil then
    result = {}
  end
  return result
end

return runtime

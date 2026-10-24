-- Finds the mod folders inside the game's "mods" folder. Pure logic: it works on the directory tree that UE4SS's
-- IterateGameDirectories() returns (each folder has __name, __absolute_path and __files), so it can be tested
-- without the game. Nothing is read from disk here.
local discover = {}

local function hasNonAscii(text)
  return text:find("[\128-\255]") ~= nil
end

-- Subfolders of a tree node. __files is served through a metatable, so pairs() yields only real subfolders.
local function subfolders(node)
  local list = {}
  for _, value in pairs(node) do
    if type(value) == "table" and type(value.__name) == "string" then
      list[#list + 1] = value
    end
  end
  table.sort(list, function(a, b)
    return a.__name < b.__name
  end)
  return list
end

local function findManifest(folder)
  for _, file in ipairs(folder.__files or {}) do
    if type(file.__name) == "string" and file.__name:lower() == "mod.json" then
      return file.__absolute_path
    end
  end
end

-- Returns the reason a folder is not a candidate and what to do (or nil), or a note to show the player.
local function classify(folder)
  local name = folder.__name
  if name:sub(1, 1) == "." then -- .data and similar loader folders
    return nil, nil
  elseif name:lower():sub(-9) == ".disabled" then
    return nil, nil, name .. " is disabled. Remove '.disabled' from the folder name to turn it on"
  elseif hasNonAscii(name) then
    return "the folder name has letters the game can't read in file paths (like ñ or accents)",
      "rename the folder to plain letters and numbers"
  elseif not findManifest(folder) then
    return "there is no mod.json in this folder, so it is not a mod", "add a mod.json, or move this folder out of mods"
  end
end

-- root: the node returned by IterateGameDirectories(), or nil when UE4SS could not find it.
-- Returns { ok = false, error } or { ok = true, modsDir, dataDir, candidates, skipped, notes }.
-- candidates are { folder, dir, manifestPath } sorted by folder name; skipped are { folder, reason, advice };
-- notes are plain messages for the log.
function discover.discover(root)
  if type(root) ~= "table" or type(root.__absolute_path) ~= "string" then
    return { ok = false, error = "could not find the game folder, so no mods were loaded" }
  end
  local rootPath = root.__absolute_path:gsub("[/\\]+$", "")
  if hasNonAscii(rootPath) then
    return {
      ok = false,
      error = 'the game folder path "'
        .. rootPath
        .. '" has letters outside plain English (like accents). AF mods cannot read files there.'
        .. " Move the game to a folder with plain letters and numbers only, then start it again",
    }
  end

  local modsFolder, notes = nil, {}
  for _, folder in ipairs(subfolders(root)) do
    if folder.__name:lower() == "mods" then
      modsFolder = folder
      if folder.__name ~= "mods" then
        notes[#notes + 1] = "found '" .. folder.__name .. "', expected 'mods'. Using it anyway"
      end
      break
    end
  end
  if not modsFolder then
    return {
      ok = false,
      error = "mods folder not found at "
        .. rootPath
        .. "\\mods: create that folder (and mods\\.data in it), or reinstall AF Mod Loader",
    }
  end

  local candidates, skipped = {}, {}
  for _, folder in ipairs(subfolders(modsFolder)) do
    local reason, advice, note = classify(folder)
    if reason then
      skipped[#skipped + 1] = { folder = folder.__name, reason = reason, advice = advice }
    elseif note then
      notes[#notes + 1] = note
    elseif folder.__name:sub(1, 1) ~= "." then
      candidates[#candidates + 1] =
        { folder = folder.__name, dir = folder.__absolute_path, manifestPath = findManifest(folder) }
    end
  end

  return {
    ok = true,
    modsDir = modsFolder.__absolute_path,
    dataDir = modsFolder.__absolute_path .. "\\.data",
    candidates = candidates,
    skipped = skipped,
    notes = notes,
  }
end

return discover

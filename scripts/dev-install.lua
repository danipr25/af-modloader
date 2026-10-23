-- Developer tool: copies the loader from this repository into a game that already has UE4SS, so it can be tested
-- without an installer. Never shipped. It runs on the developer's machine, so it may use os.execute.
-- Usage (from the repository root): lua scripts/dev-install.lua [game folder] [--with-samples] [--remove-samples]
local HELP = [[
Usage: lua scripts/dev-install.lua [game folder] [--with-samples] [--remove-samples]

Run it from the repository root, with the game closed.

  game folder       Alchemy Factory install folder. Default: the AF_GAME_DIR environment variable, then
                    C:\Program Files (x86)\Steam\steamapps\common\Alchemy Factory
  --with-samples    Also copy the sample mods from tests/fixtures/mods into <game>\mods
  --remove-samples  Remove those sample mods from <game>\mods (and nothing else), then stop

It replaces <game>\AlchemyFactory\Binaries\Win64\ue4ss\Mods\AFModLoader with loader/AFModLoader, adds an empty
enabled.txt there, and creates <game>\mods\.data. It does not touch mods.txt or mods.json. UE4SS must already be
installed.]]

local STEAM_DEFAULT = "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Alchemy Factory"
local GAME_EXES = { "AlchemyFactory-Win64-Shipping.exe", "AlchemyFactory.exe" }
local SOURCE = "loader\\AFModLoader"
local SAMPLES = "tests\\fixtures\\mods"

local function fail(message)
  print("Error: " .. message)
  os.exit(1)
end

local function say(message)
  print(message)
end

-- Runs a command. The tool name goes first and unquoted, or cmd.exe would mis-handle the quotes around the paths.
local function run(command, what)
  say("  " .. command)
  local ok = os.execute(command)
  if ok ~= true then
    fail(what .. " failed")
  end
end

local function dirExists(path)
  return os.execute('if exist "' .. path .. '\\" (exit 0) else (exit 1)') == true
end

local function fileExists(path)
  local file = io.open(path, "rb")
  if file then
    file:close()
  end
  return file ~= nil
end

local function isRunning(exe)
  local output = assert(io.popen('tasklist /FI "IMAGENAME eq ' .. exe .. '" /NH')):read("a")
  return output:lower():find(exe:lower(), 1, true) ~= nil
end

-- cmd.exe loops over the sample folders itself: names with accents do not survive a trip through Lua.
-- The command removes <game>\mods\<folder name> only for folders that exist in tests/fixtures/mods.
local function samplesCommand(modsDir, action)
  local target = '"' .. modsDir .. "\\%~nxD" .. '"'
  local loop = '@for /d %D in ("' .. SAMPLES .. '\\*") do '
  return loop .. "@if exist " .. target .. " (@echo " .. action .. " %~nxD & @rmdir /S /Q " .. target .. ")"
end

-- Exits with an error if any sample folder is still in <game>\mods (rmdir can fail half way and still report 0).
local function samplesGoneCommand(modsDir)
  local loop = '@for /d %D in ("' .. SAMPLES .. '\\*") do '
  return loop .. '@if exist "' .. modsDir .. '\\%~nxD" exit 1'
end

-- Removes the same-named sample folders from <game>\mods and checks that they are really gone.
local function removeOldSamples(modsDir, action)
  run(samplesCommand(modsDir, action), "removing the old samples")
  run(samplesGoneCommand(modsDir), "removing the old samples (a folder could not be deleted: is a file in it open?)")
end

local gameDir, withSamples, removeSamples
for _, argument in ipairs(arg) do
  if argument == "--help" or argument == "-h" then
    print(HELP)
    os.exit(0)
  elseif argument == "--with-samples" then
    withSamples = true
  elseif argument == "--remove-samples" then
    removeSamples = true
  elseif argument:sub(1, 2) == "--" then
    fail("unknown option " .. argument .. ". Run with --help to see the options")
  elseif not gameDir then
    gameDir = argument
  else
    fail("only one game folder can be given")
  end
end
gameDir = (gameDir or os.getenv("AF_GAME_DIR") or STEAM_DEFAULT):gsub("/", "\\"):gsub("\\+$", "")

if not fileExists(SOURCE .. "\\Scripts\\main.lua") then
  fail("run this from the repository root (" .. SOURCE .. "\\Scripts\\main.lua was not found)")
end

say("Game folder: " .. gameDir)
for _, exe in ipairs(GAME_EXES) do
  if isRunning(exe) then
    fail(exe .. " is running. Close the game first")
  end
end

local win64 = gameDir .. "\\AlchemyFactory\\Binaries\\Win64"
local ue4ss = win64 .. "\\ue4ss"
local hasUe4ss = fileExists(ue4ss .. "\\UE4SS.dll") or (dirExists(ue4ss) and fileExists(win64 .. "\\dwmapi.dll"))
if not hasUe4ss then
  fail("UE4SS is not installed in " .. win64 .. ". Install UE4SS first")
end

local modsDir = gameDir .. "\\mods"
assert(modsDir:lower():sub(-5) == "\\mods", "the samples must go inside the game's mods folder")

if removeSamples then
  say("Removing the sample mods from " .. modsDir)
  removeOldSamples(modsDir, "Removing sample")
  say("Done.")
  os.exit(0)
end

local target = ue4ss .. "\\Mods\\AFModLoader"
if target:lower():sub(-#"\\ue4ss\\mods\\afmodloader") ~= "\\ue4ss\\mods\\afmodloader" then
  fail("refusing to delete " .. target)
end
if dirExists(target) then
  say("Removing the old loader")
  run('rmdir /S /Q "' .. target .. '"', "removing the old loader")
  if dirExists(target) then
    fail("could not remove " .. target .. " (is a file in it open?)")
  end
end
say("Copying the loader")
run('xcopy "' .. SOURCE .. '" "' .. target .. '" /E /I /Y /Q', "copying the loader")

say("Enabling the loader (enabled.txt)")
local enabled = assert(io.open(target .. "\\enabled.txt", "wb"))
enabled:close()

if not dirExists(modsDir .. "\\.data") then
  say("Creating " .. modsDir .. "\\.data")
  run('mkdir "' .. modsDir .. '\\.data"', "creating the data folder")
end

if withSamples then
  say("Adding the sample mods to " .. modsDir .. " (same-named folders are replaced)")
  removeOldSamples(modsDir, "Replacing sample")
  run('xcopy "' .. SAMPLES .. '" "' .. modsDir .. '" /E /I /Y /Q', "copying the samples")
end

say("Done. If the loader does not start in the game, check that UE4SS picked up enabled.txt in " .. target)

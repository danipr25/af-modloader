-- Builds the installer exe. Usage (from the repository root): lua scripts/build-installer.lua
-- The output goes to dist/. Releases are built by GitHub Actions, never from here.
local HELP = [[
Usage: lua scripts/build-installer.lua

Run it from the repository root. Needs the .NET SDK pinned in global.json (the first build restores NuGet packages).

Builds installer/AFModLoader.Installer in Release and copies the single exe to dist\AFModLoader-Installer-<version>.exe.
The version is LOADER_VERSION from loader/AFModLoader/Scripts/main.lua.]]

local MAIN = "loader/AFModLoader/Scripts/main.lua"
local PROJECT = "installer/AFModLoader.Installer/AFModLoader.Installer.csproj"
local BUILT = "installer/AFModLoader.Installer/bin/Release/net48/AFModLoader-Installer.exe"

local function fail(message)
  print("Error: " .. message)
  os.exit(1)
end

local function loaderVersion()
  local file = io.open(MAIN, "rb")
  if not file then
    fail("run this from the repository root (" .. MAIN .. " was not found)")
  end
  local text = file:read("a")
  file:close()
  local version = text:match('local LOADER_VERSION = "(%d+%.%d+%.%d+)"')
  if not version then
    fail("could not find LOADER_VERSION in " .. MAIN)
  end
  return version
end

for _, argument in ipairs(arg) do
  if argument == "--help" or argument == "-h" then
    print(HELP)
    os.exit(0)
  else
    fail("unknown option " .. argument .. ". Run with --help to see the options")
  end
end

local version = loaderVersion()
print("Loader version: " .. version)
local command = "dotnet build " .. PROJECT .. " -c Release -p:Version=" .. version
print(command)
if os.execute(command) ~= true then
  fail("dotnet build failed")
end

local input = io.open(BUILT, "rb")
if not input then
  fail("the build did not produce " .. BUILT)
end
local bytes = input:read("a")
input:close()

os.execute("if not exist dist mkdir dist")
local target = "dist/AFModLoader-Installer-" .. version .. ".exe"
local output = assert(io.open(target, "wb"))
output:write(bytes)
output:close()
print("Done: " .. target .. " (" .. #bytes .. " bytes)")
os.execute('certutil -hashfile "' .. target:gsub("/", "\\") .. '" SHA256')

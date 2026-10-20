-- Runs every check: formatting, lint and tests, for the Lua code and for the installer (C#).
-- Usage: lua scripts/check.lua [--fix]
-- With --fix, formatting is applied instead of only checked.
local fix = arg[1] == "--fix"

local SOLUTION = "installer/AFModLoader.Installer.sln"

local steps = {
  { name = "format", command = fix and "stylua ." or "stylua --check ." },
  { name = "lint", command = "luacheck ." },
  { name = "tests", command = "lua tests/run.lua" },
}

-- The installer steps need the .NET SDK pinned in global.json. Without it, say so once instead of failing three times.
local hasDotnet = os.execute("dotnet --version >nul 2>&1") == true
if hasDotnet then
  steps[#steps + 1] = { name = "installer build", command = "dotnet build " .. SOLUTION }
  steps[#steps + 1] = { name = "installer test", command = "dotnet test " .. SOLUTION .. " --no-build" }
  steps[#steps + 1] = {
    name = "installer format",
    command = "dotnet format " .. SOLUTION .. (fix and "" or " --verify-no-changes"),
  }
else
  print("The .NET SDK was not found (or not the version in global.json): install the .NET SDK version in global.json.")
  print("Skipping the installer build, test and format steps.")
end

local failed = {}
for _, step in ipairs(steps) do
  print(string.format("== %s: %s", step.name, step.command))
  if not os.execute(step.command) then
    failed[#failed + 1] = step.name
  end
end
if not hasDotnet then
  failed[#failed + 1] = "installer (dotnet missing)"
end

if #failed > 0 then
  print("Failed: " .. table.concat(failed, ", "))
  os.exit(1)
end
print("All checks passed.")

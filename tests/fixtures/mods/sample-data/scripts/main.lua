-- Sample mod: saves and reads a file in its data folder with AF.json, and tries the blocked ModRef.
local path = AF.dataDir .. "\\sample-data.json"
local file = assert(io.open(path, "wb"))
file:write(AF.json.encode({ greeting = "hi", count = 3 }))
file:close()

file = assert(io.open(path, "rb"))
local text = file:read("a")
file:close()
local back = AF.json.decode(text)
AF.log("wrote " .. text .. ", read back greeting=" .. back.greeting .. " count=" .. back.count)

local ok, message = pcall(function()
  return ModRef.Name
end)
AF.log("ModRef blocked: " .. tostring(not ok) .. " (" .. tostring(message) .. ")")

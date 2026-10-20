-- Sample mod for testing the loader: loads, logs, exports a function and uses its own local module.
local util = require("util")
AF.log("started, loader " .. AF.loaderVersion .. ", local module: " .. util.name)
return {
  hello = function()
    return "hello from sample-base"
  end,
}

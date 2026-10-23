-- Sample mod: uses sample-base through AF.require and has its own util, different from sample-base's.
local base = AF.require("sample-base")
local util = require("util")
AF.log(base.hello())
AF.log("local module: " .. util.name)

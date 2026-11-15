-- ap-responsive.lua: scales the uosc hover-bar icons with the window height so they stay visible in a small window.
-- uosc applies `script-opts uosc-controls_size` live. Full size (32) at 720p and above, down to 18 in a tiny window.
local mp = require 'mp'
local last = nil

local function apply()
    local dim = mp.get_property_native('osd-dimensions')
    local h = dim and dim.h or 0
    if h <= 0 then return end
    local size = math.floor(math.max(18, math.min(32, 32 * h / 720)) + 0.5)
    if size == last then return end
    last = size
    local opts = mp.get_property_native('script-opts') or {}
    opts['uosc-controls_size'] = tostring(size)
    opts['uosc-controls_spacing'] = tostring(size <= 22 and 1 or 2)
    mp.set_property_native('script-opts', opts)
end

local timer = mp.add_timeout(0.15, apply)   -- resizing fires many events: apply once it settles
timer:kill()
mp.observe_property('osd-dimensions', 'native', function() timer:kill(); timer:resume() end)

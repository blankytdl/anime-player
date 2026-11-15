-- AnimePlayer: smoother seeking / pausing. These options live HERE (not only in mpv.conf) because with the mpv-anime-build the build's
-- own mpv.conf is kept and AnimePlayer's mpv.conf is never installed.
--   hr-seek=yes + hr-seek-framedrop=yes : exact seeks (the frame you asked for) that drop frames while decoding up to it = quick on weak CPUs
--   demuxer-readahead-secs / cache-secs : read further ahead, so a jump forward finds its data already in memory
--   audio-buffer                        : a little more audio buffer = no click / stutter when pausing and resuming
-- Switch it off with smooth=no in script-opts/ap-smooth.conf
local mp = require 'mp'
local opts = { smooth = true, show_build = true }
require('mp.options').read_options(opts, 'ap-smooth')

-- which build is this? (the number comes from BUILD.txt next to mpvnet.exe; it is also in About and in the exe's file properties)
do
    local f = io.open(mp.command_native({ 'expand-path', '~~exe_dir/BUILD.txt' }), 'r')
    local n = f and f:read('*l') or nil
    if f then f:close() end
    if n and n ~= '' then
        n = n:gsub('%s+', '')
        mp.set_property('user-data/ap/build', n)
        mp.msg.info('AnimePlayer build ' .. n)
        if opts.show_build then mp.add_timeout(1.5, function() mp.osd_message('AnimePlayer build ' .. n, 2.5) end) end
    end
end

if not opts.smooth then return end

local SET = {
    { 'hr-seek', 'yes' },
    { 'hr-seek-framedrop', 'yes' },
    { 'demuxer-readahead-secs', '30' },
    { 'cache-secs', '60' },
    { 'audio-buffer', '0.3' },
}
for _, kv in ipairs(SET) do
    local ok = pcall(mp.set_property, kv[1], kv[2])
    if not ok then mp.msg.warn('ap-smooth: could not set ' .. kv[1]) end
end

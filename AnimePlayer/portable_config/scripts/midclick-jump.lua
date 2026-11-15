-- Middle mouse button = jump forward (default 5 s; set a longer jump such as 85 s to skip an opening).
-- The middle button is bound HERE (not in input.conf), so it does not clutter the key list; rebind it in input.conf with
-- `MBTN_MID script-binding midclick-jump/jump` if you ever want another key / button for it.
-- Change the length with script-opts/midclick-jump.conf  ->  seconds=5   (or in AnimePlayer Settings)
local mp = require 'mp'
local opts = { seconds = 5 }
require('mp.options').read_options(opts, 'midclick-jump')

mp.add_key_binding('MBTN_MID', 'jump', function()
    mp.command('async seek ' .. tostring(opts.seconds) .. ' relative+exact')
    mp.osd_message('>> +' .. opts.seconds .. ' s', 0.8)
end)

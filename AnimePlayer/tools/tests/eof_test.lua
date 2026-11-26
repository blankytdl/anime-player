local now, timers, calls = 0, {}, {}
local P = { ['eof-reached'] = false, pause = false, duration = 1400, ['time-pos'] = 100, ['playlist-pos'] = 0, ['playlist-count'] = 3 }
local cur_id, obs, ev = 10, {}, {}
local mp = {}
function mp.add_timeout(s, f) local t = { due = now + s, fn = f, on = true }; function t:kill() self.on = false end; timers[#timers+1] = t; return t end
function mp.get_property_bool(n, d) local v = P[n]; if v == nil then return d end return v end
function mp.get_property_number(n, d) local v = P[n]; if v == nil then return d end return v end
function mp.get_property_native(n) if n == 'playlist' then return { { id = cur_id - 1 }, { id = cur_id, current = true }, { id = cur_id + 1 } } end end
function mp.commandv(...) calls[#calls+1] = table.concat({ ... }, ' ') end
function mp.observe_property(n, _, f) obs[n] = f end
function mp.register_event(n, f) ev[n] = f end
package.preload['mp'] = function() return mp end
package.preload['mp.msg'] = function() return { info = function() end } end
dofile(arg[1])
local function adv(d) now = now + d; for _, t in ipairs(timers) do if t.on and t.due <= now then t.on = false; t.fn() end end end
local function set(n, v) P[n] = v; if obs[n] then obs[n](n, v) end end
local fails = 0
local function check(name, c) print((c and '  ok   ' or '  FAIL ') .. name); if not c then fails = fails + 1 end end
local function reset() calls = {}; for _, t in ipairs(timers) do t.on = false end; P['eof-reached'] = false; P.pause = false; P['time-pos'] = 100; P['playlist-pos'] = 0; P['playlist-count'] = 3 end

print('1) drag to the very end (uosc pauses), release (unpauses)')
reset(); set('pause', true); P['time-pos'] = 1400; set('eof-reached', true); adv(1)
check('still paused while the bar is held: nothing happens', #calls == 0)
set('pause', false); adv(0.6)
check('after release -> playlist-next force', calls[1] == 'playlist-next force')
print('2) normal end: mpv moves on by itself')
reset(); set('eof-reached', true); cur_id = cur_id + 1; adv(0.6)
check('no extra skip', #calls == 0); cur_id = cur_id - 1
print('3) last video of the list')
reset(); P['playlist-pos'] = 2; set('eof-reached', true); adv(1)
check('stays on the last frame (no command)', #calls == 0)
print('4) user seeks back before the timer fires')
reset(); set('eof-reached', true); P['eof-reached'] = false; P['time-pos'] = 300; adv(0.6)
check('not skipped', #calls == 0)
print('5) paused on purpose near the end')
reset(); P['time-pos'] = 1399; P.pause = true; ev['playback-restart'](); adv(2)
check('not skipped', #calls == 0)
print('6) seek (playback-restart) lands at the end while playing, mpv stuck')
reset(); P['time-pos'] = 1400; ev['playback-restart'](); adv(0.6)
check('-> next', calls[1] == 'playlist-next force')
print(fails == 0 and 'ALL OK' or 'FAILURES ' .. fails)

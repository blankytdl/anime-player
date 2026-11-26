-- mock mpv harness for thumbfast.lua  (run: texlua tf_test.lua <script>)
local script = arg[1]
local now = 0
local timers, jobs, calls = {}, {}, {}
local fs = { ['/pl/ffmpeg.exe'] = 1 }
OPT = { prefetch = false }      -- the older sections test the hover logic alone; section H switches the prefetch on
local handlers, events = {}, {}
local props = { path = '/v/ep.mkv', duration = 7200 }
local vp = { w = 1920, h = 1080, dw = 1920, dh = 1080 }
local realenv = os.getenv
os.getenv = function(k) if k == 'TEMP' then return '/tmp/tfm' end return realenv(k) end
local realremove = os.remove
os.remove = function(p) fs[p] = nil; calls[#calls + 1] = { 'rm', p }; return true end

local mp = {}
function mp.add_timeout(sec, fn)
  local t = { timeout = sec, fn = fn, active = true, due = now + sec }
  function t:kill() self.active = false end
  function t:resume() self.active = true; self.due = now + self.timeout end
  timers[#timers + 1] = t; return t
end
function mp.command_native(c)
  if c[1] == 'expand-path' then if c[2] == '~~/' then return '/cfg/' elseif c[2] == '~~exe_dir/' then return '/pl/' end end
end
function mp.get_time() return now end
function mp.get_property(n) return props[n] end
function mp.get_property_number(n, d) return props[n] or d end
function mp.get_property_native(n)
  if n == 'video-params' then return vp end
  if n == 'track-list' then return { { type = 'video', selected = true } } end
end
function mp.set_property_native() end
function mp.commandv(...) calls[#calls + 1] = { ... } end
function mp.register_script_message(n, f) handlers[n] = f end
function mp.register_event(n, f) events[n] = f end
function mp.observe_property() end
function mp.osd_message() end
function mp.command_native_async(tbl, cb)
  local j = { args = tbl.args, cb = cb, done = false }
  jobs[#jobs + 1] = j; return j
end
function mp.abort_async_command(j)
  if j and not j.done then j.done = true; j.aborted = true; j.cb(true, { killed_by_us = true, status = -2 }) end
end
local utils = {}
function utils.file_info(p) if fs[p] then return { is_file = true, is_dir = false, size = fs[p] } end end
function utils.readdir() return {} end
function utils.join_path(a, b) if a:sub(-1) == '/' then return a .. b end return a .. '/' .. b end
function utils.format_json(t) local s = {} for k, v in pairs(t) do s[#s + 1] = k .. '=' .. tostring(v) end table.sort(s) return '{' .. table.concat(s, ',') .. '}' end
package.preload['mp'] = function() return mp end
package.preload['mp.utils'] = function() return utils end
package.preload['mp.msg'] = function() return { info = function() end, warn = function() end } end
package.preload['mp.options'] = function() return { read_options = function(o) for k, v in pairs(OPT) do o[k] = v end end } end

local function advance(dt)
  now = now + dt
  for _, t in ipairs(timers) do
    if t.active and t.due <= now then
      t.active = false
      local ok, err = pcall(t.fn)
      if not ok then print('  !! TIMER ERROR: ' .. tostring(err)) end
    end
  end
end
local function open_jobs() local o = {} for _, j in ipairs(jobs) do if not j.done then o[#o + 1] = j end end return o end
local function finish(j, status)
  j.done = true
  if (status or 0) == 0 then fs[j.args[#j.args]] = 200 * 112 * 4 end
  j.cb(true, { status = status or 0, stderr = '' })
end
local function ss(j) for i, a in ipairs(j.args) do if a == '-ss' then return tonumber(j.args[i + 1]) end end end
local function has(j, f) for _, a in ipairs(j.args) do if a == f then return true end end return false end
local function last_overlay() for i = #calls, 1, -1 do local c = calls[i] if c[1] == 'overlay-add' then return c end if c[1] == 'overlay-remove' then return 'removed' end end end
local function overlay_file() local o = last_overlay(); return type(o) == 'table' and o[5] or o end
local fails = 0
local function check(name, cond) print((cond and '  ok   ' or '  FAIL ') .. name); if not cond then fails = fails + 1 end end
local function reset_file(dur)
  props.duration = dur
  if events['start-file'] then events['start-file']() end
  jobs, calls = {}, {}
  events['file-loaded']()
end

dofile(script)
reset_file(7200)

print('A) long video (2 h): mouse moves faster than ffmpeg')
handlers.thumb('100', '300', '900')
local o = open_jobs()
check('first hover starts exactly one job', #o == 1)
check('rough job uses keyframe seek (-noaccurate_seek)', o[1] and has(o[1], '-noaccurate_seek'))
handlers.thumb('500', '320', '900'); handlers.thumb('900', '340', '900'); handlers.thumb('1500', '360', '900')
check('no extra job piles up while one runs', #open_jobs() == 1)
finish(o[1])
local o2 = open_jobs()
check('stale frame is shown at the NEWEST mouse x (no frozen thumbnail)', last_overlay()[3] == '360')
check('newest request (1500) is cut next, not the old 500/900', #o2 == 1 and math.abs(ss(o2[1]) - 1500) < 1)
finish(o2[1])
check('frame for 1500 on screen at x=360', last_overlay()[3] == '360' and #open_jobs() == 0)

print('B) mouse rests -> exact frame (refine timer)')
advance(0.3)
local o3 = open_jobs()
check('exact job started after 0.25 s rest', #o3 == 1 and not has(o3[1], '-noaccurate_seek'))
local before = overlay_file()
finish(o3[1])
check('exact frame swapped in', overlay_file() ~= before)

print('C) exact job is cancelled when the mouse moves on')
handlers.thumb('3000', '400', '900'); finish(open_jobs()[1])
advance(0.3)
local ex = open_jobs()[1]
check('exact job running', ex and not has(ex, '-noaccurate_seek'))
handlers.thumb('4000', '420', '900')
check('exact job aborted', ex.aborted == true)
local o4 = open_jobs()
check('new rough job for 4000 started', #o4 == 1 and math.abs(ss(o4[1]) - 4000) < 1 and has(o4[1], '-noaccurate_seek'))
finish(o4[1])

print('D) watchdog: a hanging ffmpeg can never block the queue')
handlers.thumb('5000', '440', '900')
local hang = open_jobs()[1]
advance(3.1)
check('hanging job aborted by watchdog', hang.aborted == true)
check('no endless retry loop for the same frame', #open_jobs() == 0)
handlers.thumb('5001', '450', '900')
check('same frame is not retried', #open_jobs() == 0)
handlers.thumb('6000', '460', '900')
check('next position still works', #open_jobs() == 1)
finish(open_jobs()[1])

print('E) clear (mouse leaves the bar) and come back')
handlers.thumb('7000', '470', '900')
handlers.clear()
check('overlay removed', last_overlay() == 'removed')
check('nothing stuck running', #open_jobs() == 0)
handlers.thumb('7100', '480', '900')
check('hover works again right away', #open_jobs() == 1)
finish(open_jobs()[1])

print('F) short video (12 s)')
reset_file(12)
handlers.thumb('11.9', '300', '900')
local s1 = open_jobs()[1]
check('time clamped inside the video (<= 11.5)', s1 and ss(s1) <= 11.5)
finish(s1, 1)            -- ffmpeg finds no frame that close to the end
check('failed frame: no crash, no loop', #open_jobs() == 0)
handlers.thumb('3', '310', '900'); finish(open_jobs()[1])
handlers.thumb('11.95', '320', '900')
check('failed frame falls back to nearest cached one', #open_jobs() == 0 and last_overlay()[3] == '320')
handlers.thumb('0', '200', '900')
check('t=0 works', #open_jobs() == 1)
finish(open_jobs()[1])

print('G) new file while a job runs')
reset_file(1500)
handlers.thumb('600', '300', '900')
local run = open_jobs()[1]
events['start-file']()
check('job aborted on file change', run.aborted)
jobs = {}
events['file-loaded']()
handlers.thumb('300', '300', '900')
check('thumbnails work in the next file', #open_jobs() == 1)


print('H) background prefetch (the instant layer)')
OPT = { prefetch = true }
timers, jobs, calls = {}, {}, {}
dofile(script)
props.duration = 1500
events['file-loaded']()
check('nothing is cut before the start-up delay (playback settles first)', #open_jobs() == 0)
advance(3.1)
local b1 = open_jobs()
check('then ONE background cut starts', #b1 == 1)
check('background cut: keyframe seek, one thread', b1[1] and has(b1[1], '-noaccurate_seek') and b1[1].args[5] == '-threads' and b1[1].args[6] == '1')
finish(b1[1])
check('the next background cut follows by itself (one at a time)', #open_jobs() == 1)
local times = { ss(b1[1]) }
local second = open_jobs()[1]
times[2] = ss(second)
finish(second)
-- two frames cached now (start and middle); a third background cut is running
local third = open_jobs()[1]
check('coarse-to-fine order: the start, then the middle', math.abs(times[1] - 11.7) < 1 and math.abs(times[2] - 761.7) < 20)
handlers.thumb('700', '300', '900')
local ov = last_overlay()
check('hover shows the nearest cached frame AT ONCE (before any new cut ends)', type(ov) == 'table' and ov[3] == '300')
check('the background cut was stopped for the hover request', third.aborted == true)
local hv = open_jobs()
check('the hover frame is cut next', #hv == 1 and math.abs(ss(hv[1]) - 700) < 20)
finish(hv[1])
check('mouse still moving: the background waits', #open_jobs() == 0)
advance(0.5)
local function bg_jobs() local n = 0 for _, j in ipairs(open_jobs()) do if j.args[6] == '1' then n = n + 1 end end return n end
check('still no background cut 0.5 s after the last move', bg_jobs() == 0)
advance(1.0)
local resumed = open_jobs()
for _, j in ipairs(open_jobs()) do if j.args[6] ~= '1' then finish(j) end end     -- the exact frame of the resting mouse is cut first
check('mouse rested: the background goes on', bg_jobs() >= 1)
-- the refine timer (0.2 s) has fired meanwhile = exact job first; finish everything that is open until the plan is done
local count, total_bg = 0, 0
guard = 0
while #open_jobs() > 0 and guard < 400 do
  guard = guard + 1
  local j = open_jobs()[1]
  if has(j, '-noaccurate_seek') and j.args[6] == '1' then total_bg = total_bg + 1 end
  finish(j)
  advance(0.01)
end
check('the whole plan finishes (64 frames for a 25 min video), no loop', total_bg >= 55 and total_bg <= 64 and guard < 400)

print('   leaving the bar must not kill a background cut')
reset_file(1500)
advance(3.2)
local bgjob = open_jobs()[1]
check('a background cut is running', bgjob ~= nil)
handlers.clear()
check('clear() does not abort the background cut', bgjob.aborted ~= true and #open_jobs() == 1)

print('   short video / switched off')
reset_file(12)
advance(3.2)
check('video under 20 s: no prefetch', #open_jobs() == 0)
OPT = { prefetch = false }
timers, jobs, calls = {}, {}, {}
dofile(script)
props.duration = 1500
events['file-loaded']()
advance(5)
check('prefetch=no: nothing is cut in the background', #open_jobs() == 0)

print(fails == 0 and '\nALL OK' or ('\nFAILURES: ' .. fails))
os.exit(fails == 0 and 0 or 1)

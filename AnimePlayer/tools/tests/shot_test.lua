-- mock mpv harness for ap-screenshot.lua (build 37)   run: texlua shot_test.lua <path to ap-screenshot.lua>
local script = arg[1]
local now, timers, handlers, osd, log, pending = 0, {}, {}, {}, {}, {}
local OPT = {}
local props = { ['sub-visibility'] = true, ['secondary-sub-visibility'] = false, sid = 1, path = '/v/Show - 01.mkv', ['filename/no-ext'] = 'Show - 01',
  ['time-pos'] = 754.321, ['time-pos/full'] = '00:12:34.321', ['screenshot-directory'] = '/pics', ['screenshot-format'] = 'png' }
local fs = { ['/pics'] = { dir = true }, ['/pl/ffmpeg.exe'] = { size = 1 } }

local mp = {}
function mp.get_time() return now end
function mp.add_timeout(s, f) timers[#timers + 1] = { due = now + s, fn = f } end
function mp.command_native(c) if c[1] == 'expand-path' then local m = { ['~~/'] = '/cfg/', ['~~exe_dir/'] = '/pl/', ['~~desktop/'] = '/desk/' } return m[c[2]] or c[2] end end
function mp.get_property(n) local v = props[n]; if v == nil then return nil end return tostring(v) end
function mp.get_property_number(n, d) return props[n] ~= nil and tonumber(props[n]) or d end
function mp.get_property_osd(n) return props[n] end
function mp.get_property_native(n) if n == 'video-params' then return { w = 1 } end return props[n] end
function mp.set_property_native(n, v) log[#log + 1] = 'set ' .. n .. '=' .. tostring(v); props[n] = v end
function mp.register_script_message(n, f) handlers[n] = f end
function mp.osd_message(m) osd[#osd + 1] = m end
function mp.command_native_async(t, cb) local j = { t = t, cb = cb }; pending[#pending + 1] = j; return j end
local utils = {}
function utils.file_info(p) local f = fs[p]; if f then return { is_file = not f.dir, is_dir = f.dir == true, size = f.size or 0 } end end
function utils.readdir() return {} end
function utils.join_path(a, b) if a:sub(-1) == '/' or a:sub(-1) == '\\' then return a .. b end return a .. '/' .. b end
package.preload['mp'] = function() return mp end
package.preload['mp.utils'] = function() return utils end
package.preload['mp.msg'] = function() return { info = function() end, warn = function() end } end
package.preload['mp.options'] = function() return { read_options = function(o) for k, v in pairs(OPT) do o[k] = v end end } end
local realopen = io.open
io.open = function(p, m) if p:find('ap%-screenshot%.log') then return { write = function() end, close = function() end } end return realopen(p, m) end

local fails = 0
local function check(n, c) print((c and '  ok   ' or '  FAIL ') .. n); if not c then fails = fails + 1 end end
local function adv(d) now = now + d; for _, t in ipairs(timers) do if t.due <= now and not t.done then t.done = true; t.fn() end end end
local function reset() now = now + 25; log, osd, pending, timers = {}, {}, {}, {}; props['sub-visibility'] = true; props['secondary-sub-visibility'] = false; props.sid = 1 end
local function job(name) for _, j in ipairs(pending) do if j.t.name == name and not j.done then return j end end end
local function finish(j, ok, res, err) j.done = true; j.cb(ok, res, err) end

dofile(script)
local press = handlers['ap-shot-nosubs']
local OUT = '/pics/Show - 01_00-12-34.321_nosubs.png'

print('A) mpv path: subtitles off for the shot, file written, subtitles restored')
reset(); press(); press()
check('second press while busy does nothing', #pending == 0 and #timers == 1)
check('subtitles are switched off first', props['sub-visibility'] == false)
adv(0.1); check('waits for the renderer before the shot', not job('screenshot-to-file'))
adv(0.3)
local j = job('screenshot-to-file')
check('screenshot-to-file with the video flag and an explicit file', j and j.t.flags == 'video' and j.t.filename == OUT)
check('subtitles are still off while the shot is taken', props['sub-visibility'] == false)
fs[OUT] = { size = 12345 }; finish(j, true)
check('sub-visibility restored to true, secondary to false', props['sub-visibility'] == true and props['secondary-sub-visibility'] == false)
check('message names the file', osd[#osd]:find('Show - 01_00-12-34.321_nosubs.png', 1, true) ~= nil and osd[#osd]:find('saved') ~= nil)
check('no ffmpeg needed', not job('subprocess'))

print('B) mpv fails -> ffmpeg fallback; subtitles restored BEFORE ffmpeg runs')
reset(); fs[OUT] = nil; press(); adv(0.4)
finish(job('screenshot-to-file'), false, nil, 'error running command')
local ff = job('subprocess')
check('ffmpeg started as fallback', ff ~= nil)
check('subtitles restored already', props['sub-visibility'] == true)
local a = ff and table.concat(ff.t.args, ' ') or ''
check('ffmpeg cuts the same time from the same file into the same name', a:find('%-ss 754%.321') and a:find('/v/Show %- 01%.mkv') and a:sub(-#OUT) == OUT)
check('ffmpeg drops subtitle streams (-sn)', a:find(' %-sn ') ~= nil)
fs[OUT] = { size = 999 }; finish(ff, true, { status = 0 })
check('message says plain frame', osd[#osd]:find('plain frame') ~= nil)

print('C) mpv says OK but no file exists -> fallback')
reset(); fs[OUT] = nil; press(); adv(0.4)
finish(job('screenshot-to-file'), true)
check('fallback started because nothing was written', job('subprocess') ~= nil)
fs[OUT] = { size = 5 }; finish(job('subprocess'), true, { status = 0 })

print('D) both fail: visible error, key works again')
reset(); fs[OUT] = nil; press(); adv(0.4)
finish(job('screenshot-to-file'), false, nil, 'no disk')
finish(job('subprocess'), true, { status = 1, stderr = 'Invalid data', error_string = '' })
check('failure message with the reason', osd[#osd]:find('Screenshot failed') ~= nil and osd[#osd]:find('Invalid data') ~= nil)
check('subtitles restored', props['sub-visibility'] == true)
reset(); press(); check('next press starts a new shot', #timers == 1)

print('E) method=ffmpeg: subtitle settings are never touched')
OPT = { method = 'ffmpeg' }; dofile(script); press = handlers['ap-shot-nosubs']
reset(); fs[OUT] = nil; press()
check('goes straight to ffmpeg', job('subprocess') ~= nil and not job('screenshot-to-file'))
check('no set sub-visibility call', #log == 0)
fs[OUT] = { size = 5 }; finish(job('subprocess'), true, { status = 0 })

print('F) method=mpv: no silent fallback, error shown')
OPT = { method = 'mpv' }; dofile(script); press = handlers['ap-shot-nosubs']
reset(); fs[OUT] = nil; press(); adv(0.4)
finish(job('screenshot-to-file'), false, nil, 'boom')
check('error shown, no ffmpeg', not job('subprocess') and osd[#osd]:find('boom') ~= nil)
OPT = {}; dofile(script); press = handlers['ap-shot-nosubs']

print('G) a shot that never reports back cannot block the key for ever')
reset(); fs[OUT] = nil; press(); adv(0.4)           -- callback never comes
now = now + 25; log, osd, pending, timers = {}, {}, {}, {}
press(); check('after 20 s a new press is accepted', #timers == 1)

print('H) no subtitle track active -> burned-in hint')
reset(); fs[OUT] = nil; props.sid = false; press(); adv(0.4)
fs[OUT] = { size = 5 }; finish(job('screenshot-to-file'), true)
check('message explains burned-in text', osd[#osd]:find('burned into the video') ~= nil)

print('I) network stream: ffmpeg fallback is refused with a clear reason')
reset(); props.path = 'https://x/y.m3u8'; fs[OUT] = nil; press(); adv(0.4)
finish(job('screenshot-to-file'), false, nil, 'fail')
check('clear message', osd[#osd]:find('network streams') ~= nil)
props.path = '/v/Show - 01.mkv'

print('J) name collision gets a number')
reset(); fs[OUT] = { size = 1 }; press(); adv(0.4)
local jj = job('screenshot-to-file')
check('_2 suffix', jj and jj.t.filename == '/pics/Show - 01_00-12-34.321_nosubs_2.png')
fs[jj.t.filename] = { size = 1 }; finish(jj, true)

print(fails == 0 and '\nALL OK' or ('\nFAILURES: ' .. fails))
os.exit(fails == 0 and 0 or 1)

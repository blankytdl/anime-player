function round(x) return math.floor(x + 0.5) end
function clamp(a, v, b) return math.max(a, math.min(v, b)) end
Elements = {update_proximities = function() end}
function request_render() end
Controls = {}
dofile('/tmp/layout_fn.lua')
local function mk(kind, n, extra)
  local list = {}
  for i = 1, n do
    list[#list+1] = {sizing = 'static', scale = 1, ratio = 1, element = {enabled = true, set_coordinates = function(self, ax, ay, bx, by) self.ax, self.bx = ax, bx end, prop = extra and extra[i]}}
  end
  return list
end
local function run(width, nleft, ncenter, nright, size, spacing)
  local layout = {}
  layout[#layout+1] = {sizing = 'space'}
  for _, c in ipairs(mk('l', nleft)) do layout[#layout+1] = c end
  -- the speed dial: dynamic
  layout[#layout+1] = {sizing = 'dynamic', scale = 1, ratio = 3, ratio_min = 1.6, element = {enabled = true, set_coordinates = function(self, ax, ay, bx, by) self.ax, self.bx = ax, bx end}}
  layout[#layout+1] = {sizing = 'space'}
  local center = mk('c', ncenter, {nil, nil, 'pause'})
  for _, c in ipairs(center) do layout[#layout+1] = c end
  layout[#layout+1] = {sizing = 'space'}
  for _, c in ipairs(mk('r', nright)) do layout[#layout+1] = c end
  layout[#layout+1] = {sizing = 'space'}
  for _, c in ipairs(layout) do c.hide = false end
  local self = {layout = layout, ax = 0, bx = width, by = 40}
  local ok = Controls.layout_centered(self, size, spacing)
  local cmin, cmax, lmax, rmin, hidden = 1e9, -1e9, -1e9, 1e9, 0
  local lcount, rcount, ccount = 0,0,0
  for i, c in ipairs(layout) do
    if c.sizing ~= 'space' then
      if c.hide then hidden = hidden + 1 end
    end
  end
  for _, c in ipairs(center) do if not c.hide then cmin = math.min(cmin, c.element.ax); cmax = math.max(cmax, c.element.bx); ccount = ccount + 1 end end
  for i, c in ipairs(layout) do
    if c.sizing ~= 'space' and not c.hide and c.element.ax then
      local isc = false for _, x in ipairs(center) do if x == c then isc = true end end
      if not isc then
        if c.element.bx <= cmin then lmax = math.max(lmax, c.element.bx); lcount = lcount + 1
        elseif c.element.ax >= cmax then rmin = math.min(rmin, c.element.ax); rcount = rcount + 1 end
      end
    end
  end
  local centre_mid = (cmin + cmax) / 2
  print(string.format('width %4d size %2d: ok=%s center %4d..%4d (mid %.0f, bar mid %.0f) left ends %s right starts %s  hidden=%d  center shown=%d left=%d right=%d',
    width, size, tostring(ok), cmin, cmax, centre_mid, width / 2, lmax > -1e9 and lmax or '-', rmin < 1e9 and rmin or '-', hidden, ccount, lcount, rcount))
end
run(1920, 8, 5, 10, 36, 4)
run(1280, 8, 5, 10, 36, 4)
run(900, 8, 5, 10, 32, 4)
run(600, 8, 5, 10, 28, 3)
run(420, 8, 5, 10, 24, 2)
run(300, 8, 5, 10, 22, 2)

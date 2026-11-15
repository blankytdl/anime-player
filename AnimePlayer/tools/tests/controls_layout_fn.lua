
-- [AP7: centered transport fn] returns true when it laid out the bar.
-- Sections are split by `space` items: 4 spaces -> [lead][left][CENTER][right][tail]; 2 spaces -> [left][CENTER][right].
function Controls:layout_centered(size, spacing)
	local sections, cur = {{}}, 1
	local space_count = 0
	for _, control in ipairs(self.layout) do
		if control.sizing == 'space' then
			space_count = space_count + 1
			cur = cur + 1
			sections[cur] = {}
		else
			table.insert(sections[cur], control)
		end
	end
	local left, center, right
	if space_count == 4 then left, center, right = sections[2], sections[3], sections[4]
	elseif space_count == 2 then left, center, right = sections[1], sections[2], sections[3]
	else return false end
	if #center == 0 then return false end

	local function item_min(c)
		if c.sizing == 'static' then return size * c.scale * c.ratio end
		if c.sizing == 'dynamic' then return size * c.scale * c.ratio_min end
		return 0
	end
	local function item_max(c)
		if c.sizing == 'static' or c.sizing == 'dynamic' then return size * c.scale * c.ratio end
		return 0
	end
	local function group_min(list)
		local w, n = 0, 0
		for _, c in ipairs(list) do
			if not c.hide and c.sizing ~= 'gap' then w = w + item_min(c); n = n + 1 end
		end
		return w + math.max(0, n - 1) * spacing, n
	end
	-- hide from the middle of a group outwards (same order the build used) until it fits
	local function shrink(list, avail, keep)
		while group_min(list) > avail do
			local best, best_d
			local mid = (#list + 1) / 2
			for i, c in ipairs(list) do
				if not c.hide and c.sizing ~= 'gap' and c ~= keep then
					local d = math.abs(i - mid)
					if not best_d or d < best_d then best, best_d = c, d end
				end
			end
			if not best then break end
			best.hide = true
			if best.element then best.element.enabled = false end
		end
	end

	local total = self.bx - self.ax
	-- the center group: keep the play/pause button (a `cycle` on `pause`) until last; drop the outer buttons first
	local keep
	for _, c in ipairs(center) do
		if c.element and c.element.prop == 'pause' then keep = c end
	end
	do
		local outer = {}
		for i, c in ipairs(center) do outer[#outer + 1] = {c = c, d = math.abs(i - (#center + 1) / 2)} end
		table.sort(outer, function(a, b) return a.d > b.d end)
		for _, o in ipairs(outer) do
			if group_min(center) <= total then break end
			if o.c ~= keep then
				o.c.hide = true
				if o.c.element then o.c.element.enabled = false end
			end
		end
	end
	local center_w = group_min(center)
	local center_ax = self.ax + (total - center_w) / 2
	local side_gap = math.max(spacing * 3, round(size * 0.45))   -- [AP10: tight groups] left / right groups sit right next to the centre block
	local left_avail = math.max(0, center_ax - self.ax - side_gap)
	local right_avail = math.max(0, self.bx - (center_ax + center_w) - side_gap)
	shrink(left, left_avail)
	shrink(right, right_avail)

	local function place(list, start_x, avail, align_right)
		local mins, maxes, n = 0, 0, 0
		for _, c in ipairs(list) do
			if not c.hide and c.sizing ~= 'gap' then mins = mins + item_min(c); maxes = maxes + item_max(c); n = n + 1 end
		end
		local gaps_w = math.max(0, n - 1) * spacing
		-- dynamic items (the speed dial) take what is left, between their min and max width
		local fixed = 0
		local dyn_min, dyn_max = 0, 0
		for _, c in ipairs(list) do
			if not c.hide then
				if c.sizing == 'static' then fixed = fixed + item_min(c)
				elseif c.sizing == 'dynamic' then dyn_min = dyn_min + item_min(c); dyn_max = dyn_max + item_max(c) end
			end
		end
		local dyn_w = clamp(dyn_min, avail - fixed - gaps_w, dyn_max)
		local total_w = fixed + gaps_w + (dyn_max > 0 and dyn_w or 0)
		local x = align_right and (start_x - total_w) or start_x
		for _, c in ipairs(list) do
			if not c.hide and c.sizing ~= 'gap' then
				local h = size * c.scale
				local w
				if c.sizing == 'dynamic' then
					w = dyn_max > 0 and (dyn_w * (item_max(c) / dyn_max)) or 0
				else w = h * c.ratio end
				if c.element then c.element:set_coordinates(round(x), round(self.by - h), round(x + w), self.by) end
				x = x + w + spacing
			end
		end
	end
	place(left, center_ax - side_gap, left_avail, true)
	place(center, center_ax, center_w, false)
	place(right, center_ax + center_w + side_gap, right_avail, false)

	Elements:update_proximities()
	request_render()
	return true
end


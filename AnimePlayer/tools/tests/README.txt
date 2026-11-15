Small simulations of the player's Lua logic, run with any Lua 5.1-5.4 (e.g. `lua queue_staging_test.lua`; TeX Live's `texlua` works too).
They fake mpv (playlist, events, properties) and print what the scripts do. Paths inside are the author's sandbox paths (/tmp, /home/claude): edit them first.
  queue_staging_test.lua      ap-queue.lua: hidden "next" queue video, no duplicates, shuffle / sort / clear, playing row, double-click guard
  controls_layout_test.lua    the centred control-bar layout at 1920 ... 300 px wide (needs controls_layout_fn.lua, cut from Controls.lua)
  keybinds_editor_test.lua    ap-keybinds.lua: change key / conflict / unbind on a scratch input.conf

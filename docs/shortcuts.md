# Shortcut action catalog

Canonical action IDs used by the web UI and JSON profiles. Profiles map these IDs to keys; the UI keeps button positions stable across GSPro and Pureplay.

| Action ID | Typical GSPro key | Group |
|-----------|-------------------|-------|
| `prev_club` | `k` | play |
| `next_club` | `i` | play |
| `putt_toggle` | `u` | play |
| `mulligan` | `ctrl+m` | play |
| `reset_aim` | `a` | play |
| `aim_left` / `aim_right` / `aim_up` / `aim_down` | arrows | play |
| `tee_left` / `tee_right` | `c` / `v` (GSPro; Pureplay may still use `[` / `]`) | play |
| `heat_map` | `y` | green |
| `green_grid` | `g` | green |
| `vertical_dots` | `d` | green |
| `pin_indicator` | `p` | green |
| `range_finder` | `r` | green |
| `aim_point` | `f3` | green |
| `shot_cam` | `j` | cameras |
| `flyover` | `o` | cameras |
| `free_look` | `f5` | cameras |
| `cam_go_to_ball` | `5` | cameras |
| `cam_fly_to_ball` | `6` | cameras |
| `goto_ball` | `8` | cameras |
| `scorecard` | `t` | round |
| `prev_hole` / `next_hole` | `9` / `0` | round |
| `clear_tracers` | `f1` | round |
| `hud_toggle` | `h` | round |
| `toggle_ui` | `esc` | round |
| `fullscreen` | `f11` | round |

Pureplay mappings start aligned with the same catalog. Adjust [`profiles/pureplay.json`](../profiles/pureplay.json) on your bay if a key differs in your build.

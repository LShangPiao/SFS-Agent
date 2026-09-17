# SFS-Agent

<p align="center">
  Developed and maintained by <b>Galaxy Exploration Studio</b> (星河拓航工作室)
</p>

<p align="center">
  <a href="README.md">中文</a> · <b>English</b>
</p>

An **in-game mod for Spaceflight Simulator** that opens a mostly read-only HTTP
endpoint on `127.0.0.1:21578`, exposing game state to external programs — for
example the N.E.K.O.
[Spaceflight Simulator Assistant](https://github.com/LShangPiao/n.e.k.o_plugin_sfs_bridge)
plugin.

It lets an AI assistant:

- read **flight telemetry** (height, speed, throttle, stage, mass, …)
- **screenshot** the current frame
- read the **rocket's part breakdown**
- **enumerate and click UI buttons** (main menu, save list, build menu, settings)
- **place parts at a given coordinate** (no dragging needed)
- send a small set of **flight commands** (throttle, staging, RCS)

## Scope: a general-purpose bridge, not tied to any assistant

This mod has exactly one job: **expose Spaceflight Simulator's capabilities over
HTTP**. It does not know — and should not know — who is on the other side. Any
program can talk to it: your own script, another desktop assistant, a debug tool.

It therefore contains **no** assistant-specific prompts, tool naming, or
conversation logic. The adapter layer aimed at the N.E.K.O. assistant (tool
descriptions, operating guide, anti-hallucination guidance) lives in a separate
repository:

> <https://github.com/LShangPiao/n.e.k.o_plugin_sfs_bridge>

| | SFS-Agent (this repo) | sfs_bridge (plugin repo) |
| --- | --- | --- |
| Form | In-game C# mod | N.E.K.O. Python plugin |
| Role | **Shared** capability layer: telemetry / screenshot / input / building | **Specific** adapter layer: tools and prompts for the assistant |
| Audience | Any program | N.E.K.O. |
| Depends on | The game only | This mod + N.E.K.O. |

## Built-in config page

After the mod loads it opens <http://127.0.0.1:21578/> in your default browser:
live status, the full endpoint list, and SFS default controls — so you can verify
the bridge works without writing any code.

Auto-open can be turned off. The config file is `sfs-agent.ini` next to the DLL
(created on first run):

```ini
# Bridge port (game restart required)
port=21578
# Open this page in the browser on game start
open_browser=true
# Enter Agent Exclusive Mode
exclusive_input=false
# Show the on-screen overlay while exclusive
overlay=true
```

Missing config falls back to defaults — the mod never refuses to work because
of a missing setting.

The config page can **edit and save** all of these directly: it renders **every**
key found in the ini, so you can add your own entries. Saving only overwrites the
keys that changed, never wiping the rest.

## Agent Exclusive Mode

`POST /exclusive {"on":true}` (or set `exclusive_input=true` and restart) makes
the game **accept only agent input**:

- the user's own mouse clicks and key presses are swallowed (Harmony patches the
  `Input` mouse/keyboard queries)
- keys and clicks injected by the agent keep working
- the screen shows a top and bottom **light-blue gradient** plus
  **"Agent controlling"** (follows the `lang` setting, zh/en)
- To release: **flip the switch on the config page**, or press **F10** as an
  escape hatch, or `POST /exclusive {"on":false}`

> There is **deliberately no release button** in the game window. The release
> path is centralised on the config page, so you never have to hunt for a button
> on the game screen while input is locked.

> Implementation note: the overlay is built via reflection using
> **Canvas + Image/Text**. `OnGUI` is not used because it requires an `OnGUI`
> method on a MonoBehaviour, and this mod's base class is not a MonoBehaviour and
> has no suitable attachment point.

## Install

Download `SFS-Agent.dll` from
[Releases](https://github.com/LShangPiao/SFS-Agent/releases) and place it
following SFS's one-directory-per-mod convention:

```
<Steam>\steamapps\common\Spaceflight Simulator\Spaceflight Simulator Game\Mods\SFS-Agent\SFS-Agent.dll
```

> ⚠️ It must live in the `Mods\SFS-Agent\` **subdirectory**, with the file name
> matching the directory name. Do not flatten it into `Mods\` and do not keep
> other old mod directories around — two mods will fight over port 21578 and the
> one you connect to will not be the one you expect.

**Then restart the game** — the mod only loads at game start.

Verify the install:

```powershell
curl http://127.0.0.1:21578/ping
# {"ok":true,"mod":"sfs_agent","version":"0.4.5","key_injection":"on",...}
```

## Build from source

Requires the `csc.exe` bundled with Windows (.NET Framework 4.x) and an installed
copy of the game.

```powershell
pwsh -File build.ps1
```

The script will:

1. copy `Assembly-CSharp.dll` and `0Harmony.dll` from the game install into a
   local `refs/`
2. compile `src/*.cs` into `dist/SFS-Agent.dll` with `csc`
3. deploy to the game's `Mods\SFS-Agent\` and clean up conflicting legacy mod
   directories

> SFS's Unity modules reference **netstandard 2.1**, which .NET Framework's `csc`
> does not support. This mod therefore **references no UnityEngine assembly** and
> talks to the game purely through reflection + Harmony dynamic patches. Only C# 5
> syntax is available — do not use expression-bodied members (`=>`), string
> interpolation, or `?.`.

## HTTP API

Listens on `127.0.0.1` only; not exposed to the LAN or the internet.

| Method | Path | Description |
| --- | --- | --- |
| GET | `/` | **Built-in config page** (HTML dashboard + editable config, auto-opened on game start) |
| GET | `/config` | Read all config (arbitrary key=value) |
| POST | `/config` | Merge-write config (only keys present in the request are touched) |
| POST | `/camera` | Camera control: `x` / `y` / `distance` / `zoom_delta` / `rotation` |
| POST | `/exclusive` | Toggle Agent Exclusive Mode (`{"on":true}`; omitted = toggle) |
| GET | `/ping` | Liveness; returns mod name, version and key-injection state |
| GET | `/health` | Health check |
| GET | `/log` | **Runtime log** (`since=<seq>` for incremental fetch, `clear=1` to clear, `limit`) |
| GET | `/gamelog` | Game-log forwarding status (`on=0` / `on=1` to toggle) |
| GET | `/settings` | **Read the game's own settings** (volume / video / fps, 10 items) |
| POST | `/settings` | Change a game setting: `{"key":"fps","value":60}` |
| GET | `/state` | Flight telemetry |
| GET | `/build` | Rocket part breakdown |
| GET | `/build_catalog` | Available part names (by default runs the game's own `LoadParts()` on the main thread for the full list) |
| GET | `/blueprints` | Blueprints saved by the player |
| POST | `/blueprint_load` | Load a whole blueprint into the build scene by name (the most reliable way to build) |
| GET | `/ui` | Currently clickable UI elements |
| GET | `/screenshot` | Capture the current frame as PNG |
| POST | `/command` | Flight commands: `set_throttle` / `throttle_on` / `throttle_off` / `stage` / `staging_program` / `rcs_on` / `rcs_off` / `rcs_toggle` |
| POST | `/ui_click` | Click a UI element by index (in-game dispatch) |
| POST | `/click` | Click by normalised coordinate (in-game dispatch) |
| POST | `/click_raw` | Same, but via Win32 real-mouse simulation (**steals mouse and focus**; troubleshooting only) |
| POST | `/key` | Send a key (in-game injection; the game need not be focused) |
| POST | `/key_raw` | Same, but via Win32 `keybd_event` (**steals focus**; troubleshooting only) |
| POST | `/build_place` | Place a part directly at a build-grid coordinate (no dragging) |
| POST | `/scroll` | Scroll (Win32; the wheel event goes to the window under the cursor) |
| POST | `/debug_methods` | Diagnostic: dump reflection info for one element |
| POST | `/debug_parts` | Diagnostic: dump the various part-name sources |

> Everything that touches a Unity API runs on the **main thread** (HTTP threads
> only enqueue work or set flags). This constraint is hard: calling
> `Resources.LoadAll` from an HTTP thread previously hard-crashed the game
> (a native access violation that managed try/catch cannot intercept).

### `/state` sample response

```json
{"ok":true,"in_world":false,"flying":false,"rocket":"","planet":"",
 "height":0,"speed":0,"velocity_x":0,"velocity_y":0,
 "throttle":0,"throttle_on":false,"stage":-1,"has_control":false,"mass":0}
```

### `/ui` sample response

```json
{"ok":true,"count":9,"elements":[
  {"index":4,"label":"Play","x":0.5,"y":0.481},
  {"index":3,"label":"Settings","x":0.5,"y":0.5843}
]}
```

`x`/`y` are **normalised coordinates** relative to the game window (0–1, origin
at the top-left). Greyed-out buttons (`buttonEnabled == false`) are filtered out
automatically — that is why `Play` / `Rename` / `Delete` do not appear in the
save list until you select a save.

## Clicking does not steal your mouse

`POST /ui_click` and `POST /click` **do not move the system cursor** and do not
require the game window to be focused.

They call SFS's own input dispatch entry points:

```
SFS.Input.InputManager.CheckMouseOverState(TouchPosition)
SFS.Input.InputManager.InputStart(index, InputType, TouchPosition)
SFS.Input.InputManager.TouchEnd (index, InputType, TouchPosition)
```

`InputManager` performs the hit test itself and writes the result into
`mouseOverElement`, therefore:

- **`CheckMouseOverState` must be called first** — without it, `InputStart` would
  use the previous (usually null) hovered element and the click would do nothing
- hit testing is done by the game, far more accurate than comparing button rects
  ourselves
- it goes through the game's native button dispatch, so every wiring style
  (`clickEvent` / `onClick`) fires correctly

> A detour worth recording: directly `Invoke`-ing a button's `clickEvent`. It hits
> some buttons, but **when a button wires its logic to `onClick`
> (`OptionalDelegate`) it reports success while doing nothing** — observed with
> the Cancel button of the Esc quit-confirmation dialog. That is a "false
> success". Everything now goes through `InputManager`; `clickEvent` remains only
> as a last resort when no coordinate is available.
>
> Another trap: at runtime, `GetMethods()` **cannot enumerate explicit interface
> implementations** such as `CheckMouseOverState` (offline reflection on the same
> `Assembly-CSharp.dll` can). Lookups here therefore match by method name +
> parameter count, and field access uses `GetFields()`, which does work at
> runtime.

The press and release phases of a click happen on **separate frames** (a real
click has a press-release sequence anyway), so `/click` waits for the state
machine to finish before replying.

## Keys do not steal focus either

`POST /key` injects keys by Harmony-patching
`UnityEngine.Input.GetKey / GetKeyDown / GetKeyUp`:

- only the keys currently being injected have their return value overridden;
  every other key falls through to the original method
- the game's own input logic is untouched, so staging, steering and menus behave
  natively
- **no system input is touched and the game need not be focused** (verified: with
  the game in the background, pressing Esc still opened the quit confirmation)
- on any error the prefix lets the original method run — game input is never
  broken

`vk` takes a virtual key code (the same numbering as `UnityEngine.KeyCode`); pass
`hold_ms` to control how long the key is held. The `key_injection` field of
`GET /ping` reports honestly whether the patches are installed.

## Building rockets: prefer loading a blueprint

Rocket designs are stored as **blueprint files** in
`Saving/Blueprints/<name>/Blueprint.txt`, JSON carrying part sizes and staging:

```jsonc
{
  "center": 7.0,
  "parts": [
    {
      "n": "Fuel Tank",                       // internal part name
      "p": { "x": 7.0, "y": 8.0 },            // attachment-point coordinate
      "o": { "x": 1.0, "y": 1.0, "z": 0.0 },  // orientation: x/y are surface offsets, z is an angle
      "t": "-Infinity",
      "N": { "width_original": 2.0, "width_a": 2.0, "width_b": 2.0,
             "height": 4.0, "fuel_percent": 1.0 },   // ← size and scaling
      "T": { "color_tex": "_", "shape_tex": "_" }    // ← textures
    }
  ],
  "stages": [ { "stageId": 1, "partIndexes": [5, 6] } ]
}
```

The mod takes the **game's own loading path** rather than assembling parts itself:

```
Blueprint_Saving.GetBlueprintsList()          -> list of blueprint names
Blueprint_Saving.LoadBlueprint(name, cb)      -> the game parses it (callback gives a Blueprint)
BuildState.main.SpawnBlueprint(blueprint, …)  -> spawns into the build scene
```

Measured loading a player's 141-part rocket: mass 712.4 t, thrust 356 t,
thrust-to-weight 0.51 — matching the blueprint exactly.

> **Why not assemble `PartSave` yourself**: `N` (size), `T` (textures) and
> `stages` are all mandatory. With only a name and a coordinate, a part has no
> size and belongs to no stage — the observed result is that the rocket falls
> apart on launch, drills straight into the ground, and reports 0 t of thrust
> forever. `POST /build_place` now copies `N`/`T` from the game's loaded part
> table and registers a stage, but **the placement still needs correct attachment
> spacing**, so prefer blueprints over hand-assembly.

## Placing a single part (`/build_place`)

In the build screen parts must be **dragged** from the left-hand menu onto the
rocket; clicking alone cannot place them.
`POST /build_place {"name": "...", "x": 0, "y": 0, "stack": "top"}` constructs the
game's own data structures directly:

```
SFS.Parts.PartSave { name, position, orientation, NUMBER_VARIABLES, TEXT_VARIABLES }
  -> SFS.Builds.Blueprint(parts, stages, center, rotation, interiorView)
    -> SFS.Builds.BuildState.main.SpawnBlueprint(blueprint, applyUndo, logger)
```

`stack` is optional: `top` / `bottom` **compute the position from an existing
part's attachment point**; `none` (default) uses the `x`/`y` you provide.

The part name must be valid (query `GET /build_catalog` first):

```jsonc
// GET /build_catalog
{"ok":true,"ready":true,"count":66,"source":"scene_parts+PartsLoader.LoadParts()",
 "parts":["Fuel Tank","Engine Valiant","Cone","Probe", ...]}
```

Note that the **internal name differs from the UI display name** (UI
"Valiant Engine" = internal `Engine Valiant`; UI "Aerodynamic Nose Cone" =
internal `Cone`). The catalog returns internal names.

`source` reports where the part names came from, which helps with troubleshooting.
`POST /build_place` validates the name against the catalog first and **refuses
anything uncertain** rather than handing unverified data to the game internals.

Other known facts (game v1.6.00.16):

- menu buttons have runtime type `SFS.UI.ButtonPC : SFS.UI.Button : MonoBehaviour`
- `SFS.UI.Button` fields: `clickEvent` (`SFS.UI.ClickUnityEvent` :
  `UnityEvent<OnInputEndData>`), `onClick` / `onUp` / `onRightClick`
  (`OptionalDelegate<OnInputEndData>`), `buttonEnabled` (`bool`)
- `OnInputEndData(InputType, TouchPosition, bool click)`; `InputType`:
  `Touch=0, MouseLeft=1, MouseRight=2`
- `FindObjectsOfType` cannot find components on inactive objects (e.g. the part
  menu `PickGridUI`); retrieve the instance through its owner field instead
  (`BuildManager.main.pickGrid`)
- `PartsLoader.parts` / `partVariants` **are null** in v1.6.00.16; part names must
  come from the **return value** of `PartsLoader.LoadParts()` (that method does
  not write the static field)

## SFS default controls

For reference by an upper-layer AI or a human (pass the matching virtual key code
to `POST /key`):

| Action | Key | VK |
| --- | --- | --- |
| Turn left / right | Q / E | 81 / 69 |
| Translate and pitch (RCS on first) | W / A / S / D | 87 / 65 / 83 / 68 |
| Throttle up / down | Shift / Ctrl | 16 / 17 |
| RCS toggle | R | 82 |
| Ignite / next stage | Space | 32 |
| Staging program | Enter | 13 |
| Back | Esc | 27 |

> Throttle and staging need not go through keys at all: `POST /command` with
> `set_throttle` / `throttle_on` / `throttle_off` / `stage` / `rcs_toggle`
> modifies game state directly and is the most reliable path.

## Verified capabilities (in-game testing)

| Capability | Status |
| --- | --- |
| Flight telemetry (height / speed / throttle / stage / mass / controllable) | ✅ |
| Menu clicking: main menu → save list → world → build | ✅ including multi-step confirmation dialogs |
| UI element enumeration (off-screen and inactive filtered out) | ✅ |
| Key injection (Esc / Space / Q, **game need not be focused**) | ✅ |
| Flight control: ignition, throttle, staging, RCS | ✅ measured speed rising under throttle |
| Building: **load a whole blueprint** (sizes/textures/staging included, parsed by the game) | ✅ measured 141 parts, 712.4 t, 356 t thrust |
| Building: list blueprints saved by the player | ✅ 20 found |
| Building: place a single part at a coordinate + camera follows | ✅ part count and mass exactly +1 |
| Screenshot | ✅ |
| Built-in config page + auto-open browser on start | ✅ |
| **Runtime log** (status tags / poll folding / incremental fetch) | ✅ |
| **Player action log** (key presses) | ✅ |
| **Flight data log** (altitude / attitude / orbital elements) | ✅ |
| **State change detection** (screen / scene / world / rocket) | ✅ |
| **Game log forwarding** (Player.log merged into the same panel) | ✅ |
| **Read/write game settings** (volume / fps / FXAA / orbit lines etc.) | ✅ |
| **Hot config apply** (including port switch, no game restart) | ✅ |
| **Orbital elements** (apsides / eccentricity / period / true anomaly / time to apsis) | ✅ |
| **Multi-body aware** (radius / GM / atmosphere all read from the game) | ✅ |
| **Hand-assembling a flyable rocket from scratch** | ⚠️ see Known limitations |

## Known limitations

- **Hand-assembling a rocket from scratch is still incomplete; load a blueprint
  instead.** A blueprint's part coordinates are **attachment points**, and
  spacing is determined by each part's own height — but the height variable name
  is not consistent (`height` / `width` / `size` / `height_max`, and some parts
  have none at all). Rocket built with a fixed spacing (e.g. always 4.0) fell
  apart on launch: the parts above dropped and blew the rocket up.
  `stack=top/bottom` attempts to attach using `N.height`, but that value is not
  always reliable. **If you can use a blueprint, use a blueprint.**
- `build_place` performs **no snapping or collision checks**; overlaps are
  possible.
- UI enumeration filters out **off-screen** and **inactive** elements; an element
  **occluded by another screen** cannot be detected at enumeration time —
  detecting it would require calling the game's own hit test
  (`InputManager.CheckMouseOverState`), which writes `mouseOverElement` for every
  element and makes all buttons highlight in sequence (observed; visible to the
  user). Occlusion is therefore verified **once at click time**, and a mismatch is
  reported in the `warning` field of the response.
- An element clipped inside a scroll view can still have its rect centre inside
  the screen, so enumeration may report it spuriously; the click-time `warning`
  catches that case.

## Notes

- The mod only performs read-only telemetry collection plus a small set of
  commands; it **does not modify save files**.
- `build_place` really does spawn parts into the current build scene; if you do
  not want that, work in a throwaway save.
- Spaceflight Simulator is a commercial game by Team Curiosity. This project is
  unaffiliated with the developers and distributes a third-party mod of our own
  making; the repository **contains no game files**.

## About Galaxy Exploration Studio

This mod is developed and maintained by **Galaxy Exploration Studio**
(星河拓航工作室).

Galaxy Exploration Studio is an informal online spaceflight outreach group made
up of space enthusiasts from all over, most of them students. We want to help
more people learn about real spaceflight in a fun and reliable way.

- Website: <https://xhth.top/>
- Bilibili: <https://space.bilibili.com/3546949529635067>
- Contact: <contact@xhth.top>

All comrades are welcome to join us, and feedback or bug reports are always
appreciated.

## License

This project is licensed under the
[GNU General Public License v3.0](LICENSE).

Copyright (C) 2026 Galaxy Exploration Studio (星河拓航工作室)

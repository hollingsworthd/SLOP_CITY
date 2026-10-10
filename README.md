# SLOP CITY — Daniel’s playable lounge

A compiled Apple Silicon Mac lounge prototype and editable Blender environment for **1984 DRH / Fishticklesworth / Beefcake / Daniel**. Home base is a deep teal couch in a warm loft above a dystopian city, with Machine Publishers on the TV and Hood Rich Records throughout the room.

Download the compiled Mac app from the [v0.1.0 release](https://github.com/hollingsworthd/slop-city/releases/tag/v0.1.0). This repository contains the Blender and Unity source; generated executables and editor caches are excluded from Git.

## Play on Mac

Download `SlopCity-macOS-AppleSilicon.zip` from the release, unzip it, and double-click **SLOP CITY.app**, then choose **Play**. After a local build, the app is at `Unity/Builds/macOS/SLOP CITY.app`. Use **WASD** or arrow keys to walk, the **mouse** to look, **E** to stand/sit/interact, **R** to return to the couch, and **Esc** to pause or resume. Get up, inspect the broadcast, and return home to complete the first lounge loop.

The standalone app runs without Unity Editor, Unity Hub, or a Unity license. Those tools and an activated license are needed only to edit or rebuild the Unity project. This is a local single-player lounge prototype; the complete networked MMORPG, mobile releases, and console releases are future work.

## Open the Blender scene

Open `SlopCity_CouchSpawn.blend` in Blender 4.5 LTS or later. The file opens in **first person from the couch with a clear, unobstructed view**. It contains the authored geometry, editable text, procedural materials, packed font, three cameras, and lighting. All dimensions use metres.

- `CAM_03_Daniel_Seated` (default): the player’s eye position in the VR headset, facing the TV.
- `CAM_01_CouchSpawn_Hero`: the full cutaway room.
- `CAM_02_Comfort`: cushions, piping, throw blanket, and personal effects.

Collection **07 • INTERIOR WALLS — hide for cutaway** contains the front and right walls, ceiling, and an interior light. Collection **08 • DANIEL’S VR HEADSET** contains the headset geometry. The interior walls are visible by default. The headset is hidden from the first-person viewport/render, retaining the model without black visor curves. Hide the interior-wall collection as well when using either cutaway camera. The headset is a visual model and camera rig; this scaffold does not yet implement stereo XR or tracked controllers.

The room includes car keys, sauce packets, a Roblox-inspired coding laptop, fictional 2026 Project Blue Book notes, records, a radio, and a Machine Publishers broadcast screen. These are authored props based on Daniel’s supplied themes, not a reconstruction of his actual home. No likeness or additional biographical details are inferred.

## Files

| File | Purpose |
| --- | --- |
| GitHub release / `Unity/Builds/macOS/SLOP CITY.app` after rebuilding | Compiled Apple Silicon Mac game |
| `SlopCity_CouchSpawn.blend` | Editable native source |
| `renders/01-couch-spawn-hero.png` | Full room render |
| `renders/02-couch-comfort.png` | Couch detail render |
| `renders/03-seated-spawn.png` | Seated player view |
| `Unity/Assets/Art/SlopCity_Lounge.fbx` | Unity model with named spawn anchors |
| `assets/SlopCity_Lounge.glb` | Portable model with base materials |
| `Unity/README.md` | Unity setup and controls |
| `scripts/build_scene.py` | Reproducible scene, render, and export generator |
| `scripts/couch.py` | Upholstery, seams, cushions, blanket geometry |
| `scripts/vr_headset.py` | Daniel’s modeled VR headset and visor rim |
| `scripts/validate_assets.py` | Native and reimport validation |

## Clone and edit

Clone this private repository with your GitHub account, then open the `Unity` subfolder in Unity Hub using **6000.3.26f1**. The saved scene is `Unity/Assets/Scenes/SlopCity_CouchSpawn.unity`. Unity regenerates its local `Library` cache on first open.

```sh
gh repo clone hollingsworthd/slop-city
```

The repository preserves Unity `.meta` files, package locks, Blender source, exports, renders, and build scripts. No Git LFS setup is required for this initial version.

## Unity game project

`Unity` is a Unity 6.3 LTS project with a successfully compiled Apple Silicon Mac app, a repeatable build command, first-person controller, menus, local objectives, and touch input scaffolding. Open `Unity/README.md` for controls and rebuild instructions, and `Unity/PLATFORM_ROADMAP.md` for Mac → iOS/Android → Windows/Linux → console sequencing.

The curved black visor lines have been removed from the default first-person render. The headset equipment remains editable/exportable, while its local-player renderers are disabled in Unity. Full tracked VR and the networked MMO are not implemented by this small lounge prototype.

## Source media

- [@beef_talko](https://www.tiktok.com/@beef_talko)
- [Machine Publishers](https://www.tiktok.com/@machine_publishers)
- [Original: In-Q-Tel — 1984DRH & Hood Rich Records](https://www.tiktok.com/@machine_publishers/video/7573075941463756062)
- [Daniel’s duet](https://www.tiktok.com/@beef_talko/video/7689137720760864030)

The `.blend` contains original geometry and typography. It does not contain downloaded TikTok video or commercial audio.

## Verified

The native scene and clean Blender reimports of both FBX and GLB passed validation: correct metre scale, six unique spawn/interaction markers, 936 exported meshes, retained headset hierarchy, first-person opening camera, and packed resources. See `assets/validation-report.json`. The three rendered views were visually inspected. These checks do not substitute for Unity Play Mode or XR device testing.

The Mac application has compiled successfully. Unity build and scene-validation receipts are in `Unity/Builds/Reports`; interactive gameplay and device testing are separate checks.

## Rebuild

From this directory, run Blender’s executable with:

```sh
blender --background --factory-startup --python scripts/build_scene.py
blender --background --factory-startup --python-exit-code 1 --python scripts/validate_assets.py
```

On this machine the executable is `../.tools/Blender.app/Contents/MacOS/Blender`. The generator overwrites this kit’s native scene, renders, and exports; save manual edits under a different name before rebuilding.

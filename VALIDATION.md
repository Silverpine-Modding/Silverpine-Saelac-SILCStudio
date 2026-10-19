# 0.2.1 verification

## Public multi-platform release, 2026-10-18

- Fresh self-contained single-file publishes succeeded for **win-x64,
  win-arm64, linux-x64, linux-arm64, osx-x64, and osx-arm64**, with no publish
  warnings/errors. All use the same 0.2.1 source and .NET 10.0.12 runtime.
- The non-Windows binaries no longer embed the unused Windows artwork helper.
  Windows ARM64 includes the x64 helper and needs Windows 11 x64 emulation for
  extraction. Linux/macOS use Python 3 with the documented optional dependencies.
- **1,050 full checks**, **91 fixture-only checks**, and **4 Python sprite tests**
  passed again. The 193 live SILCs and save were only read. The missing Daniel
  reference is the same pre-existing validation warning.
- The full check read 10,155 cells / 16,581 objects, extracted 90 prefab types,
  and verified the 2-table/4-chair group. Two sprite-less utility markers remain
  unavailable. First render/PNG: 274 ms; cached draw: 2 ms on this machine.
- The fresh Windows x64 release was running, responsive, and had Skia loaded
  in a hidden startup check against project-local fixtures. Only that test
  process was stopped. The executable reports version 0.2.1.0.
- The other five targets are **cross-built, not runtime-tested**. In particular,
  macOS signing/Gatekeeper, Linux desktop dependencies, and ARM64 behavior need
  native-system testing. No signing or notarization is claimed.
- Release packaging checks PE/ELF/Mach-O processor headers, archive contents,
  extracted executable hashes, and Unix mode 0755. No settings, personal paths,
  saves, definitions, debug symbols, or extracted world-art caches are packaged.
- `SHA256SUMS.txt` accompanies all six platform archives and the source archive.
  Third-party license notices and installation instructions accompany each build.

Public-release Windows x64 executable SHA-256:
`09C7428635F24E5BF41EE7130BD29C3CF7BCA6354FFE15ABC017CEE523C112D8`

## Local pre-release build, 2026-10-17

Verified on Windows on 2026-10-17. The published Windows EXE reports 0.2.1.0;
the previous 0.2.0 EXE was retained under `publish/win-x64/Previous versions`.

- **1,050 checks passed** against the same 193 installed SILCs. No live SILCs
  or saves were modified. The existing missing Daniel character reference
  remains a validation warning, not an automatic repair.
- **91 fixture-only checks passed**, plus four Python sprite-composition tests.
- Main-window integration covers opening the embedded map from a routine,
  applying an ID change, refreshing the regular editor, and invalidating a
  Dynamic Lua selector. No separate map window is created.
- Map-session tests cover all destination/waypoint markers, overlapping-point
  selection, direct activity/component edits, multi-routine draft retention,
  Apply/Cancel, invalid inputs on a non-selected routine, and conditional
  Patrol controls. Preview toggles do not write SILCs.
- Pixel tests verify furniture above rugs, water above floor tiles regardless
  of save enumeration, globally sorted ambient shadows below furniture,
  shadow/vegetation toggle cache invalidation, and Y-sort offset rounding.
- The game TagManager defines Floors → Shadows → Default → Characters → FOV.
  Native prefab metadata confirms floors/order 100, rugs and water/order 101,
  and table YSorter offset -100. Sprite renderers are extracted separately,
  preserving global layer/order and the game's `round(-Y * 10) + offset` rule.
- The fixed bathhouse pool has no serializable component. Following
  `SerializationManager.Load`, 20 water tiles are read from the actual scene
  transforms and added only to already represented locations in the preview.
  Tests confirm the read-only saved map is not mutated.
- Native extraction returned 90 prefabs with zero conversion errors. The only
  two unavailable requested prefabs are the same sprite-less utility markers.
  The western rug area contains **2 tables and 4 chairs**, verified in saved
  placements and the rendered view. Bathhouse water is visible above the floor.
- The last full run parsed 10,155 cells / 16,581 objects in 59 ms. First detailed
  rendering plus PNG encoding took 357 ms; cached drawing took 3 ms. These are
  local off-screen measurements, not hardware-independent performance promises.
- Inspected rendered views include furniture, the bathhouse, vegetation-hidden
  terrain, embedded map/patrol/non-patrol/component panels, and minimum sizing.
- Self-contained win-x64, linux-x64, and osx-arm64 publishing succeeded.
  Linux/macOS are cross-published, not runtime-tested on those systems.
- The updated Windows executable passed a hidden startup test with project-local
  fixtures: running, responsive, and Skia loaded. Only the test-owned process
  was terminated. The rebuilt image-only helper is embedded in the Windows EXE.

Windows EXE SHA-256:
`5C6CE2F3E28B12585D752D81C5B0CE4AD11771DAAAD69CDFC4511375E905A6E9`

## Previous 0.2.0 baseline

Verified on the Windows development machine on 2026-10-17.

- Main cross-platform project: .NET 10, Avalonia 11.3.12. No WinForms/GDI+ bitmap dependency in the production build.
- Self-contained single-file publishing succeeded for win-x64, linux-x64, and osx-arm64 with no build warnings/errors.
- Linux and macOS binaries were cross-published, **not run on those operating systems**. Their file dialogs, desktop integration, and artwork Python environment still need native-system testing.
- Windows packaged executable loaded the Skia renderer and remained responsive in a hidden startup smoke test outside the execution sandbox. Only the test-owned process was closed.
- NuGet's vulnerability audit reported no known vulnerable packages using the configured sources. Tmds.DBus.Protocol is explicitly pinned to the patched 0.21.3 version.

## Preservation and UI regression tests

The portable suite passed **1,000 checks** against 193 existing definitions:
87 NPCs, 104 characters, and 2 model definitions. No live SILCs or saves were modified.

192 definitions passed existing game-style validation. The remaining pre-existing issue was NPC_Daniel referencing a missing Daniel character definition. Its data was preserved, not repaired automatically.

Coverage includes:

- No-edit byte preservation of parsed definition fields, semantic save/reload, and unchanged source-file hashes.
- Numeric precision, custom racial whitespace/order/duplicates, unknown fields, and named facing directions.
- Lighting minutes-to-turns conversion, half-minute preservation, and invalid duration rejection.
- Patrol encoding, runtime cursor reset only on waypoint edits, map draft Apply/Cancel, and negative coordinate transforms.
- Portrait preview options not marking files dirty or changing serialized data.
- Expression top-strip replacement with transparency and dimension validation.
- Exact backup bytes and atomic save/reload using project-local fixtures.
- Nonempty object validation for model JSON.
- Rendered inspections of the library, all definition tabs, routine components, portrait guides, map overview/detail, and the minimum window size.

The fixture-only mode passed **45 checks** without any game installation or personal SILCs.
The retained legacy WinForms suite also passed: 193 files scanned, 0 failures, 1 existing validation warning.

## Save map and artwork

One real save loaded 10,155 occupied map cells / 16,581 serialized objects in approximately 31 ms on this machine. It had one serialized world region.

89 of 91 requested prefab sprites were extracted from the local Unity game files. The two unavailable entries were utility markers without usable sprite artwork:
prefab_utility_autoturnback and prefab_utility_gatheringspot. The extractor reported no sprite conversion errors.

At the tested 1320×850 viewport, the first detailed render plus PNG encoding took approximately 275 ms. A cached redraw excluding PNG encoding took approximately 2 ms. These are local off-screen measurements, not a frame-rate guarantee on other hardware.

Repeated panning and zooming kept the chunk cache at or below 24 bitmaps. The decoded sprite cache has a separate 32 MiB limit. Only saved locations are shown; the renderer does not simulate pathfinding, game lighting, procedural generation, or dynamic object states.

The source README documents the optional Python dependencies for artwork extraction on macOS/Linux. All extracted world artwork remains in a local cache, not in release builds.

# Silverpine SILC Studio · 0.2.1

A native desktop editor for Silverpine SILCs, built with Avalonia and .NET 10.
The **main project is now cross-platform**, with no WinForms dependency.
Self-contained x64 and ARM64 builds are available for Windows, Linux, and macOS.
Runtime and rendering tests were performed on Windows x64; the other targets
are cross-built and still require validation on real systems.

Created by Saelac and ChatGPT. This is a standalone editor, not a BepInEx plugin
or a port of the game.

**[Download the latest release](https://github.com/Silverpine-Modding/Silverpine-Saelac-SILCStudio/releases/latest)**
and follow [INSTALL.md](INSTALL.md). Select the archive matching your OS and
processor; GitHub's automatic source download is not a runnable application.

## Run

Windows: extract the downloaded ZIP and run `SilverpineSilcEditor.exe`.
Linux/macOS: extract the matching tar.gz and run `./SilverpineSilcEditor`.
All six builds include .NET; native OS libraries are still required.
Place it beside Silverpine.exe to detect the game installation automatically,
or use **Game folder…**. Selecting game assets does not launch Unity or execute
game assemblies.

Choose **Definitions folder…** if the editor does not find your definitions.
All top-level SILCs appear in the scrolling library, with search and type
filters. Backups and disabled subfolders are not mixed into the list.
An optional command-line argument may specify a Definitions folder or SILC.

No developer paths or user settings are bundled into the executable. The
Definitions folder, game folder, and backup preference are saved per-user under
the OS's `Environment.SpecialFolder.LocalApplicationData` directory, inside
`Silverpine SILC Editor`. On Windows this is
`%LOCALAPPDATA%\Silverpine SILC Editor`. Existing definitions-folder.txt
settings are reused. Artwork caches and the extracted helper live there too.

## Editing

- Characters: description, allowed sexes, searchable racial checkbox cards,
  custom trait names, PNG images, expressions, and visual portrait offsets.
- NPCs: identity, descriptions, knowledge, relationships, routines, Lua, and
  advanced fields. Relationships autocomplete unused known NPCs, allow future
  custom names, and deliberately have no two-Important-relationships limit.
- Routines: activity and destination fields, plus clearly named behavior
  components. Face Direction uses Up/Down/Left/Right. Turn On Lights uses
  Minutes, Only if Dark, and Heat Sources Only, following the game's wording.
  One minute is two turns; half-minutes preserve existing odd turn counts.
  Movement, direction, door, and exclusive groups follow the game's rules,
  with confirmation before replacing a conflicting component.
- Lua: multiline, monospaced, no word wrap. Existing script bytes are retained
  until changed. Dynamic routines keep the generated selector read-only.
- Models: JSON must be a nonempty object.
- Advanced: unfamiliar scalar fields remain editable. Structured binary data
  and fields owned by other tabs are read-only here to avoid conflicting edits.

Existing unknown fields, opaque argument streams, extension tails, PNG bytes,
and untouched float values are retained. Unknown racial IDs, whitespace,
duplicates, and ordering survive an unchanged selection. Out-of-range original
portrait values are not clamped.

Changing knowledge names/lore clears the entry's word cloud and the NPC's
condensed-knowledge cache. Knowledge changes, private-description edits, and
routine ID changes invalidate a dynamic NPC's generated selector according to
the game's editor rules. Regeneration happens when the game's model-based cache
processing runs, not necessarily immediately at boot.

## Routine map

Open an NPC's **Routines → Destination & activity → Edit on map…**. The map
uses the main editor window; it does not open a separate map window.

1. **Load save…** reads a Silverpine .sav without modifying it. Only saved
   persistent objects and serialized world chunks are shown.
2. **Load game artwork…** reads prefab sprites directly from the selected Unity
   game installation. The first load extracts a per-user cache; later loads
   reuse it. No game assets are bundled into the map renderer.
3. All of the current NPC's routine destinations and patrol waypoints are
   displayed. **Select routine** is the default mode: click a point to edit
   its owning routine in the sidebar. Overlapping points offer a chooser.
   The routine dropdown also reaches off-screen points; **Show all points**
   frames the full set. Position, activity/ID, and components are editable here.
4. Choose **Set destination** to move a destination. **Add waypoint** and
   **Move selected waypoint**, along with waypoint lists and reorder controls,
   appear only if the selected routine has a Patrol component. Add/remove
   Patrol through Components. Selecting points never moves them.
5. Right/middle-drag pans; the wheel zooms. Positive Y is north. Manual
   coordinates, region selection, and Center destination are also available.
6. Switching routines keeps their map-session drafts. **Apply routine changes**
   validates and applies all edited routines to the in-memory SILC. **Cancel**
   discards the entire map session. Closing the main window while the map is
   open first cancels the map and returns to the regular editor. Use the main
   **Save changes** button to write the SILC. Renaming a routine invalidates a
   Dynamic selector, just as renaming it outside the map does.

The renderer follows the game's **global** sorting layers: Floors, Shadows,
Default, Characters, then FOV, and sorting order within each layer. Y-sorted
sprites use `round(-worldY * 10) + offset` from the game's `YSorter`, including
each prefab's offset. Renderers stay separate instead of flattening a whole
prefab into one incorrectly ordered image. Rugs use Floors/order 101, floor
tiles order 100, and furniture uses Characters. Water/order 101 appears above
floor tiles. The two tables and four chairs in the western house's rug area
are verified in the saved placements and in the rendered preview.

The planning map does not simulate time-of-day or dynamic lighting.
**Ambient shadows**, enabled by default, draws native shadow and `sprite_fakeao`
overlays on their correct global layers, below walls/furniture. Disable it for
a flat planning view. Shading painted into sprite pixels is retained either
way. **Hide trees & grass** removes tree and grass/overgrowth objects; ground
tiles remain visible. These toggles affect only the preview and are remembered
for the current editor session.

Bathhouse pool water is not serialized in the save: it remains in the game's
fixed scene during `SerializationManager.Load`. The reader supplements only
these known non-serialized water tiles from their actual scene transforms,
only at locations already represented in the save. It does not restore
deleted furniture or guess procedural terrain. These supplements are preview
data, not changes to the loaded save model.

Version 0.2.1 uses a new cache identity; incorrect older composites are not
reused. Reload artwork once after updating. No save files are changed.

The numbered connecting lines show waypoint order, not navigable paths.
Unvisited/unserialized chunks remain empty. Dynamic sprite states, game lighting,
collision/pathfinding, animation, procedural generation, and modded layouts are
not simulated. Missing art uses basic terrain/object placeholders; hover reveals
the saved prefab names. Utility markers without sprites can be unavailable.

Rendering uses viewport-only cached chunks, a low-detail terrain overview when
zoomed out, at most 24 chunk bitmaps (up to 24 MiB), and a 32 MiB decoded-sprite
cache. Map parsing has size/decompression limits and cancellation.

On Windows the published editor includes an image-only UnityPy helper, so users
need neither Python nor Unity. Windows ARM64 uses Windows 11's x64 emulation
for this helper only; the editor itself is ARM64. On macOS/Linux, the desktop editor and coordinate
map need no Python, but **Unity artwork extraction currently requires Python 3
with UnityPy and Pillow**, accessible as `python3`. Install the dependencies
in your own Python environment using `artwork-requirements.txt`; [INSTALL.md](INSTALL.md)
explains how to activate an isolated environment before launching the editor.
PyInstaller is only needed to package the helper, not to run its source.
The same reader accepts a copied Windows game-data folder or a Silverpine.app
bundle; it does not make the game itself cross-platform.

## Portrait and expression previews

The **Portrait offset** tab uses the original game's silhouette and red guides,
the full PNG including transparent margins, the 1436-wide logical canvas, and
the selected game aspect ratio. NPC dialogue mode mirrors both the image and
stored X offset. Drag to move, use arrow keys (Shift for ten units), or scroll
to adjust scale. Exact numeric fields, Reset, and Restore opened values remain
available. Only offset/scale edits affect the SILC; preview choices do not.

The game backdrop, dialogue text, and modded UI transforms are not reproduced.
See Assets/PortraitGuides/README.md for the extracted reference geometry.

Expression previews **replace the top strip**, including transparent pixels,
rather than alpha-blending over the old face. New/replacement expressions must
match the Dialog portrait's width and cannot be taller. New expressions follow
the game's 16-expression limit. Existing files are not silently repaired.
Exports retain the original PNG bytes; preview composition never recompresses
the stored image.

## Backups and saving

**Back up before saving** defaults to enabled and is remembered per user.
Backups contain the exact previous file, under:

- `Definitions/SILC Editor Backups/Per-file saves/`
- `Definitions/SILC Editor Backups/Full backups/` for **Back up all SILCs**

Saving builds and reparses the replacement, then uses a same-directory temporary
file and atomic replacement. The editor refuses to save if the SILC changed on
disk since opening, so another editor's changes are not silently overwritten.
Reload in that case. Avoid simultaneous editing in the game.

## Build from source

Install the .NET 10 SDK. From this project folder:

```text
dotnet build SilverpineSilcEditor.csproj -c Release
dotnet run --project SilverpineSilcEditor.csproj
dotnet publish SilverpineSilcEditor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish SilverpineSilcEditor.csproj -c Release -r win-arm64 --self-contained true -p:PublishSingleFile=true
dotnet publish SilverpineSilcEditor.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish SilverpineSilcEditor.csproj -c Release -r linux-arm64 --self-contained true -p:PublishSingleFile=true
dotnet publish SilverpineSilcEditor.csproj -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish SilverpineSilcEditor.csproj -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true
```

All six runtime IDs above were published successfully. Linux needs an Avalonia-compatible
desktop/X11 environment and native font/rendering dependencies. macOS output
is an unsigned executable, not a notarized app bundle.

`Build-Release.ps1` publishes all six targets sequentially to
`publish/release-0.2.1/<runtime>/`, without replacing previous local releases.
Use PowerShell 7 (`pwsh ./Build-Release.ps1`) to run the same script on other OSes.
The Windows helper must be built on Windows before producing Windows archives.
`collect_licenses.py` collects dependency notices (pass the Python package
directories used to build the helper with `--python-site`), then
`package_release.py --licenses publish/release-0.2.1/licenses` produces verified
archives, a source-only tree, and SHA-256 checksums. Non-Windows archives preserve
executable permissions, and binary architecture headers are checked before packing.

Before a Windows publish, build the embedded Unity asset reader:

```text
python -m pip install -r AssetReader/requirements.txt
python AssetReader/build_reader.py
```

This writes AssetReader/dist/SilverpineAssetReader.exe on Windows; the main
project embeds it if present. Without it, SILC/coordinate editing still works,
but Windows artwork extraction reports the missing helper.

Portable code is in Core/ and Desktop/. The old Windows-only interface is
retained under UI/, with its entry point Program.cs and separate
LegacyWinForms/SilverpineSilcEditor.Legacy.csproj, only for regression comparison.
It is not compiled into the main application.

## Tests

For a self-contained fixture run (no game installation or personal files):

```text
dotnet run --project PortableTests/SilverpineSilcEditor.PortableTests.csproj -c Release -- --self-test ./PortableTests/artifacts-self-test
```

To additionally check real definitions, a save, and Unity artwork:

```text
dotnet run --project PortableTests/SilverpineSilcEditor.PortableTests.csproj -c Release -- "/path/to/Definitions" "./PortableTests/artifacts" "/path/to/save.sav" "/path/to/game"
```

The save and game arguments are optional. Live SILCs and saves are read-only.
Tests write fixtures, backups, and rendered UI screenshots to the output folder.
The suite covers all-file semantic round trips, no-edit UI preservation,
racial custom IDs, lighting conversion, named directions, patrol serialization,
multi-routine map draft Apply/Cancel, marker selection/overlap handling,
conditional patrol controls, direct component edits, expression transparency,
backup/save, map loading, coordinate transforms, and cache bounds.

`python AssetReader/test_read_assets.py` checks sorting-layer/order regressions
using synthetic sprites and the reader's dependencies; no game assets are needed.

The legacy Windows suite remains at Tests/SilverpineSilcEditor.SmokeTests.csproj.
It also reads installed definitions without modifying them.

See VALIDATION.md for this release's verification results. Do not distribute
PortableTests/artifacts, local settings, saves, extracted map artwork, or game
files with the editor. The two embedded portrait guides belong to Silverpine;
their ownership is acknowledged in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

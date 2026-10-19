# SILC Studio 0.2.1

Standalone native desktop SILC editor for Silverpine, created by Saelac and ChatGPT.
This is an editor, not a port of the game, and does not require BepInEx.

## Downloads

Choose one archive matching your operating system and processor:

| System | Archive suffix | Verification |
| --- | --- | --- |
| Windows, Intel/AMD 64-bit | `win-x64.zip` | Windows runtime and rendering tests |
| Windows 11, ARM64 | `win-arm64.zip` | Cross-built; native testing needed |
| Linux, Intel/AMD 64-bit | `linux-x64.tar.gz` | Cross-built; native testing needed |
| Linux, ARM64 | `linux-arm64.tar.gz` | Cross-built; native testing needed |
| macOS, Intel | `osx-x64.tar.gz` | Cross-built; native testing needed |
| macOS, Apple Silicon | `osx-arm64.tar.gz` | Cross-built; native testing needed |

Every desktop build bundles its .NET runtime. Extract the entire archive to a
writable folder. Windows: run `SilverpineSilcEditor.exe`. Linux/macOS: run
`./SilverpineSilcEditor` from the extracted directory; executable permissions
are included in the tar archive. The Mac download is an unsigned executable,
not a signed/notarized `.app`; macOS may require explicit approval to launch it.
Linux requires a glibc-based desktop with X11/XWayland and the native libraries
required by .NET 10 and Avalonia (including fontconfig). These are not Alpine/musl builds.

The Windows image extractor is bundled: no Python installation is required.
The ARM64 Windows editor is native, but its bundled x64 extractor uses Windows
11's x64 emulation. On Linux/macOS, **only loading Unity map artwork** needs
Python 3 with `UnityPy==1.25.2` and `Pillow==12.3.0`. See `INSTALL.md` for setup.
All ordinary SILC editing and coordinate-map functions work without Python.

## Highlights

- Searchable SILC library; character, NPC, model, relationship, knowledge, Lua,
  racial-checkbox, sprite, and portrait-offset editors.
- Optional exact-byte backups enabled by default, manual full backup, and
  conflict detection before replacing a SILC.
- Routine map embedded in the main window, with all current NPC routine
  destinations and patrol waypoints selectable for direct editing.
- Patrol-only controls stay hidden for routines that do not use Patrol.
- Game-style floor/shadow/furniture sorting, separate sprite renderers,
  correct rug/furniture order, and fixed-scene bathhouse water.
- Ambient shadows / fake AO toggle and a separate Hide trees & grass toggle.
  No time-of-day or dynamic lighting is simulated.
- Game data and saves are read-only. Save changes explicitly in the main editor.

After updating, reload game artwork once to populate the new renderer cache.
Keep backups enabled and avoid simultaneous editing in the game.

No personal definitions, saves, paths, settings, extracted world artwork,
or game assemblies are included. The two small portrait reference guides are
from Silverpine and remain the property of the game's owners.

The source archive is separate from the runnable downloads. SHA-256 checksums
are supplied in `SHA256SUMS.txt`. See `VALIDATION.md` for test scope and limits.

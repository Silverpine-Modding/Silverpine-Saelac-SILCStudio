# Install SILC Studio

Download the matching archive from the repository's Releases page, not GitHub's
automatically generated source download. Extract it before starting the editor.
The .NET runtime is included. This tool edits SILCs outside the game and does
not require BepInEx or make Silverpine itself run on another operating system.

## Windows

Run `SilverpineSilcEditor.exe`. Place it next to `Silverpine.exe` for automatic
game-folder discovery, or select **Game folder...** inside the editor. The
bundled artwork helper includes Python; no separate install is needed.

Choose win-x64 for Intel/AMD PCs. Choose win-arm64 for Windows 11 ARM64 PCs;
artwork extraction uses the bundled x64 helper through Windows x64 emulation.
The executables are unsigned, so Windows may display a publisher warning.

## Linux and macOS

Extract the `.tar.gz`, open a terminal in the extracted folder, and run:

```sh
./SilverpineSilcEditor
```

If an archive manager discarded the executable bit, use
`chmod +x SilverpineSilcEditor`. macOS output is an unsigned standalone binary,
not a notarized app bundle. If macOS blocks a trusted download, review the
specific app in System Settings > Privacy & Security; do not disable system-wide
security protections. These builds still need real Linux/macOS runtime testing.

Linux needs a glibc desktop, X11 or XWayland, fontconfig, and the native libraries
required by [.NET 10](https://learn.microsoft.com/en-us/dotnet/core/install/linux-scripted-manual)
and [Avalonia 11](https://v11.docs.avaloniaui.net/docs/overview/supported-platforms/).
No .NET install is needed, but self-contained publishing does not bundle all
operating-system libraries. Alpine/musl and headless servers are not targeted.

### Optional Unity artwork extraction

Only **Load game artwork...** requires Python on Linux/macOS. Install Python 3
with venv support through your OS package manager, then create an isolated
environment and launch the editor with it activated:

```sh
python3 -m venv "$HOME/.local/share/silc-studio-python"
. "$HOME/.local/share/silc-studio-python/bin/activate"
python3 -m pip install -r artwork-requirements.txt
./SilverpineSilcEditor
```

Repeat the activation before launching the editor in a new terminal. The editor
uses `python3` on its inherited PATH; a GUI-launched editor may not inherit an
activated environment. PyInstaller is not needed on these systems.

Select your Silverpine installation or a copied `Silverpine_Data` installation
folder's parent using **Game folder...**. A Silverpine.app bundle is also
recognized. Unity itself is not required, and no Unity code is executed.

## Your files

Select **Definitions folder...** to choose your own SILCs. Settings and artwork
caches are per-user; developer settings and paths are not shipped. Backups are
enabled by default and are stored in `SILC Editor Backups` beneath the selected
Definitions folder. Avoid editing the same file in the game at the same time.

Load a `.sav` in the routine map to view saved locations. Saves are never written
by the map. Apply map edits and then use the main **Save changes** button to
write a modified SILC. Keep backup copies of your original files.

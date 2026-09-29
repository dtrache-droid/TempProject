# AppLauncher

A button that sits in the empty **far-left corner of the Windows 11 taskbar**, even while your taskbar icons stay centered. Click it to open a list of **your own apps**: icon on the left, name on the right, sorted A-Z.

- **Left-click** the button: your app list.
- **Right-click** the button: the settings window.
- Starts with Windows. The button keeps its place if Explorer restarts or the screen changes, and hides while a fullscreen app or game is running.

## Install

1. **Get the exe.** Either:
   - double-click `build.bat`. It uses the C# compiler built into Windows and creates `bin\AppLauncher.exe`, or
   - download the **AppLauncher** artifact from the latest **Build** run in the repo's **Actions** tab.
2. Move `AppLauncher.exe` to a permanent folder, e.g. `C:\Tools\AppLauncher\`.
3. Double-click it. The button appears on the far left of the taskbar. On the first run it also sets itself to start with Windows.

**Don't pin the exe.** It isn't a pinned taskbar icon: it draws its own button in the empty corner.

> If **Widgets** is turned on, Windows puts it in the same corner. Turn it off under *Settings → Personalization → Taskbar → Widgets*.

## Settings (right-click the button)

Right-clicking the button opens the settings window. Every change is saved and applied straight away.

**Your apps**
- **Add from Start menu...** shows every program in your Start menu with its icon. Tick the ones you want and click **Add**. This is the easiest way.
- **Browse for a file...** picks any `.exe` or `.lnk` from disk.
- **Drag and drop:** drag an app or shortcut from the desktop or a folder onto the window.
- **Rename** (or press F2) and **Remove** (or press Delete) work on the selected apps. The list is always sorted A-Z.

**Look of the list**
- Pick a background color from the swatches, or the rainbow one for any color.
- **Transparency** goes from solid to *no background* (only icons and names are drawn).
- **Icon and text size** goes from 16 to 64 px.
- A live preview on the right shows the result on a mock taskbar.

**Button icon**
- Savy S (default), Color tiles, Dots, List, Sparkle, or **Choose an image...** for your own (png, jpg, bmp, gif or ico).

**General**
- **Start with Windows** switch, a shortcut to the settings folder, and **Exit**.

**Not supported:** Microsoft Store apps can't be picked from a file dialog, so they can't be added unless you have a `.lnk` shortcut for them.

Your settings live in `%APPDATA%\AppLauncher\settings.ini`. To uninstall, switch off **Start with Windows**, click **Exit**, then delete the exe and that folder.

## How it works

Windows 11 has no supported way to add buttons to the taskbar. AppLauncher places a small always-on-top window with a transparent background exactly over the left end of the taskbar. It watches the taskbar and repositions or re-shows itself when:
- the resolution or scaling changes,
- Explorer restarts,
- the taskbar is clicked.

The app list is a second window drawn with per-pixel transparency, which is how the background can go all the way to "no background" while the icons and text stay solid.

| File | Purpose |
| --- | --- |
| `src/Program.cs` | Startup, single instance, *Start with Windows* |
| `src/TaskbarButton.cs` | The taskbar button |
| `src/AppPopup.cs`, `src/PopupPainter.cs` | The app list window and how it is laid out and drawn |
| `src/SettingsForm.cs`, `src/StartMenuPicker.cs`, `src/UiKit.cs` | The settings window, the Start menu picker and the custom controls |
| `src/Settings.cs`, `src/ButtonIcons.cs` | Loading and saving settings; the button icon choices |
| `src/Shell.cs`, `src/NativeMethods.cs` | Launching apps and extracting icons; Windows API declarations |
| `src/icon-*.png`, `src/launcher.ico` | Button icons and the exe icon (regenerate with `python3 tools/make_icons.py`) |
| `build.bat` | Builds `bin\AppLauncher.exe` |

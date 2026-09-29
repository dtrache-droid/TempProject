# AppLauncher

A button that sits in the empty **far-left corner of the Windows 11 taskbar**, even while your taskbar icons stay centered. Click it to open a list of **your own apps**: icon on the left, name on the right, sorted A-Z.

- **Left-click** the button: your app list.
- **Right-click** the button: settings.
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

| Menu item | What it does |
| --- | --- |
| **Start with Windows** | Tick or untick. |
| **Add app...** | Pick one or more programs (`.exe`) or shortcuts (`.lnk`). Only what you add shows up in the list. |
| **Rename app** | Change the name shown in the list. |
| **Remove app** | Take an app out of the list. |
| **Background color...** | Any color for the list background. |
| **Background transparency** | 0% (solid) to 100% (no background at all: only icons and names are drawn). |
| **Icon and menu size** | 16 to 64 px. The row height and text size scale with it. |
| **Button icon** | Savy S (default), Color tiles, Dots, List, Sparkle, or **Choose an image...** to use your own (png, jpg, bmp, gif or ico). |
| **Exit AppLauncher** | Closes the button (it comes back at the next login, or run the exe again). |

The list is always sorted alphabetically, and changes apply the next time you open it.

**Tip:** the easiest way to find an app to add is the Start-menu folder. The **Add app...** dialog opens there, and its shortcuts have the right icons.

**Not supported:** Microsoft Store apps can't be picked from a file dialog, so they can't be added unless you have a `.lnk` shortcut for them.

Your settings live in `%APPDATA%\AppLauncher\settings.ini`. To uninstall, untick **Start with Windows**, click **Exit AppLauncher**, then delete the exe and that folder.

## How it works

Windows 11 has no supported way to add buttons to the taskbar. AppLauncher places a small always-on-top window with a transparent background exactly over the left end of the taskbar. It watches the taskbar and repositions or re-shows itself when:
- the resolution or scaling changes,
- Explorer restarts,
- the taskbar is clicked.

The app list is a second window drawn with per-pixel transparency, which is how the background can go all the way to "no background" while the icons and text stay solid.

| File | Purpose |
| --- | --- |
| `src/Program.cs` | Startup, single instance, *Start with Windows* |
| `src/TaskbarButton.cs` | The taskbar button and the right-click settings menu |
| `src/AppPopup.cs` | The app list window |
| `src/Settings.cs` | Loading and saving the settings |
| `src/Shell.cs` | Launching apps, extracting their icons, the rename dialog |
| `src/NativeMethods.cs` | Windows API declarations |
| `src/icon-*.png`, `src/launcher.ico` | Button icons and the exe icon (regenerate with `python3 tools/make_icons.py`) |
| `build.bat` | Builds `bin\AppLauncher.exe` |

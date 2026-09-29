# AppLauncher

A button that sits in the empty **far-left corner of the Windows 11 taskbar**, even while your taskbar icons stay centered. Click it to get a menu of the apps, folders, files and websites you choose.

```
 [▦]                     ⊞  📁  🌐  ...  (centered icons)                    ENG  🔊  4:01 AM
  ^ AppLauncher
```

- **Left-click** the button to open your app menu.
- **Right-click** it for *Edit app list*, *Start with Windows* and *Exit*.
- The button starts with Windows, keeps its place if Explorer restarts, and hides while a fullscreen app or game is running.
- Your list lives in a plain text file (`apps.txt`) next to the exe.

## Install

1. **Get the exe.** Either:
   - double-click `build.bat`. It uses the C# compiler built into Windows and creates `bin\AppLauncher.exe`, or
   - download the **AppLauncher** artifact from the latest **Build** run in the repo's **Actions** tab.
2. Move `AppLauncher.exe` to a permanent folder, e.g. `C:\Tools\AppLauncher\`.
3. Double-click it. The button appears on the far left of the taskbar. On its first run, AppLauncher also:
   - creates `apps.txt` with a few examples, and
   - sets itself to start with Windows (right-click the button to turn that off).

**Don't pin the exe.** It isn't a pinned taskbar icon: it draws its own button in the empty corner.

> If **Widgets** is turned on, Windows puts it in the same corner. Turn it off under *Settings → Personalization → Taskbar → Widgets*.

## Choose your apps

Right-click the button → **Edit app list...** This opens `apps.txt`. Each line is one item:

```
Display name | what to open | optional arguments
```

Example:

```
[Work]
Outlook        | C:\Program Files\Microsoft Office\root\Office16\OUTLOOK.EXE
VS Code        | %LOCALAPPDATA%\Programs\Microsoft VS Code\Code.exe
Teams          | %APPDATA%\Microsoft\Windows\Start Menu\Programs\Microsoft Teams.lnk
---
[Browse]
Chrome (work)  | C:\Program Files\Google\Chrome\Application\chrome.exe | --profile-directory="Profile 1"
Gmail          | https://mail.google.com
---
[Folders]
Projects       | D:\Projects
Downloads      | %USERPROFILE%\Downloads
```

- **What to open** can be an `.exe`, a shortcut (`.lnk`), a folder, any file, or a URL.
- `---` adds a separator line. `[Name]` adds a small group heading.
- Environment variables such as `%USERPROFILE%`, `%APPDATA%` and `%LOCALAPPDATA%` work.
- Lines starting with `#` are ignored.
- Save the file. The next click shows your changes, with no restart needed.

**Tip:** to find an app's path, open the Start menu, right-click the app → *Open file location*. Then either:
- point the line at that `.lnk` shortcut, or
- right-click the shortcut → *Properties* and copy the **Target**.

## Custom button icon

Put an `icon.png` (ideally 256×256 with a transparent background) or an `icon.ico` next to `AppLauncher.exe`. Then right-click the button → *Exit* and start it again.

## Uninstall

Right-click the button:
1. Untick **Start with Windows**.
2. Click **Exit**.
3. Delete the folder.

## How it works

Windows 11 has no supported way to add buttons to the taskbar. AppLauncher places a small always-on-top window with a transparent background exactly over the left end of the taskbar. It watches the taskbar and repositions or re-shows itself when:
- the resolution or scaling changes,
- Explorer restarts,
- the taskbar is clicked.

| File | Purpose |
| --- | --- |
| `src/Program.cs` | Startup, single instance, *Start with Windows* |
| `src/TaskbarButton.cs` | The taskbar button: placement, drawing, clicks |
| `src/AppMenu.cs` | Reads `apps.txt`, builds the menu, launches apps |
| `src/NativeMethods.cs` | Windows API declarations |
| `build.bat` | Builds `bin\AppLauncher.exe` |

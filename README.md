# AppLauncher

A tiny Windows taskbar launcher. Pin it to the far left of your taskbar. Clicking it pops up a menu of the apps, folders, files and websites you choose.

- It's one small `.exe`. It doesn't run in the background: it opens, shows the menu, launches what you pick, and closes.
- Your list lives in a plain text file (`apps.txt`) next to the exe.
- Works on Windows 10 and 11.

## 1. Get the exe

**Option A: build it yourself (no tools to install)**

Double-click `build.bat`. It uses the C# compiler that already comes with Windows and creates `bin\AppLauncher.exe`.

**Option B: download it**

Every push to GitHub builds the exe automatically. Open the repo's **Actions** tab, pick the latest **Build** run and download the **AppLauncher** artifact.

Move `AppLauncher.exe` somewhere permanent before pinning it, for example `C:\Tools\AppLauncher\`.

## 2. Pin it to the far left of the taskbar

1. **Windows 11 only:** go to *Settings → Personalization → Taskbar → Taskbar behaviors* and set **Taskbar alignment** to **Left**.
2. Run `AppLauncher.exe` once. This creates `apps.txt` next to it (click away to close the menu).
3. Right-click `AppLauncher.exe` → *Show more options* (Windows 11) → **Pin to taskbar**.
4. Drag the new icon all the way to the left of the pinned icons.

> Windows always keeps the Start button (and the Search / Task View buttons, if they're on) left of pinned apps. To make AppLauncher the very first icon after Start, hide those buttons under *Settings → Personalization → Taskbar*.

## 3. Choose your apps

Click the icon and choose **Edit app list...**. This opens `apps.txt`. Each line is one item:

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
- Save the file. The next click on the taskbar icon shows your changes.

**Tip:** to find an app's path, open the Start menu, right-click the app → *Open file location*. This opens its shortcut in File Explorer. Then either:
- point the line at that `.lnk` shortcut, or
- right-click the shortcut → *Properties* and copy the **Target**.

## Files

| File | Purpose |
| --- | --- |
| `src/AppLauncher.cs` | The whole program (WinForms, .NET Framework 4.x) |
| `src/launcher.ico` | Taskbar icon |
| `src/app.manifest` | Keeps the menu sharp on high-DPI screens |
| `build.bat` | Builds `bin\AppLauncher.exe` |

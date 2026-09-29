# AppLauncher – handover notes

Everything you need to keep working on AppLauncher on your own PC. `README.md` covers how to *use* the app; this file covers how to *change* it.

**State at handover (v3.2, 29 Sep 2026):**
- All work is on branch `claude/admiring-mayer-sd9odl` of `dtrache-droid/TempProject`. No pull request yet, and nothing is merged to a main branch.
- Every commit was built by the GitHub Actions workflow on Windows.
- It was only *run* on your PC. There are no automated tests.

---

## 1. Get it running locally

```bat
git clone https://github.com/dtrache-droid/TempProject.git
cd TempProject
git checkout claude/admiring-mayer-sd9odl
build.bat
bin\AppLauncher.exe
```

- **No SDK or Visual Studio needed.** `build.bat` calls the C# compiler that ships with Windows (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`).
- **Rebuilding while it runs:** the exe is locked while AppLauncher is running. Close it first (right-click the button → **General** → **Exit**, or `taskkill /im AppLauncher.exe /f`), rebuild, then start it again.
- **Only one copy runs at a time** (a named mutex). If you start a second copy, it exits silently. When "nothing happens" after a rebuild, the old copy is usually still running.
- **Bitdefender:** a freshly built unsigned exe can be blocked again by Advanced Threat Defense. Add an exclusion for your working folder (e.g. `C:\dev\TempProject\bin`) while developing.

### Using Visual Studio / Rider / VS Code instead (optional)

You can open the files in any editor, but there is no `.csproj`. To debug with breakpoints, create a **Windows Forms App (.NET Framework 4.8)** project and add these items:
- all of `src\*.cs`,
- `src\app.manifest` as the manifest,
- `src\launcher.ico` as the application icon,
- the five `src\icon-*.png` files as **Embedded Resource**. Set their resource names to exactly `icon-savy.png`, `icon-grid.png` and so on (see `ButtonIcons.Load`). If the names differ, the icons silently fall back to nothing.

If you do this, keep `build.bat` working too, because the CI build uses it.

---

## 2. Hard constraints (read before changing code)

1. **C# 5 only.** The built-in `csc.exe` is the old compiler and only understands C# 5, so these fail to build:
   - `$"..."` string interpolation, `?.`, `nameof`, `=>` members, `out var`, tuples, pattern matching, `is not`, local functions.

   Use `string.Format`, explicit null checks, full property bodies, and `delegate { }` for handlers. The GitHub build catches mistakes within seconds of a push.
2. **.NET Framework 4.x WinForms, no NuGet.** Only `System.Windows.Forms.dll` and `System.Drawing.dll` are referenced. Windows APIs go through `NativeMethods.cs` (P/Invoke). The COM calls (`WScript.Shell` for `.lnk` files) use late binding, so no interop assembly is needed.
3. **System-DPI-aware, not per-monitor.** `app.manifest` declares `dpiAware=true`. All sizes are written for 100% scaling and multiplied by `scale = DPI / 96` (`Px()`, `S()`, `R()` helpers). Keep doing that for anything new.
4. **Antivirus sensitivity.** Bitdefender flagged v3.0. Since then:
   - "Start with Windows" uses a **Startup-folder shortcut**, not the registry `Run` key.
   - The `SetWinEventHook` foreground hook was **removed**.

   Before adding anything that looks like malware behavior, weigh the cost:
   - global hooks (keyboard, mouse, WinEvent),
   - injecting into or subclassing Explorer,
   - registry autostart,
   - downloading anything,
   - hiding the process.

   A code-signing certificate would fix the "unknown unsigned exe" part for good.

---

## 3. How it works

```
Program.Main
 ├─ Settings.Load()               %APPDATA%\AppLauncher\settings.ini
 ├─ Autostart (Startup .lnk)      first run: enable; migrate old registry entry
 └─ TaskbarButton (hidden Form)   Application.Run() with no main form
     ├─ left-click  → AppPopup    (the app list, PopupPainter draws it)
     └─ right-click → SettingsForm
                        ├─ StartMenuPicker (Add from Start menu)
                        └─ UiKit controls + PreviewBox (uses PopupPainter too)
```

### The taskbar button (`TaskbarButton.cs`)
- Windows 11 has **no API** for putting a button on the taskbar. The button is a borderless top-level window with the styles `WS_EX_LAYERED | TOOLWINDOW | TOPMOST`, placed over the taskbar's far-left corner.
- **Drawing:** it is drawn with `UpdateLayeredWindow` (per-pixel alpha, see `Layered.Present` in `NativeMethods.cs`). The background is alpha **1**, which is invisible but still receives clicks. Alpha 0 would be click-through.
- **Placement:** `UpdatePlacement()` runs on a **150 ms timer**. It finds `Shell_TrayWnd`, works out the taskbar rectangle, and hides the button when:
  - a fullscreen app is running (`SHQueryUserNotificationState` plus a foreground-window-covers-the-monitor check), or
  - the taskbar is auto-hidden.

  When the taskbar is in the foreground it re-asserts `HWND_TOPMOST`, because clicking the taskbar raises it above the button. It re-finds the taskbar after the `TaskbarCreated` message, which arrives when Explorer restarts.
- **Click area vs. drawn area:**
  - `Bounds` is the click area. It is stretched to the screen's left and bottom edges (the corner fix in v3.2).
  - `visual` is the square area where the icon is drawn.

  Keep the two separate when you change the look.
- **Open-close-reopen guard:** `suppressClick` and `lastMenuClosedAt` stop a click that closes the popup from reopening it straight away.

### The app list (`AppPopup.cs` + `PopupPainter.cs`)
- `PopupPainter` holds all the layout and drawing:
  - rows sized from `IconSize`,
  - the Segoe UI font,
  - background colour and transparency,
  - a text outline when transparency is 30% or more.

  The settings preview uses the same class, so the preview always matches the real list.
- `AppPopup` is also a layered window. It handles hover, click, wheel scrolling, the keys ↑ ↓ Home End Enter Esc, and closing when it loses focus. A 200 ms watchdog closes it if the `Deactivate` event is missed.

### The settings window (`SettingsForm.cs`, `UiKit.cs`, `StartMenuPicker.cs`)
- The window has four pages, each a `Panel` built in code with absolute positions (all passed through `R()` for scaling). There is no designer file.
- **Custom controls** in `UiKit.cs`:
  - `IconButton`, which uses glyphs from the Segoe Fluent Icons / Segoe MDL2 font (codes listed in `IconFont`),
  - `Card`, `Swatch`, `ToggleSwitch`, `NavList`, `PreviewBox`.
- **Saving:** changes save through a 400 ms debounce (`SaveSoon`) and always on close.
- **Button icon changes:** `ButtonIconChanged` tells the taskbar button to reload its icon. Look changes need no event, because the popup is rebuilt from `Settings` each time it opens.
- **Start menu picker:** it scans `CommonPrograms` and `Programs` for `.lnk` files. It skips names containing "uninstall" and apps already in the list, and loads icons a few at a time on a timer so the window opens instantly.

### Settings file (`Settings.cs`)
`%APPDATA%\AppLauncher\settings.ini`, UTF-8, one `key=value` per line:

```
backColor=#202020
transparency=10          ; 0 (solid) .. 100 (no background)
iconSize=24              ; 12..96, the UI slider offers 16..64
buttonIcon=savy          ; savy | grid | dots | list | sparkle | custom
app=Name<TAB>C:\path\to\app.exe-or.lnk
```

- A user's own button image is copied to `%APPDATA%\AppLauncher\custom-icon.png` (256×256).
- **Adding a setting:** add a field, a `case` in `Load`, and a line in `Save`. Unknown keys are ignored, so older versions can still read newer files.

### Icons (`ButtonIcons.cs`, `tools/make_icons.py`)
- The PNGs in `src/` are generated. Run `python3 tools/make_icons.py`. It is pure Python with no dependencies and writes the five `icon-*.png` files plus `launcher.ico`, the exe icon.
- **The Savy S** is a hand-rasterised copy of your SVG. The SVG shapes are in the `SHAPES` list in the script.
- **Tinted icons:** `dots`, `list` and `sparkle` are white masks, recoloured at runtime (`ButtonIcons.Tint`) to suit a light or dark taskbar. `savy`, `grid` and custom images are drawn as they are.
- **Adding a built-in icon:**
  1. Draw it in the script.
  2. Add an `Entry` in `ButtonIcons.BuiltIn`.
  3. Add a `/resource:` line in `build.bat`.

---

## 4. Files

| File | What's in it |
| --- | --- |
| `src/Program.cs` | `Main`, single-instance mutex, `Autostart` (Startup-folder shortcut + old-registry migration), assembly version |
| `src/TaskbarButton.cs` | The corner button: placement, topmost handling, fullscreen detection, drawing, clicks |
| `src/AppPopup.cs` | The pop-up list window (input, keyboard, closing) |
| `src/PopupPainter.cs` | Layout and drawing of the list, shared with the preview |
| `src/SettingsForm.cs` | Settings window: Your apps / Look of the list / Button icon / General |
| `src/StartMenuPicker.cs` | "Add from Start menu" dialog |
| `src/UiKit.cs` | Theme colours, icon font, custom controls, preview box |
| `src/Settings.cs` | `settings.ini` load/save, sorted app list |
| `src/ButtonIcons.cs` | Built-in and custom button icons, tinting, preview tiles |
| `src/Shell.cs` | Launching apps, icon extraction (with `.lnk` resolution and cache), friendly names |
| `src/NativeMethods.cs` | P/Invoke declarations and `Layered.Present` |
| `src/app.manifest` | DPI awareness, Win10/11 compatibility, `asInvoker` |
| `src/icon-*.png`, `src/launcher.ico` | Generated by `tools/make_icons.py` |
| `build.bat` | Local and CI build |
| `.github/workflows/build.yml` | Builds on `windows-latest` on every push and uploads `AppLauncher.exe` as an artifact |

---

## 5. Decisions so far (and why)

| # | Decision | Why |
| --- | --- | --- |
| 1 | Overlay window, not a pinned icon | Windows 11 keeps pinned icons in the centered group, and you wanted the far-left corner. |
| 2 | Only apps you add, no URLs or folders | Your request: "only my apps". |
| 3 | List always sorted A-Z, icon left and name right | Your request. |
| 4 | Savy S is the default button icon and the exe icon | Your request. |
| 5 | Startup-folder shortcut instead of registry `Run` | Bitdefender Advanced Threat Defense block on v3.0. |
| 6 | No WinEvent hook; 150 ms polling timer instead | Same reason. The cost is up to 150 ms before the button comes back in front after a taskbar click. |
| 7 | Settings window instead of a context menu | v3.2 request: nicer graphics, easier adding. |
| 8 | Click area reaches the screen's left and bottom edges | v3.2 request: fast "throw the mouse into the corner" clicking (Fitts's law). |
| 9 | Settings in `%APPDATA%`, not next to the exe | The exe can live in a read-only folder, and settings survive replacing the exe. |

---

## 6. Known limitations and things never checked

- **Microsoft Store apps** can't be added unless there is a `.lnk` for them. Possible fix: list `shell:AppsFolder` and launch with `explorer.exe shell:AppsFolder\<AUMID>`.
- **Primary monitor only.** Taskbars on other monitors (`Shell_SecondaryTrayWnd`) are ignored.
- **Widgets** occupies the same corner. If it's turned on, the two overlap.
- **Mixed-DPI setups:** because the app is system-DPI-aware, sizes can be off on a secondary monitor with a different scale.
- **Settings window theme:** it is always light, and has no dark mode yet.
- **Never verified on a real screen:**
  - the icon-font glyphs on Windows 10,
  - the look at 150% and 200% scaling,
  - left, right and top taskbars (the code supports them).
- **Loss of the settings file:** if `settings.ini` can't be written, the error is swallowed and changes are lost silently.
- **Old files:** `apps.txt` from v1 and v2 is no longer read.

## 7. Ideas for next steps

- Drag to reorder, or groups/folders in the list. That would mean letting you turn off the A-Z sort.
- Microsoft Store app support (see above).
- Keyboard shortcut to open the list, such as Win+Alt+A (`RegisterHotKey`). This is fine AV-wise, unlike a global keyboard hook.
- Dark theme for the settings window, following `AppsUseLightTheme`.
- Per-monitor DPI v2 and support for taskbars on several monitors.
- Code signing, plus a small installer (Inno Setup or MSIX).
- A per-app icon override in the list.

## 8. Releasing a new version

1. Bump `AssemblyVersion` and `AssemblyFileVersion` in `src/Program.cs`. Also update the version text on the General page in `SettingsForm.cs` (`"AppLauncher 3.2 - ..."`).
2. Push. The **Build** workflow produces the `AppLauncher` artifact, which you download from the run's page.
3. To update an installed copy:
   - exit the running one,
   - replace the exe in its permanent folder.

   Settings in `%APPDATA%` carry over, and the Startup shortcut points at the same path.

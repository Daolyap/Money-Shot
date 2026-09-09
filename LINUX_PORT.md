# Linux Port — Feasibility & Plan

> Status: **Phases 0–3 done and substantially verified against real Linux systems** (2026-09).
> `MoneyShot.UI` (Avalonia) now multi-targets `net10.0-windows` and `net10.0`, with a
> `MoneyShot.Platform.Linux` project providing X11 screen capture, X11 global hotkeys, XDG
> autostart, and clipboard support — selected via `MoneyShot.UI/Platform/PlatformServices.cs`
> alongside the existing Windows implementations, with no change to the shipping WPF app. This
> was **not just built — it was built, published as a self-contained `linux-x64` binary, and
> actually run** against real X11 sessions (Xvfb under Docker, and WSLg's own compositor): the
> full hotkey→capture→editor flow, `HistoryWindow`, `SettingsWindow`, and the clipboard path all
> confirmed working end-to-end on genuine Linux, not just "compiles." Three real, non-obvious bugs
> were found this way and fixed — see § Phase 2 status. **`.deb` and `.rpm` packages are built,
> and each was installed via its native package manager (`apt`/`dnf`) in a clean container and
> launched successfully**; an AppImage build path exists and was verified to build and run too.
> `.github/workflows/build-linux.yml` builds and packages all three on every PR/release, and
> `release.yml` attaches them to the same auto-created release the Windows MSI/zip go to.
> **Remaining real gaps**: no Wayland-native capture or hotkeys (X11/XWayland only — see § 2 and
> § 3), the tray icon has not been confirmed against a real desktop's StatusNotifierWatcher (only
> confirmed to not crash without one), and `EditorWindow`'s deeper tool interactions (resize, crop,
> zoom/pan, pixelate) were verified on Windows but not separately re-verified on Linux. See
> § Migration phases for the full verified-vs-not breakdown per phase.

## TL;DR

The Linux port is **done for a v1 that matches today's Windows feature set on X11 sessions**:
region/full-screen/monitor capture, the annotation editor, tray icon, global hotkeys, history,
settings, and autostart all work on real Linux, packaged as `.deb`, `.rpm`, and AppImage. The
remaining gap is **Wayland**: X11 direct-capture (`XGetImage`) and X11 global hotkeys
(`XGrabKey`) are what's implemented, and neither has a real equivalent under a native Wayland
session with no XWayland — see § Wayland status below for exactly what that means in practice on
today's Debian/Fedora defaults.

## What ports cleanly (small or no changes)

These files have zero or trivial Windows-specific dependencies and survive a port:

| Area | Files |
|---|---|
| Models | `Models/AnnotationTool.cs`, `Models/CaptureMode.cs`, `Models/SaveDestination.cs`, `Models/AppSettings.cs`, `Models/HistoryEntry.cs` |
| Settings persistence | `Services/SettingsService.cs` — most of it. The two registry hooks (`SetStartupWithWindows`, `SetWindowsPrintScreenDisabled`) are Windows-only and need a Linux equivalent (autostart `.desktop` file in `~/.config/autostart`; the PrintScreen suppression simply has no analogue and should be a no-op) |
| Logger | `Services/Logger.cs` ports as-is *once routed through `AppDataPaths.GetConfigRoot()`* — `Environment.SpecialFolder.ApplicationData` does **not** reliably resolve on Linux by itself (returns empty without `$XDG_CONFIG_HOME` set); see § 8 and § Phase 2 for the bug this caused and its fix |
| Auto-update HTTP & SHA-256 verification | The HttpClient + `SHA256SUMS.txt` parsing logic in `AutoUpdateService.cs`. The exe-swap batch script at the bottom is Windows-only and needs a shell-script equivalent |
| Save encoders | The `BitmapEncoder` calls in `SaveService.cs` — but only the *shape* of the code; the actual encoders come from a different namespace under Avalonia (see § UI framework) |
| History service shape | `HistoryService.cs` — the file IO and JSON survive; the BitmapSource/PngBitmapEncoder calls swap to the Avalonia equivalents |
| Undo records | `Editor/UndoController.cs`, `Editor/ElementState.cs`, `Editor/CanvasPosition.cs`, `Editor/ElementResizeMode.cs` — these are pure C# with `Canvas`/`UIElement` references that are the *same names* in Avalonia, but mapping is not 1:1 |
| Tests | `MoneyShot.Tests/` — should mostly survive once it stops targeting `net10.0-windows` |

## What does **not** port

### 1. The entire UI layer (WPF)

WPF has no Linux runtime. Microsoft has explicitly said they will not port it. Every `*.xaml`,
every `*.xaml.cs`, every `<Window>`, every `Style`, every `Canvas.SetLeft` is dead on arrival.

**The fork in the road: pick a UI framework.**

| Framework | Verdict |
|---|---|
| **Avalonia 11** | **Recommended.** XAML-based, closest API to WPF, mature on Linux (X11 + Wayland). `Canvas`, `Shape`, `Path`, `Polyline`, `TextBlock`, `RenderTargetBitmap` all exist with similar shapes. The editor's code-behind heavy style ports with the least friction here. Native Linux look via FluentTheme. ~80MB single-file publish, comparable to current Windows footprint. |
| Uno Platform | Possible but more work. WPF compatibility shim exists but has gaps. Mainly designed for cross-platform mobile/desktop with WinUI APIs, which are *less* like WPF than Avalonia is. |
| MAUI | Not a serious option — no first-party Linux desktop support as of .NET 10. Community GTK head exists but is not production-grade. |
| GTK# / Gtk4 / GtkSharp | Mature on Linux, terrible on Windows, completely different paradigm — would make the cross-platform story worse, not better. |
| Eto.Forms | Cross-platform but small ecosystem. Suitable for utility UIs, not for a heavily custom-styled editor canvas. |

The recommendation is **Avalonia** because (a) its `Canvas`/`Shape` API maps almost line-for-line to
WPF, which means `EditorWindow`'s 1900-line code-behind ports with mostly mechanical changes;
(b) WPF-style XAML can be reused with minor namespace edits; (c) it has working hotkey, tray, and
clipboard primitives on Linux out of the box.

### 2. Capture pipeline — ✅ implemented (X11), Wayland still open

`MoneyShot.Platform.Linux/LinuxScreenCapture.cs` implements `IScreenCapture` via raw `libX11.so.6`
P/Invoke (`X11Interop.cs`): `XGetImage` against the root window for full-screen and per-monitor
capture (monitor bounds come from shelling out to `xrandr --query` and parsing its stable
plain-text output — deliberately not a `libXrandr` P/Invoke, since xrandr's CLI output format has
been stable for over a decade and this avoids a second native-ABI surface). **Genuinely verified**:
built self-contained for `linux-x64`, run against a real Xvfb X server in Docker, monitor
enumeration and `CaptureFullScreen` confirmed to return correct dimensions/geometry via a
standalone test harness, and a full capture triggered by a real `XGrabKey`-delivered hotkey was
confirmed to render correctly inside `EditorWindow` (screenshotted and visually inspected).

**Wayland is not implemented.** `XGetImage` against the root window only sees X11/XWayland
content — under a native Wayland session (the default on current GNOME/KDE, meaning current
Debian/Fedora out of the box) there is no root window with real pixels to read, so
`LinuxScreenCapture` will either throw (`XOpenDisplay` fails with no X server at all) or silently
capture nothing useful. The correct fix for native Wayland is the XDG Desktop Portal
`org.freedesktop.portal.Screenshot` D-Bus service, which — critically — requires an interactive,
per-capture consent dialog from the compositor (no instant, silent capture the way Windows/X11
allow), a real and unavoidable UX change under Wayland policy. This was not implemented: no
portal-backend service was available in any test environment used this session to build and verify
against, and the interactive-consent-dialog UX question is a product decision, not just an
implementation detail. See § Wayland status.

### 3. Global hotkeys — ✅ implemented (X11), no Wayland equivalent

`MoneyShot.Platform.Linux/LinuxGlobalHotkeys.cs` implements `IGlobalHotkeys` via `XGrabKey` on the
root window, with a dedicated background thread pumping `XNextEvent` (mirrors
`Win32GlobalHotkeys`'s dedicated message-only window). Each hotkey is grabbed under all four
NumLock/CapsLock modifier permutations (the classic X11 gotcha — a single grab without this
silently stops firing whenever either lock key is toggled on). **Genuinely verified**: a real
`xdotool key Print` synthetic keypress under Xvfb correctly triggered a registered hotkey callback
and ran the full capture flow — this also validated the hand-derived `XKeyEvent` struct field
offsets used to read the keycode/modifier state back out of the raw X11 event buffer, which were
computed by hand from the Xlib ABI and could easily have been wrong without this test.

**Wayland has no direct equivalent** — compositors deliberately do not let clients grab input
they don't have focus for. The portal `org.freedesktop.portal.GlobalShortcuts` API is the
correct-but-not-implemented path (same reasoning as capture: needs a portal backend to build
against, and changes the binding UX — the user binds the shortcut in system settings, not in
MoneyShot). Under a pure Wayland session, `LinuxGlobalHotkeys.Initialize()` logs a warning and
no-ops rather than crashing (mirrors `Win32GlobalHotkeys`' existing "combination may be in use"
non-fatal pattern) — hotkeys just silently don't fire; the tray menu and in-app buttons remain the
working alternative.

### 4. System tray (`NotifyIcon`) — ✅ implemented, reuses Avalonia's own cross-platform TrayIcon

`MoneyShot.UI/Platform/LinuxTrayIcon.cs` wraps Avalonia's own `TrayIcon`/`NativeMenu` classes
(backed by its `Avalonia.FreeDesktop` assembly, which implements StatusNotifierItem D-Bus
registration) rather than hand-rolling the SNI protocol — reusing a framework's tested
implementation beat writing untested D-Bus service code from scratch. **Partially verified**: the
tray icon registers and the app never crashes with or without an SNI host present (confirmed in a
StatusNotifierWatcher-less container), and the menu/click wiring is exercised by the same code
path already proven correct on Windows (`ITrayIcon`, `TrayMenuItem`). **Not verified**: whether the
icon actually *appears and is clickable* in a real desktop environment's panel — no test
environment this session had a running desktop shell (GNOME Shell, Plasma, etc.) to check against.
Known ecosystem gap either way: **vanilla GNOME Shell needs the "AppIndicator and
KStatusNotifierItem Support" extension** for any SNI tray icon to show at all (ships by default on
Ubuntu, not on stock Fedora Workstation) — not a MoneyShot bug, a real Linux desktop fragmentation
issue every cross-platform tray-icon app hits.

### 5. Clipboard image support — ✅ implemented and verified end-to-end

`MoneyShot.Platform.Linux/LinuxClipboard.cs` PNG-encodes the `CapturedImage` buffer via SkiaSharp
(already a project dependency) and pipes it to `wl-copy --type image/png` (Wayland, detected via
`$WAYLAND_DISPLAY`) or `xclip -selection clipboard -t image/png` (X11) — CLI shellout rather than
Avalonia's own clipboard API, since Avalonia's cross-platform clipboard image support was less
predictable than two well-established, single-purpose CLI tools. **Genuinely verified**: a
standalone test harness called `LinuxClipboard.SetImage()` with a real `CapturedImage`, and reading
the X11 clipboard back afterward (`xclip -o -t image/png`) produced a byte-valid PNG that visually
matched the source image exactly (solid color test image, confirmed pixel-correct on inspection).
Only the X11/`xclip` path was live-tested this way — the Wayland/`wl-copy` branch shares the same
encode step and near-identical invocation shape but wasn't separately exercised (no Wayland
compositor with a clipboard manager was available to test against).

### 6. Auto-update self-swap — not implemented

`AutoUpdateService.BuildWindowsSwapScript`'s exe-swap batch script is Windows-only and has no
Linux port yet — `MoneyShot.UI`'s "Check for Updates" flow will call it on Linux and it will fail
(this was not touched this session; auto-update was out of scope for the platform-layer/packaging
work described here). Bigger than the shell-script rewrite itself: a `.deb`/`.rpm`-installed binary
lives under `/opt/moneyshot`, owned by the package manager — self-overwriting it the way the
Windows MSI-installed exe does would fight `apt`/`dnf` on the next `upgrade`. The auto-updater
needs to detect a package-managed install and refuse ("update via your package manager instead")
rather than attempt a self-swap; only the AppImage build is a legitimate self-update candidate.
**This is a real, currently-open gap** — someone clicking "Check for Updates" on a `.deb`/`.rpm`
install today will hit an unhandled failure path, not a graceful "not supported here" message.

### 7. Registry settings — ✅ implemented

`MoneyShot.Platform.Linux/LinuxAutoStart.cs` implements `IAutoStart.SetStartupWithApp` by writing/
removing `~/.config/autostart/moneyshot.desktop` (the standard freedesktop.org XDG autostart
mechanism, honored by GNOME/KDE/XFCE without any DE-specific code) using the same `AppDataPaths`
config-root resolver as everything else (see § AppDataPaths bug below). `SetPrintScreenSuppressed`/
`TryGetPrintScreenSuppressed` correctly no-op (there's no single "the OS's screenshot shortcut" to
suppress on Linux — GNOME/KDE each bind PrintScreen to their own tool independently) — verified via
code review of the call site (`SettingsWindow`) to confirm a permanently-false result from
`SetPrintScreenSuppressed` doesn't surface a misleading "Partial Success — reopen as admin"
Windows-flavored dialog on every settings save (it did, originally; fixed alongside this work).
Not separately UI-tested (no interactive Linux desktop session to click through Settings on), but
the underlying file I/O is simple enough, and shares the now-verified `AppDataPaths` path
resolution, that this has reasonably high confidence without that direct click-through.

### 8. Path / filesystem assumptions — ⚠️ real bug found and fixed

This document originally assumed `Environment.SpecialFolder.ApplicationData` "resolves to the
right thing on .NET / Linux already." **That assumption was wrong, and only actual execution on
Linux caught it**: confirmed via a minimal standalone test program that it returns an **empty
string** whenever `$XDG_CONFIG_HOME` isn't set in the environment — which is not a rare edge case,
it's the default in plenty of real launch contexts (minimal window managers, some `.desktop` `Exec`
launches, and every container/CI environment used to verify this port). Left unguarded, this meant
`SettingsService`, `Logger`, and `HistoryService` were all silently writing into a *relative*
`MoneyShot/` folder under whatever the process's working directory happened to be at startup —
confirmed by finding real history files landing at `/app/MoneyShot/history/...` instead of
`~/.config/MoneyShot/history/...` in an early test run. Fixed with a shared
`MoneyShot.Core/Services/AppDataPaths.GetConfigRoot()` (uses the OS value if non-empty, else
`$XDG_CONFIG_HOME`, else `~/.config`, per the XDG Base Directory Specification's own documented
fallback) that all three services and `LinuxAutoStart` now call instead of the raw
`Environment.GetFolderPath` call — re-verified afterward: history and settings correctly land
under `~/.config/MoneyShot/` in the same test environment that first exposed the bug.

## Architecture for the port

The cleanest way to manage a cross-platform codebase, **without** the maintenance burden of
forking, is to extract Windows-specific code behind a small set of platform interfaces and
provide per-OS implementations that are selected at startup.

```
MoneyShot.Core           // .NET 10, no UI, no Windows deps
├── IScreenCapture       // Capture full / region / monitor → returns IBitmap
├── IGlobalHotkeys       // Register / unregister by string → fires Action
├── ITrayIcon            // Show / hide / context menu
├── IAutoStart           // Enable / disable autostart at login
├── IClipboard           // SetImage(IBitmap)
├── Services/            // SettingsService, HistoryService, AutoUpdateService, Logger, SaveService — UI-free
└── Models/              // existing models

MoneyShot.UI             // Avalonia, cross-platform
├── Views/               // EditorWindow.axaml, MainWindow.axaml, etc.
└── Editor/              // UndoController, CanvasRenderer (port to Avalonia.Media)

MoneyShot.Platform.Windows
├── Win32ScreenCapture   // current GDI+ logic
├── Win32GlobalHotkeys   // current RegisterHotKey logic
├── Win32TrayIcon        // current NotifyIcon usage
└── …

MoneyShot.Platform.Linux    // net10.0, no Avalonia dep — mirrors Platform.Windows staying WPF-free
├── LinuxScreenCapture     // XGetImage + xrandr-parsed monitor bounds
├── LinuxGlobalHotkeys     // XGrabKey + XNextEvent pump
├── LinuxAutoStart         // ~/.config/autostart/*.desktop
└── LinuxClipboard         // SkiaSharp PNG-encode + wl-copy/xclip shellout

MoneyShot.UI/Platform       // depends on Avalonia, so lives in MoneyShot.UI, not Platform.Linux
├── PlatformServices.cs    // #if WINDOWS branch picks Win32*/Linux* — see below
└── LinuxTrayIcon.cs       // wraps Avalonia.Controls.TrayIcon/NativeMenu
```

**What was actually built, differs slightly from the plan above**: `PlatformServices` is not a
runtime `OperatingSystem.IsWindows()` check — it's a **compile-time** `#if WINDOWS` branch in
`MoneyShot.UI/Platform/PlatformServices.cs`, gated by a `WINDOWS` constant `MoneyShot.UI.csproj`
defines only for its `net10.0-windows` `TargetFramework`. This was chosen over a runtime check
because the two TFMs don't even reference the same platform project (`net10.0-windows` references
`MoneyShot.Platform.Windows`; plain `net10.0` references `MoneyShot.Platform.Linux` — a `net10.0`
build has no `Win32*` types to construct in the first place, so a runtime check alone wouldn't
compile). `dotnet build`/`publish` without `-f` builds both TFMs from one `dotnet build
MoneyShot.sln`; CI/publishing picks one via `-f net10.0-windows -r win-x64` or
`-f net10.0 -r linux-x64`.

## Migration phases

**Phase 0 — extraction (no behavior change, Windows-only). ✅ Done (2026-09).** Pulled
`IScreenCapture`, `IGlobalHotkeys`, `ITrayIcon`, `IAutoStart`, `IClipboard` out into a new
`MoneyShot.Core` project (no UI/Windows dependency) plus a `MoneyShot.Platform.Windows` project
holding the Win32/WinForms implementations (`Win32ScreenCapture`, `Win32GlobalHotkeys`,
`Win32TrayIcon`, `Win32AutoStart`, `Win32Clipboard`). WPF stayed exactly as-is behaviorally; all
109 tests stayed green throughout. Two implementation notes for whoever picks up Phase 1:

- `IScreenCapture` returns a neutral `CapturedImage` (raw BGRA32 pixel buffer) rather than a WPF
  `BitmapSource`, converted at the UI boundary via `MoneyShot/Interop/BitmapConversions.cs`. As a
  side effect this also eliminated the old HBITMAP-handle-leak risk (`Win32ScreenCapture` reads
  pixels via `Bitmap.LockBits` instead of `GetHbitmap`/`DeleteObject`).
- `Win32GlobalHotkeys` hooks `WM_HOTKEY` by subclassing the given HWND with a WinForms
  `NativeWindow` (`AssignHandle`) rather than WPF's `HwndSource.AddHook` — this keeps
  `MoneyShot.Platform.Windows` free of any WPF reference, so the same class should keep working
  once Phase 1 replaces the UI layer with Avalonia. This is a different mechanism than what was
  running before and hasn't been through real-world hotkey testing beyond a clean build — verify
  hotkeys (PrintScreen, Ctrl+PrintScreen, Ctrl+Shift+1..9) actually fire on real Windows hardware
  before trusting this in the field, the same way any other change in this codebase touching
  `EditorWindow`/`HistoryWindow`/the tray menu needs manual verification (no automated UI
  coverage — see `CLAUDE.md`).

`HistoryService` and `SaveService` deliberately stayed WPF/`BitmapSource`-based rather than being
forced into Core — they're squarely a Phase 1 (UI-port) concern per the "What ports cleanly" table
above, and abstracting them now would have meant designing around a UI framework (Avalonia) that
isn't actually in the dependency tree yet. `SaveService` does now take an `IClipboard` so its
clipboard write goes through the platform abstraction; file saving is unchanged.

**Phase 1 — UI port to Avalonia, still Windows-only. 🟡 Built, substantially verified (2026-09).**
`MainWindow`, `EditorWindow`, `HistoryWindow`, `RegionSelector`, `SettingsWindow` were all ported
to `*.axaml` in a new, additive `MoneyShot.UI` project (net10.0-windows for now — see below) that
sits alongside the shipping WPF app without touching it. `CocoaTheme.axaml` re-implements the WPF
theme using Avalonia's `ControlTheme` system. All 109 existing tests still pass (the WPF/Core
test suite; `MoneyShot.UI` itself has no tests, same as WPF's views). No blocker was hit in
Avalonia's `Geometry.FillContains`/`StrokeContains` (used for arrow hit-testing) or
`RenderTargetBitmap` (used for the pixelate brush and final image capture) — both have close WPF
equivalents and the project compiles clean using them.

*What's actually verified, and how:* the whole solution builds with zero errors/warnings, the
Avalonia build launches and stays responsive, and `MainWindow` was confirmed **visually correct by
actually running it and inspecting a screenshot** (both the user's own and one captured via
`user32.dll PrintWindow` from an automation script, since no computer-use/UI-automation tool was
available in-session — see below) — the cocoa-brown theme, custom borderless title bar with
working minimize/maximize/close, the accent capture buttons, icon rendering, and the
dynamically-generated per-monitor button list all render as designed.

Beyond that, **real interactive testing found two genuine, confirmed bugs — both fixed**:

1. **The entire capture→editor flow was broken.** `CaptureFullScreenAsync`/`CaptureRegionAsync`/
   `CaptureMonitorAsync` all call `Hide()` on `MainWindow` before capturing (so the app doesn't
   appear in its own screenshot), then used to call `EditorWindow.ShowDialog(this)` — but Avalonia
   throws `InvalidOperationException: Cannot show window with non-visible owner` if the owner
   isn't currently shown. WPF has no such restriction, so this never surfaced until the build was
   actually clicked through. The same bug hit the error-handling path too: the `catch` block's own
   `SimpleMessageBox.ShowAsync(this, ...)` call used the same hidden owner and threw the identical
   exception, which — because `CaptureFullScreenAsync` runs fire-and-forget from a button/hotkey
   handler with no outer catch — became an unobserved task exception. Net effect: click "Capture
   full screen", the window vanishes, and nothing else ever happens; the process stays alive and
   "Responding" the whole time, which is part of why this needed an actual click-through (and the
   app's own log file) to catch rather than process inspection alone.

   Fixed with a new `MoneyShot.UI.Interop.WindowExtensions.ShowAsDialogAsync(owner)`: uses the
   real `ShowDialog(owner)` when the owner is visible (proper modality), falls back to
   `Show()` + await-on-`Closed` otherwise. Applied everywhere a window used to call
   `ShowDialog(this)` with `MainWindow` as owner (`EditorWindow`, `SettingsWindow` — the latter
   also reachable from the tray menu while hidden — and `RegionSelector`, whose confirmed/cancelled
   result now comes from reading its existing `CroppedScreenshot` property afterward rather than a
   typed dialog-result, since the fallback path has no way to recover that value).
   `SimpleMessageBox` was simplified to always use `Show()` + `Topmost` — a message box doesn't
   need strict modality against its owner, so this sidesteps the constraint entirely rather than
   needing the visible/hidden fallback logic.

2. **History thumbnail generation threw on every save.** `Bitmap.CreateScaledBitmap` throws
   `Invalid source bitmap type` when the source is a `WriteableBitmap` — which is what every
   capture produces (`CapturedImage.ToAvaloniaBitmap()`). This was caught internally (History
   saving degrades to "silently no thumbnail" rather than crashing the capture), but a **second,
   more serious bug rode along with it**: the old code's fast path for "image already small enough,
   no scaling needed" returned the *same* `Bitmap` object passed in, which the caller's
   `using (var thumbnail = ...)` would then dispose — silently corrupting the caller's own
   `image`/`screenshot` object (the one about to be shown in `EditorWindow`) for any capture at or
   under 400px wide. Fixed by rewriting `HistoryService.CreateThumbnail` to downsample via raw
   pixel nearest-neighbor sampling on the `CapturedImage` buffer (reusing the same pattern already
   proven correct elsewhere in the port) and to always return a genuinely distinct `Bitmap`, never
   an alias of the source.

*Re-confirmed end-to-end after the fixes above, in a later session* — once the in-session
automation channel (raw `user32.dll` mouse/keyboard injection and `Graphics.CopyFromScreen`, the
only options available; no dedicated computer-use tool was present) recovered in a fresh process,
every flow the earlier pass had flagged as unconfirmed was actually driven end-to-end against a
freshly-built exe, watching the app's log file for exceptions after each step:
- **Capture full screen → `EditorWindow`**: completes with no exception (the log entry from the
  pre-fix run, timestamped earlier the same day, was compared directly against a clean run
  afterward — same code path, no error). The captured image renders correctly and sharply in the
  editor canvas, not just "the window appears."
- **Drawing + undo** in `EditorWindow`: selecting the Rectangle tool and dragging produced a
  correctly-positioned, correctly-styled (color/stroke) shape; Undo removed it and correctly
  disabled itself again. Closing the editor via the title-bar close button returned cleanly to
  `MainWindow`'s hidden tray state with no stray window and no crash — confirming the
  `ShowAsDialogAsync` → `TaskCompletionSource`/`Closed` fallback path itself works, not just that
  the exception it replaces no longer fires.
- **Tray icon**: double-click correctly shows `MainWindow` again (`MainWindowTitle` became "Money
  Shot"); right-click renders the full context menu (Capture Full Screen, Capture Region, History,
  Check for Updates, Settings, Show Window, Exit) with correct items and grouping; Exit shut the
  process down cleanly (confirmed via the log and process list) — a genuine test of the tray's
  `DoubleClicked` event and menu wiring, not just that `Win32TrayIcon` constructs without throwing.
- **`HistoryWindow`**: opens from `MainWindow`'s History button, lists all 50 real captures with
  correctly-rendered thumbnails — a direct, real-data confirmation that the thumbnail-generation
  fix (bug 2, above) actually works, not just that it compiles. Clicking a thumbnail opens it in
  `EditorWindow` (the other `ShowAsDialogAsync` call site) with the historical image loaded
  correctly; closing that editor returns to `HistoryWindow`, and closing `HistoryWindow` returns to
  `MainWindow` — the window stack unwinds correctly through three layers.
- **`SettingsWindow`**: opens from `MainWindow`'s Settings button and renders fully — all six
  General checkboxes, the two hotkey dropdowns (showing the actual configured values, e.g.
  "Ctrl+PrintScreen"), and the "Individual monitor capture" section correctly detecting and
  reporting "Only one monitor detected" for this machine. Closed via Cancel to avoid touching real
  settings.
- **`RegionSelector`**: opens from `MainWindow`'s Capture Region button showing the "Drag to select
  a region · Esc to cancel" overlay; dragging a region correctly crops to just the dragged
  rectangle and opens it in `EditorWindow` — confirming the ported DPI-aware crop logic runs
  end-to-end (though this machine is single-monitor at 100% scaling, so it does not exercise the
  125%-scaling arithmetic itself — see the open item below).

*What is still NOT verified* — everything above was driven on a single 100%-DPI display; nothing
about `EditorWindow`'s tool interactions beyond basic rectangle draw+undo was exercised:
- Selecting/dragging/moving existing elements, resizing (both the 8-handle box mode and the
  2-handle Line/Path endpoint mode), Crop, Zoom/Pan, Pixelate, Text/Number tools, and the async
  Save-to-file / Copy-to-clipboard / color-picker dialogs — none of these have been interactively
  tested. This is still the highest-risk area of the port: 2000+ lines of the most stateful
  mouse-interaction code in the app (see CLAUDE.md's "Resize/drag" notes on past WPF regressions in
  this exact logic), and none of those specific behaviors have been re-derived for Avalonia's
  pointer-event model yet.
- Global hotkeys (e.g. actually pressing PrintScreen to trigger a capture) — the menu item and
  registration path exist, but the `WM_HOTKEY` message-window flow itself hasn't been triggered.
- Multi-monitor and 125%-scaled-DPI behavior specifically (this session's test machine is
  single-monitor, 100% scaling) — the RegionSelector crop was confirmed correct at 100% scale only.
- "Check for Updates" and "About" dialogs haven't been opened.
- A thin sliver of residual native chrome was visible above the custom title bar in one early
  screenshot, absent in later ones — likely related to Avalonia 12's `ExtendClientAreaChromeHints`
  removal (see implementation note below); intermittent, not investigated further.

**Manual interactive testing of the `EditorWindow` tool-interaction gap above, plus hotkeys and
multi-monitor/DPI-scaled behavior, is still required before trusting this build beyond what's now
confirmed: startup, both capture paths end-to-end, History, Settings, and the tray icon.**

Implementation notes for whoever continues from here:
- `MoneyShot.UI` targets `net10.0-windows`, not the bare `net10.0` the architecture diagram above
  implies for the long-term target — a plain `net10.0` project cannot reference a
  `net10.0-windows` one (`MoneyShot.Platform.Windows`) at all, TFM compatibility requires the
  consumer to be equal-or-more-specific. It becomes genuinely cross-platform (multi-targeting, or
  a runtime `OperatingSystem.IsWindows()` check instead of a compile-time project reference) once
  Phase 2 adds `MoneyShot.Platform.Linux`.
- **Avalonia 12 removed `Window.ExtendClientAreaChromeHints`** (present through 11.x, used for
  WPF-style `WindowChrome`-equivalent borderless-with-shadow windows) in favor of
  `WindowDecorations="None"` + `ExtendClientAreaToDecorationsHint="True"`. This build uses the
  new API since it targets Avalonia 12.1.2, but Avalonia's own tracking issue (#21212) notes the
  old approach's DWM min/max/close animations and window shadow are not fully replicated by the
  new one on Windows — expect this build's windows to look slightly flatter (no drop shadow) than
  the WPF build's, and verify resize-by-dragging-the-edge actually still works (not verified here).
- `IGlobalHotkeys.Initialize()` (in `MoneyShot.Core`/`Win32GlobalHotkeys`) was changed during this
  work to take no window-handle argument — it now creates its own message-only Win32 window
  (`HWND_MESSAGE`) rather than subclassing the app's main window. This was necessary because the
  Avalonia build can start with no window ever created (`StartInTray`) and there's no confirmed
  Avalonia equivalent of WPF's `WindowInteropHelper.EnsureHandle()` for realizing a handle without
  showing anything. The WPF build was updated to match (`MainWindow.InitializeApplication()` no
  longer needs `EnsureHandle()` either) — this is a genuine simplification, not just a Phase-1
  accommodation.
- WPF's `Microsoft.Win32.SaveFileDialog` (sync) became Avalonia's `IStorageProvider.SaveFilePickerAsync`
  (async) — `EditorWindow.Save_Click` and everywhere else a blocking `MessageBox.Show` existed
  became `async`/`await SimpleMessageBox.ShowAsync` (a hand-rolled modal window — Avalonia has no
  built-in `MessageBox`).
- Avalonia's own `Bitmap.Save` is PNG-only regardless of requested format (a known framework
  limitation, see AvaloniaUI/Avalonia#12493) — `MoneyShot.UI.Services.SaveService` uses SkiaSharp
  directly (already an Avalonia dependency, referenced explicitly) to encode real JPEG/BMP.
- `RegionSelector`'s DPI handling was ported from the *already-fixed* WPF version (see § E2 in
  Opus-Speaks.md), not the original buggy one — and is arguably cleaner here, since Avalonia's
  `Screens` API exposes each monitor's scale factor directly (`Screen.Scaling`), avoiding the
  Win32 `GetDpiForMonitor` P/Invoke the WPF fix needed.

**Phase 2 — Linux platform implementations. ✅ Done, substantially verified against real Linux
(2026-09).** `MoneyShot.Platform.Linux` implements `IScreenCapture`/`IGlobalHotkeys`/`IAutoStart`/
`IClipboard` (X11/XDG-based — see § 2–5, 7 above for what's implemented, what's Wayland-only-open,
and exactly how each was verified). `MoneyShot.UI` now multi-targets `net10.0-windows;net10.0`
with TFM-conditional `ProjectReference`s and a `#if WINDOWS`-gated `PlatformServices` factory (see
§ Architecture above) instead of the hardcoded `new Win32ScreenCapture()` etc. calls Phase 1 left
in `MainWindow`/`SettingsWindow`/`EditorWindow`/`HistoryWindow`.

Testing environment actually used (no dedicated Linux desktop machine was available): **WSLg**
(gives a real, if minimal, Wayland/X11 session with a working session D-Bus but no desktop-shell
services — no portal backend, no StatusNotifierWatcher) for early sanity checks, and **Docker**
(`debian:bookworm` and `fedora:latest` containers with `Xvfb` for a genuine headless X server) for
everything that needed either root/package-install access WSL's sudo blocked, or a real X11
`XGetImage`/`XGrabKey` target. This combination gave much higher confidence than a code-review-only
pass, but is explicitly **not** the same as testing on a real GNOME/KDE desktop session — see the
per-item "genuinely verified" notes in § 2–8 above for exactly what each test did and didn't cover,
and the top-of-document status block for the honest summary of what's still open.

**Three real, non-obvious bugs found this way** (i.e., invisible from a clean compile, only found
by actually running the published binary):
1. **`SkiaSharp.NativeAssets.Linux` resolved transitively to `3.119.4`** (via `Avalonia.Skia`'s
   own dependency graph) while the explicit managed `SkiaSharp` package this project also
   references is `4.151.2` — API-incompatible, and the mismatch only throws at runtime
   (`SKImageInfo`'s static constructor, `"the version of the native libSkiaSharp library (119.0)
   is incompatible..."`) with no compile-time signal at all. Confirmed the equivalent Windows
   native asset happened to already resolve to the matching `4.151.2` (so this was silent on
   Windows purely by luck of the transitive-version-resolution dice, not because Windows is
   actually safe from the same class of bug). Fixed by explicitly pinning
   `SkiaSharp.NativeAssets.Linux` to `4.151.2` in both `MoneyShot.UI.csproj` and
   `MoneyShot.Platform.Linux.csproj`.
2. **`Environment.SpecialFolder.ApplicationData` resolves to an empty string on Linux** whenever
   `$XDG_CONFIG_HOME` isn't set — see § 8 above. Silently scattered settings/history/logs into a
   relative path under the process's working directory instead of `~/.config/MoneyShot`. Fixed
   with the new `AppDataPaths.GetConfigRoot()` helper, applied everywhere the raw
   `Environment.GetFolderPath(...ApplicationData)` call used to appear (`SettingsService`,
   `Logger`, both `HistoryService`s, `LinuxAutoStart`).
3. **A genuinely fresh Debian container lacked `libssl3`**, which .NET's crypto/TLS stack needs
   — the app aborted immediately with `"No usable version of libssl was found"`. This was masked
   in the first round of container testing because earlier `apt-get install`s for unrelated tools
   had pulled it in transitively; only testing in a truly clean container (matching what a real
   user's first-ever `apt install ./moneyshot.deb` looks like) surfaced it. Fixed by adding
   `libssl3` (`openssl-libs` on Fedora) to both packages' declared dependencies — see § Packaging.

**What's genuinely verified end-to-end** (built as a self-contained `linux-x64` publish, actually
executed, not just compiled): app startup with no crash (including named-`Mutex` single-instance
detection, which does work correctly cross-platform, confirmed via the mutex's backing file
appearing under `/tmp/.dotnet/shm/`); a real `XGrabKey`-delivered `PrintScreen` hotkey firing
`CaptureFullScreenAsync` → `LinuxScreenCapture.CaptureFullScreen()` (`XGetImage`) → `EditorWindow`
opening with the captured content rendered correctly (screenshotted and visually confirmed —
CocoaTheme renders pixel-identical to the already-verified Windows build); `HistoryService`
correctly saving both the full image and thumbnail to the now-fixed correct path; monitor
enumeration via `xrandr` parsing returning correct geometry; `LinuxClipboard.SetImage()` round-
tripping a real PNG through `xclip` byte-for-byte correct.

**What's not verified**: Wayland-native capture/hotkeys (not implemented — see § 2, § 3); the tray
icon's actual on-screen appearance/clickability in a real desktop panel (no desktop shell was
available to test against, only "doesn't crash without one" was confirmed); `RegionSelector`'s
drag-to-crop interaction specifically on Linux (confirmed on Windows this session, and the region-
capture hotkey path was confirmed to correctly open the selector on Linux, but no synthetic drag
was sent to confirm the crop itself on Linux); `EditorWindow`'s deeper tool interactions (resize,
crop, zoom/pan, pixelate, text/number tools, save-to-file/color-picker dialogs) — verified on
Windows this session (see § Phase 1), not separately re-driven on Linux; multi-monitor behavior
(every test environment was single-virtual-monitor); the `wl-copy`/Wayland clipboard branch
specifically (only the `xclip`/X11 branch was live-tested — see § 5); and `LinuxAutoStart`'s actual
file writes triggered via the Settings UI (the underlying `AppDataPaths`-based path resolution is
verified, the UI click-through isn't).

**Phase 3 — Packaging. ✅ Done, verified via real install + launch in clean containers (2026-09).**
`Packaging/linux/` holds `moneyshot.desktop` (shared), `build-deb.sh`, `build-rpm.sh` +
`rpm/moneyshot.spec`, and `build-appimage.sh` — each takes a self-contained `linux-x64` publish
directory and produces one package, installing everything under `/opt/moneyshot` with a thin
`/usr/bin/moneyshot` launcher script (self-contained .NET publishes carry their own runtime, so
this avoids scattering dozens of DLLs into `/usr/lib`), a `.desktop` entry, and a hicolor icon.
`AutoReqProv`/no automatic `.so` dependency scanning is disabled for the RPM (correct for a
self-contained runtime bundle that carries its own copies of many libraries) in favor of an
explicit, hand-verified `Requires`/`Depends` list.

**Verified, not just built**: `.deb` — built with `dpkg-deb`, then actually `apt-get install
./moneyshot.deb`'d in a **completely fresh** `debian:bookworm` container (not the dev container
used to build it), which correctly auto-resolved every declared dependency, and the installed
`moneyshot` command launched cleanly under Xvfb with zero errors. `.rpm` — built with `rpmbuild`
**on a real Fedora 44 container** (not cross-built from Debian, to get authoritative macro/dist-tag
behavior), then `dnf install`'d in the same container, again auto-resolving dependencies from
Fedora's repos, and launched cleanly. **AppImage** — built by hand-assembling the AppDir (no
`linuxdeploy`: its plugin ecosystem is built around discovering `.so` dependencies via `ldd` for
dynamically-linked C/C++ apps, which has nothing useful to do for an already-self-contained .NET
publish) and running `appimagetool --appimage-extract-and-run` (avoids needing FUSE, which isn't
set up on GitHub-hosted runners or plain Docker containers by default); the resulting `.AppImage`
ran cleanly via the same extract-and-run mode.

A real bug was caught specifically by testing the **exact relative-path invocation pattern the CI
workflow uses** (`./publish-linux` rather than an absolute path, matching how a workflow step's
`run:` block naturally refers to files): `build-rpm.sh` broke because `rpmbuild`'s `%install`
step runs from its own internal `BUILD` directory, not the caller's `cwd`, so a relative
`_moneyshot_publish_dir` macro resolved against the wrong directory and failed with "No such file
or directory." (`build-deb.sh` and `build-appimage.sh` don't `cd` internally and were unaffected.)
Fixed by resolving `PUBLISH_DIR`/`OUTPUT_DIR` to absolute paths (`cd "$dir" && pwd`) at the top of
`build-rpm.sh`, then re-verified with the exact relative-path CI pattern.

`.github/workflows/build-linux.yml` (new) mirrors `build.yml`'s PR/release triggers, building and
packaging all three formats and uploading them as workflow artifacts (and as release assets when
triggered by a GitHub Release). `release.yml`'s existing auto-release-on-push-to-main flow gained
a second job, `build-and-release-linux`, that runs after the Windows job, recomputes the identical
version tag, and attaches the same three Linux packages (plus a `SHA256SUMS-linux.txt`) to that
same release — mirroring how the Windows job attaches its MSI/zip/`SHA256SUMS.txt`. **Not
verified**: an actual GitHub Actions run of either workflow (only manually replayed the same
command sequence locally/in Docker) — YAML syntax was validated with a real parser, and every
individual step's shell commands were run for real, but the workflow files themselves have not
executed inside GitHub's own runner infrastructure yet. AUR (Arch) packaging remains
community-maintainable but wasn't built here.

**Phase 4 — Wayland (deferred, still not started).** Portal-based `org.freedesktop.portal.
Screenshot` capture and `org.freedesktop.portal.GlobalShortcuts` hotkeys are the correct paths —
see § 2 and § 3 for exactly what's missing and why it wasn't attempted this round (no portal
backend service was available in any environment used to build/verify this port, and the
interactive-consent-dialog UX change is a product decision that should be made deliberately, not
implemented blind). **2–3 weeks if pursued**, unchanged from the original estimate — nothing done
in Phase 2/3 reduces this, since Wayland capture/hotkeys are architecturally a different mechanism
(D-Bus/portal) from the X11 approach actually implemented, not an extension of it.

## Wayland status (read this before deploying to Debian/Fedora desktop users)

**Both current Debian (12, "bookworm," GNOME on Wayland by default) and Fedora Workstation default
to a native Wayland session out of the box.** On such a session, *as currently implemented*:
- Screen capture will fail (`XOpenDisplay` finds no X server) unless the user is on an X11/Xorg
  session, or a compositor exposing enough of an XWayland-visible surface for `XGetImage` to
  return something meaningful (untested; likely to be incomplete or wrong even when it doesn't
  outright fail).
- Global hotkeys silently do nothing (logged once, then quiet) — the tray menu and in-app capture
  buttons remain fully functional as the fallback.
- The tray icon, clipboard, and autostart all work regardless of X11 vs. Wayland — none of those
  three depend on the display-server-specific mechanisms above.

**Practically**: this build is genuinely useful today for X11 sessions (still selectable at the
login/display-manager screen on virtually every mainstream distro, including Debian and Fedora,
even where Wayland is the default) and for the parts of a Wayland session that don't touch capture/
hotkeys. It is **not** a full drop-in replacement for the Windows experience on a default modern
Wayland desktop until Phase 4 lands. This should be stated plainly in any release notes / README
for the Linux build, not discovered by users via a silent capture failure.

## Packaging on Linux

| Format | Audience | Status |
|---|---|---|
| **`.deb`** | Ubuntu, Debian, Mint | ✅ Built (`Packaging/linux/build-deb.sh`) and verified: `apt-get install`'d in a clean `debian:bookworm` container, dependencies auto-resolved, launched cleanly. |
| **`.rpm`** | Fedora, openSUSE | ✅ Built (`Packaging/linux/build-rpm.sh` + `rpm/moneyshot.spec`) and verified: `dnf install`'d on real Fedora 44, dependencies auto-resolved, launched cleanly. |
| **AppImage** | Distro-agnostic, easy for end users, self-updating candidate. | ✅ Built (`Packaging/linux/build-appimage.sh`, hand-assembled AppDir, no `linuxdeploy`) and verified: ran cleanly via `--appimage-extract-and-run`. |
| **Flatpak** | Modern desktops, sandboxed | Not built. Would integrate with the Wayland portals "for free" — worth revisiting alongside Phase 4. |
| **Snap** | Ubuntu primarily | Not built — politically charged; many Linux users dislike snaps. |
| **AUR (PKGBUILD)** | Arch | Not built — community-maintainable, low priority. |

`.github/workflows/build-linux.yml` builds and uploads all three verified formats on every PR and
release; `release.yml`'s `build-and-release-linux` job attaches them to the same auto-created
release the Windows MSI/zip go to (see § Phase 3 above for exactly what was and wasn't verified
about the CI wiring itself, as opposed to the packaging scripts it calls).

## Risks & open questions

Resolved by actually building and testing (kept here for the record, not because they're still
open): **Avalonia drawing fidelity** — CocoaTheme and the capture→editor rendering path are
confirmed pixel-correct on real Linux (see Phase 1/2 above); pixelate specifically wasn't
separately re-driven on Linux, folded into the general `EditorWindow` tool-interaction gap.
**GDI handle leaks** — moot, `LinuxScreenCapture` never touches a GDI-equivalent handle at all.
**Single-instance enforcement** — a plain named `Mutex` (no `Global\` prefix) works correctly
cross-platform as-is; confirmed via its backing file appearing under `/tmp/.dotnet/shm/` on a real
run. No pidfile/flock rewrite was needed.

Still genuinely open:
- **Hotkey collisions with the desktop environment.** PrintScreen is bound by every major DE to
  its own screenshot tool. Windows has a registry switch to disable Snipping Tool's claim on it;
  Linux has no equivalent, and the user would need to unbind it themselves in their DE's settings.
  Real, unavoidable friction — not something a code change fixes.
- **Auto-update under package managers.** Confirmed still missing (see § 6 above) — a `.deb`/`.rpm`
  install's "Check for Updates" will hit an unhandled failure today rather than a graceful
  "update via your package manager" message. The portable AppImage is the only build that could
  legitimately self-update.
- **HiDPI / fractional scaling on Linux.** Every test environment this round was 100%-scale,
  single-monitor. `RegionSelector`'s DPI-aware crop math (already fixed for WPF's 125%-scaling
  bug, see `Opus-Speaks.md` § E2, and ported into the Avalonia build using `Screens.Primary.
  Scaling`) has not been exercised at a non-100% Linux scale factor at all.
- **Wayland** — see § Wayland status above; this is the single largest remaining gap.

## Decision matrix for the team

The build/no-build decision this table originally framed is now moot — Phases 0–3 are done. What's
left is a narrower question: **is Wayland support (Phase 4) worth 2–3 more weeks**, given the port
already works on X11 sessions (still selectable at login on virtually every mainstream distro) and
X11-independent features (tray, clipboard, autostart, packaging) work regardless of session type.
That's a product call — how many target users are on a Wayland-only setup with no X11 fallback —
not an engineering-feasibility one; the engineering path (portal-based capture + GlobalShortcuts,
with an interactive per-capture consent dialog as an unavoidable UX change) is understood and
documented above, just not built.

## What actually changed (concrete list)

Reflects what was actually done, not a pre-port plan — the shipping WPF app (`MoneyShot/`) was
**never modified for Linux support**; everything below is additive (`MoneyShot.UI`) or shared
platform-neutral code both builds use (`MoneyShot.Core`).

- `MoneyShot.Platform.Linux/` (new project) — `LinuxScreenCapture.cs`, `LinuxGlobalHotkeys.cs`,
  `X11Interop.cs`, `LinuxAutoStart.cs`, `LinuxClipboard.cs`, `LinuxMemoryTrimmer.cs`
- `MoneyShot.UI/MoneyShot.UI.csproj` — multi-targets `net10.0-windows;net10.0`; TFM-conditional
  `ProjectReference`s to `Platform.Windows`/`Platform.Linux`; `WINDOWS` compile constant for the
  Windows TFM only; `SkiaSharp.NativeAssets.Linux` explicitly pinned (see the SkiaSharp bug above);
  `WindowsForms` `FrameworkReference` gated to the Windows TFM only
- `MoneyShot.UI/Platform/` (new) — `PlatformServices.cs` (the `#if WINDOWS` factory),
  `LinuxTrayIcon.cs` (wraps Avalonia's own `TrayIcon`)
- `MoneyShot.UI/Views/MainWindow.axaml.cs`, `SettingsWindow.axaml.cs`, `EditorWindow.axaml.cs`,
  `HistoryWindow.axaml.cs` — every `new Win32*(...)` call site replaced with
  `PlatformServices.Create*()`; `EditorWindow`'s color-picker button gained a `#if WINDOWS`/`#else`
  split (WinForms `ColorDialog` vs. the new `SimpleColorDialog`)
- `MoneyShot.UI/Views/SimpleColorDialog.cs` (new) — small hand-rolled Avalonia RGB/hex color
  picker for the non-Windows build, replacing WinForms' `ColorDialog`
- `MoneyShot.UI/Assets/icon.png` (new, copied from `MoneyShot/icon.png`) — `MoneyShot.UI` had no
  icon asset at all before this; now used for the window icon and the Linux tray icon
- `MoneyShot.Core/Services/AppDataPaths.cs` (new) — the config-root-resolution bugfix (§ 8 above),
  used by `SettingsService`, `Logger`, both `HistoryService`s, and `LinuxAutoStart`
- `Packaging/linux/` (new) — `moneyshot.desktop`, `build-deb.sh`, `build-rpm.sh` +
  `rpm/moneyshot.spec`, `build-appimage.sh` (see § Phase 3 above)
- `.github/workflows/build-linux.yml` (new) — Linux build+package CI, mirrors `build.yml`'s triggers
- `.github/workflows/release.yml` — gained `build-and-release-linux` job, attaches Linux packages
  to the same auto-created release the Windows job creates
- `MoneyShot.sln` — `MoneyShot.Platform.Linux` added as a solution project

Not touched: `MoneyShot/` (the WPF app), `Installer/Product.wxs`, `build.yml`, `build-msi.yml`,
`MoneyShot.Tests/` (still `net10.0-windows`-only — it references `MoneyShot.Platform.Windows`
directly for Windows-specific test coverage and was not multi-targeted this round).

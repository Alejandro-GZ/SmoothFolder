# SmoothFolder

**SmoothFolder brings iOS-inspired application folders to the Windows 11 desktop.**

It is a lightweight desktop companion for grouping games and applications into
compact glass folders without replacing the Windows shell, taskbar, or launcher.
SmoothFolder is designed to stay out of the way: it runs as a background GUI
process, creates no console window, and its folder UI is excluded from the
Windows taskbar and Alt+Tab/task-switcher surfaces.

> SmoothFolder is currently under active development. The core folder experience
> is usable, while deeper desktop-shell integration and additional polish are
> still planned.

## Features

- Compact desktop folders with a **3x3 live icon preview**.
- iOS-inspired **glass folder panel** with per-folder tint and tint strength.
- Opening and closing animations that expand from and collapse back into the
  folder's current desktop position.
- Drag-and-drop support for:
  - Windows shortcuts (`.lnk`)
  - Steam / Epic shortcuts (`.url`)
  - executables (`.exe`)
  - folders and other Shell-openable items.
- High-resolution icon extraction through the Windows Shell.
- Size-aware icon selection for sharper small folder previews.
- Right-click actions for renaming and removing application entries.
- Drag-and-drop reordering of applications inside an open folder.
- iOS-style **4x3 application pages** with page indicators, wheel/keyboard
  navigation, flicker-free slide transitions, and drag-to-edge page switching
  while reordering.
- Create, rename, move, tint, and delete desktop folders.
- Persistent configuration under `%LOCALAPPDATA%\SmoothFolder`.
- Background-app behavior:
  - `WinExe` output, so no console window is created.
  - no normal Windows taskbar entry.
  - auxiliary windows are marked as tool windows and excluded from Alt+Tab.
  - compact desktop tiles use `WS_EX_NOACTIVATE` and are kept below normal app windows.
  - system-tray icon with quick access to **Settings**, the data folder, a
    per-user **Start with Windows** toggle, and Exit.
  - persistent user-level Settings foundation stored separately from folder
    data, with Appearance controls for blur, tint strength and saturation.
  - single-instance process protection.
- No game or application installation is modified by SmoothFolder.

## Installation

### GitHub Releases

Tagged releases are built automatically by GitHub Actions.

Download the archive for your machine:

- `SmoothFolder-<version>-win-x64.zip` for standard 64-bit Intel/AMD Windows PCs.
- `SmoothFolder-<version>-win-arm64.zip` for Windows on ARM.

Release builds are **self-contained**, so users do not need to install the
.NET SDK or runtime separately.

Extract the archive and run:

```text
SmoothFolder.exe
```

### Build from source

Requirements:

- Windows 11
- .NET 10 SDK
- Windows SDK projection support (`net10.0-windows10.0.19041.0`)
- PowerShell or another terminal

```powershell
git clone <repository-url>
cd SmoothFolder
dotnet restore
dotnet build -c Release
dotnet run
```

The regular Release output is created under:

```text
bin\Release\net10.0-windows10.0.19041.0\
```

## Usage

On first launch, SmoothFolder creates a `Games` folder.

1. Drag Steam, Epic, Windows, or executable shortcuts onto the folder.
2. Click the folder to open it.
3. Click an item to launch it.
4. Use the mouse wheel, `Left` / `Right`, `Page Up` / `Page Down`, or the
   page indicators to move between pages in larger folders.
5. Drag an item onto another position to reorder the folder. Hold it near the
   left/right edge to move across pages.
6. Right-click an item to rename its displayed name or remove it from the folder.
7. Drag the folder itself to reposition it.
8. Right-click the closed folder for folder-level actions such as rename,
   glass tint, creating another folder, or exiting SmoothFolder.

Removing an item from SmoothFolder **does not uninstall or delete the game**.

For `.lnk` and `.url` files, SmoothFolder stores a private copy under its AppData
directory. This means the original shortcut can be removed from the desktop
without breaking the SmoothFolder entry.

## Appearance

Each folder stores its own glass appearance settings.

Use:

```text
Right-click folder → Glass tint...
```

to select a preset or custom hexadecimal color and change the tint strength.

The renderer uses per-pixel transparency, layered highlights, rounded surfaces,
and a configurable tint.

The main folder popup uses a live GPU backdrop based on the system
`Windows.UI.Composition` visual layer. A small `WS_EX_NOREDIRECTIONBITMAP`
helper HWND sits directly behind the WPF glass card and hosts a raw
`BackdropBrush` processed by a native Direct2D Gaussian blur effect descriptor.
`HostBackdropBrush` is deliberately not used for the tunable material because
Windows applies its own fixed security blur to that source, masking changes to
the app's Gaussian standard deviation. The compositor performs the only blur
stage continuously on the GPU; SmoothFolder no longer captures the desktop,
runs a CPU box blur, or maintains a 40 FPS refresh timer.

The composition visual is clipped with a rounded
`CompositionRoundedRectangleGeometry` / `CompositionGeometricClip` matching the
inner WPF card. WPF remains responsible for tint, highlights, border, icons and
text, while the composition helper contributes only the live blurred backdrop.

The Win32 composition target is obtained through C#/WinRT COM interop
(`Compositor.As<ICompositorDesktopInterop>()`) using ABI-only parameters. Legacy
direct CLR casts from `Compositor` to `ICompositorDesktopInterop` are not used,
because they fail on modern .NET/C#/WinRT projections.

The local `IGraphicsEffectD2D1Interop` projection follows the native
`windows.graphics.effects.interop.h` ABI exactly: `GetNamedPropertyMapping`
receives an `LPCWSTR`, effect properties are returned as `IPropertyValue*`, and
the COM vtable order is `GetEffectId`, `GetNamedPropertyMapping`,
`GetPropertyCount`, `GetProperty`, `GetSource`, `GetSourceCount`. The native
header declaration, rather than the ordering of methods on documentation index
pages, is treated as the ABI authority.
The projected interface follows the C#/WinRT custom-interface layout directly:
`WindowsRuntimeHelperType` points back to the projected interface itself, and
that interface owns a public nested `Vftbl` containing
`AbiToProjectionVftablePtr`. This is the layout C#/WinRT reflects when it
creates a CCW for the managed effect descriptor.
The .NET Windows SDK keeps `IPropertyValue` itself internal, so effect-property
marshalling uses its native IID (`4BD682DD-7554-40E9-9A9B-82654EDE7E62`) via
`QueryInterface` rather than referencing the inaccessible projected type.
During open/close animations SmoothFolder synchronizes only the helper geometry
with the transformed WPF card; while dragging, the live backdrop updates
automatically as the helper window moves.

The default GPU material deliberately separates blur from tint: Direct2D
Gaussian blur uses a light 3 DIP standard deviation (roughly a 9 DIP kernel
radius), while the WPF tint contribution is reduced when the GPU backdrop is
active so vivid wallpapers keep recognizable structure. Its
`Blur.BlurAmount` property is registered as animatable when the effect factory
is created and is written explicitly through
`CompositionEffectBrush.Properties`, keeping the material tunable without
rebuilding the effect graph. The descriptor exposes the complete three-property
Direct2D Gaussian Blur surface
(`StandardDeviation`, `Optimization`, and `BorderMode`) because Composition
queries the native property table when creating the effect factory. SmoothFolder
describes that effect through the standard `Windows.Graphics.Effects` contract
and carries a small local C#/WinRT projection for
`IGraphicsEffectD2D1Interop`, which the Windows SDK exposes as a native-only
interface. This avoids activating an external WinRT graphics runtime class and
therefore avoids package-registration requirements in the unpackaged WPF
process. The helper
window remains expanded beyond the visible card bounds so the Gaussian kernel
can sample outside the border and avoid edge artifacts. If GPU composition
initialization fails or High Contrast mode is active, SmoothFolder falls back
to the existing translucent WPF renderer.


## Settings

SmoothFolder exposes a dedicated Settings window from the tray menu (or by
double-clicking the tray icon). User-level preferences are stored separately
from desktop folder data at:

```text
%LOCALAPPDATA%\SmoothFolder\settings.json
```

The first Settings block provides persisted Appearance values for **blur
strength**, **tint strength**, and **saturation**, plus the existing **Start
with Windows** behavior toggle. Appearance changes raise a shared
`SettingsChanged` event and are applied live to open folder popups and compact
desktop tiles: blur and saturation update the shared Direct2D composition
effect graph while tint strength updates each WPF glass tint layer. New popups
and all persistent desktop tiles immediately use the persisted values.

## Start with Windows

The tray menu includes a checkable **Start with Windows** item. SmoothFolder
registers the current executable for the current user under:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

No administrator privileges are required. Disabling the option removes the
`SmoothFolder` value from that key. If the application is moved to a different
folder, toggling the option off and on again refreshes the registered executable
path.

## Background behavior

SmoothFolder is intended to behave like part of the desktop rather than like a
normal application window.

All SmoothFolder windows use `ShowInTaskbar="False"` and are explicitly marked
with the Windows `WS_EX_TOOLWINDOW` extended style while `WS_EX_APPWINDOW` is
removed. This keeps folder tiles and popups out of normal taskbar / Alt+Tab
surfaces and also gives shell replacements a standard signal that these are
auxiliary desktop UI windows.

Compact folder tiles are anchored to Explorer's desktop hierarchy. Each tile
also owns a transparent Windows.UI.Composition helper directly below its WPF
card in desktop Z-order, so the compact 3x3 preview uses the same raw
`BackdropBrush` + Direct2D blur/saturation material as folder popups. The helper
is re-synchronized after Explorer recovery, display changes and native tile
dragging.

SmoothFolder discovers the `WorkerW` / `Progman` host that owns
`SHELLDLL_DefView`, then keeps
each transparent WPF tile immediately above that desktop host in the top-level
Z-order. Tiles are deliberately not native-owned by Explorer: native ownership
can force an owned tile above unrelated application windows.

The compact tile also uses `WS_EX_NOACTIVATE`, so clicking it does not promote
the tile to the foreground application. Normal application windows therefore
remain above SmoothFolder. The large folder panel is intentionally a normal
top-level window so it can animate, accept focus, and extend beyond the desktop
host's client area.

This integration relies on undocumented Explorer window classes and is therefore
treated as a best-effort compatibility layer rather than a public Windows API.

Explorer recovery is event-driven: SmoothFolder listens for the shell's
`TaskbarCreated` broadcast, invalidates the old desktop hierarchy, waits for the
replacement shell to become ready with bounded backoff, and then reanchors the
existing tiles. Recovery is considered complete only after every live tile
confirms a successful desktop reattachment; partial reattachment continues
through the bounded retry sequence. A slower periodic health check remains as a
safety net for shell changes that do not emit the expected broadcast. Tiles are
temporarily hidden during the short recovery window so they cannot float above
normal applications while Explorer is rebuilding its desktop.

Desktop discovery is also layout-aware. SmoothFolder classifies the current
Explorer hierarchy instead of assuming one fixed WorkerW arrangement:

- `RaisedProgman` — modern Windows 11 raised desktop, detected through
  `WS_EX_NOREDIRECTIONBITMAP` on `Progman`.
- `ClassicWorkerW` — `SHELLDLL_DefView` is hosted by a top-level `WorkerW`.
- `ProgmanHosted` — the icon view is hosted directly by a classic `Progman`.
- `CompatibleUnknown` — an unrecognized hierarchy that is still structurally
  valid and fully owned by the same Explorer process.

SmoothFolder no longer sends Explorer's undocumented `0x052C` WorkerW message
during normal discovery. It is used only as a compatibility wake-up if
`SHELLDLL_DefView` cannot be found, reducing unnecessary shell mutations.

### Multi-monitor and DPI behavior

SmoothFolder declares Per-Monitor V2 DPI awareness and keeps desktop-shell
geometry in physical pixels. Compact folder positions are persisted relative to
their monitor device (`\\.\DISPLAYx`) rather than as one global WPF-DIP
coordinate. This allows negative virtual-desktop coordinates and mixed 100% /
125% / 150% scale layouts without introducing drag jumps at monitor boundaries.

Folder popups use the work area and effective DPI of the monitor containing the
compact tile, so the popup opens on the correct display and automatically flips
above the tile when there is not enough space below it. Legacy `X` / `Y`
configuration values are migrated automatically when a folder is next placed.

Live topology changes are debounced and reconciled while SmoothFolder is
running. This covers monitor hot-plug, resolution/layout changes, scale changes
that alter the effective desktop geometry, and work-area changes such as moving
the taskbar. If a persisted monitor disappears, its folders are moved to the
current primary display using the same monitor-relative offset and then clamped
to that display's work area. Open folder popups are reflowed with their anchor.

## Data and privacy

SmoothFolder works locally.

Application data is stored in:

```text
%LOCALAPPDATA%\SmoothFolder
```

This currently contains:

- `config.json` — folder layout and settings.
- `Items\` — private copies of imported `.lnk` / `.url` shortcuts.
- `Logs\` — bounded diagnostic and desktop-host logs. The active
  `smoothfolder.log` rotates at approximately 512 KiB and SmoothFolder retains
  two previous files (`smoothfolder.1.log` and `smoothfolder.2.log`), keeping
  the normal log footprint around 1.5 MiB or less.

SmoothFolder does not need a cloud service for its core functionality.

## Development

### CI

`.github/workflows/ci.yml` runs on pushes to `main` / `master` and on pull
requests. It restores, builds, and performs a Windows x64 publish smoke test.

### Releases

`.github/workflows/release.yml` runs when a version tag is pushed:

```powershell
git tag v0.2.0
git push origin v0.2.0
```

The workflow:

1. publishes self-contained single-file builds for `win-x64` and `win-arm64`;
2. packages each build as a ZIP;
3. generates SHA-256 checksums;
4. creates a GitHub Release with automatically generated release notes.

## Current limitations

- Desktop hosting uses undocumented Explorer internals (`Progman`, `WorkerW`
  and `SHELLDLL_DefView`) and may need compatibility adjustments on future
  Windows builds or with third-party desktop replacements.
- Desktop hosting still relies on Explorer implementation details, so future
  Windows builds may require additional host-discovery compatibility rules.
- Monitor-relative tile positions are persisted by display device name. If a
  previously used display is disconnected, the tile falls back to an available
  work area and is remapped on the next successful placement.
- There is no automatic Steam/Epic library importer yet.

## Roadmap

Popup and compact desktop tiles now share the live GPU glass material and the
same persisted blur, saturation and tint controls.

Near-term priorities:

1. Replace tile and popup-item context menus with the reusable iOS-like menu UI.
2. Replace internal app reorder with an iOS-like lifted drag visual and live
   slot displacement.
3. Refine touchpad/touch page gestures and cross-page reorder behavior.
4. Extend Explorer compatibility profiles as new Windows layouts are observed.
5. Steam/Epic library import and higher-quality artwork fallbacks.

## Project structure

```text
SmoothFolder/
├── Models/                 Data models and persisted configuration
├── Native/                 Win32 / DWM integration
├── Services/               Configuration, icons, launching, imports and logging
├── Views/                  WPF folder tiles, popups and dialogs
├── .github/workflows/      CI and tagged GitHub release automation
├── App.xaml
├── App.xaml.cs
└── SmoothFolder.csproj
```

## Status

SmoothFolder is an early-stage Windows desktop customization project. APIs and
configuration formats may change while the interaction model is being refined.

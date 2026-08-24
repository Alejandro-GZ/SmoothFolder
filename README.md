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
- Create, rename, move, tint, and delete desktop folders.
- Persistent configuration under `%LOCALAPPDATA%\SmoothFolder`.
- Background-app behavior:
  - `WinExe` output, so no console window is created.
  - no normal Windows taskbar entry.
  - auxiliary windows are marked as tool windows and excluded from Alt+Tab.
  - compact desktop tiles use `WS_EX_NOACTIVATE` and are kept below normal app windows.
  - system-tray icon with quick access to the data folder and Exit.
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
bin\Release\net10.0-windows\
```

## Usage

On first launch, SmoothFolder creates a `Games` folder.

1. Drag Steam, Epic, Windows, or executable shortcuts onto the folder.
2. Click the folder to open it.
3. Click an item to launch it.
4. Right-click an item to rename its displayed name or remove it from the folder.
5. Drag the folder itself to reposition it.
6. Right-click the closed folder for folder-level actions such as rename,
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

The current renderer uses per-pixel transparency, layered highlights, rounded
surfaces, and a configurable tint. A stronger background-blur implementation is
planned once it can be added without compromising the clean rounded silhouette.

## Background behavior

SmoothFolder is intended to behave like part of the desktop rather than like a
normal application window.

All SmoothFolder windows use `ShowInTaskbar="False"` and are explicitly marked
with the Windows `WS_EX_TOOLWINDOW` extended style while `WS_EX_APPWINDOW` is
removed. This keeps folder tiles and popups out of normal taskbar / Alt+Tab
surfaces and also gives shell replacements a standard signal that these are
auxiliary desktop UI windows.

Compact folder tiles are anchored to Explorer's desktop hierarchy. SmoothFolder
discovers the `WorkerW` / `Progman` host that owns `SHELLDLL_DefView`, then keeps
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
- Items cannot yet be reordered by dragging them within an open folder.
- Large folders currently scroll rather than using iOS-style pages.
- Monitor-relative tile positions are persisted by display device name. If a
  previously used display is disconnected, the tile falls back to an available
  work area and is remapped on the next successful placement.
- There is no built-in startup-with-Windows setting yet.
- There is no automatic Steam/Epic library importer yet.

## Roadmap

Near-term priorities:

1. Extend Explorer compatibility profiles as new Windows layouts are observed.
2. Harden monitor hot-plug / topology-change recovery.
3. Drag-and-drop item reordering.
4. iOS-style folder pages and page indicators.
5. Improved glass blur while preserving transparent rounded corners.
6. Startup-with-Windows support.
7. Steam/Epic library import and higher-quality artwork fallbacks.

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

# SmoothFolder

SmoothFolder is a small WPF application for Windows 11 that creates application/game
folders directly on the desktop with an interaction model inspired by iOS.

## Included in the MVP

- A `Games` folder is created on first launch.
- Compact folder window with a 3x3 icon preview.
- Click the folder to open a glass-style panel.
- Drag and drop onto either the closed folder or the open panel.
- Practical support for:
  - `.lnk`
  - Steam / Epic `.url` shortcuts
  - `.exe`
  - folders and other items that Windows can open through ShellExecute.
- Dropped `.lnk` and `.url` shortcuts are copied to `%LOCALAPPDATA%\SmoothFolder`
  so the original desktop shortcut can be removed or hidden without breaking the folder.
- Icon resolution through the Windows Shell.
- For `.url` shortcuts, SmoothFolder first tries `IconFile=...`, which is commonly
  used by Steam and Epic shortcuts.
- Persistent configuration stored in `%LOCALAPPDATA%\SmoothFolder\config.json`.
- Move folders by dragging them around the desktop.
- Create, rename, and delete folders from the context menu.
- Rename or remove items from the context menu.
- Windows 11 system backdrop and opening animation for the folder popup.
- SmoothFolder does not appear on the taskbar and should not clutter Alt+Tab.

## Requirements

- Windows 11
- .NET 10 SDK
- PowerShell

## Build

Open PowerShell in the project directory:

```powershell
dotnet restore
dotnet build -c Release
dotnet run
```

The Release executable is generated under:

```text
bin\Release\net10.0-windows\
```

## Usage

1. Run the application.
2. A `Games` folder will appear.
3. Drag a Steam/Epic shortcut directly onto it.
4. Click the folder to open it.
5. Click a game to launch it.
6. Drag the folder to reposition it.
7. Right-click the folder to rename it, create another folder, or delete it.


## Appearance

Each folder can use its own glass tint. Right-click a desktop folder and choose
`Glass tint...` to select a preset or enter a custom hex color, then adjust tint strength.
The popup uses the Windows 11 backdrop plus a translucent tint layer and a clipped native
window region for clean rounded corners.

## Intentional v0.1 limitations

- SmoothFolder is not yet embedded into the desktop `WorkerW` layer. It behaves
  as a taskbar/Alt+Tab-hidden window, but `Win+D` can still hide it like a normal window.
- Items cannot yet be reordered with drag and drop inside a folder.
- There are no pages for large collections yet; the panel currently scrolls.
- The popup uses the Windows 11 system backdrop, but there are not yet controls
  for Aurora Glass-style tint, blur, radius, or opacity.
- Windows startup integration is not implemented yet.
- Multi-monitor positioning currently uses the primary work area to constrain the popup.

## Suggested next steps

1. Integrate with the desktop (`WorkerW`) for true desktop-icon-like behavior.
2. Add drag-and-drop item reordering.
3. Add iOS-style 3x3 / 4x3 pages.
4. Add Steam/Epic library import.
5. Add manual icon selection and local artwork fallback.
6. Add blur/tint/radius/size customization.
7. Add Windows startup integration.

## v0.1.1 changes

- Renamed the project completely to `SmoothFolder`.
- Fixed missing `System.IO` imports that prevented `LauncherService` and
  `FolderPopupWindow` from compiling.
- Persistent application data now uses `%LOCALAPPDATA%\SmoothFolder`.

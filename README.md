# CleanSweep

CleanSweep is a Windows 11 style WPF desktop utility targeting **.NET 10**. It clears the contents of selected temporary and cache folders. The selected root folders themselves are preserved.

## Included default locations

Default paths are resolved from the signed-in Windows account and Windows installation. No username or drive letter is hard-coded.

- `%LOCALAPPDATA%\Temp`
- `%LOCALAPPDATA%\Microsoft\Windows\Temporary Internet Files`
- `%APPDATA%\Microsoft\Windows\Recent`
- `%WINDIR%\Prefetch` (included, but unchecked by default)
- `%WINDIR%\Temp`
- `%LOCALAPPDATA%\Google\Chrome\User Data\Default\Cache`
- `%LOCALAPPDATA%\NVIDIA\DXCache`
- `%LOCALAPPDATA%\NVIDIA\GLCache`
- `%LOCALAPPDATA%\Microsoft\Windows\INetCache\IE`

Missing folders remain visible and are reported as missing/skipped. Add more folders with **Add folder**. All folder paths, labels, default/custom flags, and enabled states are stored in `localsetting.json` beside `CleanSweep.exe`. On first launch, the packaged JSON uses Windows environment variables such as `%LOCALAPPDATA%` and `%WINDIR%`; CleanSweep expands these to absolute paths for the current PC and writes the expanded paths back to the same JSON file. Adding or removing a custom folder updates this file. Changes to folder selection are also saved.

## Safety and deletion behaviour

- Cleanup requires explicit confirmation before any deletion starts.
- Every file and nested directory inside each selected root is targeted. The selected root folder stays in place.
- Drive roots, Windows system folders (except Windows Temp and Prefetch), Program Files, ProgramData, complete user-profile roots, and the top-level `C:\Users` directory are protected.
- Junctions and directory symbolic links are not traversed or removed. File links are deleted as links, not followed to their targets.
- Files in use, access-denied items, and other failed operations are skipped and shown in the report. The app does not close running applications, take ownership, change ACLs, or request administrator elevation.
- Files are deleted with bounded parallelism, up to four workers. Folders are removed deepest-first. The app does not apply a system-wide lock.
- Cancellation stops further work. Items deleted before cancellation remain deleted.
- Read-only attributes are cleared only on an item immediately before a deletion attempt. Other attributes are not intentionally changed.

**Important:** This is permanent deletion, not Recycle Bin cleanup. Review checked folders before pressing **Clean selected folders**. `Prefetch` is provided to match the supplied batch script, but starts unchecked because Windows manages this cache and clearing it routinely can temporarily slow application startup.

## Build and publish on Windows

### Requirements

- Windows 10 or Windows 11, x64.
- .NET 10 SDK. Download from [Microsoft .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0).
- Visual Studio 2026 with the **.NET desktop development** workload is optional. The command-line SDK is enough.

### Publish one self-contained EXE

Open PowerShell in this folder and run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\Build-Windows.ps1
```

The script publishes:

```text
publish\win-x64\CleanSweep.exe
```

The EXE is self-contained for Windows x64 and does not require the .NET runtime to be installed on the target PC. `localsetting.json` is published beside the EXE as an external configuration file, not embedded inside the EXE. Keep both files together. The app must run from a folder where the current Windows account has write permission because it updates `localsetting.json` in place. Avoid placing it under `C:\Program Files` unless write permissions are configured. The output is specific to Windows x64. For Windows ARM64, change the runtime identifier to `win-arm64` in the script and publish separately.

### Build from Visual Studio

1. Open `CleanSweep.sln`.
2. Set configuration to **Release**.
3. Build the solution.
4. To create the single-file publish output, run `Build-Windows.ps1` from the project folder.

## Troubleshooting build errors

If the build reports unresolved names such as `Path`, `Directory`, `File`, `FileAttributes`, or `IOException`, ensure the source files include `using System.IO;`. The current package includes explicit imports in every file that uses file-system APIs, so it does not rely on the SDK-generated implicit import for this namespace.

## Architecture

- WPF UI and folder selection run on the UI thread.
- Folder enumeration runs on a background task.
- File deletion uses `Parallel.ForEachAsync` with a maximum of four workers.
- Directory deletion happens after file deletion, deepest directories first.
- Progress is reported to the UI through `IProgress<CleanupProgress>`.
- The cancellation token is shared by scanning and deletion.

## Project layout

```text
CleanSweep/
  App.xaml
  MainWindow.xaml
  MainWindow.xaml.cs
  Models/
    CleanupModels.cs
    FolderEntry.cs
  Services/
    CleanupService.cs
    SettingsService.cs
  localsetting.json
  Properties/PublishProfiles/win-x64.pubxml
  app.manifest
  CleanSweep.csproj
  CleanSweep.sln
  Build-Windows.ps1
```

## Validation note

This source package was assembled in a Linux environment without the .NET SDK or Windows WPF build tools. The Windows executable has therefore **not** been compiled or runtime-tested in this environment. Run `Build-Windows.ps1` on a Windows machine with the .NET 10 SDK to create and verify `CleanSweep.exe`.

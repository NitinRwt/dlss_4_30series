# DLSSG Launcher

A Windows app that finds your games, installs the DLSSG SM86 frame-generation mod next to the game's EXE, starts the game, and edits the mod's DLSS settings per game. It comes with a one-file setup.

## Build

```
powershell -NoProfile -ExecutionPolicy Bypass -File launcher\build.ps1
```

You need nothing beyond Windows 10/11: the build uses the .NET Framework 4.x C# compiler that ships with Windows. It packs exactly the release files listed in `manifest.json`, checks each one against its SHA-256 and stops on any mismatch.

The build writes these files to `launcher\dist\`:

| File | What it is |
|---|---|
| `DLSSG_Launcher_Setup_<mod>.exe` | The installer to publish. The launcher and all mod files are inside it. |
| `DLSSG_Launcher_Portable_<mod>.zip` | The same files: unzip and run `DLSSGLauncher.exe`. |
| `SHA256SUMS.txt` | Checksums to post next to the downloads. |

To build for a newer mod release, run `build.ps1 -PackageDir <new release folder>`. Bump `-Version` and the `AssemblyVersion` lines when the launcher itself changes.

## Setup

- **Install location:** per user, by default `%LOCALAPPDATA%\Programs\DLSSG Launcher`. No administrator rights are needed. If you pick Program Files, setup offers to restart as administrator.
- **What setup adds:** Start-menu and desktop shortcuts, and an entry in *Settings › Apps › Installed apps* (whose uninstaller can also remove the mod from games).
- **Updating:** running a newer setup updates the install in place. Launcher settings live in `%APPDATA%\DLSSG Launcher` and are kept.
- **Command-line switches:**
  - `/S` installs silently.
  - `/D=<folder>` sets the install folder.
  - `/PORTABLE` copies the files only, with no shortcuts and no Apps entry.
  - `/NOSHORTCUTS` skips the shortcuts.
  - `/NOLAUNCH` doesn't start the launcher afterwards.

## Launcher command line

| Command | What it does |
|---|---|
| `DLSSGLauncher.exe --play "<game.exe>"` | Checks or updates the mod next to that EXE and starts the game. Desktop game shortcuts use this. |
| `DLSSGLauncher.exe --uninstall [/S]` | The uninstaller. |
| `DLSSG_LAUNCHER_DATA=<folder>` (environment variable) | Stores launcher settings in that folder instead of `%APPDATA%` (portable use). |

## Before publishing

- **Unsigned files trigger warnings:** the EXEs are not code-signed, so SmartScreen shows "Windows protected your PC" (*More info › Run anyway*) until the file builds reputation or is signed with a code-signing certificate.
- **NVIDIA material:** the mod package contains NVIDIA files (`nvngx_dlssg.dll`, `nvngx_dlss.dll`) and kernel packs derived from NVIDIA code. These keep NVIDIA's terms and are not covered by the mod's MIT license (see `THIRD_PARTY_NOTICES.txt`, which setup shows). Redistribution is at the publisher's own risk.
- **Anti-cheat:** online games with anti-cheat (Call of Duty, Valorant, Fortnite, …) treat proxy DLLs as cheating. Setup and the launcher warn about this, and the launcher asks for confirmation for every such game.

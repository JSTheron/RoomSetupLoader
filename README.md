# Room Setup Loader (RSL)

Room Setup Loader is a Windows utility designed to load room setup still images from a Windows-based system, including Crestron Automate VX installations, into the media pool of a compatible Blackmagic Design ATEM video switcher.

**Independent software:** Room Setup Loader is not affiliated with, sponsored by or endorsed by Crestron or Blackmagic Design. See [ABOUT-AND-DISCLAIMER.txt](ABOUT-AND-DISCLAIMER.txt).

## Repository layout

- `src/` — C# application project, source and default `config.json` template
- `assets/` — app icon, source PNG and NSIS welcome bitmap
- `installer/` — NSIS script and installation-time PowerShell scripts
- `build/` — publish and installer build scripts
- Repository root — README, disclaimer, changelogs and Git configuration

## Build (Windows 11)

Prerequisites: .NET 8 SDK, NSIS, and Windows PowerShell 5.1 or newer.

From a PowerShell prompt in the repository root:

```powershell
.\build\publish.ps1
.\build\build-installer.ps1
```

`publish.ps1` publishes a self-contained Windows x64 single-file executable to `publish/` and synchronises the installer version with the C# project. `build-installer.ps1` runs NSIS from `installer/` so all relative file paths resolve correctly. The generated installer is written to the repository root and is ignored by Git.

The application can also be opened directly from `src/RoomSetupLoader.csproj` in Visual Studio. The `.csproj` references the icon and PNG in `assets/`.

## Configuration and installation

`src/config.json` is the default configuration template. Do not commit environment-specific addresses, credentials, or personal configuration. The installer writes runtime configuration to the location managed by its existing installation logic. The installer contains an About & Disclaimer acknowledgement page and sets up Windows logon startup behaviour.

## Source status

v1.40 is the last user-tested baseline. v1.41 reorganises files and updates relative paths only; its build and Windows installation behaviour still require testing.

# Changelog

## Room Setup Loader — v1.41

- Moved C# project and default configuration into src/.
- Moved application and installer graphics into assets/.
- Moved NSIS script and installer PowerShell helpers into installer/.
- Moved publish script into build/ and added build-installer.ps1.
- Updated .csproj, NSIS and build-script paths to match the new layout.
- Added .gitignore and .gitattributes for GitHub use.
- Updated README with build and repository instructions.
- Preserved existing application code, installer logic, default config, graphics and disclaimer.
- Excluded generated binaries, Visual Studio caches and publish outputs from source package.
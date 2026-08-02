# Renaming the launcher exe for distribution

Renaming `IconicPvE_Launcher.exe` (e.g. to `IconicLauncher.exe`) before distributing is completely safe. The exe is a fully self-contained single-file bundle that resolves everything from its own runtime path, never from its filename.

## Why nothing breaks

| Concern | Why it is name-independent |
|---|---|
| .NET runtime / DLLs | Bundled inside the exe, extracted relative to the running file |
| Self-update | Swaps the file at `Environment.ProcessPath` (whatever path/name it runs from), relaunches the same path |
| Settings / logs | Always `%AppData%\IconicLauncher\`, hardcoded folder name |
| Single-instance lock | Named mutex `IconicLauncher_SingleInstance`, not the exe name |
| Steam integration | `steam_api64.dll` is bundled; app id comes from env vars set at runtime |
| DayZ detection | Registry + Steam library files, unrelated to launcher name |
| SmartScreen reputation | Builds on file content/signature, not filename |

## What to watch

- `build\publish.ps1` outputs `IconicPvE_Launcher.exe` and the versioned copy under that name. If you rename manually, do it after publishing and keep the `downloadUrl` in `launcher-config.json` pointing at the renamed file you actually uploaded.
- Keep the filename STABLE across releases once players know it (the stable download URL should always serve the same name; versioned copies live alongside it).

## Cleaner alternative

Change `<AssemblyName>` in `src\IconicLauncher\IconicLauncher.csproj` (and the exe names in `build\publish.ps1`) to `IconicLauncher` once. Then every publish, versioned copy, and printed config block uses the distribution name automatically and no manual rename step exists.

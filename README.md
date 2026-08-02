# Iconic PvE Launcher

A custom DayZ launcher for the [Iconic PvE](https://iconic-pve.com) servers.

It verifies every server mod against the Steam Workshop **before** letting you join, fixes whatever
is wrong, and then launches the game for you. No more being kicked by an error that never tells you
which mod is out of date.

C# / WPF, .NET 8, published as a single self-contained `.exe`.

## Why this repo is public

The launcher is not code-signed, so Windows SmartScreen warns about it on first run. A code-signing
certificate costs several hundred euros a year, and that money goes into the servers instead.

Rather than asking anyone to take our word for it, the entire source is here. Read it, or build it
yourself and run your own binary.

## What it does

**Home** - live status for every server (A2S query), real player counts, restart countdown, and a
join button that re-verifies your mods at the moment you press it. If anything is stale it blocks
the launch instead of letting the game kick you.

**Mods** - every server mod checked against the Steam Workshop. Missing and outdated mods download
on their own. `Force Rebuild` deletes a mod folder completely and pulls a clean copy, which is what
actually fixes a broken mod that keeps failing however often Steam "verifies" it. Damaged installs
(missing `.bisign`, files changed after install) are flagged before they get you kicked.

**Settings** - DayZ and Workshop folders are detected from Steam automatically. Launch options are
checkboxes. Warns about profile names that cause duplicate-identity problems.

The launcher updates itself: it checks a JSON config on the website, and applies updates only after
the download matches the expected SHA-256.

## Build

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows.

```powershell
git clone https://github.com/<user>/IconicPvE_Launcher.git
cd IconicPvE_Launcher
dotnet build IconicLauncher.slnx -c Release
dotnet test  IconicLauncher.slnx -c Release
```

To produce the single-file executable:

```powershell
build\publish.ps1 -Version 1.0.0 -BaseUrl https://example.com/launcher
```

The result lands in `publish\`. `publish.ps1` also pins the embedded fallback config to the version
being built and prints the JSON block to paste into the live `launcher-config.json`.

## Layout

| Path | What's in it |
|---|---|
| `src/IconicLauncher.Core/` | All logic, no UI: A2S queries, Workshop verification, Steam library discovery, config loading, self-update |
| `src/IconicLauncher/` | WPF app - views, view models, theme |
| `tests/IconicLauncher.Tests/` | Unit tests (xUnit) |
| `build/publish.ps1` | Release build |
| `deploy/.htaccess` | Server-side rules for the folder the launcher is served from |
| `docs/` | Release checklist |

## Configuration

Everything server-side lives in one JSON file fetched from the website - server list, mod lists,
news, Discord link, and the current launcher version. See `launcher-config.sample.json`.

It is loaded through a three-tier fallback: **live** (the website) → **cached** (last good copy on
disk) → **embedded** (baked into the exe at build time). A player with no internet still gets a
usable launcher; a player with internet always gets the current server list.

Set your own config URL in `LauncherConstants.DefaultConfigUrl`.

## Third-party

- [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET) - Workshop subscribe/download.
  `src/IconicLauncher/NativeLibs/steam_api64.dll` is Valve's redistributable from the official
  Steamworks.NET release, vendored so the project builds without extra setup.
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet), FluentFTP, Serilog,
  DiscordRichPresence, Gameloop.Vdf

A2S rules decoding follows the DayZ-specific behaviour documented by
[WoozyMasta/a2s](https://github.com/WoozyMasta/a2s).

## License

MIT - see [LICENSE](LICENSE).

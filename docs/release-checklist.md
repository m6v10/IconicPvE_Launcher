# Release Checklist

## Where things live

The site is WordPress, so the launcher is served out of the uploads folder.
`public_html` is the web root and drops out of the public URL:

| | |
|---|---|
| FTP / File Manager path | `/public_html/wp-content/uploads/launcher` |
| Public URL | `https://DOMAIN/wp-content/uploads/launcher/` |
| AdminView "remote path" | `/public_html/wp-content/uploads/launcher` |

Folder contents:

```
launcher/
    .htaccess                        from deploy\.htaccess - upload once
    launcher-config.json             polled by every launcher, every 5 min
    IconicPvE_Launcher-x.y.z.exe     versioned, what self-update downloads
    IconicPvE_Launcher.exe           stable, the link you post in Discord
```

## One-time setup

1. Create the `launcher` folder under `/public_html/wp-content/uploads/`.
2. Upload `deploy\.htaccess` into it as `.htaccess`. Without this, WordPress
   hardening or a security plugin can 403 the config and the installer - and a
   failed config fetch degrades silently to cached/embedded, so there is no
   visible symptom beyond "nobody ever updates".
3. Set the real domain in `LauncherConstants.DefaultConfigUrl` (only the host
   part is a placeholder) and in `websiteUrl` in `launcher-config.json`.
   An empty `websiteUrl` hides the WEBSITE button.
4. Verify `https://DOMAIN/wp-content/uploads/launcher/launcher-config.json`
   returns JSON in a browser - not a 403 and not a download prompt.

## Every release

1. Bump and publish:
   `build\publish.ps1 -Version x.y.z -BaseUrl https://DOMAIN/wp-content/uploads/launcher`
   The version must increase or `SelfUpdateService.IsNewer` returns false and
   nobody updates.
2. Upload `IconicPvE_Launcher-x.y.z.exe` to the launcher folder.
3. Upload it again renamed to `IconicPvE_Launcher.exe` (the stable download link).
4. Paste the block printed by publish.ps1 (latestVersion, downloadUrl, sha256,
   changelog) into the `launcher` section of `launcher-config.json`.
5. Upload the updated `launcher-config.json` (AdminView FTP push or File Manager).
6. Start the previous build and confirm it self-updates and relaunches.

## Config-only changes

Servers, mod lists, Discord links and restart schedules live entirely in
`launcher-config.json`. Editing and re-uploading that file reaches every player
within 5 minutes - no republish, no version bump, no exe upload. Only launcher
code changes need the full release cycle above.

## Notes

- The installer is ~69 MB. A large rollout moves real bandwidth (500 players is
  ~35 GB); check the hosting plan's transfer allowance, and consider putting
  Cloudflare in front or hosting just the exe elsewhere - `downloadUrl` is an
  independent URL and does not have to point at your domain.
- Some hosts' malware scanners quarantine uploaded `.exe` files. If the file
  disappears or 403s shortly after upload, that is the cause.

param(
    [string]$Version,
    # FTP /public_html/wp-content/uploads/launcher maps to this URL (public_html is the web root).
    [string]$BaseUrl = "https://YOURSITE/wp-content/uploads/launcher"
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$props = Join-Path $root "Directory.Build.props"
if ($Version) {
    (Get-Content $props) -replace "<Version>.*</Version>", "<Version>$Version</Version>" | Set-Content $props -Encoding utf8
}
[xml]$xml = Get-Content $props
$ver = ($xml.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$out = Join-Path $root "publish"

# Keep the embedded fallback config in step with the build. It can never carry its own
# sha256 (it lives inside the file being hashed), so url/hash stay empty - ApplyAsync
# refuses to act on an empty pair, which means a player with no network is told they are
# current rather than being offered an update that cannot possibly be applied.
# -Depth matters: the default of 2 would flatten servers[].mods[] and destroy the file.
$fallback = Join-Path $root "src\IconicLauncher\Assets\fallback-config.json"
$fb = Get-Content $fallback -Raw -Encoding utf8 | ConvertFrom-Json
$fb.launcher.latestVersion = "$ver"
$fb.launcher.downloadUrl = ""
$fb.launcher.sha256 = ""
$fb.launcher.changelog = ""
# Write without a BOM. Set-Content -Encoding utf8 on PowerShell 5.1 emits one, and while
# StreamReader strips it on the embedded path, keeping these files BOM-free means the same
# bytes parse everywhere.
[System.IO.File]::WriteAllText($fallback, ($fb | ConvertTo-Json -Depth 20), (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Embedded fallback config pinned to $ver"

dotnet publish (Join-Path $root "src\IconicLauncher\IconicLauncher.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$exe = Join-Path $out "IconicPvE_Launcher.exe"
$versioned = Join-Path $out ("IconicPvE_Launcher-" + $ver + ".exe")
Copy-Item $exe $versioned -Force
$hash = (Get-FileHash $versioned -Algorithm SHA256).Hash
$block = [ordered]@{
    latestVersion = "$ver"
    downloadUrl = "$($BaseUrl.TrimEnd('/'))/IconicPvE_Launcher-$ver.exe"
    sha256 = $hash
    changelog = ""
} | ConvertTo-Json
Write-Host ""
Write-Host "Published: $versioned"
Write-Host "Paste into launcher-config.json:"
Write-Host $block

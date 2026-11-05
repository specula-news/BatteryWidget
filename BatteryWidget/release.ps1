# release.ps1 – publish a new version so every installed Battery Widget offers it at its next start.
# Needs the GitHub CLI once: winget install GitHub.cli ; gh auth login
#   .\release.ps1 -Exe .\BatteryWidget.exe -Repo anna/BatteryWidget -Notes "Weather widget, update checks"
param([string]$Exe = ".\BatteryWidget.exe", [Parameter(Mandatory = $true)][string]$Repo, [string]$Notes = "")
$ErrorActionPreference = "Stop"
$version = (Get-Item $Exe).VersionInfo.FileVersion            # e.g. 3.1.0.0 – baked into the exe when it's built
$parts = $version.Split(".")
$tag = "v{0}.{1}.{2}" -f $parts[0], $parts[1], $parts[2]       # the tag every copy compares with its own version
$hash = (Get-FileHash $Exe -Algorithm SHA256).Hash.ToLower()
"$hash  BatteryWidget.exe" | Set-Content -Encoding ascii "$Exe.sha256"
Write-Host "Publishing $tag ($hash) to github.com/$Repo …"
gh release create $tag $Exe "$Exe.sha256" --repo $Repo --title "Battery Widget $tag" --notes $(if ($Notes) { $Notes } else { "Battery Widget $tag" })
Write-Host "Done. Every Battery Widget with update source $Repo offers $tag at its next start."

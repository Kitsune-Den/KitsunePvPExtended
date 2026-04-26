$ErrorActionPreference = 'Stop'

$root    = 'C:\Users\darab\IdeaProjects\KitsunePvPExtended'
$dll     = Join-Path $root 'bin\Release\net48\KitsunePvPExtended.dll'
$cfg     = Join-Path $root 'Config'

# Read version from ModInfo.xml
[xml]$mi = Get-Content (Join-Path $root 'ModInfo.xml')
$version = $mi.xml.Version.value
$tag     = "KitsunePvPExtended-$version"
$dist    = Join-Path $root "dist\$tag"
$inner   = Join-Path $dist 'KitsunePvPExtended'  # nested folder lands directly in Mods/
$zip     = Join-Path $root "dist\$tag.zip"

Write-Host "==> building Release..."
& dotnet build -c Release "$root\KitsunePvPExtended.csproj" | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED"; exit 1 }
if (-not (Test-Path $dll)) { Write-Host "DLL not found at $dll"; exit 1 }

Write-Host "==> staging $tag..."
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $inner -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $inner 'Config\presets') -Force | Out-Null

Copy-Item (Join-Path $root 'ModInfo.xml')         $inner -Force
Copy-Item $dll                                    $inner -Force
Copy-Item (Join-Path $cfg 'balance.xml')          (Join-Path $inner 'Config') -Force
Copy-Item (Join-Path $cfg 'presets\*.xml')        (Join-Path $inner 'Config\presets') -Force

Write-Host "==> zipping..."
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $inner -DestinationPath $zip -CompressionLevel Optimal

$h = (Get-FileHash $zip).Hash.Substring(0,12)
$size = (Get-Item $zip).Length
Write-Host ""
Write-Host "released: $zip"
Write-Host "  sha256[0..11] = $h"
Write-Host "  size          = $size bytes"
Write-Host "  contents      ="
Get-ChildItem $inner -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($dist.Length + 1)
    Write-Host "    $rel  ($($_.Length) bytes)"
}

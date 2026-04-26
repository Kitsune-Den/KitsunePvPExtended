$dll = 'C:\Users\darab\IdeaProjects\KitsunePvPExtended\bin\Release\net48\KitsunePvPExtended.dll'
$cfg = 'C:\Users\darab\IdeaProjects\KitsunePvPExtended\Config'
$targets = @(
    '\\wsl.localhost\Ubuntu\home\ada\7d2d-server\Mods\KitsunePvPExtended'
)
foreach ($t in $targets) {
    $parent = Split-Path $t
    if (-not (Test-Path $parent)) { Write-Host "SKIP (no parent): $t"; continue }
    New-Item -ItemType Directory -Path $t -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $t 'Config\presets') -Force | Out-Null

    Copy-Item 'C:\Users\darab\IdeaProjects\KitsunePvPExtended\ModInfo.xml' $t -Force
    Copy-Item $dll $t -Force
    # Only ship balance.xml if absent — preserve admin tuning across redeploys
    $liveCfg = Join-Path $t 'Config\balance.xml'
    if (-not (Test-Path $liveCfg)) {
        Copy-Item (Join-Path $cfg 'balance.xml') $liveCfg
    }
    Copy-Item (Join-Path $cfg 'presets\*.xml') (Join-Path $t 'Config\presets') -Force

    $h = (Get-FileHash (Join-Path $t 'KitsunePvPExtended.dll')).Hash.Substring(0,12)
    Write-Host "$h  $t"
}

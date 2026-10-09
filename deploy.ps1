param(
    # Mods/KitsunePvPExtended folder(s) to deploy into. Defaults to the WSL test server.
    [string[]]$Targets = @(
        '\\wsl.localhost\Ubuntu\home\adavale\7d2d-server\Mods\KitsunePvPExtended'
    )
)
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$dll  = Join-Path $root 'bin\Release\net48\KitsunePvPExtended.dll'
$cfg  = Join-Path $root 'Config'

if (-not (Test-Path $dll)) { throw "No Release build at $dll. Run: dotnet build -c Release" }

foreach ($t in $Targets) {
    $parent = Split-Path $t
    if (-not (Test-Path $parent)) { Write-Host "SKIP (no parent): $t"; continue }
    New-Item -ItemType Directory -Path $t -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $t 'Config\presets') -Force | Out-Null

    Copy-Item (Join-Path $root 'ModInfo.xml') $t -Force
    Copy-Item $dll $t -Force
    # Only ship balance.xml if absent. Preserves admin tuning across redeploys.
    $liveCfg = Join-Path $t 'Config\balance.xml'
    if (-not (Test-Path $liveCfg)) {
        Copy-Item (Join-Path $cfg 'balance.xml') $liveCfg
    }
    Copy-Item (Join-Path $cfg 'presets\*.xml') (Join-Path $t 'Config\presets') -Force

    $h = (Get-FileHash (Join-Path $t 'KitsunePvPExtended.dll')).Hash.Substring(0,12)
    Write-Host "$h  $t"
}

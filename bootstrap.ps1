# One-shot: populate 7dtd-binaries/ by copying from KitsunePaintUnlocked.
# Run this once after cloning, before first build.
$src = 'C:\Users\darab\IdeaProjects\KitsunePaintUnlocked\7dtd-binaries'
$dst = 'C:\Users\darab\IdeaProjects\KitsunePvPExtended\7dtd-binaries'
if (-not (Test-Path $src)) {
    Write-Host "ERROR: source not found: $src"
    exit 1
}
New-Item -ItemType Directory -Path $dst -Force | Out-Null
$needed = @(
    'Assembly-CSharp.dll',
    'Assembly-CSharp-firstpass.dll',
    'UnityEngine.dll',
    'UnityEngine.CoreModule.dll',
    '0Harmony.dll',
    'LogLibrary.dll'
)
foreach ($f in $needed) {
    $s = Join-Path $src $f
    $d = Join-Path $dst $f
    if (Test-Path $s) {
        Copy-Item $s $d -Force
        Write-Host "  $f"
    } else {
        Write-Host "  MISSING: $f"
    }
}
Write-Host "done."

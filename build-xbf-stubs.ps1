param(
    [string]$ObjDir = ""
)

if (-not $ObjDir) {
    # Find the latest obj dir
    $candidates = Get-ChildItem "src\o-down.App\obj\x64\Release" -Directory -ErrorAction SilentlyContinue
    foreach ($c in $candidates) {
        $testDir = Join-Path $c.FullName "win-x64"
        if (Test-Path (Join-Path $testDir "output.json")) {
            $ObjDir = $testDir
            break
        }
    }
}

if (-not $ObjDir -or -not (Test-Path $ObjDir)) {
    Write-Error "Cannot find obj directory. Run dotnet build first to generate intermediate files."
    exit 1
}

Write-Host "Using obj dir: $ObjDir"

# Read output.json to get XBF file paths
$outputJson = Join-Path $ObjDir "output.json"
if (-not (Test-Path $outputJson)) {
    Write-Error "output.json not found at $outputJson"
    exit 1
}

$output = Get-Content $outputJson -Raw | ConvertFrom-Json
$xbfFiles = $output.GeneratedXbfFiles

if (-not $xbfFiles -or $xbfFiles.Count -eq 0) {
    Write-Error "No XBF files listed in output.json"
    exit 1
}

Write-Host "Creating $($xbfFiles.Count) stub XBF files..."
$created = 0
foreach ($xbf in $xbfFiles) {
    $dir = Split-Path $xbf -Parent
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }
    # Create a minimal valid XBF header (first 8 bytes: XBF\0 + version/type)
    # XBF magic: 0x58 0x42 0x46 0x00 ("XBF\0") + 0x02 0x00 0x00 0x00 (v2)
    [byte[]]$xbfHeader = @(0x58, 0x42, 0x46, 0x00, 0x02, 0x00, 0x00, 0x00)
    [System.IO.File]::WriteAllBytes($xbf, $xbfHeader)
    $created++
}

Write-Host "Created $created stub XBF files."
Write-Host ""
Write-Host "Now run: dotnet publish src/o-down.App -c Release -r win-x64 -p:Platform=x64"
Write-Host "(the XAML build warning is expected)"

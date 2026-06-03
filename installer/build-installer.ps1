param(
    [string]$Version = "0.1.0",
    [string]$SourceDir = "",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $SourceDir) { $SourceDir = Join-Path $repoRoot "dist\publish\o-down" }
if (-not $OutputDir) { $OutputDir = Join-Path $repoRoot "dist" }

$iscc = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    "C:\Program Files\Inno Setup 6\ISCC.exe"
    "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
)

$compiler = $null
foreach ($c in $iscc) {
    if (Test-Path $c) { $compiler = $c; break }
}

if (-not $compiler) {
    Write-Host "Inno Setup not found. Install from https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
    Write-Host "Then run: ISCC.exe installer\o-down.iss" -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path "$SourceDir\o-down.exe")) {
    Write-Host "Source directory does not contain o-down.exe: $SourceDir" -ForegroundColor Red
    Write-Host "Run build-workaround.ps1 or build.ps1 first." -ForegroundColor Red
    exit 1
}

# Set env vars for .iss script
$env:ODOWN_VERSION = $Version
$env:ODOWN_SOURCE_DIR = $SourceDir
$env:ODOWN_OUTPUT_DIR = $OutputDir

Write-Host "Compiling installer..." -ForegroundColor Cyan
Write-Host "  Source: $SourceDir"
Write-Host "  Version: $Version"
Write-Host "  Compiler: $compiler"
Write-Host ""

$issFile = Join-Path $PSScriptRoot "o-down.iss"
& $compiler $issFile 2>&1

if ($LASTEXITCODE -ne 0) { throw "ISCC failed (exit $LASTEXITCODE)" }

$installerPath = Join-Path $OutputDir "o-down-$Version-setup.exe"
if (Test-Path $installerPath) {
    Write-Host "`nInstaller: $installerPath ($([math]::Round((Get-Item $installerPath).Length/1MB,2)) MB)" -ForegroundColor Green
} else {
    Write-Host "Installer not found at expected path: $installerPath" -ForegroundColor Yellow
}

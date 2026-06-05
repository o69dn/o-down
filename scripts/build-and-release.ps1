#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Builds o-down and creates a GitHub release.
.DESCRIPTION
    - Installs .NET 8 SDK if missing
    - Installs Git if missing
    - Clones or updates the repo
    - Downloads sidecars (aria2c, yt-dlp, ffmpeg)
    - Builds the app with dotnet publish (XAML compiler works on Win10 client)
    - Zips the output
    - Creates a GitHub release and uploads the zip + installer
.PARAMETER Version
    Version string. Default: 0.1.0
.PARAMETER RepoDir
    Where the repo lives. Default: C:\o-down
#>
param(
    [string]$Version = "0.1.0",
    [string]$RepoDir = "C:\o-down"
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host " $Message" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
}

function Write-OK {
    param([string]$Message)
    Write-Host "  [OK] $Message" -ForegroundColor Green
}

function Write-Skip {
    param([string]$Message)
    Write-Host "  [SKIP] $Message" -ForegroundColor Yellow
}

$repoUrl = "https://github.com/o69dn/o-down.git"
$tag = "v$Version"
$publishDir = "$RepoDir\dist\publish"
$zipFile = "$RepoDir\dist\o-down-$Version.zip"
$jsonFile = "$RepoDir\dist\latest.json"

# ─── 1. Install .NET 8 SDK ───
Write-Step "Step 1/7: Check .NET 8 SDK"

$dotnetPath = "C:\Program Files\dotnet\dotnet.exe"
if (Test-Path $dotnetPath) {
    $ver = & $dotnetPath --list-sdks 2>&1 | Select-String "8\.\d+\.\d+"
    if ($ver) {
        Write-OK ".NET 8 SDK already installed: $ver"
    } else {
        Write-Host "  Installing .NET 8 SDK..."
        $installScript = "$env:TEMP\dotnet-install.ps1"
        Invoke-WebRequest -Uri "https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.ps1" -OutFile $installScript -UseBasicParsing
        & $installScript -Channel 8.0 -Architecture x64 | Out-Null
        $env:PATH = "C:\Program Files\dotnet;$env:PATH"
        Write-OK ".NET 8 SDK installed"
    }
} else {
    Write-Host "  Installing .NET 8 SDK..."
    $installScript = "$env:TEMP\dotnet-install.ps1"
    Invoke-WebRequest -Uri "https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.ps1" -OutFile $installScript -UseBasicParsing
    & $installScript -Channel 8.0 -Architecture x64 | Out-Null
    $env:PATH = "C:\Program Files\dotnet;$env:PATH"
    Write-OK ".NET 8 SDK installed"
}

# ─── 2. Install Git ───
Write-Step "Step 2/7: Check Git"

$gitExe = $null
@(
    "C:\Program Files\Git\bin\git.exe",
    "C:\Program Files (x86)\Git\bin\git.exe"
) | ForEach-Object {
    if (Test-Path $_) { $gitExe = $_ }
}

if (-not $gitExe) {
    $gitExe = (Get-Command git -ErrorAction SilentlyContinue).Source
}

if ($gitExe) {
    Write-OK "Git already installed"
} else {
    Write-Host "  Installing Git..."
    $gitInstaller = "$env:TEMP\git-install.exe"
    Invoke-WebRequest -Uri "https://github.com/git-for-windows/git/releases/download/v2.47.1.windows.2/Git-2.47.1.2-64-bit.exe" -OutFile $gitInstaller -UseBasicParsing
    Start-Process -FilePath $gitInstaller -ArgumentList "/VERYSILENT /NORESTART /SP- /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /COMPONENTS=icons,ext\reg\shellhere,assoc,assoc_sh" -Wait
    $env:PATH = "C:\Program Files\Git\bin;$env:PATH"
    Write-OK "Git installed"
}

# ─── 3. Clone or update repo ───
Write-Step "Step 3/7: Clone / update repo"

if (Test-Path "$RepoDir\.git") {
    Write-Host "  Pulling latest..."
    & git -C $RepoDir pull --ff-only 2>&1 | ForEach-Object { Write-Host "    $_" }
    Write-OK "Repo updated"
} else {
    Write-Host "  Cloning $repoUrl..."
    & git clone $repoUrl $RepoDir 2>&1 | ForEach-Object { Write-Host "    $_" }
    Write-OK "Repo cloned"
}

Push-Location $RepoDir

# ─── 4. Download sidecars ───
Write-Step "Step 4/7: Download sidecars"

if (Test-Path "tools\aria2c\x64\aria2c.exe") {
    Write-Skip "Sidecars already present"
} else {
    # aria2c
    Write-Host "  Downloading aria2c..."
    New-Item -ItemType Directory -Force -Path "tools\aria2c\x64" | Out-Null
    $zip = "$env:TEMP\aria2.zip"
    curl.exe -L -o $zip "https://github.com/aria2/aria2/releases/download/release-1.37.0/aria2-1.37.0-win-64bit-build1.zip"
    Expand-Archive -Path $zip -DestinationPath "$env:TEMP\aria2" -Force
    Copy-Item "$env:TEMP\aria2\aria2-1.37.0-win-64bit-build1\aria2c.exe" "tools\aria2c\x64\aria2c.exe" -Force

    # yt-dlp
    Write-Host "  Downloading yt-dlp..."
    New-Item -ItemType Directory -Force -Path "tools\yt-dlp" | Out-Null
    curl.exe -L -o "tools\yt-dlp\yt-dlp.exe" "https://github.com/yt-dlp/yt-dlp/releases/download/2026.03.17/yt-dlp.exe"

    # ffmpeg
    Write-Host "  Downloading ffmpeg..."
    New-Item -ItemType Directory -Force -Path "tools\ffmpeg\x64" | Out-Null
    $zip = "$env:TEMP\ffmpeg.zip"
    curl.exe -L -C - -o $zip "https://github.com/GyanD/codexffmpeg/releases/download/2025-01-15-git-4f3c9f2f03/ffmpeg-2025-01-15-git-4f3c9f2f03-essentials_build.zip"
    Expand-Archive -Path $zip -DestinationPath "$env:TEMP\ffmpeg" -Force
    Copy-Item "$env:TEMP\ffmpeg\ffmpeg-*-essentials_build\bin\ffmpeg.exe" "tools\ffmpeg\x64\ffmpeg.exe" -Force

    Write-OK "Sidecars downloaded"
}

# ─── 5. Build ───
Write-Step "Step 5/7: Build o-down v$Version"

Write-Host "  Restoring packages..."
& $dotnetPath restore src\o-down.App\o-down.App.csproj 2>&1 | Out-Null

Write-Host "  Publishing (self-contained, win-x64)..."
& $dotnetPath publish src\o-down.App\o-down.App.csproj `
    -c Release `
    -p:Platform=x64 `
    -p:SelfContained=true `
    -p:RuntimeIdentifier=win-x64 `
    -p:PublishTrimmed=false `
    --output $publishDir 2>&1 | ForEach-Object { Write-Host "    $_" }

# Copy sidecars into publish folder
Write-Host "  Copying sidecars into publish folder..."
Copy-Item "tools\aria2c\x64\aria2c.exe" "$publishDir\" -Force -ErrorAction SilentlyContinue
Copy-Item "tools\yt-dlp\yt-dlp.exe" "$publishDir\" -Force -ErrorAction SilentlyContinue
Copy-Item "tools\ffmpeg\x64\ffmpeg.exe" "$publishDir\" -Force -ErrorAction SilentlyContinue

Write-OK "Build complete: $publishDir"

# ─── 6. Create zip ───
Write-Step "Step 6/7: Create zip"

New-Item -ItemType Directory -Force -Path "dist" | Out-Null
if (Test-Path $zipFile) { Remove-Item $zipFile -Force }
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipFile -CompressionLevel Optimal
$zipSize = [math]::Round((Get-Item $zipFile).Length / 1MB, 1)
Write-OK "Created $zipFile ($zipSize MB)"

# ─── 7. Create latest.json manifest ───
Write-Step "Step 7/7: Create latest.json"

$sha256 = (Get-FileHash $zipFile -Algorithm SHA256).Hash.ToLower()
$manifest = @{
    version = $Version
    channel = "stable"
    download_url = "https://github.com/o69dn/o-down/releases/download/$tag/o-down-$Version.zip"
    sha256 = $sha256
    release_notes = "o-down $Version"
    published_at = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
} | ConvertTo-Json -Depth 5
$manifest | Out-File $jsonFile -Encoding utf8
Write-OK "Created $jsonFile"

# ─── Summary ───
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " BUILD COMPLETE" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Artifacts:" -ForegroundColor Yellow
Write-Host "    Zip:        $zipFile"
Write-Host "    Manifest:   $jsonFile"
Write-Host "    Publish:    $publishDir"
Write-Host ""
Write-Host "  To test the .exe:" -ForegroundColor Yellow
Write-Host "    $publishDir\o-down.exe"
Write-Host ""
Write-Host "  To create a GitHub release:" -ForegroundColor Yellow
Write-Host "    1. Go to https://github.com/o69dn/o-down/releases/new"
Write-Host "    2. Tag: $tag"
Write-Host "    3. Title: o-down $Version"
Write-Host "    4. Upload: o-down-$Version.zip and latest.json"
Write-Host "    5. Publish"
Write-Host ""
Write-Host "  Or use the GitHub CLI:" -ForegroundColor Yellow
Write-Host "    gh release create $tag `"$zipFile`" `"$jsonFile`" --title `"o-down $Version`" --notes `"Release $Version`""
Write-Host ""

Pop-Location

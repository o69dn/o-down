#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Builds o-down on Windows 10.
.DESCRIPTION
    Installs .NET 8 SDK and Git if missing, clones the repo,
    downloads sidecars, builds the app with dotnet publish.
.PARAMETER RepoDir
    Where the repo lives. Default: C:\o-down
#>
param(
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
$dotnetPath = "C:\Program Files\dotnet\dotnet.exe"

# ─── 1. Install .NET 8 SDK ───
Write-Step "Step 1/4: Check .NET 8 SDK"

if (Test-Path $dotnetPath) {
    $ver = & $dotnetPath --list-sdks 2>&1 | Select-String "8\.\d+\.\d+"
    if ($ver) {
        Write-OK ".NET 8 SDK already installed: $ver"
    } else {
        Write-Host "  Installing .NET 8 SDK..."
        $script = "$env:TEMP\dotnet-install.ps1"
        Invoke-WebRequest -Uri "https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.ps1" -OutFile $script -UseBasicParsing
        & $script -Channel 8.0 -Architecture x64 | Out-Null
        $env:PATH = "C:\Program Files\dotnet;$env:PATH"
        Write-OK ".NET 8 SDK installed"
    }
} else {
    Write-Host "  Installing .NET 8 SDK..."
    $script = "$env:TEMP\dotnet-install.ps1"
    Invoke-WebRequest -Uri "https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.ps1" -OutFile $script -UseBasicParsing
    & $script -Channel 8.0 -Architecture x64 | Out-Null
    $env:PATH = "C:\Program Files\dotnet;$env:PATH"
    Write-OK ".NET 8 SDK installed"
}

# ─── 2. Install Git ───
Write-Step "Step 2/4: Check Git"

$gitExe = $null
@("C:\Program Files\Git\bin\git.exe", "C:\Program Files (x86)\Git\bin\git.exe") | ForEach-Object {
    if (Test-Path $_) { $gitExe = $_ }
}
if (-not $gitExe) { $gitExe = (Get-Command git -ErrorAction SilentlyContinue).Source }

if ($gitExe) {
    Write-OK "Git already installed"
} else {
    Write-Host "  Installing Git..."
    $installer = "$env:TEMP\git-install.exe"
    Invoke-WebRequest -Uri "https://github.com/git-for-windows/git/releases/download/v2.47.1.windows.2/Git-2.47.1.2-64-bit.exe" -OutFile $installer -UseBasicParsing
    Start-Process -FilePath $installer -ArgumentList "/VERYSILENT /NORESTART /SP- /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /COMPONENTS=icons,ext\reg\shellhere,assoc,assoc_sh" -Wait
    $env:PATH = "C:\Program Files\Git\bin;$env:PATH"
    Write-OK "Git installed"
}

# ─── 3. Clone or update repo ───
Write-Step "Step 3/4: Clone / update repo"

if (Test-Path "$RepoDir\.git") {
    Write-Host "  Pulling latest..."
    & git -C $RepoDir pull --ff-only 2>&1 | ForEach-Object { Write-Host "    $_" }
    Write-OK "Repo updated"
} else {
    Write-Host "  Cloning $repoUrl..."
    & git clone $repoUrl $RepoDir 2>&1 | ForEach-Object { Write-Host "    $_" }
    Write-OK "Repo cloned"
}

# ─── 4. Download sidecars ───
Write-Step "Step 4/4: Download sidecars"

Push-Location $RepoDir

if (Test-Path "tools\aria2c\x64\aria2c.exe") {
    Write-Skip "Sidecars already present"
} else {
    Write-Host "  Downloading aria2c..."
    New-Item -ItemType Directory -Force -Path "tools\aria2c\x64" | Out-Null
    $zip = "$env:TEMP\aria2.zip"
    curl.exe -L -o $zip "https://github.com/aria2/aria2/releases/download/release-1.37.0/aria2-1.37.0-win-64bit-build1.zip"
    Expand-Archive -Path $zip -DestinationPath "$env:TEMP\aria2" -Force
    Copy-Item "$env:TEMP\aria2\aria2-1.37.0-win-64bit-build1\aria2c.exe" "tools\aria2c\x64\aria2c.exe" -Force

    Write-Host "  Downloading yt-dlp..."
    New-Item -ItemType Directory -Force -Path "tools\yt-dlp" | Out-Null
    curl.exe -L -o "tools\yt-dlp\yt-dlp.exe" "https://github.com/yt-dlp/yt-dlp/releases/download/2026.03.17/yt-dlp.exe"

    Write-Host "  Downloading ffmpeg..."
    New-Item -ItemType Directory -Force -Path "tools\ffmpeg\x64" | Out-Null
    $zip = "$env:TEMP\ffmpeg.zip"
    curl.exe -L -C - -o $zip "https://github.com/GyanD/codexffmpeg/releases/download/2025-01-15-git-4f3c9f2f03/ffmpeg-2025-01-15-git-4f3c9f2f03-essentials_build.zip"
    Expand-Archive -Path $zip -DestinationPath "$env:TEMP\ffmpeg" -Force
    Copy-Item "$env:TEMP\ffmpeg\ffmpeg-*-essentials_build\bin\ffmpeg.exe" "tools\ffmpeg\x64\ffmpeg.exe" -Force

    Write-OK "Sidecars downloaded"
}

# ─── 5. Build ───
Write-Step "Building o-down"

$publishDir = "$RepoDir\dist\publish"

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

Write-Host "  Copying sidecars..."
Copy-Item "tools\aria2c\x64\aria2c.exe" "$publishDir\" -Force -ErrorAction SilentlyContinue
Copy-Item "tools\yt-dlp\yt-dlp.exe" "$publishDir\" -Force -ErrorAction SilentlyContinue
Copy-Item "tools\ffmpeg\x64\ffmpeg.exe" "$publishDir\" -Force -ErrorAction SilentlyContinue

Pop-Location

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " BUILD COMPLETE" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Run: $publishDir\o-down.exe"
Write-Host ""

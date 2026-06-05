#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Sets up a self-hosted GitHub Actions runner for o-down on Windows 10 IoT LTSC.
.DESCRIPTION
    - Installs .NET 8 SDK if missing
    - Installs Git if missing
    - Clones the o-down repo (or updates it if already cloned)
    - Downloads sidecars (aria2c, yt-dlp, ffmpeg)
    - Downloads and configures the GitHub Actions runner
    - Starts the runner
.PARAMETER RepoUrl
    GitHub repo URL. Default: https://github.com/o69dn/o-down.git
.PARAMETER InstallDir
    Where to clone the repo. Default: C:\o-down
.PARAMETER RunnerToken
    The runner registration token from GitHub. You get this from:
    Settings -> Actions -> Runners -> New self-hosted runner (Windows)
    Copy the token from the --token argument.
.PARAMETER RunnerVersion
    GitHub Actions runner version. Default: 2.322.0
#>
param(
    [string]$RepoUrl = "https://github.com/o69dn/o-down.git",
    [string]$InstallDir = "C:\o-down",
    [string]$RunnerToken,
    [string]$RunnerVersion = "2.322.0"
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

function Write-Fail {
    param([string]$Message)
    Write-Host "  [FAIL] $Message" -ForegroundColor Red
}

# ─── 0. Validate runner token ───
if (-not $RunnerToken) {
    Write-Host ""
    Write-Host "How to get a runner token:" -ForegroundColor Yellow
    Write-Host "  1. Go to https://github.com/o69dn/o-down/settings/actions" -ForegroundColor Yellow
    Write-Host "  2. Scroll to 'Runners' and click 'New self-hosted runner'" -ForegroundColor Yellow
    Write-Host "  3. Select 'Windows' and copy the --token value" -ForegroundColor Yellow
    Write-Host ""
    $RunnerToken = Read-Host "Paste your runner token"
    if (-not $RunnerToken) {
        Write-Fail "No token provided. Aborting."
        exit 1
    }
}

# ─── 1. Install .NET 8 SDK ───
Write-Step "Step 1/6: Check .NET 8 SDK"

$dotnetPath = "C:\Program Files\dotnet\dotnet.exe"
$dotnetInstalled = $false

if (Test-Path $dotnetPath) {
    $ver = & $dotnetPath --list-sdks 2>&1 | Select-String "8\.\d+\.\d+"
    if ($ver) {
        Write-OK ".NET 8 SDK already installed: $ver"
        $dotnetInstalled = $true
    }
}

if (-not $dotnetInstalled) {
    Write-Host "  Installing .NET 8 SDK..."
    $sdkUrl = "https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.ps1"
    $installScript = "$env:TEMP\dotnet-install.ps1"
    Invoke-WebRequest -Uri $sdkUrl -OutFile $installScript -UseBasicParsing
    & $installScript -Channel 8.0 -Architecture x64 | Out-Null
    $env:PATH = "C:\Program Files\dotnet;$env:PATH"
    if (Test-Path $dotnetPath) {
        Write-OK ".NET 8 SDK installed"
    } else {
        Write-Fail "Failed to install .NET 8 SDK. Install manually from https://dotnet.microsoft.com/download/dotnet/8.0"
        exit 1
    }
}

# ─── 2. Install Git ───
Write-Step "Step 2/6: Check Git"

$gitPath = $null
$gitCandidates = @(
    "C:\Program Files\Git\bin\git.exe",
    "C:\Program Files (x86)\Git\bin\git.exe",
    "$env:LOCALAPPDATA\Programs\Git\bin\git.exe"
)

foreach ($c in $gitCandidates) {
    if (Test-Path $c) { $gitPath = $c; break }
}

if (-not $gitPath) {
    $gitPath = (Get-Command git -ErrorAction SilentlyContinue).Source
}

if ($gitPath) {
    Write-OK "Git already installed: $gitPath"
} else {
    Write-Host "  Installing Git..."
    $gitUrl = "https://github.com/git-for-windows/git/releases/download/v2.47.1.windows.2/Git-2.47.1.2-64-bit.exe"
    $gitInstaller = "$env:TEMP\git-install.exe"
    Invoke-WebRequest -Uri $gitUrl -OutFile $gitInstaller -UseBasicParsing
    Start-Process -FilePath $gitInstaller -ArgumentList "/VERYSILENT /NORESTART /NOCANCEL /SP- /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /COMPONENTS=icons,ext\reg\shellhere,assoc,assoc_sh" -Wait
    $env:PATH = "C:\Program Files\Git\bin;$env:PATH"
    $gitPath = "C:\Program Files\Git\bin\git.exe"
    if (Test-Path $gitPath) {
        Write-OK "Git installed"
    } else {
        Write-Fail "Failed to install Git. Install manually from https://git-scm.com/download/win"
        exit 1
    }
}

# ─── 3. Clone or update repo ───
Write-Step "Step 3/6: Clone / update repo"

if (Test-Path "$InstallDir\.git") {
    Write-Host "  Repo exists at $InstallDir, pulling latest..."
    & git -C $InstallDir pull --ff-only 2>&1 | ForEach-Object { Write-Host "    $_" }
    Write-OK "Repo updated"
} else {
    Write-Host "  Cloning $RepoUrl -> $InstallDir ..."
    & git clone $RepoUrl $InstallDir 2>&1 | ForEach-Object { Write-Host "    $_" }
    Write-OK "Repo cloned"
}

# ─── 4. Download sidecars ───
Write-Step "Step 4/6: Download sidecars"

Push-Location $InstallDir

# aria2c
$aria2Exe = "tools\aria2c\x64\aria2c.exe"
if (Test-Path $aria2Exe) {
    Write-Skip "aria2c already present"
} else {
    Write-Host "  Downloading aria2c 1.37.0..."
    New-Item -ItemType Directory -Force -Path "tools\aria2c\x64" | Out-Null
    $zip = "$env:TEMP\aria2.zip"
    curl.exe -L -o $zip "https://github.com/aria2/aria2/releases/download/release-1.37.0/aria2-1.37.0-win-64bit-build1.zip"
    Expand-Archive -Path $zip -DestinationPath "$env:TEMP\aria2" -Force
    Copy-Item "$env:TEMP\aria2\aria2-1.37.0-win-64bit-build1\aria2c.exe" $aria2Exe -Force
    Write-OK "aria2c downloaded"
}

# yt-dlp
$ytdlpExe = "tools\yt-dlp\yt-dlp.exe"
if (Test-Path $ytdlpExe) {
    Write-Skip "yt-dlp already present"
} else {
    Write-Host "  Downloading yt-dlp..."
    New-Item -ItemType Directory -Force -Path "tools\yt-dlp" | Out-Null
    curl.exe -L -o $ytdlpExe "https://github.com/yt-dlp/yt-dlp/releases/download/2026.03.17/yt-dlp.exe"
    Write-OK "yt-dlp downloaded"
}

# ffmpeg
$ffmpegExe = "tools\ffmpeg\x64\ffmpeg.exe"
if (Test-Path $ffmpegExe) {
    Write-Skip "ffmpeg already present"
} else {
    Write-Host "  Downloading ffmpeg..."
    New-Item -ItemType Directory -Force -Path "tools\ffmpeg\x64" | Out-Null
    $zip = "$env:TEMP\ffmpeg.zip"
    curl.exe -L -C - -o $zip "https://github.com/GyanD/codexffmpeg/releases/download/2025-01-15-git-4f3c9f2f03/ffmpeg-2025-01-15-git-4f3c9f2f03-essentials_build.zip"
    Expand-Archive -Path $zip -DestinationPath "$env:TEMP\ffmpeg" -Force
    Copy-Item "$env:TEMP\ffmpeg\ffmpeg-*-essentials_build\bin\ffmpeg.exe" $ffmpegExe -Force
    Write-OK "ffmpeg downloaded"
}

Pop-Location

# ─── 5. Download and configure runner ───
Write-Step "Step 5/6: Download and configure GitHub Actions runner"

$runnerDir = "$InstallDir\.runner"
$runnerZip = "$env:TEMP\actions-runner.zip"

if (Test-Path "$runnerDir\run.cmd") {
    Write-Skip "Runner already downloaded and configured"
} else {
    Write-Host "  Downloading runner v$RunnerVersion..."
    New-Item -ItemType Directory -Force -Path $runnerDir | Out-Null
    $runnerUrl = "https://github.com/actions/runner/releases/download/v$RunnerVersion/actions-runner-win-x64-$RunnerVersion.zip"
    curl.exe -L -o $runnerZip $runnerUrl
    Expand-Archive -Path $runnerZip -DestinationPath $runnerDir -Force
    Write-OK "Runner downloaded"

    Write-Host "  Configuring runner..."
    Push-Location $runnerDir
    $tokenClean = $RunnerToken.Trim()
    & .\config.cmd --url $RepoUrl --token $tokenClean --unattended 2>&1 | ForEach-Object { Write-Host "    $_" }
    Pop-Location
    Write-OK "Runner configured"
}

# ─── 6. Install as Windows service + start ───
Write-Step "Step 6/6: Install and start runner service"

Push-Location $runnerDir

# Install the service
if (-not (Get-Service -Name "actions.runner.*" -ErrorAction SilentlyContinue)) {
    & .\config.cmd --service --unattended 2>&1 | ForEach-Object { Write-Host "    $_" }
}

# Find and start the service
$svc = Get-Service -Name "actions.runner.*" -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -ne "Running") {
        Start-Service $svc.Name
        Write-OK "Runner service started: $($svc.Name)"
    } else {
        Write-OK "Runner service already running: $($svc.Name)"
    }
} else {
    Write-Host "  Starting runner manually (run.cmd)..."
    Start-Process -FilePath "cmd.exe" -ArgumentList "/c cd /d `"$runnerDir`" && run.cmd" -WindowStyle Minimized
    Write-OK "Runner started (manual mode - runs in background)"
}

Pop-Location

# ─── Done ───
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " SETUP COMPLETE" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Repo:        $InstallDir"
Write-Host "  Runner dir:  $runnerDir"
Write-Host ""
Write-Host "  To verify:" -ForegroundColor Yellow
Write-Host "    Go to https://github.com/o69dn/o-down/settings/actions" -ForegroundColor Yellow
Write-Host "    You should see your runner listed as 'Idle'" -ForegroundColor Yellow
Write-Host ""
Write-Host "  To test it:" -ForegroundColor Yellow
Write-Host "    Push any commit to master - the workflow will use your runner" -ForegroundColor Yellow
Write-Host "    and produce a real GUI .exe artifact (not a stub)." -ForegroundColor Yellow
Write-Host ""
Write-Host "  To stop the runner:" -ForegroundColor Yellow
Write-Host "    Stop-Service 'actions.runner.o69dn-o-down.*'" -ForegroundColor Yellow
Write-Host ""

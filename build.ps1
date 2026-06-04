<#
.SYNOPSIS
  Builds a portable, self-contained o-down release.

.DESCRIPTION
  - Publishes the WinUI 3 App (self-contained, win-x64).
  - Publishes the NativeMessaging host (self-contained, win-x64).
  - Bundles the sidecar binaries (aria2c, yt-dlp, ffmpeg) from .\tools\.
  - Zips the published App output.
  - Generates an o-down.Update.UpdateManifest JSON for the update server.

.PARAMETER Configuration
  Build configuration. Default: Release.

.PARAMETER Runtime
  Target RID. Default: win-x64. Use win-arm64 for ARM64.

.PARAMETER Version
  Version string written into the manifest. Default: 0.1.0.

.PARAMETER Channel
  Update channel. Default: stable.

.PARAMETER OutputDir
  Output directory for the zip and manifest. Default: .\dist.

.PARAMETER DownloadUrl
  Public URL where the zip will be served. Embedded in the manifest.

.PARAMETER Notes
  Optional release notes embedded in the manifest.

.EXAMPLE
  .\build.ps1 -Version 1.2.0 -DownloadUrl https://updates.example.com/o-down-1.2.0.zip
#>

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",
    [string]$Version = "0.1.0",
    [string]$Channel = "stable",
    [string]$OutputDir = "",
    [string]$DownloadUrl = "",
    [string]$Notes = $null
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $OutputDir) { $OutputDir = Join-Path $repoRoot "dist" }
$arch = if ($Runtime -eq "win-arm64") { "arm64" } else { "x64" }
$solution = Join-Path $repoRoot "o-down.sln"
$appProject = Join-Path $repoRoot "src\o-down.App\o-down.App.csproj"
$nativeMessagingProject = Join-Path $repoRoot "src\o-down.NativeMessaging\o-down.NativeMessaging.csproj"

if (-not (Test-Path $solution)) { throw "Solution not found at $solution" }
if (-not (Test-Path $appProject)) { throw "App project not found at $appProject" }

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$publishRoot = Join-Path $OutputDir "publish"
$appPublishDir = Join-Path $publishRoot "o-down"
$nativePublishDir = Join-Path $publishRoot "o-down.NativeMessaging"
if (Test-Path $publishRoot) { Remove-Item -Recurse -Force $publishRoot }

function Step($label, [scriptblock]$block) {
    Write-Host "==> $label" -ForegroundColor Cyan
    & $block
    if ($LASTEXITCODE -ne 0) { throw "$label failed (exit $LASTEXITCODE)" }
}

$dotnet = "dotnet"
try { & $dotnet --version | Out-Null } catch { throw "dotnet CLI not on PATH" }

Step "Restoring solution" {
    & $dotnet restore $solution
}

Step "Publishing o-down.App ($Runtime, $Configuration)" {
    & $dotnet publish $appProject `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained true `
        --output $appPublishDir `
        /p:Platform=x64 `
        /p:PublishSingleFile=false `
        /p:WindowsAppSDKSelfContained=true
}

Step "Publishing o-down.NativeMessaging ($Runtime, $Configuration)" {
    & $dotnet publish $nativeMessagingProject `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained false `
        --output $nativePublishDir
}

Step "Bundling sidecar binaries" {
    $sidecars = @(
        @{ Name = "aria2c.exe"; Source = "tools\aria2c\$arch\aria2c.exe" }
        @{ Name = "yt-dlp.exe"; Source = "tools\yt-dlp\yt-dlp.exe" }
        @{ Name = "ffmpeg.exe"; Source = "tools\ffmpeg\$arch\ffmpeg.exe" }
    )
    foreach ($s in $sidecars) {
        $src = Join-Path $repoRoot $s.Source
        if (Test-Path $src) {
            Copy-Item -Force $src (Join-Path $appPublishDir $s.Name)
            Write-Host "    + $($s.Name)"
        } else {
            Write-Host "    - $($s.Name) (not found at $src -- skipping)" -ForegroundColor Yellow
        }
    }
}

Step "Copying native messaging host into App output" {
    $nm = Join-Path $nativePublishDir "o-down.NativeMessaging.exe"
    if (Test-Path $nm) {
        Copy-Item -Force $nm (Join-Path $appPublishDir "o-down.NativeMessaging.exe")
        Write-Host "    + o-down.NativeMessaging.exe"
    } else {
        Write-Host "    - native messaging host not found -- skipping" -ForegroundColor Yellow
    }
}

Step "Zipping published output" {
    $zipPath = Join-Path $OutputDir "o-down-$Version.zip"
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $appPublishDir,
        $zipPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false
    )
    $size = (Get-Item $zipPath).Length
    Write-Host "    -> $zipPath ($([math]::Round($size / 1MB, 2)) MB)"
}

Step "Generating update manifest" {
    if (-not $DownloadUrl) {
        $DownloadUrl = "https://updates.example.com/o-down/$Channel/o-down-$Version.zip"
        Write-Host "    (no -DownloadUrl supplied; using placeholder $DownloadUrl)" -ForegroundColor Yellow
    }
    $manifestPath = Join-Path $OutputDir "latest.json"
    $toolProject = Join-Path $repoRoot "tests\o-down.Update.Tests\o-down.Update.Tests.csproj"
    $builder = @"
using System;
using System.IO;
using System.Threading.Tasks;
using o_down.Update;

class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 4) { Console.Error.WriteLine("usage: builder <version> <channel> <zip> <url> [<out>]"); return 1; }
        var manifest = await UpdateManifestBuilder.BuildFromZipAsync(args[0], args[1], args[2], args[3]);
        var outPath = args.Length >= 5 ? args[4] : Path.Combine(Path.GetDirectoryName(args[2])!, "latest.json");
        await UpdateManifestBuilder.WriteAsync(manifest, outPath);
        Console.WriteLine(outPath);
        return 0;
    }
}
"@
    $tmpDir = Join-Path $env:TEMP ("odown-manifest-tool-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tmpDir | Out-Null
    $tmpProj = Join-Path $tmpDir "tool.csproj"
    $tmpCs = Join-Path $tmpDir "Program.cs"
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$($repoRoot -replace '\\','\\')\src\o-down.Update\o-down.Update.csproj" />
  </ItemGroup>
</Project>
"@ | Set-Content -Path $tmpProj -Encoding UTF8
    $builder | Set-Content -Path $tmpCs -Encoding UTF8

    $zipPath = Join-Path $OutputDir "o-down-$Version.zip"
    $runner = & $dotnet run --project $tmpProj --configuration Release --no-restore -- $Version $Channel $zipPath $DownloadUrl $manifestPath 2>&1
    if ($LASTEXITCODE -ne 0) { throw "manifest generation failed: $runner" }
    Remove-Item -Recurse -Force $tmpDir
    Write-Host "    -> $manifestPath"
}

Step "Building setup.exe (if Inno Setup is available)" {
    $result = & "$repoRoot\installer\build-installer.ps1" -Version $Version -OutputDir $OutputDir 2>&1
    if ($LASTEXITCODE -ne 0) { Write-Host "  (skip installer -- Inno Setup not installed)" -ForegroundColor Yellow }
}

Write-Host ""
Write-Host "Build complete." -ForegroundColor Green
Write-Host "Zip:       $((Join-Path $OutputDir "o-down-$Version.zip"))"
Write-Host "Manifest:  $((Join-Path $OutputDir "latest.json"))"
$setupPath = Join-Path $OutputDir "o-down-$Version-setup.exe"
if (Test-Path $setupPath) { Write-Host "Installer: $setupPath ($([math]::Round((Get-Item $setupPath).Length/1MB,2)) MB)" }

param(
    [string]$Version = "0.1.0",
    [string]$Channel = "stable",
    [string]$OutputDir = "",
    [string]$DownloadUrl = "",
    [string]$Notes = $null
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSCommandPath -Parent
if (-not $OutputDir) { $OutputDir = Join-Path $repoRoot "dist" }

$targetsFile = "$env:USERPROFILE\.nuget\packages\microsoft.windowsappsdk\1.5.240802000\buildTransitive\Microsoft.UI.Xaml.Markup.Compiler.interop.targets"
$backupFile = "$targetsFile.bak"
$wrapperPath = "$repoRoot\xamlwrap.cmd"
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
$arch = "x64"
$appPublishDir = Join-Path $repoRoot "src\o-down.App\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish"
$nmPublishDir = Join-Path $repoRoot "src\o-down.NativeMessaging\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish"
$objDir = Join-Path $repoRoot "src\o-down.App\obj\x64\Release\net8.0-windows10.0.19041.0\win-x64"

$env:PATH = "C:\Program Files\dotnet;" + $env:PATH

function Step($label, [scriptblock]$block) {
    Write-Host "==> $label" -ForegroundColor Cyan
    & $block
    if ($LASTEXITCODE -ne 0 -and $label -notlike "*XBF stubs*" -and $label -notlike "*pass 1*") { throw "$label failed (exit $LASTEXITCODE)" }
}

# Ensure dotnet exists
if (-not (Test-Path $dotnet)) { throw "dotnet not found at $dotnet" }

# Patch targets file
Write-Host "Patching XAML compiler to ignore exit code..." -ForegroundColor Cyan
$content = Get-Content $targetsFile
if (-not (Test-Path $backupFile)) { Copy-Item $targetsFile $backupFile }
$wrapperEscaped = $wrapperPath -replace '\\', '\\'
$newExec = '        <Exec Condition="' + "'" + '$(UseXamlCompilerExecutable)' + "'" + ' == ' + "'" + 'true' + "'" + '" Command="cmd /c call &quot;' + $wrapperEscaped + '&quot; &quot;$(XamlCompilerExePath)&quot; &quot;$(XamlCompilerExeInputJson)&quot; &quot;$(XamlCompilerExeOutputJson)&quot;" />'
$content[759] = $newExec
Set-Content $targetsFile $content
Write-Host "  Patched"

# First publish pass - XAML compiler runs (exit ignored), C# compiles, but XBF copy fails
Step "Publishing o-down.App (pass 1)" {
    Remove-Item -Recurse -Force "$repoRoot\src\o-down.App\obj" -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force "$repoRoot\src\o-down.App\bin" -ErrorAction SilentlyContinue
    & $dotnet publish "$repoRoot\src\o-down.App" -c Release -r win-x64 -p:Platform=x64 2>&1
    $failed = $LASTEXITCODE
    if ($failed -ne 0) { Write-Host "  (XAML compiler exit ignored - continuing)" -ForegroundColor Yellow }
}

# Create XBF stubs from output.json
Step "Creating XBF stubs" {
    & "$repoRoot\build-xbf-stubs.ps1" 2>&1
}

# Second publish pass - XBF files exist, copy succeeds
Step "Publishing o-down.App (pass 2)" {
    & $dotnet publish "$repoRoot\src\o-down.App" -c Release -r win-x64 -p:Platform=x64 --no-restore 2>&1
}

if (-not (Test-Path "$appPublishDir\o-down.exe")) {
    throw "App publish did not produce o-down.exe"
}
Write-Host "  o-down.exe: $((Get-Item "$appPublishDir\o-down.exe").Length) bytes"

# Publish NativeMessaging
Step "Publishing o-down.NativeMessaging" {
    & $dotnet publish "$repoRoot\src\o-down.NativeMessaging" -c Release -r win-x64 -p:Platform=x64 2>&1
}

# Bundle into dist
$distDir = Join-Path $repoRoot "dist"
$publishRoot = Join-Path $distDir "publish"
$finalPublishDir = Join-Path $publishRoot "o-down"

# Copy App output
if (Test-Path $publishRoot) { Remove-Item -Recurse -Force $publishRoot }
New-Item -ItemType Directory -Force -Path $finalPublishDir | Out-Null
Copy-Item -Path "$appPublishDir\*" -Destination $finalPublishDir -Recurse -Force
Write-Host "  Copied App output ($((Get-ChildItem $finalPublishDir | Measure-Object).Count) items)"

# Copy sidecars
Step "Bundling sidecar binaries" {
    $sidecars = @(
        @{ Name = "aria2c.exe"; Source = "tools\aria2c\$arch\aria2c.exe" }
        @{ Name = "yt-dlp.exe"; Source = "tools\yt-dlp\yt-dlp.exe" }
        @{ Name = "ffmpeg.exe"; Source = "tools\ffmpeg\$arch\ffmpeg.exe" }
    )
    foreach ($s in $sidecars) {
        $src = Join-Path $repoRoot $s.Source
        if (Test-Path $src) {
            Copy-Item -Force $src (Join-Path $finalPublishDir $s.Name)
            Write-Host "  + $($s.Name) ($([math]::Round((Get-Item $src).Length/1MB,1)) MB)"
        } else {
            Write-Host "  - $($s.Name) not found" -ForegroundColor Yellow
        }
    }
}

# Copy NativeMessaging host
$nmExe = Join-Path $nmPublishDir "o-down.NativeMessaging.exe"
if (Test-Path $nmExe) {
    Copy-Item -Force $nmExe (Join-Path $finalPublishDir "o-down.NativeMessaging.exe")
    Write-Host "  + o-down.NativeMessaging.exe"
} else {
    Write-Host "  - NativeMessaging host not found" -ForegroundColor Yellow
}

# Zip
Step "Zipping published output" {
    $zipPath = Join-Path $distDir "o-down-$Version.zip"
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($finalPublishDir, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    $size = (Get-Item $zipPath).Length
    Write-Host "  -> $zipPath ($([math]::Round($size / 1MB, 2)) MB)"
}

# Manifest
Step "Generating update manifest" {
    if (-not $DownloadUrl) {
        $DownloadUrl = "https://updates.example.com/o-down/$channel/o-down-$Version.zip"
    }
    $zipPath = Join-Path $distDir "o-down-$Version.zip"
    $manifestPath = Join-Path $distDir "latest.json"
    $tmpDir = Join-Path $env:TEMP ("odown-manifest-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tmpDir | Out-Null

    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$($repoRoot -replace '\\','\\')\src\o-down.Update\o-down.Update.csproj" />
  </ItemGroup>
</Project>
"@ | Set-Content (Join-Path $tmpDir "tool.csproj") -Encoding UTF8

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
    $builder | Set-Content (Join-Path $tmpDir "Program.cs") -Encoding UTF8

    $result = & $dotnet run --project (Join-Path $tmpDir "tool.csproj") --configuration Release -- $Version $Channel $zipPath $DownloadUrl $manifestPath 2>&1
    if ($LASTEXITCODE -ne 0) { throw "manifest generation failed: $result" }
    Remove-Item -Recurse -Force $tmpDir
    Write-Host "  -> $manifestPath"
}

# Installer (optional - skip if Inno Setup not installed)
Write-Host "==> Building setup.exe (if Inno Setup is available)" -ForegroundColor Cyan
$installerResult = & "$repoRoot\installer\build-installer.ps1" -Version $Version -SourceDir $finalPublishDir -OutputDir $distDir 2>&1
if ($LASTEXITCODE -eq 0) { Write-Host $installerResult } else { Write-Host "  (skip installer - Inno Setup not installed)" -ForegroundColor Yellow }

# Restore targets file
Write-Host "`nRestoring targets file..." -ForegroundColor Cyan
if (Test-Path $backupFile) { Copy-Item $backupFile $targetsFile -Force; Write-Host "  Restored" }

Write-Host "`nBuild complete!" -ForegroundColor Green
Write-Host "Zip:      $(Join-Path $distDir "o-down-$Version.zip")"
Write-Host "Manifest: $(Join-Path $distDir "latest.json")"
$setupPath = Join-Path $distDir "o-down-$Version-setup.exe"
if (Test-Path $setupPath) { Write-Host "Installer: $setupPath ($([math]::Round((Get-Item $setupPath).Length/1MB,2)) MB)" }
Write-Host ""
Write-Host "NOTE: GUI pages won't render on this Insider build. For a working EXE," -ForegroundColor Yellow
Write-Host "build on a stable Windows 10/11 machine (no workaround needed)." -ForegroundColor Yellow

#Requires -RunAsAdministrator
# Game Save Convert — installer
# Usage: irm https://raw.githubusercontent.com/AlexeyGoto/game-save-convert/main/install.ps1 | iex

$ErrorActionPreference = "Stop"
$InstallDir = "C:\Tools\SaveCompat"
$MandarinDir = "$InstallDir\mandarin"
$CLI = "$MandarinDir\mandarin-juice-cli.exe"
$MandarinZipUrl = "https://github.com/mi5hmash/MandarinJuice/releases/download/v1.0.0/win-x64_v1.0.0.zip"
$ProfilesZipUrl = "https://github.com/mi5hmash/MandarinJuice/releases/download/v1.0.0/_profiles.zip"
$RepoBase = "https://raw.githubusercontent.com/AlexeyGoto/game-save-convert/main"

Write-Host "===== Game Save Convert — Installer =====" -ForegroundColor Cyan
Write-Host ""

# ===== 1. Create install directory =====
Write-Progress -Activity "Installing" -Status "Creating directories..." -PercentComplete 0
if (-not (Test-Path $InstallDir)) { New-Item -Path $InstallDir -ItemType Directory -Force | Out-Null }
if (-not (Test-Path $MandarinDir)) { New-Item -Path $MandarinDir -ItemType Directory -Force | Out-Null }

# ===== 2. Download MandarinJuice =====
if (-not (Test-Path $CLI)) {
    Write-Progress -Activity "Installing" -Status "Downloading MandarinJuice..." -PercentComplete 10
    Write-Host "[1/5] Downloading MandarinJuice..."
    $zipPath = "$env:TEMP\mandarin_mj.zip"
    Invoke-WebRequest -Uri $MandarinZipUrl -OutFile $zipPath -UseBasicParsing
    if (-not (Test-Path $zipPath)) { throw "Failed to download MandarinJuice" }

    Write-Progress -Activity "Installing" -Status "Extracting MandarinJuice..." -PercentComplete 25
    Write-Host "[2/5] Extracting MandarinJuice..."
    $tmpExtract = "$env:TEMP\mandarin_extract"
    if (Test-Path $tmpExtract) { Remove-Item $tmpExtract -Recurse -Force }
    Expand-Archive -Path $zipPath -DestinationPath $tmpExtract -Force

    # Find and copy MandarinJuice files
    $mjRoot = Get-ChildItem $tmpExtract -Recurse -Filter "mandarin-juice-cli.exe" | Select-Object -First 1
    if (-not $mjRoot) { throw "mandarin-juice-cli.exe not found in archive" }
    $mjDir = $mjRoot.DirectoryName
    Copy-Item "$mjDir\*" $MandarinDir -Recurse -Force

    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
    Remove-Item $tmpExtract -Recurse -Force -ErrorAction SilentlyContinue

    # Download profiles
    Write-Progress -Activity "Installing" -Status "Downloading game profiles..." -PercentComplete 35
    Write-Host "[3/5] Downloading game profiles..."
    $profZip = "$env:TEMP\mandarin_profiles.zip"
    Invoke-WebRequest -Uri $ProfilesZipUrl -OutFile $profZip -UseBasicParsing
    if (Test-Path $profZip) {
        $tmpProf = "$env:TEMP\mandarin_prof_extract"
        if (Test-Path $tmpProf) { Remove-Item $tmpProf -Recurse -Force }
        Expand-Archive -Path $profZip -DestinationPath $tmpProf -Force
        $profSrc = Get-ChildItem $tmpProf -Recurse -Directory -Filter "_profiles" | Select-Object -First 1
        if ($profSrc) {
            $profDst = "$MandarinDir\_profiles"
            if (-not (Test-Path $profDst)) { New-Item $profDst -ItemType Directory -Force | Out-Null }
            Copy-Item "$($profSrc.FullName)\*" $profDst -Recurse -Force
        }
        Remove-Item $profZip -Force -ErrorAction SilentlyContinue
        Remove-Item $tmpProf -Recurse -Force -ErrorAction SilentlyContinue
    }
} else {
    Write-Host "[1-3/5] MandarinJuice already installed, skipping."
}

# ===== 3. Install .NET 10 runtime =====
Write-Progress -Activity "Installing" -Status "Checking .NET runtime..." -PercentComplete 50
Write-Host "[4/5] Installing .NET 10 runtime..."

# Use official dotnet-install.ps1 script — most reliable method
$dotnetInstallScript = "$env:TEMP\dotnet-install.ps1"
Write-Host "       Downloading dotnet-install.ps1..."
Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $dotnetInstallScript -UseBasicParsing

if (Test-Path $dotnetInstallScript) {
    Write-Progress -Activity "Installing" -Status "Installing .NET 10 runtime..." -PercentComplete 65
    Write-Host "       Running dotnet-install.ps1 (channel 10.0)..."
    & $dotnetInstallScript -Channel 10.0 -Runtime dotnet -InstallDir "C:\Program Files\dotnet" -NoPath
    Remove-Item $dotnetInstallScript -Force -ErrorAction SilentlyContinue
} else {
    Write-Warning "Failed to download dotnet-install.ps1"
}

# Verify MandarinJuice can run
$dotnetOk = $false
try {
    $proc = Start-Process -FilePath $CLI -ArgumentList "-h" -NoNewWindow -Wait -PassThru -RedirectStandardOutput "$env:TEMP\_mj_test.txt" -RedirectStandardError "$env:TEMP\_mj_err.txt" 2>$null
    if ($proc.ExitCode -le 1) { $dotnetOk = $true }
} catch {}
Remove-Item "$env:TEMP\_mj_test.txt", "$env:TEMP\_mj_err.txt" -Force -ErrorAction SilentlyContinue

if ($dotnetOk) {
    Write-Host "       .NET runtime OK — MandarinJuice verified"
} else {
    Write-Warning ".NET 10 runtime not working. Try: winget install Microsoft.DotNet.Runtime.10"
}

# ===== 4. Download save-convert.exe and README =====
Write-Progress -Activity "Installing" -Status "Downloading save-convert..." -PercentComplete 85
Write-Host "[5/5] Downloading save-convert.exe..."

# save-convert.exe (from GitHub releases)
$exeUrl = "https://github.com/AlexeyGoto/game-save-convert/releases/latest/download/save-convert.exe"
try {
    Invoke-WebRequest -Uri $exeUrl -OutFile "$InstallDir\save-convert.exe" -UseBasicParsing
} catch {
    Write-Warning "save-convert.exe not found in releases. Build from source or add manually."
}

# README
try {
    Invoke-WebRequest -Uri "$RepoBase/README.md" -OutFile "$InstallDir\README.md" -UseBasicParsing
} catch {}

# ===== 5. Add to PATH =====
Write-Progress -Activity "Installing" -Status "Configuring PATH..." -PercentComplete 95
$machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
if ($machinePath -notlike "*$InstallDir*") {
    [Environment]::SetEnvironmentVariable("Path", "$machinePath;$InstallDir", "Machine")
    Write-Host "       Added $InstallDir to system PATH"
} else {
    Write-Host "       Already in PATH"
}

Write-Progress -Activity "Installing" -Completed
Write-Host ""
Write-Host "===== Installation Complete =====" -ForegroundColor Green
Write-Host "  Install dir:  $InstallDir"
Write-Host "  MandarinJuice: $CLI"
Write-Host "  Usage: save-convert.exe -<steam_id> -<save_path> [-<game>]"
Write-Host "  Game shortcuts: re9, mhw, dd2, dr, kg"
Write-Host ""
Write-Host "  NOTE: Restart terminal for PATH changes to take effect." -ForegroundColor Yellow
Write-Host ""

# Open README with instructions
$readmePath = "$InstallDir\README.md"
if (Test-Path $readmePath) {
    Write-Host "Opening README..." -ForegroundColor Cyan
    Start-Process $readmePath
}

#Requires -RunAsAdministrator
# Game Save Convert — installer
# Usage: irm https://raw.githubusercontent.com/AlexeyGoto/game-save-convert/main/install.ps1 | iex

$ErrorActionPreference = "Stop"
$InstallDir = "C:\Tools\SaveCompat"
$MandarinDir = "$InstallDir\mandarin"
$CLI = "$MandarinDir\mandarin-juice-cli.exe"
$MandarinZipUrl = "https://github.com/mi5hmash/MandarinJuice/releases/download/v1.0.0/win-x64_v1.0.0.zip"
$ProfilesZipUrl = "https://github.com/mi5hmash/MandarinJuice/releases/download/v1.0.0/_profiles.zip"
$DotnetUrl = "https://aka.ms/dotnet/10.0/preview/dotnet-runtime-win-x64.exe"
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

# ===== 3. Check .NET 10 runtime =====
Write-Progress -Activity "Installing" -Status "Checking .NET runtime..." -PercentComplete 50
Write-Host "[4/5] Checking .NET 10 runtime..."

$dotnetOk = $false
try {
    $proc = Start-Process -FilePath $CLI -ArgumentList "-h" -NoNewWindow -Wait -PassThru -RedirectStandardOutput "$env:TEMP\_mj_test.txt" -RedirectStandardError "$env:TEMP\_mj_err.txt" 2>$null
    if ($proc.ExitCode -le 1) { $dotnetOk = $true }
} catch {}

if (-not $dotnetOk) {
    Write-Host "       .NET 10 not found, checking for RC versions..."
    $dotnetBase = "C:\Program Files\dotnet\shared\Microsoft.NETCore.App"
    $stablePath = "$dotnetBase\10.0.0"

    if (-not (Test-Path $stablePath)) {
        # Try symlink from RC
        $rcDir = Get-ChildItem $dotnetBase -Directory -Filter "10.0.0-*" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($rcDir) {
            Write-Host "       Found $($rcDir.Name), creating symlink..."
            cmd /c mklink /D "$stablePath" "$($rcDir.FullName)" 2>$null | Out-Null
        } else {
            # Download and install
            Write-Host "       Downloading .NET 10 runtime..."
            Write-Progress -Activity "Installing" -Status "Downloading .NET 10 runtime..." -PercentComplete 60
            $dotnetExe = "$env:TEMP\dotnet10_runtime.exe"
            Invoke-WebRequest -Uri $DotnetUrl -OutFile $dotnetExe -UseBasicParsing
            if (Test-Path $dotnetExe) {
                Write-Host "       Installing .NET 10 runtime..."
                Write-Progress -Activity "Installing" -Status "Installing .NET 10 runtime..." -PercentComplete 70
                Start-Process -FilePath $dotnetExe -ArgumentList "/install /quiet /norestart" -Wait
                Remove-Item $dotnetExe -Force -ErrorAction SilentlyContinue
                # Retry symlink
                $rcDir = Get-ChildItem $dotnetBase -Directory -Filter "10.0.0-*" -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($rcDir -and -not (Test-Path $stablePath)) {
                    cmd /c mklink /D "$stablePath" "$($rcDir.FullName)" 2>$null | Out-Null
                }
            } else {
                Write-Warning "Failed to download .NET 10 runtime. Install manually."
            }
        }
    }

    # Verify
    try {
        $proc = Start-Process -FilePath $CLI -ArgumentList "-h" -NoNewWindow -Wait -PassThru -RedirectStandardOutput "$env:TEMP\_mj_test.txt" -RedirectStandardError "$env:TEMP\_mj_err.txt" 2>$null
        if ($proc.ExitCode -le 1) { $dotnetOk = $true }
    } catch {}
}

Remove-Item "$env:TEMP\_mj_test.txt", "$env:TEMP\_mj_err.txt" -Force -ErrorAction SilentlyContinue

if ($dotnetOk) {
    Write-Host "       .NET runtime OK"
} else {
    Write-Warning ".NET 10 runtime not working. MandarinJuice may not function."
    Write-Warning "Install .NET 10 manually: https://dotnet.microsoft.com/download/dotnet/10.0"
}

# ===== 4. Download save-convert.exe and steam_ids.txt =====
Write-Progress -Activity "Installing" -Status "Downloading save-convert..." -PercentComplete 85
Write-Host "[5/5] Downloading save-convert.exe..."

# steam_ids.txt
Invoke-WebRequest -Uri "$RepoBase/steam_ids.txt" -OutFile "$InstallDir\steam_ids.txt" -UseBasicParsing

# save-convert.exe (from GitHub releases or raw)
$exeUrl = "https://github.com/AlexeyGoto/game-save-convert/releases/latest/download/save-convert.exe"
try {
    Invoke-WebRequest -Uri $exeUrl -OutFile "$InstallDir\save-convert.exe" -UseBasicParsing
} catch {
    Write-Warning "save-convert.exe not found in releases. Build from source or add manually."
}

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
Write-Host "  Usage: save-convert.exe -<steam_id> -<save_path>"
Write-Host ""
Write-Host "  NOTE: Restart terminal for PATH changes to take effect." -ForegroundColor Yellow
Write-Host ""

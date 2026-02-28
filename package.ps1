# ═══════════════════════════════════════════════════════════
# CIC BIM Addin - Package Script (chay tren may dev)
# Build Release -> Tao thu muc phan phoi -> Dong ZIP
# ═══════════════════════════════════════════════════════════

param(
    [string]$OutputDir = ".\dist"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host ""
Write-Host "=== CIC BIM Addin - Dong goi phan mem ===" -ForegroundColor Cyan
Write-Host ""

# --- Step 1: Build Release ---
Write-Host "[1/4] Building Release (Revit 2024)..." -ForegroundColor Yellow
dotnet build "$ScriptDir\CIC.BIM.Addin.sln" -c Release -f net48 --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host "  X Build FAILED!" -ForegroundColor Red
    exit 1
}
Write-Host "  OK Build thanh cong" -ForegroundColor Green

# --- Step 2: Tao thu muc phan phoi ---
Write-Host "[2/4] Tao thu muc phan phoi..." -ForegroundColor Yellow

$distDir = Join-Path $ScriptDir $OutputDir
$packageName = "CIC_BIM_Addin_v1.0.0"
$packageDir = Join-Path $distDir $packageName

# Don cu
if (Test-Path $packageDir) {
    Remove-Item -Path $packageDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null

# --- Step 3: Copy files ---
Write-Host "[3/4] Copy files vao package..." -ForegroundColor Yellow

# Copy Revit 2024 files
$src2024 = Join-Path $ScriptDir "bin\Release\Revit2024"
$dst2024 = Join-Path $packageDir "Revit2024"
if (Test-Path $src2024) {
    New-Item -ItemType Directory -Force -Path $dst2024 | Out-Null
    Copy-Item -Path "$src2024\*" -Destination $dst2024 -Recurse -Force
    Write-Host "  OK Revit 2024 files copied" -ForegroundColor DarkGreen
}

# Copy Revit 2025 files (neu co)
$src2025 = Join-Path $ScriptDir "bin\Release\Revit2025"
$dst2025 = Join-Path $packageDir "Revit2025"
if (Test-Path $src2025) {
    New-Item -ItemType Directory -Force -Path $dst2025 | Out-Null
    Copy-Item -Path "$src2025\*" -Destination $dst2025 -Recurse -Force
    Write-Host "  OK Revit 2025 files copied" -ForegroundColor DarkGreen
}

# Copy installer script & huong dan
Copy-Item -Path (Join-Path $ScriptDir "install.ps1") -Destination $packageDir -Force
Copy-Item -Path (Join-Path $ScriptDir "HUONG_DAN_CAI_DAT.txt") -Destination $packageDir -Force
Write-Host "  OK Installer and guide copied" -ForegroundColor DarkGreen

# --- Step 4: Nen ZIP ---
Write-Host "[4/4] Creating ZIP..." -ForegroundColor Yellow
$zipPath = Join-Path $distDir "$packageName.zip"
if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force
}
Compress-Archive -Path "$packageDir\*" -DestinationPath $zipPath -Force
Write-Host "  OK ZIP created: $zipPath" -ForegroundColor Green

# --- Result ---
$zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "DONE! Package created successfully!" -ForegroundColor Green
Write-Host "  File: $zipPath" -ForegroundColor Green
Write-Host "  Size: ${zipSize} MB" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Gui file ZIP nay cho nhan su de cai dat." -ForegroundColor Yellow

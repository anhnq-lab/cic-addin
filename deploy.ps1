# ═══════════════════════════════════════════════════════════
# CIC BIM Addin - Deploy Script
# Triển khai add-in cho Revit 2024 và Revit 2025
# ═══════════════════════════════════════════════════════════

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$BinDir = Join-Path $ScriptDir "bin\$Configuration"
$AddinsBase = Join-Path $env:APPDATA "Autodesk\Revit\Addins"

Write-Host "═══ CIC BIM Addin Deployment ═══" -ForegroundColor Cyan
Write-Host ""

# ─── Revit 2024 ───
$src2024 = Join-Path $BinDir "Revit2024"
$dst2024 = Join-Path $AddinsBase "2024"
$dstTool2024 = Join-Path $dst2024 "CICTool"

if (Test-Path $src2024) {
    Write-Host "[Revit 2024] Deploying..." -ForegroundColor Green
    
    # Tạo thư mục nếu chưa có
    New-Item -ItemType Directory -Force -Path $dstTool2024 | Out-Null
    
    # Copy DLLs và dependencies
    Copy-Item -Path "$src2024\*" -Destination $dstTool2024 -Recurse -Force
    
    # Copy .addin manifest
    $addinSrc = Join-Path $src2024 "CIC.BIM.Addin.2024.addin"
    if (Test-Path $addinSrc) {
        Copy-Item -Path $addinSrc -Destination (Join-Path $dst2024 "CIC.BIM.Addin.addin") -Force
    }
    
    Write-Host "  ✓ Deployed to: $dstTool2024" -ForegroundColor DarkGreen
} else {
    Write-Host "[Revit 2024] Build not found: $src2024" -ForegroundColor Yellow
}

# ─── Revit 2025 ───
$src2025 = Join-Path $BinDir "Revit2025"
$dst2025 = Join-Path $AddinsBase "2025"
$dstTool2025 = Join-Path $dst2025 "CICTool"

if (Test-Path $src2025) {
    Write-Host "[Revit 2025] Deploying..." -ForegroundColor Green
    
    # Tạo thư mục nếu chưa có
    New-Item -ItemType Directory -Force -Path $dstTool2025 | Out-Null
    
    # Copy DLLs và dependencies
    Copy-Item -Path "$src2025\*" -Destination $dstTool2025 -Recurse -Force
    
    # Copy .addin manifest
    $addinSrc = Join-Path $src2025 "CIC.BIM.Addin.addin"
    if (Test-Path $addinSrc) {
        Copy-Item -Path $addinSrc -Destination (Join-Path $dst2025 "CIC.BIM.Addin.addin") -Force
    }
    
    Write-Host "  ✓ Deployed to: $dstTool2025" -ForegroundColor DarkGreen
} else {
    Write-Host "[Revit 2025] Build not found: $src2025" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "═══ Hoàn tất! Khởi động lại Revit để sử dụng add-in. ═══" -ForegroundColor Cyan

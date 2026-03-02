# ═══════════════════════════════════════════════════════════
# CIC BIM Addin - Script Cài đặt
# Dành cho nhân sự: Click phải → Run with PowerShell
# ═══════════════════════════════════════════════════════════

$ErrorActionPreference = "Stop"
$Host.UI.RawUI.WindowTitle = "CIC BIM Addin - Cai dat"

Write-Host ""
Write-Host "  ╔═══════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "  ║     CIC BIM Addin - Chuong trinh cai dat     ║" -ForegroundColor Cyan
Write-Host "  ║     Phien ban: 1.0.4                         ║" -ForegroundColor Cyan
Write-Host "  ╚═══════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$AddinsBase = Join-Path $env:APPDATA "Autodesk\Revit\Addins"
$installed = @()

# ─── Hàm cài đặt cho từng phiên bản Revit ───
function Install-ForRevit {
    param(
        [string]$Version,
        [string]$SourceDir,
        [string]$AddinFile
    )
    
    $srcPath = Join-Path $ScriptDir $SourceDir
    if (-not (Test-Path $srcPath)) {
        Write-Host "  [!] Khong tim thay thu muc $SourceDir" -ForegroundColor Yellow
        return $false
    }
    
    $dstAddins = Join-Path $AddinsBase $Version
    $dstTool = Join-Path $dstAddins "CICTool"
    
    Write-Host "  [Revit $Version] Dang cai dat..." -ForegroundColor Green
    
    # Tạo thư mục
    New-Item -ItemType Directory -Force -Path $dstTool | Out-Null
    
    # Copy tất cả DLLs và dependencies
    Copy-Item -Path "$srcPath\*" -Destination $dstTool -Recurse -Force
    
    # Copy .addin manifest
    $addinSrc = Join-Path $srcPath $AddinFile
    if (Test-Path $addinSrc) {
        Copy-Item -Path $addinSrc -Destination (Join-Path $dstAddins "CIC.BIM.Addin.addin") -Force
    }
    
    Write-Host "    -> Da cai vao: $dstTool" -ForegroundColor DarkGreen
    return $true
}

# ─── Cài đặt Revit 2024 ───
if (Install-ForRevit -Version "2024" -SourceDir "Revit2024" -AddinFile "CIC.BIM.Addin.2024.addin") {
    $installed += "Revit 2024"
}

# ─── Cài đặt Revit 2025 ───
if (Install-ForRevit -Version "2025" -SourceDir "Revit2025" -AddinFile "CIC.BIM.Addin.addin") {
    $installed += "Revit 2025"
}

# ─── Kết quả ───
Write-Host ""
if ($installed.Count -gt 0) {
    Write-Host "  ════════════════════════════════════════════" -ForegroundColor Green
    Write-Host "  ✓ CAI DAT THANH CONG!" -ForegroundColor Green
    Write-Host "    Da cai cho: $($installed -join ', ')" -ForegroundColor Green
    Write-Host "  ════════════════════════════════════════════" -ForegroundColor Green
    Write-Host ""
    Write-Host "  LUU Y: Hay dong Revit va mo lai de su dung CIC Tool." -ForegroundColor Yellow
}
else {
    Write-Host "  ════════════════════════════════════════════" -ForegroundColor Red
    Write-Host "  ✗ KHONG TIM THAY FILE CAI DAT!" -ForegroundColor Red
    Write-Host "    Vui long kiem tra lai thu muc cai dat." -ForegroundColor Red
    Write-Host "  ════════════════════════════════════════════" -ForegroundColor Red
}

Write-Host ""
Write-Host "  Nhan phim bat ky de dong..." -ForegroundColor DarkGray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

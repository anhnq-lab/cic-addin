# ═══════════════════════════════════════════════════════════
# CIC BIM Addin - Create Modern Icons
# Design: Minimalist, refined, modern pastel backgrounds with dark crisp text, 
# generous padding so they look small and neat in Revit.
# ═══════════════════════════════════════════════════════════

Add-Type -AssemblyName System.Drawing
$dir = Join-Path (Get-Location).Path "src\CIC.BIM.Addin\Resources"
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

function New-ModernIcon {
    param([string]$name, [string]$bgHex, [string]$fgHex, [string]$text, [float]$fontSize = 8)
    
    $bmp = New-Object System.Drawing.Bitmap(32, 32)
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $gfx.SmoothingMode = 'HighQuality'
    $gfx.TextRenderingHint = 'AntiAlias'
    $gfx.Clear([System.Drawing.Color]::Transparent)
    
    # Padding and rounded rectangle size
    $pad = 5
    $size = 32 - ($pad * 2)
    
    $bgColor = [System.Drawing.ColorTranslator]::FromHtml($bgHex)
    $fgColor = [System.Drawing.ColorTranslator]::FromHtml($fgHex)
    $brush = New-Object System.Drawing.SolidBrush($bgColor)
    
    # Draw rounded rectangle (approximate with circles and rects)
    $r = 4 # border radius
    $gfx.FillEllipse($brush, $pad, $pad, $r * 2, $r * 2)
    $gfx.FillEllipse($brush, 32 - $pad - $r * 2, $pad, $r * 2, $r * 2)
    $gfx.FillEllipse($brush, $pad, 32 - $pad - $r * 2, $r * 2, $r * 2)
    $gfx.FillEllipse($brush, 32 - $pad - $r * 2, 32 - $pad - $r * 2, $r * 2, $r * 2)
    $gfx.FillRectangle($brush, $pad + $r, $pad, $size - $r * 2, $size)
    $gfx.FillRectangle($brush, $pad, $pad + $r, $size, $size - $r * 2)
    
    # Draw Text
    $font = New-Object System.Drawing.Font('Segoe UI', $fontSize, [System.Drawing.FontStyle]::Bold)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = 'Center'
    $fmt.LineAlignment = 'Center'
    $textBrush = New-Object System.Drawing.SolidBrush($fgColor)
    $rect = New-Object System.Drawing.RectangleF(0, 1, 32, 32)
    $gfx.DrawString($text, $font, $textBrush, $rect, $fmt)
    
    $gfx.Dispose()
    $path = [System.IO.Path]::Combine($dir, $name)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Created Modern: $name"
}

# Theme 1: Primary Blue (Data, Params) -> Light blue bg, dark blue text
New-ModernIcon 'icon_assign_params.png' '#E3F2FD' '#1565C0' 'P+' 8.5

# Theme 2: Structure/Joint (Modelling) -> Light orange bg, dark orange text
New-ModernIcon 'icon_kc.png' '#FFF3E0' '#E65100' 'KC' 8.5
New-ModernIcon 'icon_plaster.png' '#FFF3E0' '#E65100' 'HT' 8.5
New-ModernIcon 'icon_room_bounding.png' '#FFF3E0' '#E65100' 'RB' 8.5

# Theme 3: CAD Utils -> Light green bg, dark green text
New-ModernIcon 'icon_block_cad.png' '#E8F5E9' '#2E7D32' 'CAD' 7
New-ModernIcon 'icon_duct.png' '#E8F5E9' '#2E7D32' 'MEP' 7

# Theme 4: QA/QC -> Light red bg, dark red text
New-ModernIcon 'icon_pipe_slope.png' '#FFEBEE' '#C62828' 'QA' 8.5

# Theme 5: QTO (Formwork) -> Light purple bg, dark purple text
New-ModernIcon 'icon_formwork.png' '#F3E5F5' '#6A1B9A' 'VK' 8.5

# Theme 6: Facility Mgmt -> Light teal bg, dark teal text
New-ModernIcon 'icon_fill_data.png' '#E0F2F1' '#00695C' 'DL' 8.5
New-ModernIcon 'icon_export_report.png' '#E0F2F1' '#00695C' 'XL' 8.5

# Theme 7: AI/Dashboard -> Dark grey/blue bg, white text for contrast
New-ModernIcon 'icon_ai_chat.png' '#37474F' '#FFFFFF' 'AI' 8.5
New-ModernIcon 'icon_dashboard.png' '#37474F' '#FFFFFF' 'DB' 8.5

Write-Host "Done generating modern minimal icons."

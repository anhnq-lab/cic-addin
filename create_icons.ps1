Add-Type -AssemblyName System.Drawing
$dir = 'd:\QuocAnh\2026\cic-addin\src\CIC.BIM.Addin\Resources'

function New-CircleIcon {
    param([string]$name, [int]$r, [int]$g2, [int]$b, [string]$text, [float]$fontSize = 9)
    $bmp = New-Object System.Drawing.Bitmap(32, 32)
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $gfx.SmoothingMode = 'HighQuality'
    $gfx.TextRenderingHint = 'AntiAlias'
    $gfx.Clear([System.Drawing.Color]::Transparent)
    
    $bgColor = [System.Drawing.Color]::FromArgb($r, $g2, $b)
    $brush = New-Object System.Drawing.SolidBrush($bgColor)
    $gfx.FillEllipse($brush, 1, 1, 30, 30)
    
    $font = New-Object System.Drawing.Font('Segoe UI', $fontSize, [System.Drawing.FontStyle]::Bold)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = 'Center'
    $fmt.LineAlignment = 'Center'
    $textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $rect = New-Object System.Drawing.RectangleF(0, 1, 32, 32)
    $gfx.DrawString($text, $font, $textBrush, $rect, $fmt)
    
    $gfx.Dispose()
    $path = [System.IO.Path]::Combine($dir, $name)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Created: $name"
}

# FM panel icons — teal/cyan color (0, 150, 136) like Teal 500
New-CircleIcon -name 'icon_assign_params.png' -r 0 -g2 150 -b 136 -text 'P+' -fontSize 9
New-CircleIcon -name 'icon_fill_data.png' -r 0 -g2 150 -b 136 -text 'DL' -fontSize 9
New-CircleIcon -name 'icon_export_report.png' -r 0 -g2 150 -b 136 -text 'XL' -fontSize 9

# Dashboard icon — purple (103, 58, 183) like Deep Purple 500
New-CircleIcon -name 'icon_dashboard.png' -r 103 -g2 58 -b 183 -text 'DB' -fontSize 9

Write-Host "`nAll FM + Dashboard icons created!"

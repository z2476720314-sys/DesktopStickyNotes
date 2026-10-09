param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'Assets'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$frames = @()
foreach ($size in @(16,20,24,32,40,48,64,128,256)) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.ScaleTransform($size / 256.0, $size / 256.0)
    $paper = New-Object System.Drawing.Drawing2D.GraphicsPath
    $paper.AddLine(54,16,160,16); $paper.AddLine(160,16,216,72); $paper.AddLine(216,72,216,218)
    $paper.AddArc(172,196,44,44,0,90); $paper.AddLine(194,240,54,240)
    $paper.AddArc(32,196,44,44,90,90); $paper.AddLine(32,218,32,38)
    $paper.AddArc(32,16,44,44,180,90); $paper.CloseFigure()
    $ink = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(45,93,115))
    $mint = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(163,224,220))
    $line = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(248,252,255), 13)
    $line.StartCap = $line.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.FillPath($ink, $paper)
    $fold = [System.Drawing.PointF[]]@([System.Drawing.PointF]::new(160,16),[System.Drawing.PointF]::new(216,72),[System.Drawing.PointF]::new(160,72))
    $graphics.FillPolygon($mint, $fold)
    $graphics.DrawLine($line, 67,97,179,97)
    $graphics.DrawLine($line, 67,128,162,128)
    $graphics.DrawLine($line, 67,159,133,159)
    $pin = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(177,151),[System.Drawing.PointF]::new(207,151),
        [System.Drawing.PointF]::new(200,163),[System.Drawing.PointF]::new(200,173),
        [System.Drawing.PointF]::new(210,183),[System.Drawing.PointF]::new(210,188),
        [System.Drawing.PointF]::new(174,188),[System.Drawing.PointF]::new(174,183),
        [System.Drawing.PointF]::new(184,173),[System.Drawing.PointF]::new(184,163))
    $graphics.FillPolygon($mint, $pin)
    $pinLine = New-Object System.Drawing.Pen($mint.Color, 5)
    $pinLine.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLine($pinLine, 192,188,192,213)
    if ($size -eq 256) { $bitmap.Save((Join-Path $OutputDirectory 'app-icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $memory = New-Object System.IO.MemoryStream
    $bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += [pscustomobject]@{ Size = $size; Bytes = $memory.ToArray() }
    $memory.Dispose(); $pinLine.Dispose(); $line.Dispose(); $mint.Dispose(); $ink.Dispose(); $paper.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$output = [System.IO.File]::Create((Join-Path $OutputDirectory 'app.ico'))
$writer = New-Object System.IO.BinaryWriter($output)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose(); $output.Dispose() }
Write-Output 'Icon sizes: 16,20,24,32,40,48,64,128,256'

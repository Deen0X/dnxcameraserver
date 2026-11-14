# Genera los iconos web (favicon, PWA, logo de la UI) a partir del PNG maestro.
#   pwsh tools/generate-icons.ps1
# Requiere System.Drawing (Windows PowerShell / pwsh en Windows).

param(
    [string]$Source = "resources/CameraServer.png",
    [string]$OutDir = "src/DNXCameraServer.Web/wwwroot/assets",
    [string]$FaviconDir = "src/DNXCameraServer.Web/wwwroot"
)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$srcPath = Join-Path $root $Source
$outPath = Join-Path $root $OutDir
$favPath = Join-Path $root $FaviconDir

New-Item -ItemType Directory -Force -Path $outPath | Out-Null

$src = [System.Drawing.Image]::FromFile($srcPath)

function New-Square([int]$size, [double]$padding, $background) {
    $bmp = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear($background)

    $inner = [int][Math]::Round($size * (1.0 - 2.0 * $padding))
    $offset = [int](($size - $inner) / 2)
    $g.DrawImage($src, $offset, $offset, $inner, $inner)
    $g.Dispose()
    return $bmp
}

function Save-Png($bitmap, [string]$path) {
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
}

# Logo de la interfaz (sidebar y login).
$logo = New-Square 128 0 ([System.Drawing.Color]::Transparent)
Save-Png $logo (Join-Path $outPath "logo.png")
$logo.Dispose()

# Favicons PNG.
foreach ($size in 16, 32) {
    $bmp = New-Square $size 0 ([System.Drawing.Color]::Transparent)
    Save-Png $bmp (Join-Path $favPath "favicon-$size.png")
    $bmp.Dispose()
}

# Apple touch icon: iOS no admite transparencia, fondo del tema oscuro.
$apple = New-Square 180 0.08 ([System.Drawing.Color]::FromArgb(255, 11, 15, 20))
Save-Png $apple (Join-Path $outPath "apple-touch-icon.png")
$apple.Dispose()

# PWA.
$icon192 = New-Square 192 0 ([System.Drawing.Color]::Transparent)
Save-Png $icon192 (Join-Path $outPath "icon-192.png")
$icon192.Dispose()

$icon512 = New-Square 512 0 ([System.Drawing.Color]::Transparent)
Save-Png $icon512 (Join-Path $outPath "icon-512.png")
$icon512.Dispose()

# Maskable: zona segura del 80 %, fondo opaco.
$maskable = New-Square 512 0.12 ([System.Drawing.Color]::FromArgb(255, 11, 15, 20))
Save-Png $maskable (Join-Path $outPath "icon-maskable-512.png")
$maskable.Dispose()

# favicon.ico multi-tamaño con entradas PNG (soportado por navegadores modernos).
$sizes = 16, 32, 48
$pngs = @()
foreach ($size in $sizes) {
    $bmp = New-Square $size 0 ([System.Drawing.Color]::Transparent)
    $ms = [System.IO.MemoryStream]::new()
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , @{ Size = $size; Bytes = $ms.ToArray() }
    $ms.Dispose()
    $bmp.Dispose()
}

$icoPath = Join-Path $favPath "favicon.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = [System.IO.BinaryWriter]::new($fs)

$bw.Write([uint16]0)              # reservado
$bw.Write([uint16]1)              # tipo: icono
$bw.Write([uint16]$pngs.Count)    # número de imágenes

$offset = 6 + (16 * $pngs.Count)
foreach ($img in $pngs) {
    $bw.Write([byte]$img.Size)    # ancho
    $bw.Write([byte]$img.Size)    # alto
    $bw.Write([byte]0)            # colores de paleta
    $bw.Write([byte]0)            # reservado
    $bw.Write([uint16]1)          # planos
    $bw.Write([uint16]32)         # bits por píxel
    $bw.Write([uint32]$img.Bytes.Length)
    $bw.Write([uint32]$offset)
    $offset += $img.Bytes.Length
}
foreach ($img in $pngs) { $bw.Write($img.Bytes) }

$bw.Dispose()
$fs.Dispose()
$src.Dispose()

Write-Host "Iconos generados:"
Get-ChildItem $outPath -File | ForEach-Object { "  {0} ({1:N0} bytes)" -f $_.Name, $_.Length }
Get-ChildItem $favPath -File -Filter "favicon*" | ForEach-Object { "  {0} ({1:N0} bytes)" -f $_.Name, $_.Length }

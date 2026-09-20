# Run on Windows after changing radar.ico. These are format/size conversions of
# the existing artwork; publishing uses the checked-in files on every platform.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $PSScriptRoot '../SlickWatch.App/Assets'
$icon = [System.Drawing.Icon]::new((Join-Path $assets 'radar.ico'))
$source = $icon.ToBitmap()
$container = [System.IO.MemoryStream]::new()
function Write-BigEndian([System.IO.Stream]$Stream, [int]$Value) {
    $bytes = [BitConverter]::GetBytes($Value)
    if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($bytes) }
    $Stream.Write($bytes, 0, 4)
}
try {
    $source.Save((Join-Path $assets 'radar.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    # Modern ICNS representations contain PNG data, preceded by type and length.
    foreach ($entry in @(@('icp6', 64), @('ic07', 128), @('ic08', 256), @('ic09', 512), @('ic10', 1024))) {
        $size = [int]$entry[1]
        $bitmap = [System.Drawing.Bitmap]::new($size, $size)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $png = [System.IO.MemoryStream]::new()
        try {
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($source, [System.Drawing.Rectangle]::new(0, 0, $size, $size))
            $bitmap.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
            if ($size -eq 256) { [System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'linux/SlickWatch.png'), $png.ToArray()) }
            $type = [System.Text.Encoding]::ASCII.GetBytes($entry[0])
            $container.Write($type, 0, 4)
            Write-BigEndian $container ($png.Length + 8)
            $png.Position = 0
            $png.CopyTo($container)
        }
        finally { $png.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
    $file = [System.IO.File]::Create((Join-Path $PSScriptRoot 'macos/SlickWatch.icns'))
    try {
        $file.Write([System.Text.Encoding]::ASCII.GetBytes('icns'), 0, 4)
        Write-BigEndian $file ($container.Length + 8)
        $container.Position = 0
        $container.CopyTo($file)
    }
    finally { $file.Dispose() }
}
finally { $container.Dispose(); $source.Dispose(); $icon.Dispose() }

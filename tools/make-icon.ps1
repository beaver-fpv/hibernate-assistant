$ErrorActionPreference = 'Stop'
$assetDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets'
Add-Type -AssemblyName WindowsBase,PresentationCore,PresentationFramework
function Color-FromHex([string]$hex) { return [Windows.Media.ColorConverter]::ConvertFromString($hex) }

$drawing = [Windows.Media.DrawingVisual]::new()
$canvas = $drawing.RenderOpen()
$blue = [Windows.Media.LinearGradientBrush]::new()
$blue.StartPoint = [Windows.Point]::new(0.15,0)
$blue.EndPoint = [Windows.Point]::new(0.8,1)
$blue.GradientStops.Add([Windows.Media.GradientStop]::new((Color-FromHex '#205BA4'),0))
$blue.GradientStops.Add([Windows.Media.GradientStop]::new((Color-FromHex '#163E7B'),0.48))
$blue.GradientStops.Add([Windows.Media.GradientStop]::new((Color-FromHex '#0C224A'),1))
$edge = [Windows.Media.Pen]::new([Windows.Media.SolidColorBrush]::new([Windows.Media.Color]::FromArgb(55,173,205,244)),1.5)
$canvas.DrawRoundedRectangle($blue,$edge,[Windows.Rect]::new(8,8,240,240),54,54)
$outer = [Windows.Media.EllipseGeometry]::new([Windows.Point]::new(122,131),69,69)
$cutout = [Windows.Media.EllipseGeometry]::new([Windows.Point]::new(151,105),62,62)
$moon = [Windows.Media.CombinedGeometry]::new([Windows.Media.GeometryCombineMode]::Exclude,$outer,$cutout)
$moonFill = [Windows.Media.LinearGradientBrush]::new((Color-FromHex '#FFFFFF'),(Color-FromHex '#E1F0FF'),90)
$canvas.DrawGeometry($moonFill,$null,$moon)
$star = [Windows.Media.Geometry]::Parse('M 186,53 C 188,65 191,68 203,70 C 191,72 188,75 186,87 C 184,75 181,72 169,70 C 181,68 184,65 186,53 Z')
$canvas.DrawGeometry([Windows.Media.Brushes]::White,$null,$star)
$canvas.Close()

$sizes = @(16,20,24,32,40,48,64,128,256)
$images = [Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    $context.PushTransform([Windows.Media.ScaleTransform]::new($size/256.0,$size/256.0))
    $context.DrawDrawing($drawing.Drawing)
    $context.Pop()
    $context.Close()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    $encoder.Save($stream)
    $images.Add($stream.ToArray())
    if ($size -eq 256) { [IO.File]::WriteAllBytes((Join-Path $assetDirectory 'icon-blue.png'),$stream.ToArray()) }
    if ($size -eq 48) { [IO.File]::WriteAllBytes((Join-Path $assetDirectory 'icon-blue-48.png'),$stream.ToArray()) }
    $stream.Dispose()
}

# Store full RGBA PNG images at each Windows icon size, preserving transparency.
$file = [IO.File]::Create((Join-Path $assetDirectory 'app.ico'))
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16*$sizes.Count
    for ($i=0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
} finally { $writer.Dispose(); $file.Dispose() }

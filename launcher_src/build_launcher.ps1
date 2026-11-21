# Builds ..\DLSSG_Launcher.exe from DLSSG_Launcher.cs, embedding ..\DLSSG_Launcher.ps1 and an icon.
# Run:  powershell -NoProfile -ExecutionPolicy Bypass -File launcher_src\build_launcher.ps1

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = Split-Path $here -Parent
Add-Type -AssemblyName System.Drawing

# --- icon: green rounded square with a white play triangle (PNG-compressed .ico, 16..256 px) ---
$ico = Join-Path $here 'launcher.ico'
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $bmp = New-Object Drawing.Bitmap $s, $s, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([Drawing.Color]::Transparent)
    $r = [math]::Max(2, [int]($s * 0.22))
    $m = [math]::Max(0, [int]($s * 0.04))
    $w = $s - 2 * $m
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc($m, $m, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($m + $w - 2 * $r, $m, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($m + $w - 2 * $r, $m + $w - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc($m, $m + $w - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $brush = New-Object Drawing.Drawing2D.LinearGradientBrush((New-Object Drawing.Point 0, 0), (New-Object Drawing.Point 0, $s),
        [Drawing.Color]::FromArgb(140, 205, 20), [Drawing.Color]::FromArgb(80, 150, 0))
    $g.FillPath($brush, $path)
    $tri = [Drawing.PointF[]]@(
        (New-Object Drawing.PointF ($s * 0.38), ($s * 0.27)),
        (New-Object Drawing.PointF ($s * 0.38), ($s * 0.73)),
        (New-Object Drawing.PointF ($s * 0.76), ($s * 0.50)))
    $g.FillPolygon([Drawing.Brushes]::White, $tri)
    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}
$out = New-Object IO.MemoryStream
$bw = New-Object IO.BinaryWriter $out
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = $pngs[$i]
    $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$d.Length); $bw.Write([uint32]$offset)
    $offset += $d.Length
}
foreach ($d in $pngs) { $bw.Write($d) }
$bw.Flush()
[IO.File]::WriteAllBytes($ico, $out.ToArray())

# --- compile ---
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'C# compiler (csc.exe, .NET Framework 4.x) not found.' }

$exe    = Join-Path $root 'DLSSG_Launcher.exe'
$script = Join-Path $root 'DLSSG_Launcher.ps1'
& $csc /nologo /target:winexe /optimize+ /platform:anycpu "/out:$exe" "/win32icon:$ico" `
    "/resource:$script,DLSSG_Launcher.ps1" /reference:System.Windows.Forms.dll (Join-Path $here 'DLSSG_Launcher.cs')
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }
Write-Output "Built $exe"

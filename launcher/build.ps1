# Builds DLSSG Launcher and its setup.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File launcher\build.ps1 [-PackageDir <release folder>]
#
# Output (launcher\dist\):
#   DLSSG_Launcher_Setup_<mod version>.exe     the installer to publish (launcher + mod files inside)
#   DLSSG_Launcher_Portable_<mod version>.zip  the same files, unzip-and-run
#   SHA256SUMS.txt
# Needs only what ships with Windows 10 / 11: the .NET Framework 4.x C# compiler.

param(
    [string]$PackageDir = (Split-Path $PSScriptRoot -Parent),   # the DLSSG release folder (has manifest.json)
    [string]$Version = '2.0.0'
)

$ErrorActionPreference = 'Stop'
$here  = $PSScriptRoot
$src   = Join-Path $here 'src'
$dist  = Join-Path $here 'dist'
$obj   = Join-Path $here 'obj'
$stage = Join-Path $obj 'stage'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression.FileSystem

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'C# compiler (csc.exe, .NET Framework 4.x) not found.' }

$manifest = Get-Content -LiteralPath (Join-Path $PackageDir 'manifest.json') -Raw | ConvertFrom-Json
$modVersion = $manifest.bundle.version
Write-Output "Mod package: $PackageDir  (version $modVersion)"

Remove-Item -Recurse -Force $obj -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $obj, $stage, $dist | Out-Null

# ---------------------------------------------------------------- icon (PNG-compressed .ico, 16..256 px)
$ico = Join-Path $obj 'app.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $bmp = New-Object Drawing.Bitmap $s, $s, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([Drawing.Color]::Transparent)
    $m = [math]::Max(0, [int]($s * 0.04)); $w = $s - 2 * $m; $r = [math]::Max(2, [int]($w * 0.24))
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc($m, $m, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($m + $w - 2 * $r, $m, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($m + $w - 2 * $r, $m + $w - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc($m, $m + $w - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $brush = New-Object Drawing.Drawing2D.LinearGradientBrush((New-Object Drawing.Point 0, 0), (New-Object Drawing.Point 0, $s),
        [Drawing.Color]::FromArgb(146, 210, 24), [Drawing.Color]::FromArgb(78, 146, 0))
    $g.FillPath($brush, $path)
    $tri = [Drawing.PointF[]]@(
        (New-Object Drawing.PointF ($s * 0.38), ($s * 0.27)),
        (New-Object Drawing.PointF ($s * 0.38), ($s * 0.73)),
        (New-Object Drawing.PointF ($s * 0.76), ($s * 0.50)))
    $g.FillPolygon([Drawing.Brushes]::White, $tri)
    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    , $ms.ToArray()
}
$out = New-Object IO.MemoryStream; $bw = New-Object IO.BinaryWriter $out
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = $pngs[$i]
    $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$d.Length); $bw.Write([uint32]$offset)
    $offset += $d.Length
}
foreach ($d in $pngs) { $bw.Write($d) }
$bw.Flush(); [IO.File]::WriteAllBytes($ico, $out.ToArray())

$common = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu', '/langversion:5', "/win32icon:$ico", "/win32manifest:$(Join-Path $src 'app.manifest')",
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:Microsoft.CSharp.dll')

function Invoke-Csc([string[]]$csArgs) {
    & $csc @csArgs
    if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }
}

# ---------------------------------------------------------------- launcher
$launcherExe = Join-Path $stage 'DLSSGLauncher.exe'
Invoke-Csc ($common + @("/out:$launcherExe", '/r:System.Web.Extensions.dll') +
    @(Get-ChildItem (Join-Path $src 'Shared\*.cs'), (Join-Path $src 'Launcher\*.cs') | ForEach-Object FullName))
Write-Output "Built  DLSSGLauncher.exe"

# ---------------------------------------------------------------- stage: launcher + mod\<exactly the release files>
$modDir = Join-Path $stage 'mod'
$total = 0
foreach ($f in $manifest.files) {
    $from = Join-Path $PackageDir ($f.path -replace '/', '\')
    if (-not (Test-Path -LiteralPath $from)) { throw "Release file missing: $($f.path)" }
    $hash = (Get-FileHash -LiteralPath $from -Algorithm SHA256).Hash.ToLower()
    if ($hash -ne $f.sha256) { throw "Release file does not match manifest.json: $($f.path)" }
    $to = Join-Path $modDir ($f.path -replace '/', '\')
    New-Item -ItemType Directory -Force (Split-Path $to) | Out-Null
    Copy-Item -LiteralPath $from -Destination $to
    $total += (Get-Item -LiteralPath $to).Length
}
Copy-Item -LiteralPath (Join-Path $PackageDir 'manifest.json') -Destination (Join-Path $modDir 'manifest.json')
Write-Output ("Staged {0} release files, verified against manifest.json ({1:N0} MB)" -f $manifest.files.Count, ($total / 1MB))

$payload = Join-Path $obj 'payload.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $payload, [IO.Compression.CompressionLevel]::Optimal, $false)
$stageBytes = (Get-ChildItem -Recurse -File $stage | Measure-Object Length -Sum).Sum
Write-Output ("Payload {0:N0} MB (unpacked {1:N0} MB)" -f ((Get-Item $payload).Length / 1MB), ($stageBytes / 1MB))

# ---------------------------------------------------------------- setup
$license = Join-Path $obj 'license.txt'
$lt = "This setup installs DLSSG Launcher $Version and the DLSSG SM86 frame generation mod $modVersion.`r`n" +
      "The mod's license and its third-party notices follow. NVIDIA files keep NVIDIA's own terms.`r`n`r`n" +
      "----- DLSSG SM86: LICENSE.md -----`r`n`r`n" +
      (Get-Content -LiteralPath (Join-Path $PackageDir 'LICENSE.md') -Raw) +
      "`r`n`r`n----- THIRD_PARTY_NOTICES.txt -----`r`n`r`n" +
      (Get-Content -LiteralPath (Join-Path $PackageDir 'THIRD_PARTY_NOTICES.txt') -Raw)
[IO.File]::WriteAllText($license, $lt, (New-Object Text.UTF8Encoding $false))
$info = Join-Path $obj 'setup_info.txt'
[IO.File]::WriteAllText($info, "version=$Version`r`nmod=$modVersion`r`nbytes=$stageBytes`r`n")

$setupExe = Join-Path $dist "DLSSG_Launcher_Setup_$modVersion.exe"
Invoke-Csc ($common + @("/out:$setupExe", '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll',
    "/resource:$payload,payload.zip", "/resource:$license,license.txt", "/resource:$info,setup_info.txt") +
    @(Get-ChildItem (Join-Path $src 'Shared\*.cs'), (Join-Path $src 'Setup\*.cs') | ForEach-Object FullName))
Write-Output "Built  $(Split-Path $setupExe -Leaf)"

$portable = Join-Path $dist "DLSSG_Launcher_Portable_$modVersion.zip"
Copy-Item -LiteralPath $payload -Destination $portable -Force

$sums = foreach ($f in $setupExe, $portable) { '{0}  {1}' -f (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash.ToLower(), (Split-Path $f -Leaf) }
Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Value $sums -Encoding ASCII
Get-ChildItem $dist | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize | Out-String | Write-Output

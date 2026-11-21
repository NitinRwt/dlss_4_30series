#requires -Version 5.1
# DLSSG SM86 Game Launcher
#
# Finds games (default: every sub-folder of C:\Games), installs this package's mod files next to the
# game's rendering EXE and launches the game:
#   <proxy>.dll (version.dll by default), dlssg_sm86.ini and the dlssg_sm86\ folder.
#
# Keep this file in the package root, beside version.dll / dlssg_sm86.ini / dlssg_sm86\.
# Start it with DLSSG_Launcher.cmd (double-click).
#
#   -ScanOnly          print what the game scan finds and exit (no window)
#   -Play "<game exe>" no window: check / update the mod next to that EXE, then start the game (desktop shortcuts use this)

param([switch]$ScanOnly, [string]$Play)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

# ---------------------------------------------------------------------------------------------------
# Package
# ---------------------------------------------------------------------------------------------------
# DLSSG_Launcher.exe passes its folder in DLSSG_PACKAGE_DIR (it may run an embedded copy of this script from %TEMP%)
$script:PackageDir   = if ($env:DLSSG_PACKAGE_DIR -and [IO.Directory]::Exists($env:DLSSG_PACKAGE_DIR)) { $env:DLSSG_PACKAGE_DIR.TrimEnd('\') } else { $PSScriptRoot }
$script:SettingsPath = Join-Path $PackageDir 'launcher_settings.json'
$script:ProxyNames   = @('version.dll', 'winmm.dll', 'dbghelp.dll', 'dinput8.dll', 'dxgi.dll', 'd3d12.dll')
$script:MarkerName   = 'launcher_install.json'     # written into <game>\dlssg_sm86\
$script:BackupSuffix = '.dlssg-backup'
$script:HashCache    = @{}

function Get-ProxySource([string]$name) {
    if ($name -eq 'version.dll') { Join-Path $PackageDir 'version.dll' }
    else { Join-Path $PackageDir ('alternatives\' + $name) }
}

$required = @('version.dll', 'dlssg_sm86.ini', 'dlssg_sm86\backend\sm86_backend.dll', 'dlssg_sm86\runtime\nvngx_dlssg.dll')
$missing  = @($required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $PackageDir $_)) })
if ($missing.Count) {
    $msg = "This launcher must sit in the DLSSG release folder (beside version.dll and dlssg_sm86\).`r`n`r`nMissing:`r`n  " + ($missing -join "`r`n  ")
    if ($ScanOnly) { Write-Error $msg } else { [void][Windows.Forms.MessageBox]::Show($msg, 'DLSSG Launcher', 'OK', 'Error') }
    exit 1
}

$script:PackageVersion = ''
try { $script:PackageVersion = (Get-Content -LiteralPath (Join-Path $PackageDir 'manifest.json') -Raw | ConvertFrom-Json).bundle.version } catch { }

function Get-Sha256([string]$path) {
    $fi  = New-Object IO.FileInfo $path
    $key = '{0}|{1}|{2}' -f $fi.FullName, $fi.Length, $fi.LastWriteTimeUtc.Ticks
    if (-not $script:HashCache.ContainsKey($key)) {
        $script:HashCache[$key] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
    $script:HashCache[$key]
}

# hash -> proxy name, for every proxy this package ships (identifies "our" DLLs in a game folder)
$script:PackageProxyHashes = @{}
foreach ($n in $ProxyNames) {
    $p = Get-ProxySource $n
    if (Test-Path -LiteralPath $p) { $script:PackageProxyHashes[(Get-Sha256 $p)] = $n }
}

# ---------------------------------------------------------------------------------------------------
# Settings (launcher_settings.json beside this script)
# ---------------------------------------------------------------------------------------------------
function Read-Settings {
    $s = @{ ScanRoots = @('C:\Games'); ExtraGames = @(); ExeChoice = @{}; Args = @{}; AckAntiCheat = @(); LastExe = '' }
    if (Test-Path -LiteralPath $SettingsPath) {
        try {
            $j = Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
            if ($j.ScanRoots)    { $s.ScanRoots    = @($j.ScanRoots    | ForEach-Object { [string]$_ }) }
            if ($j.ExtraGames)   { $s.ExtraGames   = @($j.ExtraGames   | ForEach-Object { [string]$_ }) }
            if ($j.AckAntiCheat) { $s.AckAntiCheat = @($j.AckAntiCheat | ForEach-Object { [string]$_ }) }
            if ($j.LastExe)      { $s.LastExe      = [string]$j.LastExe }
            foreach ($k in 'ExeChoice', 'Args') {
                if ($j.$k) { foreach ($p in $j.$k.PSObject.Properties) { $s[$k][$p.Name] = [string]$p.Value } }
            }
        } catch { }
    }
    $s
}

function Save-Settings {
    try { $script:Settings | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $SettingsPath -Encoding UTF8 } catch { }
}

$script:Settings = Read-Settings

# ---------------------------------------------------------------------------------------------------
# Game / EXE detection
# ---------------------------------------------------------------------------------------------------
# EXEs that are never the rendering EXE (crash handlers, installers, launchers, anti-cheat, ...)
$script:ExcludeExeRegex = '(?i)(crash|handler|report|unins|install|setup|redist|dxweb|dxsetup|bootstrap|downloader|broker|service|cleaner|attestation|logsgenerator|updater|patcher|anticheat|battleye|beservice|eac_|cefprocess|cefsubprocess|webhelper|quicksfv|prereq|dotnet|helper|sender|cleanup|activation|launcher|configurator|settings|uploader|touchup|register|elevate|7z|unitycrash)'
# Folders that never hold the rendering EXE (skipped while searching, also keeps the scan fast)
$script:ExcludeDirRegex = '(?i)^(_?redist|_?commonredist|redistributables?|__installer|_?installers?|directx|dotnet|vcredist|support|easyanticheat.*|battleye|prereqs?|_cleanup|crashreport.*|thirdparty|engine|logs?|saves?|savegames?|screenshots|movies|videos?|shadercache|cache|\$recycle\.bin|_retail_cache|mods?)$'
# Generic folder names skipped when naming a game after its folder
$script:GenericDirRegex = '(?i)^(win64|win32|x64|x86|bin|bin64|binaries|game|retail|shipping|release|app|build)$'

function Find-Exes([string]$root, [int]$maxDepth = 5) { Find-Files $root '*.exe' $maxDepth }

function Find-Files([string]$root, [string]$pattern, [int]$maxDepth = 5) {
    $out   = New-Object System.Collections.Generic.List[System.IO.FileInfo]
    $stack = New-Object System.Collections.Generic.Stack[object]
    $stack.Push([Tuple]::Create($root, 0))
    while ($stack.Count -gt 0) {
        $item = $stack.Pop()
        try { foreach ($f in [IO.Directory]::EnumerateFiles($item.Item1, $pattern)) { $out.Add((New-Object IO.FileInfo $f)) } } catch { }
        if ($item.Item2 -lt $maxDepth) {
            try {
                foreach ($sub in [IO.Directory]::EnumerateDirectories($item.Item1)) {
                    if ([IO.Path]::GetFileName($sub) -notmatch $ExcludeDirRegex) { $stack.Push([Tuple]::Create($sub, $item.Item2 + 1)) }
                }
            } catch { }
        }
    }
    , $out
}

function Get-ExeScore([IO.FileInfo]$f) {
    $s = [math]::Min($f.Length / 1MB, 900)                                     # big EXE = likely the game
    if ($f.Name -match '(?i)-(Win64|WinGDK)-Shipping\.exe$') { $s += 1000 }   # Unreal Engine game binary
    if ($f.FullName -match '(?i)\\(Binaries\\Win64|bin\\x64|bin64|x64)\\') { $s += 100 }
    $s
}

# All plausible rendering EXEs in a game folder, best guess first
function Get-RankedExes([string]$folder) {
    $all  = @((Find-Exes $folder).ToArray())
    $good = @($all | Where-Object { $_.Name -notmatch $ExcludeExeRegex })
    if (-not $good.Count) { $good = $all }
    @($good | Sort-Object -Property @{ Expression = { Get-ExeScore $_ }; Descending = $true } | ForEach-Object { $_.FullName })
}

function Get-DisplayName([string]$path) {
    $dir = if ([IO.File]::Exists($path)) { [IO.Path]::GetDirectoryName($path) } else { $path }
    $d = $dir
    while ($d -and ([IO.Path]::GetFileName($d) -match $GenericDirRegex)) { $d = [IO.Path]::GetDirectoryName($d) }
    if (-not $d) { $d = $dir }
    $leaf = [IO.Path]::GetFileName($d)
    if ($leaf) { $leaf } else { $d }
}

function New-Game([string]$folder, [string]$name) {
    $cands  = @(Get-RankedExes $folder)
    $chosen = $Settings.ExeChoice[$folder]
    if ($chosen -and [IO.File]::Exists($chosen)) {
        if ($cands -notcontains $chosen) { $cands = @($chosen) + $cands }
    } else {
        if (-not $cands.Count) { return $null }
        $chosen = $cands[0]
    }
    [pscustomobject]@{ Name = $name; Folder = $folder; Candidates = $cands; Exe = $chosen }
}

function Test-PathUnder([string]$path, [string]$folder) {
    $f = $folder.TrimEnd('\') + '\'
    $path.StartsWith($f, [StringComparison]::OrdinalIgnoreCase) -or $path.Equals($folder.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)
}

function Get-AllGames([scriptblock]$progress) {
    $games = New-Object System.Collections.ArrayList
    $seen  = @{}
    foreach ($root in @($Settings.ScanRoots)) {
        if (-not [IO.Directory]::Exists($root)) { continue }
        foreach ($sub in [IO.Directory]::GetDirectories($root)) {
            if ([IO.Path]::GetFileName($sub) -match '^\$') { continue }
            if ($progress) { & $progress $sub }
            $g = New-Game $sub ([IO.Path]::GetFileName($sub))
            if ($g -and -not $seen.ContainsKey($g.Folder)) { $seen[$g.Folder] = $true; [void]$games.Add($g) }
        }
    }
    foreach ($f in @($Settings.ExtraGames)) {
        if ($seen.ContainsKey($f) -or -not [IO.Directory]::Exists($f)) { continue }
        if (@($games | Where-Object { Test-PathUnder $f $_.Folder }).Count) { continue }   # already inside a scanned game
        if ($progress) { & $progress $f }
        $exe = $Settings.ExeChoice[$f]
        $g = New-Game $f (Get-DisplayName $(if ($exe) { $exe } else { $f }))
        if ($g) { $seen[$f] = $true; [void]$games.Add($g) }
    }
    , $games
}

# ---------------------------------------------------------------------------------------------------
# Mod state / anti-cheat
# ---------------------------------------------------------------------------------------------------
function Get-ModState([string]$dir) {
    $ours = @(); $foreign = @()
    foreach ($n in $ProxyNames) {
        $p = Join-Path $dir $n
        if ([IO.File]::Exists($p)) {
            if ($PackageProxyHashes.ContainsKey((Get-Sha256 $p))) { $ours += $n } else { $foreign += $n }
        }
    }
    [pscustomobject]@{
        Ours      = $ours
        Foreign   = $foreign
        HasFolder = [IO.File]::Exists((Join-Path $dir 'dlssg_sm86\backend\sm86_backend.dll'))
        HasIni    = [IO.File]::Exists((Join-Path $dir 'dlssg_sm86.ini'))
    }
}

function Format-ModState($st) {
    if ($st.Ours.Count -and $st.HasFolder -and $st.HasIni) { 'Installed (' + ($st.Ours -join ', ') + ')' }
    elseif ($st.Ours.Count -or $st.HasFolder -or $st.HasIni) { 'Partial' }
    else { 'Not installed' }
}

$script:AntiCheatMarkers = @('EasyAntiCheat', 'EasyAntiCheat_EOS', 'BattlEye', 'BEService.exe', 'BEService_x64.exe',
    'start_protected_game.exe', 'EasyAntiCheat_EOS_Setup.exe', 'EAAntiCheat.GameServiceLauncher.exe', 'randgrid.sys',
    'CODSecureAttestationWizard.exe', 'vgc.exe', 'ACE', 'xigncode', 'nProtect', 'GameGuard', 'mhyprot2.sys')
$script:AntiCheatNameRegex = '(?i)(call of duty|warzone|valorant|fortnite|apex legends|pubg|rainbow six|siege|battlefield|destiny 2|the finals|marvel rivals|counter-strike|overwatch|escape from tarkov|dayz|naraka|delta force|xdefiant|black ops|modern warfare|league of legends|genshin|honkai|zenless)'

function Get-AntiCheatReason([string]$exe, [string]$gameFolder) {
    if ("$gameFolder\$exe" -match $AntiCheatNameRegex) { return "the game looks like '" + $Matches[1] + "', which uses online anti-cheat" }
    $dirs = New-Object System.Collections.Generic.List[string]
    $d = [IO.Path]::GetDirectoryName($exe)
    while ($d) {
        $dirs.Add($d)
        if (-not $gameFolder -or -not (Test-PathUnder $d $gameFolder) -or $d.TrimEnd('\') -ieq $gameFolder.TrimEnd('\')) { break }
        $d = [IO.Path]::GetDirectoryName($d)
    }
    foreach ($dir in $dirs) {
        foreach ($m in $AntiCheatMarkers) {
            $p = Join-Path $dir $m
            if ([IO.File]::Exists($p) -or [IO.Directory]::Exists($p)) { return "found '$m' in $dir" }
        }
    }
    $null
}

if ($ScanOnly) {
    Write-Output ("Scan folders: " + ($Settings.ScanRoots -join '; '))
    foreach ($g in (Get-AllGames $null)) {
        $st = Get-ModState ([IO.Path]::GetDirectoryName($g.Exe))
        Write-Output ''
        Write-Output ("Game   : " + $g.Name)
        Write-Output ("Folder : " + $g.Folder)
        Write-Output ("EXE    : " + $g.Exe)
        Write-Output ("Others : " + (($g.Candidates | Where-Object { $_ -ine $g.Exe } | ForEach-Object { [IO.Path]::GetFileName($_) }) -join ', '))
        Write-Output ("Mod    : " + (Format-ModState $st))
        $ac = Get-AntiCheatReason $g.Exe $g.Folder
        if ($ac) { Write-Output ("WARNING: anti-cheat - " + $ac) }
    }
    exit 0
}

# ---------------------------------------------------------------------------------------------------
# UI
# ---------------------------------------------------------------------------------------------------
$font     = New-Object Drawing.Font('Segoe UI', 9)
$fontBold = New-Object Drawing.Font('Segoe UI', 10, [Drawing.FontStyle]::Bold)

$form = New-Object Windows.Forms.Form
$form.Text          = 'DLSSG SM86 Launcher' + $(if ($PackageVersion) { " $PackageVersion" } else { '' })
$form.ClientSize    = New-Object Drawing.Size(860, 640)
$form.MinimumSize   = New-Object Drawing.Size(760, 600)
$form.StartPosition = 'CenterScreen'
$form.Font          = $font

function New-Ctl([string]$type, [int]$x, [int]$y, [int]$w, [int]$h, [string]$text = '', [string]$anchor = 'Top,Left') {
    $c = New-Object ("Windows.Forms." + $type)
    $c.Location = New-Object Drawing.Point($x, $y)
    $c.Size     = New-Object Drawing.Size($w, $h)
    if ($text) { $c.Text = $text }
    $c.Anchor = $anchor
    $form.Controls.Add($c)
    $c
}

$lblRoots   = New-Ctl Label 12 14 600 20 '' 'Top,Left,Right'
$btnRescan  = New-Ctl Button 618 9 100 28 'Rescan' 'Top,Right'
$btnAddRoot = New-Ctl Button 724 9 124 28 'Add scan folder...' 'Top,Right'

$lv = New-Ctl ListView 12 42 836 230 '' 'Top,Left,Right'
$lv.View = 'Details'; $lv.FullRowSelect = $true; $lv.MultiSelect = $false; $lv.HideSelection = $false; $lv.GridLines = $true
[void]$lv.Columns.Add('Game', 210)
[void]$lv.Columns.Add('Executable', 460)
[void]$lv.Columns.Add('DLSSG mod', 145)

$null       = New-Ctl Label 12 288 72 20 'Game EXE:'
$cmbExe     = New-Ctl ComboBox 88 284 760 24 '' 'Top,Left,Right'
$cmbExe.DropDownStyle = 'DropDownList'

$btnBrowse  = New-Ctl Button 88 316 120 28 'Browse EXE...'
$btnFolder  = New-Ctl Button 214 316 150 28 'Open Game Folder...'
$btnExplore = New-Ctl Button 370 316 130 28 'Show in Explorer'
$btnIni     = New-Ctl Button 506 316 112 28 'DLSS Settings...'
$btnLog     = New-Ctl Button 624 316 84 28 'Open log'
$btnShort   = New-Ctl Button 714 316 134 28 'Desktop shortcut'

$null       = New-Ctl Label 12 360 72 20 'Proxy DLL:'
$cmbProxy   = New-Ctl ComboBox 88 356 140 24
$cmbProxy.DropDownStyle = 'DropDownList'
foreach ($n in $ProxyNames) { [void]$cmbProxy.Items.Add($n) }
$cmbProxy.SelectedIndex = 0
$null       = New-Ctl Label 244 360 84 20 'Launch args:'
$txtArgs    = New-Ctl TextBox 330 356 518 24 '' 'Top,Left,Right'

$chkIni     = New-Ctl CheckBox 88 386 560 22 "Overwrite the game's dlssg_sm86.ini with this package's copy (keeps your edits when off)"

$btnPlay    = New-Ctl Button 88 416 260 46 ([string][char]0x25B6 + '  Play with DLSS mod')
$btnPlay.Font = $fontBold
$btnPlay.BackColor = [Drawing.Color]::FromArgb(118, 185, 0); $btnPlay.ForeColor = [Drawing.Color]::White
$btnPlay.FlatStyle = 'Flat'; $btnPlay.FlatAppearance.BorderSize = 0
$btnInstall = New-Ctl Button 358 416 130 46 'Install only'
$btnRemove  = New-Ctl Button 498 416 130 46 'Remove mod'
$btnPlain   = New-Ctl Button 638 416 130 46 'Play without mod'

$txtLog = New-Ctl TextBox 12 474 836 140 '' 'Top,Bottom,Left,Right'
$txtLog.Multiline = $true; $txtLog.ReadOnly = $true; $txtLog.ScrollBars = 'Vertical'
$txtLog.Font = New-Object Drawing.Font('Consolas', 9)
$txtLog.BackColor = [Drawing.Color]::White

$status = New-Object Windows.Forms.StatusStrip
$statusLabel = New-Object Windows.Forms.ToolStripStatusLabel
$statusLabel.Spring = $true; $statusLabel.TextAlign = 'MiddleLeft'
[void]$status.Items.Add($statusLabel)
$form.Controls.Add($status)

$menu       = New-Object Windows.Forms.ContextMenuStrip
$miPlay     = $menu.Items.Add('Play with DLSS mod')
$miExplore  = $menu.Items.Add('Show in Explorer')
$miForget   = $menu.Items.Add('Remove from list')
$lv.ContextMenuStrip = $menu

$tips = New-Object Windows.Forms.ToolTip
$tips.SetToolTip($btnFolder, 'Pick the game''s install folder - the game EXE inside it is found automatically')
$tips.SetToolTip($btnBrowse, 'Pick the game EXE yourself')
$tips.SetToolTip($cmbProxy,  'version.dll works for nearly every game. If the game does not load it, try the next one (one at a time).')
$tips.SetToolTip($btnPlay,   'Copies the mod next to the game EXE (only changed files) and starts the game')
$tips.SetToolTip($btnRemove, 'Deletes this package''s proxy DLL, dlssg_sm86.ini and dlssg_sm86\ from the game folder')
$tips.SetToolTip($btnIni,    'Change frame generation (2X-6X), engine, preset, super resolution ... in this game''s dlssg_sm86.ini')
$tips.SetToolTip($btnShort,  'Creates a desktop shortcut that checks the mod and starts this game with it')

$script:Games    = New-Object System.Collections.ArrayList
$script:Current  = $null
$script:Updating = $false
$actionButtons   = @($btnPlay, $btnInstall, $btnRemove, $btnPlain, $btnRescan, $btnAddRoot, $btnBrowse, $btnFolder)

$script:PlayLog      = $null    # set in -Play mode: the log also goes to launcher_play.log
$script:RelaunchArgs = ''       # arguments for a "restart as administrator"

function Write-Log([string]$msg) {
    $line = '[{0:HH:mm:ss}] {1}' -f (Get-Date), $msg
    $txtLog.AppendText($line + "`r`n")
    if ($script:PlayLog) { try { Add-Content -LiteralPath $script:PlayLog -Value $line -Encoding UTF8 } catch { } }
}
function Set-Status([string]$msg) { $statusLabel.Text = $msg; [Windows.Forms.Application]::DoEvents() }

function Show-Msg([string]$text, [string]$buttons = 'OK', [string]$icon = 'Information', [string]$default = 'Button1') {
    $owner = if ($script:ActiveDialog -and $script:ActiveDialog.Visible) { $script:ActiveDialog } elseif ($form.Visible) { $form } else { $null }
    if ($owner) { [Windows.Forms.MessageBox]::Show($owner, $text, 'DLSSG Launcher', $buttons, $icon, $default) }
    else { [Windows.Forms.MessageBox]::Show($text, 'DLSSG Launcher', $buttons, $icon, $default, [Windows.Forms.MessageBoxOptions]::DefaultDesktopOnly) }
}

function Update-RootsLabel { $lblRoots.Text = 'Games found in:  ' + (@($Settings.ScanRoots) -join ';  ') + '   (double-click a game to play)' }

function Update-Item([Windows.Forms.ListViewItem]$it) {
    $g = $it.Tag
    $it.SubItems[1].Text = $g.Exe
    $it.SubItems[2].Text = Format-ModState (Get-ModState ([IO.Path]::GetDirectoryName($g.Exe)))
}

function Add-GameItem($g) {
    $it = New-Object Windows.Forms.ListViewItem($g.Name)
    [void]$it.SubItems.Add('')
    [void]$it.SubItems.Add('')
    $it.Tag = $g
    Update-Item $it
    [void]$lv.Items.Add($it)
    $it
}

function Select-GameItem($g) {
    foreach ($it in $lv.Items) {
        if ($it.Tag -eq $g) { $it.Selected = $true; $it.Focused = $true; $it.EnsureVisible(); return }
    }
}

function Get-CurrentItem { if ($lv.SelectedItems.Count) { $lv.SelectedItems[0] } else { $null } }

function Show-Game($g) {
    $script:Updating = $true
    try {
        $script:Current = $g
        $cmbExe.Items.Clear()
        if ($g) {
            foreach ($c in $g.Candidates) { [void]$cmbExe.Items.Add($c) }
            $cmbExe.SelectedItem = $g.Exe
            Show-ExeDetails
        } else {
            $txtArgs.Text = ''
        }
    } finally { $script:Updating = $false }
}

function Show-ExeDetails {
    $g = $script:Current
    if (-not $g) { return }
    $txtArgs.Text = [string]$Settings.Args[$g.Exe]
    $st = Get-ModState ([IO.Path]::GetDirectoryName($g.Exe))
    if ($st.Ours.Count) { $cmbProxy.SelectedItem = $st.Ours[0] } else { $cmbProxy.SelectedItem = 'version.dll' }
    Set-Status ($g.Exe + '   -   ' + (Format-ModState $st))
}

function Update-GameList {
    $lv.BeginUpdate()
    try {
        $lv.Items.Clear()
        $script:Games = Get-AllGames { param($f) Set-Status ("Scanning " + $f + " ...") }
        foreach ($g in $Games) { [void](Add-GameItem $g) }
    } finally { $lv.EndUpdate() }
    $roots = @($Settings.ScanRoots | Where-Object { -not [IO.Directory]::Exists($_) })
    foreach ($r in $roots) { Write-Log "Scan folder not found: $r" }
    Write-Log ("Found {0} game(s)." -f $Games.Count)
    Set-Status ("Found {0} game(s)." -f $Games.Count)
}

function Add-ExtraGame([string]$folder) {
    if (@($Settings.ExtraGames) -notcontains $folder) { $Settings.ExtraGames = @($Settings.ExtraGames) + $folder }
}

function Get-GameForPath([string]$path) {
    foreach ($g in $Games) { if (Test-PathUnder $path $g.Folder) { return $g } }
    $null
}

function Set-GameExe($g, [string]$exe) {
    if ($g.Candidates -notcontains $exe) { $g.Candidates = @($exe) + @($g.Candidates) }
    $g.Exe = $exe
    $Settings.ExeChoice[$g.Folder] = $exe
    $Settings.LastExe = $exe
    Save-Settings
}

function Test-Writable([string]$dir) {
    try {
        $t = Join-Path $dir ('.dlssg_write_test_' + [guid]::NewGuid().ToString('N'))
        [IO.File]::WriteAllText($t, '')
        [IO.File]::Delete($t)
        $true
    } catch { $false }
}

function Test-IsAdmin {
    ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Restart-Elevated {
    Save-Settings
    if ($env:DLSSG_LAUNCHER_EXE -and [IO.File]::Exists($env:DLSSG_LAUNCHER_EXE)) {
        if ($RelaunchArgs) { Start-Process -FilePath $env:DLSSG_LAUNCHER_EXE -Verb RunAs -ArgumentList $RelaunchArgs }
        else { Start-Process -FilePath $env:DLSSG_LAUNCHER_EXE -Verb RunAs }
    } else {
        $argList = '-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File "' + $PSCommandPath + '" ' + $RelaunchArgs
        Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $argList
    }
    $form.Close()
}

function Test-GameRunning([string]$exe) {
    @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and ($_.Path -ieq $exe) } catch { $false }
    }).Count -gt 0
}

# Returns $true when the folder is writable; offers to restart as administrator otherwise.
function Confirm-Writable([string]$dir) {
    if (Test-Writable $dir) { return $true }
    if (Test-IsAdmin) {
        [void](Show-Msg "Cannot write to:`r`n$dir`r`n`r`nCheck that the folder is not read-only and the game is closed." 'OK' 'Error')
        return $false
    }
    $r = Show-Msg "Windows does not allow writing to:`r`n$dir`r`n`r`nRestart the launcher as administrator?" 'YesNo' 'Warning'
    if ($r -eq 'Yes') { Restart-Elevated }
    $false
}

function Confirm-AntiCheat($g) {
    $reason = Get-AntiCheatReason $g.Exe $g.Folder
    if (-not $reason) { return $true }
    if (@($Settings.AckAntiCheat) -contains $g.Exe) { Write-Log "Anti-cheat warning already acknowledged for this game ($reason)."; return $true }
    $r = Show-Msg ("ANTI-CHEAT WARNING`r`n`r`nThis game seems to be protected by anti-cheat:`r`n  $reason`r`n`r`n" +
        "Anti-cheat software treats proxy DLLs such as version.dll as cheating. Using the mod can get your account BANNED, " +
        "or the game may simply refuse to start. Only use it for offline / single-player games without anti-cheat.`r`n`r`n" +
        "Install the mod into this game anyway?") 'YesNo' 'Warning' 'Button2'
    if ($r -ne 'Yes') { Write-Log 'Cancelled (anti-cheat warning).'; return $false }
    $Settings.AckAntiCheat = @($Settings.AckAntiCheat) + $g.Exe
    Save-Settings
    $true
}

# Copies changed files only (size / timestamp), skips logs\
function Sync-ModFolder([string]$src, [string]$dst) {
    $copied = 0
    $srcRoot = (New-Object IO.DirectoryInfo $src).FullName.TrimEnd('\')
    foreach ($f in (Get-ChildItem -LiteralPath $src -Recurse -File)) {
        $rel = $f.FullName.Substring($srcRoot.Length + 1)
        if ($rel -match '(?i)^logs\\' -or $f.Name -ieq $MarkerName) { continue }
        $t  = Join-Path $dst $rel
        $ti = New-Object IO.FileInfo $t
        if (-not $ti.Exists -or $ti.Length -ne $f.Length -or $ti.LastWriteTimeUtc -ne $f.LastWriteTimeUtc) {
            Set-Status ("Copying " + $rel + " ...")
            [void](New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($t)))
            Copy-Item -LiteralPath $f.FullName -Destination $t -Force
            $copied++
        }
    }
    [void](New-Item -ItemType Directory -Force -Path (Join-Path $dst 'logs'))
    $copied
}

function Install-Mod($g, [string]$proxy) {
    $dir = [IO.Path]::GetDirectoryName($g.Exe)
    if ($dir.TrimEnd('\') -ieq $PackageDir.TrimEnd('\')) { throw 'The game EXE is inside the mod package folder - pick the real game.' }
    if (-not [IO.File]::Exists($g.Exe)) { throw "EXE not found: $($g.Exe)" }
    if (-not (Confirm-Writable $dir)) { return $false }

    $st = Get-ModState $dir
    $upToDate = ($st.Ours -contains $proxy) -and ($st.Ours.Count -eq 1)
    if ((Test-GameRunning $g.Exe) -and -not $upToDate) {
        [void](Show-Msg 'The game is running. Close it first, then try again.' 'OK' 'Warning')
        return $false
    }

    Write-Log ("Installing into " + $dir)
    $backups = @()

    # A foreign file with the chosen proxy name (ReShade, another mod, an older DLSSG release): ask first, back it up
    $pd = Join-Path $dir $proxy
    $srcProxy = Get-ProxySource $proxy
    if ([IO.File]::Exists($pd) -and -not $PackageProxyHashes.ContainsKey((Get-Sha256 $pd))) {
        $r = Show-Msg ("The game folder already has a different $proxy (another mod, ReShade, or an older DLSSG release):`r`n$pd`r`n`r`n" +
            "Rename it to $proxy$BackupSuffix and install the mod's $proxy instead?`r`n(Remove mod puts it back.)") 'YesNo' 'Question'
        if ($r -ne 'Yes') { Write-Log '  cancelled.'; return $false }
        $bak = $pd + $BackupSuffix
        if ([IO.File]::Exists($bak)) { $bak = $pd + '.' + (Get-Date -Format 'yyyyMMddHHmmss') + $BackupSuffix }
        Move-Item -LiteralPath $pd -Destination $bak
        $backups += [IO.Path]::GetFileName($bak)
        Write-Log ("  backed up existing $proxy -> " + [IO.Path]::GetFileName($bak))
    }

    # One proxy at a time: drop this package's other proxies
    foreach ($n in $ProxyNames) {
        if ($n -eq $proxy) { continue }
        $p = Join-Path $dir $n
        if (-not [IO.File]::Exists($p)) { continue }
        if ($PackageProxyHashes.ContainsKey((Get-Sha256 $p))) {
            Remove-Item -LiteralPath $p -Force
            Write-Log "  removed $n (only one proxy may be active)"
        } else {
            Write-Log "  note: $n in the game folder is not from this package. If it is from an older DLSSG release, delete it (it may load first)."
        }
    }

    # proxy DLL (re-copied when it differs from this package's file)
    if ([IO.File]::Exists($pd) -and (Get-Sha256 $pd) -eq (Get-Sha256 $srcProxy)) { Write-Log "  $proxy up to date" }
    else { Copy-Item -LiteralPath $srcProxy -Destination $pd -Force; Write-Log "  copied $proxy" }

    # dlssg_sm86\
    $n = Sync-ModFolder (Join-Path $PackageDir 'dlssg_sm86') (Join-Path $dir 'dlssg_sm86')
    Write-Log $(if ($n) { "  copied $n file(s) into dlssg_sm86\" } else { '  dlssg_sm86\ up to date' })

    # dlssg_sm86.ini
    $ini = Join-Path $dir 'dlssg_sm86.ini'
    if (-not [IO.File]::Exists($ini) -or $chkIni.Checked) {
        Copy-Item -LiteralPath (Join-Path $PackageDir 'dlssg_sm86.ini') -Destination $ini -Force
        Write-Log '  copied dlssg_sm86.ini'
    } else { Write-Log '  kept existing dlssg_sm86.ini' }

    # marker (what the launcher did, for Remove mod)
    $markerPath = Join-Path $dir ('dlssg_sm86\' + $MarkerName)
    $old = $null
    try { if ([IO.File]::Exists($markerPath)) { $old = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json } } catch { }
    if ($old -and $old.backups) { $backups = @($old.backups) + $backups }
    [pscustomobject]@{ proxy = $proxy; package_version = $PackageVersion; package_dir = $PackageDir; installed_at = (Get-Date).ToString('s'); backups = @($backups) } |
        ConvertTo-Json | Set-Content -LiteralPath $markerPath -Encoding UTF8

    Write-Log ("Mod installed (" + $proxy + ").")
    $true
}

function Uninstall-Mod($g) {
    $dir = [IO.Path]::GetDirectoryName($g.Exe)
    if ($dir.TrimEnd('\') -ieq $PackageDir.TrimEnd('\')) { throw 'That is the mod package folder itself.' }
    $st = Get-ModState $dir
    $modDir = Join-Path $dir 'dlssg_sm86'
    $backupFiles = @(Get-ChildItem -LiteralPath $dir -Filter ('*' + $BackupSuffix) -File -ErrorAction SilentlyContinue)
    if (-not ($st.Ours.Count -or [IO.Directory]::Exists($modDir) -or $st.HasIni -or $backupFiles.Count)) {
        Write-Log "Nothing to remove in $dir"
        return
    }
    if (Test-GameRunning $g.Exe) { [void](Show-Msg 'The game is running. Close it first.' 'OK' 'Warning'); return }
    $r = Show-Msg ("Remove the DLSSG mod from:`r`n$dir`r`n`r`nDeletes: " + ((@($st.Ours) + 'dlssg_sm86.ini' + 'dlssg_sm86\ (including its logs)') -join ', ') +
        $(if ($backupFiles.Count) { "`r`nRestores: " + (($backupFiles | ForEach-Object { $_.Name }) -join ', ') } else { '' })) 'YesNo' 'Question'
    if ($r -ne 'Yes') { return }
    if (-not (Confirm-Writable $dir)) { return }

    foreach ($n in $st.Ours) { Remove-Item -LiteralPath (Join-Path $dir $n) -Force; Write-Log "  removed $n" }
    foreach ($b in $backupFiles) {
        $origName = $b.Name.Substring(0, $b.Name.Length - $BackupSuffix.Length) -replace '\.\d{14}$', ''
        $orig = Join-Path $dir $origName
        if (-not [IO.File]::Exists($orig)) { Move-Item -LiteralPath $b.FullName -Destination $orig; Write-Log "  restored $origName" }
    }
    if ([IO.Directory]::Exists($modDir)) {
        $looksOurs = [IO.Directory]::Exists((Join-Path $modDir 'backend')) -or [IO.Directory]::Exists((Join-Path $modDir 'runtime')) -or [IO.File]::Exists((Join-Path $modDir $MarkerName))
        if ($looksOurs) { Remove-Item -LiteralPath $modDir -Recurse -Force; Write-Log '  removed dlssg_sm86\' }
        else { Write-Log '  left dlssg_sm86\ alone (does not look like this mod)' }
    }
    $ini = Join-Path $dir 'dlssg_sm86.ini'
    if ([IO.File]::Exists($ini)) { Remove-Item -LiteralPath $ini -Force; Write-Log '  removed dlssg_sm86.ini' }
    Write-Log 'Mod removed.'
}

function Start-Game($g) {
    $p = @{ FilePath = $g.Exe; WorkingDirectory = [IO.Path]::GetDirectoryName($g.Exe) }
    $a = $txtArgs.Text.Trim()
    if ($a) { $p.ArgumentList = $a }
    Start-Process @p
    Write-Log ("Started " + [IO.Path]::GetFileName($g.Exe) + $(if ($a) { " $a" } else { '' }))
    Set-Status ("Started " + $g.Name)
}

function Invoke-Action([scriptblock]$action) {
    foreach ($b in $actionButtons) { $b.Enabled = $false }
    $form.Cursor = [Windows.Forms.Cursors]::WaitCursor
    try { & $action }
    catch {
        $e = $_.Exception
        Write-Log ("ERROR: " + $e.Message)
        if ($e -is [UnauthorizedAccessException] -and -not (Test-IsAdmin)) {
            if ((Show-Msg ($e.Message + "`r`n`r`nRestart the launcher as administrator?") 'YesNo' 'Error') -eq 'Yes') { Restart-Elevated }
        } else {
            [void](Show-Msg $e.Message 'OK' 'Error')
        }
    }
    finally {
        $form.Cursor = [Windows.Forms.Cursors]::Default
        foreach ($b in $actionButtons) { $b.Enabled = $true }
        $it = Get-CurrentItem
        if ($it) { Update-Item $it; Show-ExeDetails }
    }
}

function Get-SelectedGame {
    if (-not $script:Current) { [void](Show-Msg 'Select a game in the list, or use "Open Game Folder..." / "Browse EXE...".'); return $null }
    $Settings.Args[$Current.Exe] = $txtArgs.Text.Trim()
    $Settings.LastExe = $Current.Exe
    Save-Settings
    $script:Current
}

function Invoke-Play {
    Invoke-Action {
        $g = Get-SelectedGame
        if (-not $g) { return }
        if (-not (Confirm-AntiCheat $g)) { return }
        if (Install-Mod $g ([string]$cmbProxy.SelectedItem)) { Start-Game $g }
    }
}

# ---------------------------------------------------------------------------------------------------
# dlssg_sm86.ini editing (values are changed in place: comments, order and encoding are kept)
# ---------------------------------------------------------------------------------------------------
function Read-IniFile([string]$path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $skip = if ($bom) { 3 } else { 0 }
    $text = (New-Object Text.UTF8Encoding $false).GetString($bytes, $skip, $bytes.Length - $skip)
    $nl = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($l in ($text -split "\r?\n")) { $lines.Add($l) }
    [pscustomobject]@{ Path = $path; Bom = $bom; NewLine = $nl; Lines = $lines }
}

function Write-IniFile($ini) {
    [IO.File]::WriteAllText($ini.Path, ($ini.Lines -join $ini.NewLine), (New-Object Text.UTF8Encoding $ini.Bom))
}

# index of the KEY= line inside [section], or -1; also returns the section's header index
function Find-IniKey($ini, [string]$section, [string]$key) {
    $cur = ''; $header = -1
    for ($i = 0; $i -lt $ini.Lines.Count; $i++) {
        $l = $ini.Lines[$i]
        if ($l -match '^\s*\[([^\]]+)\]') { $cur = $Matches[1].Trim(); if ($cur -ieq $section) { $header = $i }; continue }
        if ($cur -ieq $section -and $l -notmatch '^\s*[;#]' -and $l -match ('^\s*' + [regex]::Escape($key) + '\s*=')) { return @($i, $header) }
    }
    @(-1, $header)
}

function Get-IniValue($ini, [string]$section, [string]$key) {
    $r = Find-IniKey $ini $section $key
    if ($r[0] -lt 0) { return $null }
    ($ini.Lines[$r[0]] -replace '^[^=]*=', '').Trim()
}

function Set-IniValue($ini, [string]$section, [string]$key, [string]$value) {
    $r = Find-IniKey $ini $section $key
    if ($r[0] -ge 0) {
        $name = ($ini.Lines[$r[0]] -replace '=.*$', '').Trim()
        $ini.Lines[$r[0]] = $name + '=' + $value
    } elseif ($r[1] -ge 0) {
        $ini.Lines.Insert($r[1] + 1, $key + '=' + $value)
    } else {
        $at = $ini.Lines.Count
        if ($at -gt 0 -and $ini.Lines[$at - 1] -eq '') { $at-- }
        $ini.Lines.Insert($at, '')
        $ini.Lines.Insert($at + 1, '[' + $section + ']')
        $ini.Lines.Insert($at + 2, $key + '=' + $value)
    }
}

# Folders under a game that hold a mod install (dlssg_sm86.ini + dlssg_sm86\), e.g. COD's root and sp26\
function Find-ModInstalls([string]$folder) {
    @((Find-Files $folder 'dlssg_sm86.ini' 5).ToArray() |
        Where-Object { [IO.Directory]::Exists((Join-Path $_.DirectoryName 'dlssg_sm86')) -and ($_.DirectoryName.TrimEnd('\') -ine $PackageDir.TrimEnd('\')) } |
        ForEach-Object { $_.DirectoryName })
}

# Settings shown in the DLSS Settings window: section, key, label, factory default, (value, text) choices
$script:IniFields = @(
    @{ S = 'FrameGeneration'; K = 'Engine'; L = 'Engine'; D = 'DLSS'; Group = 'fg'; I = @(
        @('DLSS', 'DLSS  -  NVIDIA DLSS-G model (default)'),
        @('XeSS', 'XeSS  -  Intel XeSS-FG model (2X to 4X only)')) },
    @{ S = 'FrameGeneration'; K = 'MaxGeneratedFrames'; L = 'Frame generation'; D = '3'; Group = 'fg'; I = @(
        @('1', '2X  -  1 generated frame per real frame'),
        @('2', '3X  -  2 generated frames'),
        @('3', '4X  -  3 generated frames (default)'),
        @('4', '5X  -  4 generated frames (DLSS engine only)'),
        @('5', '6X  -  5 generated frames (DLSS engine only, maximum)')) },
    @{ S = 'DLSSG'; K = 'Optimized'; L = 'Speed / quality tier'; D = '1'; Group = 'dlss'; I = @(
        @('0', '0  -  Stock NVIDIA kernels (slowest)'),
        @('1', '1  -  Accelerated, identical image to NVIDIA (default)'),
        @('2', '2  -  Faster, lossy image kernels (difference not visible)'),
        @('3', '3  -  Fastest, all lossy (lowest image quality)')) },
    @{ S = 'DLSSG'; K = 'Preset'; L = 'Preset'; D = 'A'; Group = 'dlss'; I = @(
        @('A', 'A  -  Default'),
        @('B', 'B  -  UI recomposition (only if the game supplies UI planes)'),
        @('C', 'C  -  FG Lite: 32-54% less GPU time, slightly lower quality')) },
    @{ S = 'XeSSFG'; K = 'FlowResolution'; L = 'Optical flow'; D = 'Middle'; Group = 'xess'; I = @(
        @('Coarse', 'Coarse  -  fastest'),
        @('Middle', 'Middle  -  (default)'),
        @('Fine', 'Fine  -  slowest, most detailed'),
        @('CoarseAt4K', 'CoarseAt4K  -  Coarse at 4K output, Middle below')) },
    @{ S = 'XeSSFG'; K = 'UseHudless'; L = 'UI compositing'; D = 'Auto'; Group = 'xess'; I = @(
        @('Auto', 'Auto  -  same as 0 (default)'),
        @('0', '0  -  Interpolate the whole image, UI included'),
        @('1', '1  -  Always use the game''s HUD-less image (errors if missing)')) },
    @{ S = 'SuperResolution'; K = 'Mode'; L = 'Super Resolution'; D = 'Off'; Group = 'other'; I = @(
        @('Off', 'Off  -  game''s own DLSS Super Resolution (default)'),
        @('M-INT8', 'M-INT8  -  preset M on this mod''s INT8 kernels'),
        @('L-INT8', 'L-INT8  -  preset L on this mod''s INT8 kernels'),
        @('M-FP16', 'M-FP16  -  preset M on NVIDIA''s kernels'),
        @('L-FP16', 'L-FP16  -  preset L on NVIDIA''s kernels')) },
    @{ S = 'Logging'; K = 'Level'; L = 'Log level'; D = '1'; Group = 'other'; I = @(
        @('0', '0  -  Off'),
        @('1', '1  -  Errors only (default)'),
        @('2', '2  -  Configuration (use this to check that the mod loads)'),
        @('3', '3  -  Full trace (slow - debugging only)')) }
)

function Set-ComboValue($c, [string]$value) {
    $vals = $c.Tag
    for ($i = 0; $i -lt $vals.Count; $i++) { if ([string]$vals[$i] -ieq $value) { $c.SelectedIndex = $i; return } }
    [void]$vals.Add($value)
    $c.SelectedIndex = $c.Items.Add($value + '  (current value, not a standard choice)')
}

function Show-DlssSettings($g) {
    $dir = [IO.Path]::GetDirectoryName($g.Exe)
    $iniPath = Join-Path $dir 'dlssg_sm86.ini'
    $isPackage = $false
    if (-not [IO.File]::Exists($iniPath)) {
        $r = Show-Msg ("The mod is not installed next to this EXE yet, so it has no dlssg_sm86.ini.`r`n`r`n" +
            "Edit the package's dlssg_sm86.ini instead? It is the one copied into games on new installs.") 'YesNo' 'Question'
        if ($r -ne 'Yes') { return }
        $iniPath = Join-Path $PackageDir 'dlssg_sm86.ini'
        $isPackage = $true
    }
    $ini = Read-IniFile $iniPath
    $others = if ($isPackage) { @() } else { @(Find-ModInstalls $g.Folder | Where-Object { $_.TrimEnd('\') -ine $dir.TrimEnd('\') }) }

    $dlg = New-Object Windows.Forms.Form
    $dlg.Text = 'DLSS Settings  -  ' + $g.Name
    $dlg.FormBorderStyle = 'FixedDialog'; $dlg.MaximizeBox = $false; $dlg.MinimizeBox = $false
    $dlg.StartPosition = 'CenterParent'; $dlg.Font = $font; $dlg.ShowInTaskbar = $false
    $dlg.ClientSize = New-Object Drawing.Size(620, 600)

    $lblFile = New-Object Windows.Forms.Label
    $lblFile.Location = New-Object Drawing.Point(14, 10); $lblFile.Size = New-Object Drawing.Size(592, 34)
    $lblFile.Text = $(if ($isPackage) { 'Package default (used for new installs):' } else { 'Editing:' }) + "`r`n" + $iniPath
    $dlg.Controls.Add($lblFile)

    $groups = @{}
    $gy = 50
    foreach ($def in @(@('fg', 'Frame generation', 124), @('dlss', 'DLSS engine', 92), @('xess', 'XeSS engine', 92), @('other', 'Other', 92))) {
        $gb = New-Object Windows.Forms.GroupBox
        $gb.Text = $def[1]; $gb.Location = New-Object Drawing.Point(14, $gy); $gb.Size = New-Object Drawing.Size(592, $def[2])
        $dlg.Controls.Add($gb); $groups[$def[0]] = $gb; $gb.Tag = 24
        $gy += $def[2] + 8
    }

    $chkEnabled = New-Object Windows.Forms.CheckBox
    $chkEnabled.Text = 'Frame generation enabled  (off = the game''s own DLSS-G, no frame generation on RTX 30 / 20)'
    $chkEnabled.Location = New-Object Drawing.Point(14, 22); $chkEnabled.Size = New-Object Drawing.Size(566, 22)
    $chkEnabled.Checked = ((Get-IniValue $ini 'General' 'Enabled') -ne '0')
    $groups['fg'].Controls.Add($chkEnabled); $groups['fg'].Tag = 54

    $combos = @{}
    foreach ($f in $IniFields) {
        $gb = $groups[$f.Group]; $y = [int]$gb.Tag
        $lbl = New-Object Windows.Forms.Label
        $lbl.Text = $f.L + ':'; $lbl.Location = New-Object Drawing.Point(14, ($y + 4)); $lbl.Size = New-Object Drawing.Size(140, 20)
        $gb.Controls.Add($lbl)
        $c = New-Object Windows.Forms.ComboBox
        $c.DropDownStyle = 'DropDownList'; $c.Location = New-Object Drawing.Point(158, $y); $c.Size = New-Object Drawing.Size(420, 24)
        $c.Tag = New-Object System.Collections.ArrayList
        foreach ($it in $f.I) { [void]$c.Tag.Add($it[0]); [void]$c.Items.Add($it[1]) }
        $cur = Get-IniValue $ini $f.S $f.K
        Set-ComboValue $c $(if ($null -ne $cur -and $cur -ne '') { $cur } else { $f.D })
        $gb.Controls.Add($c)
        $combos[$f.S + '.' + $f.K] = $c
        $gb.Tag = $y + 32
    }

    $chkAll = New-Object Windows.Forms.CheckBox
    $chkAll.Location = New-Object Drawing.Point(14, ($gy + 2)); $chkAll.Size = New-Object Drawing.Size(592, 22)
    if ($others.Count) {
        $chkAll.Text = 'Also save to the other mod install(s) of this game: ' + (($others | ForEach-Object { $_.Substring($g.Folder.TrimEnd('\').Length).TrimStart('\') } | ForEach-Object { if ($_) { $_ + '\' } else { '(game folder)' } }) -join ', ')
        $chkAll.Checked = $true
    } else { $chkAll.Visible = $false }
    $dlg.Controls.Add($chkAll)

    $lblNote = New-Object Windows.Forms.Label
    $lblNote.Location = New-Object Drawing.Point(14, ($gy + 28)); $lblNote.Size = New-Object Drawing.Size(592, 20)
    $lblNote.ForeColor = [Drawing.Color]::DimGray
    $lblNote.Text = 'Close the game completely and start it again after saving - the mod reads the INI only at start-up.'
    $dlg.Controls.Add($lblNote)

    $by = $gy + 56
    $mk = { param($t, $x, $w) $b = New-Object Windows.Forms.Button; $b.Text = $t; $b.Location = New-Object Drawing.Point($x, $by); $b.Size = New-Object Drawing.Size($w, 32); $dlg.Controls.Add($b); $b }
    $bSave = & $mk 'Save' 14 110
    $bSave.Font = $fontBold; $bSave.BackColor = [Drawing.Color]::FromArgb(118, 185, 0); $bSave.ForeColor = [Drawing.Color]::White
    $bSave.FlatStyle = 'Flat'; $bSave.FlatAppearance.BorderSize = 0
    $bDefaults = & $mk 'Factory defaults' 132 130
    $bNotepad  = & $mk 'Open in Notepad (all keys)' 270 190
    $bCancel   = & $mk 'Cancel' 496 110
    $dlg.CancelButton = $bCancel
    $dlg.ClientSize = New-Object Drawing.Size(620, ($by + 46))

    # WinForms event handlers do not see this function's locals: keep the dialog state at script scope
    $script:DS = @{ Dlg = $dlg; Ini = $ini; IniPath = $iniPath; Others = $others; Game = $g; Groups = $groups
                    Combos = $combos; ChkEnabled = $chkEnabled; ChkAll = $chkAll; Engine = $combos['FrameGeneration.Engine'] }

    $script:DS.Engine.Add_SelectedIndexChanged({ Update-DlssGroups })
    Update-DlssGroups

    $bDefaults.Add_Click({
        $script:DS.ChkEnabled.Checked = $true
        foreach ($f in $IniFields) { Set-ComboValue $script:DS.Combos[$f.S + '.' + $f.K] $f.D }
    })
    $bNotepad.Add_Click({
        Start-Process notepad.exe -ArgumentList ('"' + $script:DS.IniPath + '"')
        $script:DS.Dlg.Close()
    })
    $bCancel.Add_Click({ $script:DS.Dlg.Close() })
    $bSave.Add_Click({ Save-DlssSettings })

    $script:ActiveDialog = $dlg
    try { [void]$dlg.ShowDialog($form) } finally { $script:ActiveDialog = $null; $script:DS = $null; $dlg.Dispose() }
}

function Update-DlssGroups {
    $isXess = (Get-ComboValue $script:DS.Engine) -ieq 'XeSS'
    $script:DS.Groups['dlss'].Enabled = -not $isXess
    $script:DS.Groups['xess'].Enabled = $isXess
}

function Save-DlssSettings {
    $ds = $script:DS
    $engine = Get-ComboValue $ds.Engine
    $frames = Get-ComboValue $ds.Combos['FrameGeneration.MaxGeneratedFrames']
    if ($engine -ieq 'XeSS' -and ($frames -as [int]) -gt 3) {
        [void](Show-Msg 'The XeSS engine goes up to 4X. With 5X or 6X frame generation would not start - pick 4X or less, or the DLSS engine.' 'OK' 'Warning')
        return
    }
    $want = [ordered]@{ 'General.Enabled' = $(if ($ds.ChkEnabled.Checked) { '1' } else { '0' }) }
    foreach ($f in $IniFields) { $want[$f.S + '.' + $f.K] = Get-ComboValue $ds.Combos[$f.S + '.' + $f.K] }
    $targets = @($ds.IniPath)
    if ($ds.ChkAll.Visible -and $ds.ChkAll.Checked) { $targets += @($ds.Others | ForEach-Object { Join-Path $_ 'dlssg_sm86.ini' }) }
    try {
        foreach ($t in $targets) {
            $ti = if ($t -eq $ds.IniPath) { $ds.Ini } else { Read-IniFile $t }
            $changes = @()
            foreach ($k in $want.Keys) {
                $s, $n = $k.Split('.')
                $old = Get-IniValue $ti $s $n
                $def = if ($k -eq 'General.Enabled') { '1' } else { ($IniFields | Where-Object { ($_.S + '.' + $_.K) -eq $k }).D }
                if ($null -eq $old -and $want[$k] -ieq $def) { continue }       # absent key at its default: leave it out
                if ($old -cne $want[$k]) { Set-IniValue $ti $s $n $want[$k]; $changes += "$n=$($want[$k])" }
            }
            if ($changes.Count) { Write-IniFile $ti; Write-Log ("Saved " + $t + ": " + ($changes -join ', ')) }
            else { Write-Log ("No changes for " + $t) }
        }
    } catch {
        if ($_.Exception -is [UnauthorizedAccessException] -and -not (Test-IsAdmin)) {
            if ((Show-Msg ($_.Exception.Message + "`r`n`r`nRestart the launcher as administrator?") 'YesNo' 'Error') -eq 'Yes') { $ds.Dlg.Close(); Restart-Elevated }
        } else { [void](Show-Msg ("Could not save: " + $_.Exception.Message) 'OK' 'Error') }
        return
    }
    if (Test-GameRunning $ds.Game.Exe) { [void](Show-Msg 'Saved. The game is running - restart it for the new settings to apply.') }
    $ds.Dlg.Close()
}

function Get-ComboValue($c) { [string]$c.Tag[$c.SelectedIndex] }

# ---------------------------------------------------------------------------------------------------
# Desktop shortcut: runs the launcher in -Play mode for one EXE, with the game's own icon
# ---------------------------------------------------------------------------------------------------
function New-GameShortcut([string]$exe, [string]$name) {
    $desktop = [Environment]::GetFolderPath('Desktop')
    $safe = ($name -replace '[\\/:*?"<>|]', '').Trim()
    if (-not $safe) { throw 'Empty shortcut name.' }
    $lnk = Join-Path $desktop ($safe + '.lnk')
    $ws = New-Object -ComObject WScript.Shell
    $sc = $ws.CreateShortcut($lnk)
    $launcherExe = Join-Path $PackageDir 'DLSSG_Launcher.exe'
    if ([IO.File]::Exists($launcherExe)) {
        $sc.TargetPath = $launcherExe
        $sc.Arguments  = '-Play "' + $exe + '"'
    } else {
        $sc.TargetPath = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $sc.Arguments  = '-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File "' + (Join-Path $PackageDir 'DLSSG_Launcher.ps1') + '" -Play "' + $exe + '"'
    }
    $sc.WorkingDirectory = $PackageDir
    $sc.IconLocation = $exe + ',0'
    $sc.Description = 'Start ' + $safe + ' with the DLSSG SM86 mod'
    $sc.Save()
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($ws)
    $lnk
}

function Get-DefaultShortcutName($g) {
    $n = $g.Name
    if ($g.Candidates.Count -gt 1 -and $g.Exe -ine $g.Candidates[0]) { $n += ' - ' + [IO.Path]::GetFileNameWithoutExtension($g.Exe) }
    $n + ' (DLSS mod)'
}

# ---------------------------------------------------------------------------------------------------
# Events
# ---------------------------------------------------------------------------------------------------
$lv.Add_SelectedIndexChanged({
    $it = Get-CurrentItem
    if ($it) { Show-Game $it.Tag }
})
$lv.Add_DoubleClick({ if (Get-CurrentItem) { Invoke-Play } })
$lv.Add_KeyDown({ if ($_.KeyCode -eq 'Enter' -and (Get-CurrentItem)) { Invoke-Play } })

$cmbExe.Add_SelectedIndexChanged({
    if ($script:Updating -or -not $script:Current -or -not $cmbExe.SelectedItem) { return }
    Set-GameExe $script:Current ([string]$cmbExe.SelectedItem)
    $it = Get-CurrentItem
    if ($it) { Update-Item $it }
    Show-ExeDetails
})

$txtArgs.Add_Leave({
    if ($script:Current) { $Settings.Args[$Current.Exe] = $txtArgs.Text.Trim(); Save-Settings }
})

$btnRescan.Add_Click({ Invoke-Action { Update-GameList } })

$btnAddRoot.Add_Click({
    $dlg = New-Object Windows.Forms.FolderBrowserDialog
    $dlg.Description = 'Pick a folder that contains your games (each sub-folder = one game), e.g. D:\Games or ...\steamapps\common'
    if ($dlg.ShowDialog($form) -ne 'OK') { return }
    if (@($Settings.ScanRoots) -notcontains $dlg.SelectedPath) { $Settings.ScanRoots = @($Settings.ScanRoots) + $dlg.SelectedPath }
    Save-Settings
    Update-RootsLabel
    Invoke-Action { Update-GameList }
})

$btnBrowse.Add_Click({
    $dlg = New-Object Windows.Forms.OpenFileDialog
    $dlg.Title  = 'Select the game EXE'
    $dlg.Filter = 'Game executable (*.exe)|*.exe'
    $start = if ($script:Current) { [IO.Path]::GetDirectoryName($Current.Exe) } elseif ([IO.Directory]::Exists('C:\Games')) { 'C:\Games' } else { '' }
    if ($start) { $dlg.InitialDirectory = $start }
    if ($dlg.ShowDialog($form) -ne 'OK') { return }
    $exe = $dlg.FileName
    $g = Get-GameForPath $exe
    if ($g) {
        Set-GameExe $g $exe
    } else {
        $folder = [IO.Path]::GetDirectoryName($exe)
        Add-ExtraGame $folder
        $Settings.ExeChoice[$folder] = $exe
        $g = New-Game $folder (Get-DisplayName $exe)
        [void]$Games.Add($g)
        [void](Add-GameItem $g)
        Set-GameExe $g $exe
    }
    foreach ($it in $lv.Items) { if ($it.Tag -eq $g) { Update-Item $it } }
    Select-GameItem $g
    Show-Game $g
    Write-Log ("Selected " + $exe)
})

$btnFolder.Add_Click({
    $dlg = New-Object Windows.Forms.FolderBrowserDialog
    $dlg.Description = 'Pick the game''s install folder - the game EXE is found automatically'
    $dlg.ShowNewFolderButton = $false
    if ($script:Current) { $dlg.SelectedPath = $Current.Folder } elseif ([IO.Directory]::Exists('C:\Games')) { $dlg.SelectedPath = 'C:\Games' }
    if ($dlg.ShowDialog($form) -ne 'OK') { return }
    $folder = $dlg.SelectedPath.TrimEnd('\')
    Invoke-Action {
        $g = @($Games | Where-Object { $_.Folder.TrimEnd('\') -ieq $folder })[0]
        if (-not $g) {
            $parent = Get-GameForPath $folder
            if ($parent) {
                # a sub-folder of a known game: pick the best EXE inside it for that game
                $best = @(Get-RankedExes $folder)
                if ($best.Count) { Set-GameExe $parent $best[0]; foreach ($b in $best) { if ($parent.Candidates -notcontains $b) { $parent.Candidates += $b } } }
                $g = $parent
            }
        }
        if (-not $g) {
            Set-Status ("Searching " + $folder + " for the game EXE ...")
            $g = New-Game $folder (Get-DisplayName $folder)
            if (-not $g) { [void](Show-Msg "No .exe found in:`r`n$folder`r`n`r`nUse ""Browse EXE..."" instead." 'OK' 'Warning'); return }
            Add-ExtraGame $folder
            $Settings.ExeChoice[$folder] = $g.Exe
            [void]$Games.Add($g)
            [void](Add-GameItem $g)
            Save-Settings
        }
        Select-GameItem $g
        Show-Game $g
        Write-Log ("Game folder: " + $g.Folder)
        Write-Log ("  detected EXE: " + $g.Exe)
        if ($g.Candidates.Count -gt 1) { Write-Log ("  other EXEs (pick in 'Game EXE' if the guess is wrong): " + (($g.Candidates | Select-Object -Skip 1 | ForEach-Object { [IO.Path]::GetFileName($_) }) -join ', ')) }
    }
})

$btnExplore.Add_Click({
    if (-not $script:Current) { return }
    Start-Process explorer.exe -ArgumentList ('/select,"' + $Current.Exe + '"')
})

$btnIni.Add_Click({
    if (-not $script:Current) { [void](Show-Msg 'Select a game first.'); return }
    try { Show-DlssSettings $script:Current } catch { Write-Log ("ERROR: " + $_.Exception.Message); [void](Show-Msg $_.Exception.Message 'OK' 'Error') }
})

$btnShort.Add_Click({
    if (-not $script:Current) { [void](Show-Msg 'Select a game first.'); return }
    Add-Type -AssemblyName Microsoft.VisualBasic
    $name = [Microsoft.VisualBasic.Interaction]::InputBox(("Shortcut name (created on your desktop).`r`n`r`nIt starts:`r`n" + $Current.Exe + "`r`nand checks / updates the mod first."), 'Desktop shortcut', (Get-DefaultShortcutName $Current))
    if (-not $name.Trim()) { return }
    try {
        $lnk = New-GameShortcut $Current.Exe $name
        Write-Log ("Created shortcut " + $lnk)
        [void](Show-Msg ("Shortcut created on your desktop:`r`n" + [IO.Path]::GetFileName($lnk)))
    } catch { Write-Log ("ERROR: " + $_.Exception.Message); [void](Show-Msg $_.Exception.Message 'OK' 'Error') }
})

$btnLog.Add_Click({
    if (-not $script:Current) { return }
    $log = Join-Path ([IO.Path]::GetDirectoryName($Current.Exe)) 'dlssg_sm86\logs\dlssg_sm86.log'
    if ([IO.File]::Exists($log)) { Start-Process notepad.exe -ArgumentList ('"' + $log + '"') }
    else { [void](Show-Msg "No log yet:`r`n$log`r`n`r`nIt appears after the game has run with the mod. Set [Logging] Level=2 in the INI for more detail.") }
})

$btnPlay.Add_Click({ Invoke-Play })
$btnInstall.Add_Click({
    Invoke-Action {
        $g = Get-SelectedGame
        if ($g -and (Confirm-AntiCheat $g)) { [void](Install-Mod $g ([string]$cmbProxy.SelectedItem)) }
    }
})
$btnRemove.Add_Click({ Invoke-Action { $g = Get-SelectedGame; if ($g) { Uninstall-Mod $g } } })
$btnPlain.Add_Click({
    Invoke-Action {
        $g = Get-SelectedGame
        if (-not $g) { return }
        $st = Get-ModState ([IO.Path]::GetDirectoryName($g.Exe))
        if ($st.Ours.Count) { Write-Log 'Note: the mod is still installed in this folder, so it will load anyway. Use "Remove mod" for a clean run.' }
        Start-Game $g
    }
})

$miPlay.Add_Click({ if (Get-CurrentItem) { Invoke-Play } })
$miExplore.Add_Click({ $btnExplore.PerformClick() })
$miForget.Add_Click({
    $it = Get-CurrentItem
    if (-not $it) { return }
    $g = $it.Tag
    if (@($Settings.ExtraGames) -notcontains $g.Folder) {
        [void](Show-Msg ("This game comes from a scan folder and will be found again on the next scan.`r`nUse ""Remove mod"" to remove the mod files.")); return
    }
    $Settings.ExtraGames = @($Settings.ExtraGames | Where-Object { $_ -ne $g.Folder })
    $Settings.ExeChoice.Remove($g.Folder)
    Save-Settings
    $Games.Remove($g)
    $lv.Items.Remove($it)
    Show-Game $null
})

$form.Add_Shown({
    Update-RootsLabel
    Write-Log ("Mod package: " + $PackageDir)
    if (Test-IsAdmin) { Write-Log 'Running as administrator.' }
    Invoke-Action { Update-GameList }
    $last = $Settings.LastExe
    $pick = $null
    if ($last) { $pick = @($Games | Where-Object { $_.Exe -ieq $last -or (Test-PathUnder $last $_.Folder) })[0] }
    if (-not $pick -and $Games.Count) { $pick = $Games[0] }
    if ($pick) {
        if ($last -and [IO.File]::Exists($last) -and (Test-PathUnder $last $pick.Folder) -and $pick.Exe -ine $last) { Set-GameExe $pick $last; foreach ($it in $lv.Items) { if ($it.Tag -eq $pick) { Update-Item $it } } }
        Select-GameItem $pick
    } elseif (-not $Games.Count) {
        Write-Log 'No games found. Use "Open Game Folder..." or "Browse EXE...", or "Add scan folder..." for another library folder.'
    }
})
$form.Add_FormClosing({
    if ($script:Current) { $Settings.Args[$Current.Exe] = $txtArgs.Text.Trim() }
    Save-Settings
})

# ---------------------------------------------------------------------------------------------------
# -Play mode (desktop shortcuts): no window unless something needs an answer
# ---------------------------------------------------------------------------------------------------
if ($Play) {
    $script:PlayLog = Join-Path $PackageDir 'launcher_play.log'
    try { if ((Test-Path -LiteralPath $PlayLog) -and (Get-Item -LiteralPath $PlayLog).Length -gt 512KB) { Remove-Item -LiteralPath $PlayLog -Force } } catch { }
    $script:RelaunchArgs = '-Play "' + $Play + '"'
    $code = 1
    try {
        $exe = [IO.Path]::GetFullPath($Play.Trim('"'))
        if (-not [IO.File]::Exists($exe)) { throw "Game EXE not found:`r`n$exe`r`n`r`nThe game may have moved - make a new shortcut in the launcher." }
        $folder = [IO.Path]::GetDirectoryName($exe)
        foreach ($root in @($Settings.ScanRoots)) {        # game folder = the scan folder's sub-folder that holds the EXE
            if (Test-PathUnder $exe $root) {
                $rel = $exe.Substring($root.TrimEnd('\').Length + 1)
                if ($rel.Contains('\')) { $folder = Join-Path $root.TrimEnd('\') $rel.Split('\')[0] }
                break
            }
        }
        $g = [pscustomobject]@{ Name = (Get-DisplayName $(if ($folder -ne [IO.Path]::GetDirectoryName($exe)) { $folder } else { $exe })); Folder = $folder; Candidates = @($exe); Exe = $exe }
        $txtArgs.Text = [string]$Settings.Args[$exe]
        $st = Get-ModState ([IO.Path]::GetDirectoryName($exe))
        $proxy = if ($st.Ours.Count) { $st.Ours[0] } else { 'version.dll' }
        Write-Log ("Play: " + $exe)
        if (Confirm-AntiCheat $g) {
            if (Install-Mod $g $proxy) { Start-Game $g; $code = 0 }
        }
    } catch {
        Write-Log ("ERROR: " + $_.Exception.Message)
        [void](Show-Msg ($_.Exception.Message) 'OK' 'Error')
    }
    exit $code
}

[void]$form.ShowDialog()

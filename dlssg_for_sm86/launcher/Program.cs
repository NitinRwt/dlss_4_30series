using System.Diagnostics;
using System.IO.Compression;
using System.Management;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DlssgLauncher;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        // Desktop-shortcut mode: DLSSG-Launcher.exe --play "<game.exe>" --name "<Game>" [--steam <appid>]
        var a = Cli.Parse(args);
        if (a.ContainsKey("play")) { Cli.Play(a); return; }
        if (a.TryGetValue("settings", out var sini)) { Application.Run(new SettingsForm("Test game", sini, null, "test.exe")); return; }
        if (a.TryGetValue("ini-selftest", out var sin) && a.TryGetValue("out", out var sout))
        {
            var f = IniFile.Load(sin);
            f.Set("FrameGeneration", "Engine", "XeSS"); f.Set("FrameGeneration", "MaxGeneratedFrames", "2");
            f.Set("DLSSG", "Preset", "C"); f.Set("SuperResolution", "Mode", "M-INT8");
            f.Set("Logging", "FrameTiming", "1");          // key absent in the factory file
            f.Set("Debug", "MarkGeneratedFrames", "1");    // section absent in the factory file
            f.Remove("Runtime", "Mode");
            f.Save(sout); return;
        }
        if (a.TryGetValue("make-shortcut", out var mk)) { a.TryGetValue("name", out var nm); a.TryGetValue("steam", out var st); Cli.CreateShortcut(nm ?? Path.GetFileNameWithoutExtension(mk), mk, string.IsNullOrEmpty(st) ? null : st); return; }
        Application.Run(new MainForm(args.Length > 0 && ShortcutImporter.IsShortcut(args[0]) ? args[0] : null));
    }
}

sealed class Game
{
    public string Name = "";
    public string Dir = "";
    public string Store = "";
    public string? SteamAppId;
    public string? PreferredExe;
    public string? Lnk;
    public override string ToString() => Name;
}

static class Scanner
{
    static readonly string[] SkipStart =
    {
        "unins", "vcredist", "vc_redist", "dxsetup", "dotnet", "crashreport", "unitycrash", "easyanticheat",
        "beclient", "battleye", "setup", "ue4prereq", "ueprereq", "notification_helper", "cefsub", "webhelper"
    };
    static readonly string[] SkipContains = { "crashhandler", "redist", "installer", "updater", "uninstall" };

    static bool Skipped(string file)
    {
        var n = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
        return SkipStart.Any(n.StartsWith) || SkipContains.Any(n.Contains);
    }

    public static List<Game> ScanAll()
    {
        var games = new List<Game>();
        try { games.AddRange(Steam()); } catch { }
        try { games.AddRange(Epic()); } catch { }
        try { games.AddRange(Gog()); } catch { }
        return games
            .Where(g => Directory.Exists(g.Dir))
            .GroupBy(g => g.Dir.TrimEnd('\\').ToLowerInvariant()).Select(x => x.First())
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static IEnumerable<Game> Steam()
    {
        string? root = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                    ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        if (root == null) yield break;
        root = root.Replace('/', '\\');
        var libs = new List<string> { root };
        var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
        foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var acf in Directory.GetFiles(apps, "appmanifest_*.acf"))
            {
                var t = File.ReadAllText(acf);
                string? Get(string k) => Regex.Match(t, "\"" + k + "\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value : null;
                var name = Get("name"); var dir = Get("installdir"); var id = Get("appid");
                if (name == null || dir == null) continue;
                if (Regex.IsMatch(name, "Steamworks|Redistributable|Proton|Steam Linux Runtime|Wallpaper Engine", RegexOptions.IgnoreCase)) continue;
                yield return new Game { Name = name, Dir = Path.Combine(apps, "common", dir), Store = "Steam", SteamAppId = id };
            }
        }
    }

    static IEnumerable<Game> Epic()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                               "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(dir)) yield break;
        foreach (var f in Directory.GetFiles(dir, "*.item"))
        {
            Game? g = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                var r = doc.RootElement;
                var name = r.GetProperty("DisplayName").GetString();
                var loc = r.GetProperty("InstallLocation").GetString();
                if (name == null || loc == null) continue;
                var exe = r.TryGetProperty("LaunchExecutable", out var le) ? le.GetString() : null;
                g = new Game { Name = name, Dir = loc, Store = "Epic", PreferredExe = exe };
            }
            catch { }
            if (g != null) yield return g;
        }
    }

    static IEnumerable<Game> Gog()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
        if (k == null) yield break;
        foreach (var sub in k.GetSubKeyNames())
        {
            using var s = k.OpenSubKey(sub);
            var name = s?.GetValue("gameName") as string; var path = s?.GetValue("path") as string;
            if (name != null && path != null)
                yield return new Game { Name = name, Dir = path, Store = "GOG", PreferredExe = s?.GetValue("exe") as string };
        }
    }

    /// <summary>Candidate render executables, best guess first.</summary>
    public static List<string> FindExes(Game g)
    {
        var found = new List<(string path, long size)>();
        void Walk(string d, int depth)
        {
            if (depth > 6) return;
            try
            {
                foreach (var f in Directory.GetFiles(d, "*.exe"))
                {
                    if (Skipped(f)) continue;
                    found.Add((f, new FileInfo(f).Length));
                }
                foreach (var sub in Directory.GetDirectories(d))
                {
                    var n = Path.GetFileName(sub).ToLowerInvariant();
                    if (n is "_commonredist" or "redist" or "__installer" or "directx" or "crashreportclient") continue;
                    Walk(sub, depth + 1);
                }
            }
            catch { }
        }
        Walk(g.Dir, 0);
        // Unreal-style Binaries\Win64 shipping builds are the real renderers; the root EXE is usually a stub.
        int Score((string path, long size) e)
        {
            var p = e.path.ToLowerInvariant();
            int s = 0;
            if (p.Contains(@"\win64\")) s += 3;
            if (p.Contains("shipping")) s += 2;
            var fn = Path.GetFileNameWithoutExtension(p);
            if (System.Text.RegularExpressions.Regex.IsMatch(fn, "console|tool|server|editor|bench|config|report|diag|launcher|sdk|test|viewer")) s -= 6;
            if (g.PreferredExe != null && p.EndsWith(g.PreferredExe.Replace('/', '\\').ToLowerInvariant())) s += 1;
            return s;
        }
        var res = found.OrderByDescending(Score).ThenByDescending(e => e.size).Select(e => e.path).ToList();
        // A manually picked .exe always wins.
        if (g.Store == "Manual" && g.PreferredExe != null && Path.IsPathRooted(g.PreferredExe) && File.Exists(g.PreferredExe))
        {
            res.RemoveAll(p => p.Equals(g.PreferredExe, StringComparison.OrdinalIgnoreCase));
            res.Insert(0, g.PreferredExe);
        }
        return res;
    }
}

static class Installer
{
    public const string Marker = "dlssg_sm86_install.json";
    const string BackupSuffix = ".dlssg_backup";

    public static bool IsInstalled(string exeDir) => File.Exists(Path.Combine(exeDir, Marker));

    public static bool IsPayload(string d) =>
        File.Exists(Path.Combine(d, "version.dll")) && Directory.Exists(Path.Combine(d, "dlssg_sm86"));

    static string? FindPayloadOnDisk()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 5 && dir != null; i++, dir = dir.Parent)
        {
            if (IsPayload(dir.FullName)) return dir.FullName;
            var sib = Path.Combine(dir.FullName, "dlssg_for_sm86");
            if (IsPayload(sib)) return sib;
        }
        return null;
    }

    /// <summary>
    /// The mod files are embedded in this exe as payload.zip; unpack them once to %LOCALAPPDATA%.
    /// Falls back to a loose copy next to the exe (development builds).
    /// </summary>
    public static string? ResolvePayload()
    {
        using var res = typeof(Installer).Assembly.GetManifestResourceStream("payload.zip");
        if (res == null) return FindPayloadOnDisk();

        var stamp = res.Length.ToString("x") + "-" + File.GetLastWriteTimeUtc(Environment.ProcessPath!).Ticks.ToString("x");
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DLSSG-Launcher");
        var dest = Path.Combine(root, "payload-" + stamp);
        if (IsPayload(dest)) return dest;

        Directory.CreateDirectory(root);
        foreach (var old in Directory.GetDirectories(root, "payload-*"))
            try { Directory.Delete(old, true); } catch { }
        var tmp = dest + ".tmp";
        ZipFile.ExtractToDirectory(res, tmp, true);
        Directory.Move(tmp, dest);
        return dest;
    }

    public static void Install(string payload, string exeDir)
    {
        bool backedUp = false;
        if (IsInstalled(exeDir))
        {
            try { backedUp = JsonDocument.Parse(File.ReadAllText(Path.Combine(exeDir, Marker))).RootElement.GetProperty("backedUp").GetBoolean(); } catch { }
            var oldIni = Path.Combine(exeDir, "dlssg_sm86.ini");
            if (File.Exists(oldIni)) File.Copy(oldIni, oldIni + ".bak", true);
            RemoveFiles(exeDir);
        }
        var dll = Path.Combine(exeDir, "version.dll");
        if (File.Exists(dll) && !backedUp)
        {
            File.Move(dll, dll + BackupSuffix, true);
            backedUp = true;
        }
        File.Copy(Path.Combine(payload, "version.dll"), dll, true);
        var ini = Path.Combine(payload, "dlssg_sm86.ini");
        if (File.Exists(ini)) File.Copy(ini, Path.Combine(exeDir, "dlssg_sm86.ini"), true);
        CopyDir(Path.Combine(payload, "dlssg_sm86"), Path.Combine(exeDir, "dlssg_sm86"));
        File.WriteAllText(Path.Combine(exeDir, Marker),
            JsonSerializer.Serialize(new { backedUp, installedUtc = DateTime.UtcNow }));
    }

    public static void Uninstall(string exeDir)
    {
        bool backedUp = false;
        try { backedUp = JsonDocument.Parse(File.ReadAllText(Path.Combine(exeDir, Marker))).RootElement.GetProperty("backedUp").GetBoolean(); } catch { }
        RemoveFiles(exeDir);
        var dll = Path.Combine(exeDir, "version.dll");
        if (backedUp && File.Exists(dll + BackupSuffix)) File.Move(dll + BackupSuffix, dll, true);
        File.Delete(Path.Combine(exeDir, Marker));
    }

    static void RemoveFiles(string exeDir)
    {
        File.Delete(Path.Combine(exeDir, "version.dll"));
        File.Delete(Path.Combine(exeDir, "dlssg_sm86.ini"));
        var d = Path.Combine(exeDir, "dlssg_sm86");
        if (Directory.Exists(d)) Directory.Delete(d, true);
    }

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src))
        {
            if (Path.GetFileName(d).Equals("logs", StringComparison.OrdinalIgnoreCase)) continue;
            CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }
    }
}

static class Theme
{
    public static readonly Color Bg = Color.FromArgb(18, 22, 28);
    public static readonly Color Panel = Color.FromArgb(26, 31, 39);
    public static readonly Color Row2 = Color.FromArgb(30, 36, 45);
    public static readonly Color Selected = Color.FromArgb(34, 52, 36);
    public static readonly Color Border = Color.FromArgb(46, 53, 64);
    public static readonly Color Text = Color.FromArgb(232, 236, 241);
    public static readonly Color Dim = Color.FromArgb(138, 149, 163);
    public static readonly Color Green = Color.FromArgb(118, 230, 60);
    public static readonly Color GreenDark = Color.FromArgb(92, 196, 40);
    public static readonly Color Warn = Color.FromArgb(255, 120, 90);

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void DarkTitleBar(IntPtr hwnd)
    {
        int on = 1;
        try { DwmSetWindowAttribute(hwnd, 20, ref on, 4); } catch { }
    }

    public static Button Btn(string text, bool primary)
    {
        var b = new Button { Text = text, Height = 34 };
        StyleBtn(b, primary);
        return b;
    }

    public static void StyleBtn(Button b, bool primary)
    {
        b.FlatStyle = FlatStyle.Flat; b.Cursor = Cursors.Hand; b.UseVisualStyleBackColor = false;
        b.BackColor = primary ? Green : Panel; b.ForeColor = primary ? Color.FromArgb(14, 20, 10) : Text;
        b.FlatAppearance.BorderColor = primary ? Green : Border;
        b.FlatAppearance.MouseOverBackColor = primary ? GreenDark : Color.FromArgb(40, 47, 58);
        b.FlatAppearance.MouseDownBackColor = primary ? GreenDark : Border;
        b.EnabledChanged += (_, _) =>
        {
            b.ForeColor = b.Enabled ? (primary ? Color.FromArgb(14, 20, 10) : Text) : Color.FromArgb(90, 98, 110);
            b.BackColor = b.Enabled ? (primary ? Green : Panel) : (primary ? Color.FromArgb(52, 78, 38) : Panel);
        };
    }

    public static void StylePlay(Button b)
    {
        StyleBtn(b, true);
        b.Font = new Font("Segoe UI Semibold", 12f);
        b.Text = "▶  PLAY with Frame Generation";
        b.Height = 54;
    }
}

sealed class MainForm : Form
{
    readonly ListView list = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, Dock = DockStyle.Fill, HideSelection = false };
    readonly ComboBox exeBox = new();
    readonly Label gpuLbl = new() { AutoSize = true };
    readonly Label statusLbl = new() { AutoSize = false, ForeColor = Color.DimGray };
    readonly Button btnPlay = new();
    readonly Button btnUninstall = new() { Text = "Remove mod from this game", Height = 30 };
    readonly Button btnIni = new() { Text = "Settings...", Height = 30 };
    readonly Button btnCfg = new() { Text = "Open INI Configurator", Height = 30 };
    readonly Button btnShortcut = new() { Text = "Create desktop shortcut", Height = 30 };
    List<Game> games = new();
    string? payload;

    Game? Sel => list.SelectedItems.Count > 0 ? (Game)list.SelectedItems[0].Tag! : null;
    string? ExeDir => exeBox.SelectedItem is string s ? Path.GetDirectoryName(s) : null;

    readonly string? importOnStart;

    public MainForm(string? importPath = null)
    {
        importOnStart = importPath;
        games = Library.Load();
        AllowDrop = true;
        DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] f) foreach (var x in f) ImportFile(x); };
        Text = "DLSSG for SM86 - Launcher";
        Size = new Size(980, 600);
        MinimumSize = new Size(820, 500);
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg; ForeColor = Theme.Text;
        Icon? appIcon = null;
        try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); Icon = appIcon; } catch { }
        HandleCreated += (_, _) => Theme.DarkTitleBar(Handle);

        // ---- header: logo + title + GPU line
        var header = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Theme.Panel };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
            using var br = new SolidBrush(Theme.Green);
            e.Graphics.FillRectangle(br, 0, header.Height - 3, 120, 3);
        };
        var logo = new PictureBox { Left = 18, Top = 14, Width = 54, Height = 54, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
        if (appIcon != null) logo.Image = new Icon(appIcon, 64, 64).ToBitmap();
        var title = new Label { Left = 84, Top = 12, AutoSize = true, Text = "DLSSG for SM86", Font = new Font("Segoe UI Semibold", 17f), ForeColor = Theme.Text, BackColor = Color.Transparent };
        gpuLbl.Left = 86; gpuLbl.Top = 52; gpuLbl.ForeColor = Theme.Dim; gpuLbl.BackColor = Color.Transparent;
        header.Controls.AddRange(new Control[] { logo, title, gpuLbl });

        // ---- game list (owner-drawn dark)
        list.BackColor = Theme.Panel; list.ForeColor = Theme.Text; list.BorderStyle = BorderStyle.None;
        list.OwnerDraw = true; list.Font = new Font("Segoe UI", 10f);
        list.Columns.Add("Game", 380);
        list.Columns.Add("Store", 80);
        list.Columns.Add("Mod", 110);
        list.SelectedIndexChanged += (_, _) => { OnSelect(); list.Invalidate(); };
        list.Resize += (_, _) => { if (list.Columns.Count == 3) list.Columns[0].Width = Math.Max(160, list.ClientSize.Width - 190); };
        list.DrawColumnHeader += (_, e) =>
        {
            using var b = new SolidBrush(Theme.Bg); e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header!.Text.ToUpperInvariant(), new Font("Segoe UI", 8f, FontStyle.Bold),
                new Rectangle(e.Bounds.X + 12, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height), Theme.Dim,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };
        list.DrawItem += (_, _) => { };
        list.DrawSubItem += (_, e) =>
        {
            bool sel = e.Item!.Selected;
            using (var b = new SolidBrush(sel ? Theme.Selected : (e.ItemIndex % 2 == 0 ? Theme.Panel : Theme.Row2)))
                e.Graphics.FillRectangle(b, e.Bounds);
            if (sel && e.ColumnIndex == 0)
                using (var g = new SolidBrush(Theme.Green)) e.Graphics.FillRectangle(g, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height);
            var text = e.SubItem!.Text;
            Color col = e.ColumnIndex == 2 ? (text == "Installed" ? Theme.Green : Theme.Dim) : e.ColumnIndex == 1 ? Theme.Dim : Theme.Text;
            if (e.ColumnIndex == 2 && text == "Installed") text = "● Installed";
            TextRenderer.DrawText(e.Graphics, text, list.Font, new Rectangle(e.Bounds.X + 12, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height),
                col, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };

        var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 12, 6, 14), BackColor = Theme.Bg };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(0, 0, 0, 8), BackColor = Theme.Bg };
        var add = Theme.Btn("+  Add game (.exe)", false); add.Width = 150; add.Click += (_, _) => AddGame();
        var imp = Theme.Btn("Import shortcut...", false); imp.Width = 150; imp.Click += (_, _) => PickShortcut();
        var rescan = Theme.Btn("Rescan", false); rescan.Width = 90; rescan.Click += (_, _) => Rescan();
        bar.Controls.AddRange(new Control[] { add, imp, rescan });
        listHost.Controls.Add(list); listHost.Controls.Add(bar);

        // ---- right-hand action panel
        var right = new TableLayoutPanel { Dock = DockStyle.Right, Width = 320, Padding = new Padding(16, 14, 16, 14), ColumnCount = 1, BackColor = Theme.Panel };
        right.Paint += (_, e) => { using var pen = new Pen(Theme.Border); e.Graphics.DrawLine(pen, 0, 0, 0, right.Height); };
        var exeLabel = new Label { Text = "GAME PROGRAM (auto-picked)", Height = 22, ForeColor = Theme.Dim, Font = new Font("Segoe UI", 8f, FontStyle.Bold), BackColor = Color.Transparent };
        Theme.StylePlay(btnPlay);
        Theme.StyleBtn(btnUninstall, false); Theme.StyleBtn(btnIni, false); Theme.StyleBtn(btnCfg, false); Theme.StyleBtn(btnShortcut, false);
        exeBox.FlatStyle = FlatStyle.Flat; exeBox.BackColor = Theme.Bg; exeBox.ForeColor = Theme.Text;
        exeBox.DropDownStyle = ComboBoxStyle.DropDown; exeBox.KeyPress += (_, e) => e.Handled = true; exeBox.Cursor = Cursors.Hand;
        statusLbl.ForeColor = Theme.Dim; statusLbl.BackColor = Color.Transparent; statusLbl.Height = 100;
        foreach (var c in new Control[] { btnPlay, btnShortcut, exeLabel, exeBox, btnUninstall, btnIni, btnCfg, statusLbl })
        {
            c.Dock = DockStyle.Fill; c.Margin = new Padding(0, 0, 0, 10);
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.Controls.Add(c);
        }
        

        Controls.Add(listHost); Controls.Add(right); Controls.Add(header);

        btnPlay.Click += async (_, _) => await Play();
        btnUninstall.Click += async (_, _) => await DoUninstall();
        btnIni.Click += async (_, _) => await OpenSettings();
        btnCfg.Click += (_, _) => OpenConfigurator();
        btnShortcut.Click += (_, _) => MakeShortcut();
        exeBox.SelectedIndexChanged += (_, _) => RefreshState();

        Shown += async (_, _) =>
        {
            statusLbl.Text = "Preparing mod files...";
            try { payload = await Task.Run(Installer.ResolvePayload); }
            catch (Exception ex) { statusLbl.Text = "Could not unpack mod files: " + ex.Message; }
            ReadGpu();
            Rescan();
            list.Columns[0].Width = Math.Max(160, list.ClientSize.Width - 190);
            if (importOnStart != null) ImportFile(importOnStart);
        };
        RefreshState();
    }

    void ReadGpu()
    {
        try
        {
            var lines = new List<string>();
            using var s = new ManagementObjectSearcher("SELECT Name, DriverVersion FROM Win32_VideoController");
            foreach (ManagementObject m in s.Get())
            {
                var name = m["Name"]?.ToString() ?? "";
                var drv = m["DriverVersion"]?.ToString() ?? "";
                if (!name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) continue;
                var p = drv.Split('.');
                string nv = drv;
                if (p.Length == 4) { var d = (p[2] + p[3]); if (d.Length >= 5) { d = d[^5..]; nv = d[..3].TrimStart('0') + "." + d[3..]; } }
                bool ok = double.TryParse(nv, System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 580;
                bool rtx = name.Contains("RTX", StringComparison.OrdinalIgnoreCase);
                lines.Add($"GPU: {name}  |  driver {nv}" + (!rtx ? "  (not supported: needs an RTX 20/30/40/50)" : ok ? "" : "  (needs R580 or newer!)"));
            }
            gpuLbl.Text = lines.Count > 0 ? string.Join("   ", lines) : "GPU: no NVIDIA GPU detected - this mod needs an RTX 20 / 30 / 40 / 50.";
            if (lines.Count == 0 || lines.Any(l => (l.Contains("needs R580") || l.Contains("not supported")))) gpuLbl.ForeColor = Theme.Warn;
        }
        catch { gpuLbl.Text = "GPU: could not read"; }
    }

    void Rescan()
    {
        var manual = games.Where(g => g.Store is "Manual" or "Shortcut").ToList();
        Cursor = Cursors.WaitCursor;
        try
        {
            games = Scanner.ScanAll();
            games.AddRange(manual);
            games = games.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        finally { Cursor = Cursors.Default; }
        FillList();
        statusLbl.Text = $"{games.Count} game(s) found. Pick one, then press PLAY.\nNot listed? Use \"Add game (.exe)...\".";
    }

    void FillList(Game? select = null)
    {
        list.BeginUpdate(); list.Items.Clear();
        foreach (var g in games)
            list.Items.Add(new ListViewItem(new[] { g.Name, g.Store, StatusOf(g) }) { Tag = g });
        list.EndUpdate();
        if (select != null)
            foreach (ListViewItem it in list.Items) if (it.Tag == select) { it.Selected = true; it.EnsureVisible(); }
    }

    string StatusOf(Game g)
    {
        try
        {
            if (Directory.Exists(g.Dir) &&
                Directory.EnumerateFiles(g.Dir, Installer.Marker, SearchOption.AllDirectories).Any()) return "Installed";
        }
        catch { }
        return "-";
    }

    // Manual add: just point at the game's .exe; everything else is derived from it.
    void AddGame()
    {
        using var d = new OpenFileDialog { Title = "Pick the game's .exe", Filter = "Game program (*.exe)|*.exe" };
        if (d.ShowDialog() == DialogResult.OK) AddExe(d.FileName);
    }

    void AddExe(string file)
    {
        var dir = Path.GetDirectoryName(file)!;
        var nameDir = new DirectoryInfo(dir);
        while (nameDir.Parent != null && Regex.IsMatch(nameDir.Name, "^(win64|win32|x64|x86|binaries|bin|shipping)$", RegexOptions.IgnoreCase))
            nameDir = nameDir.Parent;
        var g = new Game
        {
            Name = Path.GetFileNameWithoutExtension(file) + "  (" + nameDir.Name + ")",
            Dir = dir, Store = "Manual", PreferredExe = file
        };
        games.Add(g);
        games = games.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        Library.Save(games);
        FillList(g);
    }

    void PickShortcut()
    {
        using var d = new OpenFileDialog { Title = "Pick a game shortcut", Filter = "Shortcuts (*.lnk;*.url)|*.lnk;*.url",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) };
        if (d.ShowDialog() == DialogResult.OK) ImportFile(d.FileName);
    }

    void ImportFile(string path)
    {
        if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { AddExe(path); return; }
        if (!ShortcutImporter.IsShortcut(path)) return;
        var (g, err) = ShortcutImporter.Resolve(path, games);
        if (g == null) { MessageBox.Show(this, err ?? "Could not use that shortcut.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var existing = games.FirstOrDefault(x => x.Store == g.Store && x.Dir.Equals(g.Dir, StringComparison.OrdinalIgnoreCase) && x.Name == g.Name);
        if (existing == null) { games.Add(g); games = games.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList(); Library.Save(games); }
        FillList(existing ?? g);
        statusLbl.Text = "Shortcut loaded: " + g.Name + "\nPress PLAY to start it with Frame Generation.";
    }

    void OnSelect()
    {
        exeBox.Items.Clear();
        if (Sel is Game g)
        {
            Cursor = Cursors.WaitCursor;
            try { foreach (var e in Scanner.FindExes(g).Take(40)) exeBox.Items.Add(e); }
            finally { Cursor = Cursors.Default; }
            int pick = 0;
            for (int i = 0; i < exeBox.Items.Count; i++)
                if (Installer.IsInstalled(Path.GetDirectoryName((string)exeBox.Items[i]!)!)) { pick = i; break; }
            if (exeBox.Items.Count > 0) exeBox.SelectedIndex = pick;
            else statusLbl.Text = "No game .exe found in that folder. Use \"Add game (.exe)...\" and pick the game's program directly.";
        }
        RefreshState();
    }

    void RefreshState()
    {
        bool have = Sel != null && ExeDir != null;
        bool inst = have && Installer.IsInstalled(ExeDir!);
        btnPlay.Enabled = have && (inst || payload != null);
        btnUninstall.Enabled = inst;
        btnIni.Enabled = have;
        btnShortcut.Enabled = have;
        btnCfg.Enabled = payload != null && File.Exists(Path.Combine(payload, "dlssg_sm86ini_Configurator.html"));
        if (have) statusLbl.Text = (inst ? "Mod installed in:\n" : "Mod not installed yet. PLAY will install it into:\n") + ExeDir;
    }

    async Task Play()
    {
        if (Sel == null || ExeDir == null) return;
        var dir = ExeDir;
        if (!Installer.IsInstalled(dir))
        {
            if (payload == null) return;
            var pl = payload;
            SetBusy(true, "Installing mod files...");
            try
            {
                await Task.Run(() => Installer.Install(pl, dir));
                if (Sel is Game g) foreach (ListViewItem it in list.Items) if (it.Tag == g) it.SubItems[2].Text = "Installed";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Install failed:\n" + ex.Message + "\n\nIs the game closed?", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetBusy(false); RefreshState(); return;
            }
            SetBusy(false);
        }
        RefreshState();
        Launch();
    }

    async Task DoUninstall()
    {
        if (ExeDir == null) return;
        var dir = ExeDir;
        SetBusy(true, "Removing files...");
        try
        {
            await Task.Run(() => Installer.Uninstall(dir));
            if (Sel is Game g) foreach (ListViewItem it in list.Items) if (it.Tag == g) it.SubItems[2].Text = StatusOf(g);
        }
        catch (Exception ex) { MessageBox.Show(this, "Uninstall failed:\n" + ex.Message + "\n\nIs the game closed?", Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(false); RefreshState(); }
    }

    void SetBusy(bool busy, string? msg = null)
    {
        foreach (var b in new[] { btnPlay, btnShortcut, btnUninstall, btnIni, btnCfg }) b.Enabled = !busy;
        list.Enabled = exeBox.Enabled = !busy;
        UseWaitCursor = busy;
        if (msg != null) statusLbl.Text = msg;
    }

    void Launch()
    {
        if (Sel is not Game g || exeBox.SelectedItem is not string exe) return;
        try
        {
            GameRunner.Start(g.Store == "Steam" ? g.SteamAppId : null, exe, g.Lnk);
            Overlay.ShowFor(g.Name, exe);
            statusLbl.Text = "Launched. Turn on DLSS Frame Generation in the game's graphics menu.\nThe small status window shows if the mod is active.";
        }
        catch (Exception ex) { MessageBox.Show(this, "Could not launch:\n" + ex.Message); }
    }

    void MakeShortcut()
    {
        if (Sel is not Game g || exeBox.SelectedItem is not string exe) return;
        try
        {
            var path = Cli.CreateShortcut(g.Name, exe, g.Store == "Steam" ? g.SteamAppId : null, g.Lnk);
            statusLbl.Text = "Desktop shortcut created:\n" + Path.GetFileName(path) + "\nIt installs the mod if needed, starts the game and shows the status window.";
        }
        catch (Exception ex) { MessageBox.Show(this, "Could not create the shortcut:\n" + ex.Message); }
    }

    async Task OpenSettings()
    {
        if (Sel is not Game g || ExeDir == null || exeBox.SelectedItem is not string exe) return;
        var dir = ExeDir;
        if (!Installer.IsInstalled(dir))
        {
            if (payload == null) return;
            if (MessageBox.Show(this, "The mod is not installed in this game yet.\nInstall it now so its settings can be edited?", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var pl = payload;
            SetBusy(true, "Installing mod files...");
            try
            {
                await Task.Run(() => Installer.Install(pl, dir));
                foreach (ListViewItem it in list.Items) if (it.Tag == g) it.SubItems[2].Text = "Installed";
            }
            catch (Exception ex) { MessageBox.Show(this, "Install failed:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); SetBusy(false); RefreshState(); return; }
            SetBusy(false); RefreshState();
        }
        var ini = Path.Combine(dir, "dlssg_sm86.ini");
        if (!File.Exists(ini) && payload != null && File.Exists(Path.Combine(payload, "dlssg_sm86.ini")))
            File.Copy(Path.Combine(payload, "dlssg_sm86.ini"), ini);
        if (!File.Exists(ini)) { MessageBox.Show(this, "dlssg_sm86.ini was not found.", Text); return; }
        try
        {
            using var f = new SettingsForm(g.Name, ini, payload == null ? null : Path.Combine(payload, "dlssg_sm86.ini"), exe);
            if (f.ShowDialog(this) == DialogResult.OK) statusLbl.Text = "Settings saved. Restart the game for them to take effect.";
        }
        catch (Exception ex) { MessageBox.Show(this, "Could not open the settings:\n" + ex.Message, Text); }
    }

    void OpenConfigurator()
    {
        if (payload == null) return;
        Process.Start(new ProcessStartInfo(Path.Combine(payload, "dlssg_sm86ini_Configurator.html")) { UseShellExecute = true });
    }
}
